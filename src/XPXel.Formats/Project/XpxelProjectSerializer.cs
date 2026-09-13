using System.Globalization;
using System.IO.Compression;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using XPXel.Core.Documents;
using XPXel.Core.Imaging;
using XPXel.Core.Primitives;
using XPXel.Core.Rendering;
using XPXel.Core.Text;
using XPXel.Formats.Png;

namespace XPXel.Formats.Project;

/// <summary>
/// Reads and writes XP-xel's own <c>.xpxel</c> project files, the only format that
/// round-trips a document without losing anything.
/// </summary>
/// <remarks>
/// See <see cref="ProjectSchema"/> for the archive layout and the reasoning behind it. This
/// class is a straightforward translation between the document model and the manifest data
/// transfer objects; anything subtle lives in the compatibility rules documented on
/// <see cref="ProjectManifest"/>.
/// </remarks>
public sealed class XpxelProjectSerializer : IImageImporter, IImageExporter
{
    /// <summary>The format this serializer handles.</summary>
    public static FormatDescriptor Format { get; } = new(
        id: "xpxel.format.project",
        displayName: "XP-xel Project",
        extensions: [".xpxel"],
        mimeType: "application/x-xpxel-project",
        capabilities: FormatCapabilities.Layers
            | FormatCapabilities.Animation
            | FormatCapabilities.Alpha
            | FormatCapabilities.IndexedColor
            | FormatCapabilities.Metadata);

    /// <inheritdoc />
    public FormatDescriptor Descriptor => Format;

    /// <inheritdoc />
    /// <remarks>
    /// A bare zip signature is not enough to claim a file, since every zip-based format
    /// starts the same way. Because the manifest is always written as the archive's first
    /// entry, its name appears at a fixed offset in the local file header, which makes the
    /// check specific.
    /// </remarks>
    public bool CanRead(ReadOnlySpan<byte> header)
    {
        ReadOnlySpan<byte> zipSignature = [0x50, 0x4B, 0x03, 0x04];
        const int fileNameOffset = 30;

        if (header.Length < fileNameOffset + ProjectSchema.ManifestEntryName.Length)
        {
            return false;
        }

        if (!header[..4].SequenceEqual(zipSignature))
        {
            return false;
        }

        ReadOnlySpan<byte> name = header.Slice(fileNameOffset, ProjectSchema.ManifestEntryName.Length);
        return Encoding.ASCII.GetString(name).Equals(ProjectSchema.ManifestEntryName, StringComparison.Ordinal);
    }

    /// <inheritdoc />
    public PixelDocument Read(Stream stream, string documentName)
    {
        ArgumentNullException.ThrowIfNull(stream);

        try
        {
            using ZipArchive archive = new(stream, ZipArchiveMode.Read, leaveOpen: true);
            ProjectManifest manifest = ReadManifest(archive);
            return BuildDocument(archive, manifest);
        }
        catch (InvalidDataException ex)
        {
            throw new ImageFormatException("The project file is not a readable XP-xel archive.", ex);
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// <see cref="ExportOptions.Scale"/> and <see cref="ExportOptions.Frame"/> are ignored:
    /// a project file is a complete, unscaled copy of the document by definition.
    /// </remarks>
    public void Write(Stream stream, PixelDocument document, ExportOptions options)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(document);

        using ZipArchive archive = new(stream, ZipArchiveMode.Create, leaveOpen: true);

        ProjectManifest manifest = BuildManifest(document, out List<Cel> orderedCels);

        // The manifest must be the first entry so that CanRead can identify the format from
        // the file's leading bytes.
        WriteManifest(archive, manifest);
        WriteCels(archive, manifest, orderedCels);
        WriteThumbnail(archive, document);
    }

    // -----------------------------------------------------------------------------------
    // Reading
    // -----------------------------------------------------------------------------------

    private static ProjectManifest ReadManifest(ZipArchive archive)
    {
        ZipArchiveEntry entry = archive.GetEntry(ProjectSchema.ManifestEntryName)
            ?? throw new ImageFormatException("The project archive has no manifest.json entry.");

        JsonNode? node;
        using (Stream manifestStream = entry.Open())
        {
            node = JsonNode.Parse(manifestStream);
        }

        if (node is not JsonObject manifestObject)
        {
            throw new ImageFormatException("The project manifest is not a JSON object.");
        }

        // Upgrade before deserializing: the strongly typed manifest describes the *current*
        // schema, so an older document has to be rewritten into that shape first.
        ProjectMigrationPipeline.Upgrade(manifestObject);

        return manifestObject.Deserialize(ProjectManifestJsonContext.Default.ProjectManifest)
            ?? throw new ImageFormatException("The project manifest could not be read.");
    }

    private static PixelDocument BuildDocument(ZipArchive archive, ProjectManifest manifest)
    {
        if (manifest.Width <= 0 || manifest.Height <= 0)
        {
            throw new ImageFormatException(
                $"The project declares an invalid canvas size of {manifest.Width}x{manifest.Height}.");
        }

        PixelDocument document = new(new ImageSize(manifest.Width, manifest.Height));

        RestoreFrames(document, manifest);
        RestoreLayers(document, manifest);
        RestoreTags(document, manifest);
        RestoreCels(archive, document, manifest);
        RestorePalette(document, manifest);

        foreach ((string key, string value) in manifest.Metadata)
        {
            document.Metadata[key] = value;
        }

        return document;
    }

    private static void RestoreFrames(PixelDocument document, ProjectManifest manifest)
    {
        List<Frame> frames = [];
        foreach (FrameManifest entry in manifest.Frames)
        {
            frames.Add(new Frame(ParseId(entry.Id, "frame")) { DurationMilliseconds = entry.DurationMilliseconds });
        }

        // A document written by any XP-xel build always has at least one frame, but a
        // hand-edited or truncated file might not; keeping the default frame is friendlier
        // than refusing to open.
        if (frames.Count > 0)
        {
            document.ReplaceFrames(frames);
        }
    }

    /// <summary>
    /// Rebuilds the document's named animations.
    /// </summary>
    /// <remarks>
    /// Ranges are clamped to the timeline that was actually loaded, and a tag left covering
    /// nothing is dropped. A hand-edited or truncated file should open showing what it can
    /// rather than presenting an animation that exports as an empty sheet.
    /// </remarks>
    private static void RestoreTags(PixelDocument document, ProjectManifest manifest)
    {
        if (manifest.Tags.Count == 0)
        {
            return;
        }

        int lastFrame = document.Frames.Count - 1;
        List<AnimationTag> tags = [];

        foreach (TagManifest entry in manifest.Tags)
        {
            int from = Math.Clamp(entry.From, 0, lastFrame);
            int to = Math.Clamp(entry.To, from, lastFrame);

            AnimationTag tag = new(ParseId(entry.Id, "tag"), entry.Name, from, to)
            {
                RepeatCount = entry.RepeatCount,
                Direction = Enum.TryParse(entry.Direction, ignoreCase: true, out LoopDirection direction)
                    ? direction
                    : LoopDirection.Forward,
                Color = entry.Color is { Length: > 0 } hex && PixelColor.TryParseHex(hex, out PixelColor color)
                    ? color
                    : null,
                CoversWholeTimeline = entry.CoversWholeTimeline,
            };

            tags.Add(tag);
        }

        document.ReplaceTags(tags);
    }

    private static void RestoreLayers(PixelDocument document, ProjectManifest manifest)
    {
        foreach (LayerManifest entry in manifest.Layers)
        {
            RestoreLayer(document, entry, document.Root);
        }
    }

    private static void RestoreLayer(PixelDocument document, LayerManifest entry, GroupLayer parent)
    {
        Guid id = ParseId(entry.Id, "layer");

        LayerNode layer = entry.Type switch
        {
            ProjectSchema.GroupLayerType => new GroupLayer(id, entry.Name),
            ProjectSchema.TextLayerType => RestoreTextLayer(id, entry),
            _ => new RasterLayer(id, entry.Name),
        };

        layer.IsVisible = entry.IsVisible;
        layer.IsLocked = entry.IsLocked;
        layer.Opacity = entry.Opacity;

        // An unrecognised blend mode falls back to Normal rather than failing the load, so a
        // file written by a newer build still opens and looks nearly right.
        layer.BlendMode = Enum.TryParse(entry.BlendMode, ignoreCase: true, out BlendMode blendMode)
            ? blendMode
            : BlendMode.Normal;

        // An unparseable label colour drops the label rather than failing the load. The
        // colour is decoration, and refusing to open an entire project over it would be a
        // poor trade.
        layer.TagColor = entry.TagColor is { Length: > 0 } tag && PixelColor.TryParseHex(tag, out PixelColor tagColor)
            ? tagColor
            : null;

        document.InsertLayer(layer, parent);

        // Declared before any cel is restored, so the store files this layer's single drawing
        // under the layer rather than under a frame. RestoreCels runs after RestoreLayers for
        // exactly this reason.
        if (entry.IsContinuous)
        {
            document.SetLayerContinuous(layer, continuous: true);
        }

        if (layer is GroupLayer group)
        {
            foreach (LayerManifest child in entry.Children)
            {
                RestoreLayer(document, child, group);
            }
        }
    }

    /// <summary>
    /// Rebuilds a text layer's words and typography from the manifest.
    /// </summary>
    /// <remarks>
    /// Nothing here is allowed to fail the load. A missing text block, an unrecognised
    /// alignment name and an unparseable colour all fall back to a default, because the
    /// layer's rendered pixels are in the archive either way — refusing to open a project
    /// over a typo in a style value would lose the artwork to protect the formatting.
    /// </remarks>
    private static TextLayer RestoreTextLayer(Guid id, LayerManifest entry)
    {
        TextLayer layer = new(id, entry.Name);

        if (entry.Text is not { } text)
        {
            return layer;
        }

        layer.Text = text.Content;
        layer.Origin = new ImagePoint(text.OriginX, text.OriginY);

        TextStyle style = layer.Style;
        style.FontFamily = text.FontFamily;
        style.FontSize = text.FontSize;
        style.IsBold = text.IsBold;
        style.IsItalic = text.IsItalic;
        style.IsUnderline = text.IsUnderline;
        style.IsStrikethrough = text.IsStrikethrough;
        style.LineSpacing = text.LineSpacing;
        style.LetterSpacing = text.LetterSpacing;
        style.IsAntiAliased = text.IsAntiAliased;

        style.Alignment = Enum.TryParse(text.Alignment, ignoreCase: true, out TextAlignment alignment)
            ? alignment
            : TextAlignment.Left;

        style.Color = PixelColor.TryParseHex(text.Color, out PixelColor color) ? color : PixelColor.Black;

        return layer;
    }

    private static void RestoreCels(ZipArchive archive, PixelDocument document, ProjectManifest manifest)
    {
        foreach (CelManifest entry in manifest.Cels)
        {
            ZipArchiveEntry? imageEntry = archive.GetEntry(entry.Image);
            if (imageEntry is null)
            {
                throw new ImageFormatException(
                    $"The project references the image '{entry.Image}', which is missing from the archive.");
            }

            // ZipArchiveEntry streams cannot seek, and the PNG decoder reads forwards only,
            // so this is safe as written.
            using Stream imageStream = imageEntry.Open();
            PixelBuffer pixels = PngCodec.DecodeImage(imageStream);

            Cel cel = new(ParseId(entry.LayerId, "layer"), ParseId(entry.FrameId, "frame"), pixels)
            {
                Offset = new ImagePoint(entry.OffsetX, entry.OffsetY),
                Opacity = entry.Opacity,
            };

            document.Cels.Set(cel);
        }
    }

    private static void RestorePalette(PixelDocument document, ProjectManifest manifest)
    {
        document.Palette.Name = manifest.Palette.Name;

        List<PixelColor> colors = [];
        foreach (string text in manifest.Palette.Colors)
        {
            if (PixelColor.TryParseHex(text, out PixelColor color))
            {
                colors.Add(color);
            }
        }

        if (colors.Count > 0)
        {
            document.Palette.ReplaceAll(colors);
        }
    }

    private static Guid ParseId(string text, string what) =>
        Guid.TryParse(text, out Guid id)
            ? id
            : throw new ImageFormatException($"The project contains an invalid {what} identifier '{text}'.");

    // -----------------------------------------------------------------------------------
    // Writing
    // -----------------------------------------------------------------------------------

    private static ProjectManifest BuildManifest(PixelDocument document, out List<Cel> orderedCels)
    {
        ProjectManifest manifest = new()
        {
            SchemaVersion = ProjectSchema.CurrentVersion,
            ApplicationVersion = typeof(XpxelProjectSerializer).Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                ?? string.Empty,
            Width = document.Size.Width,
            Height = document.Size.Height,
            Palette = new PaletteManifest { Name = document.Palette.Name },
        };

        foreach (Frame frame in document.Frames)
        {
            manifest.Frames.Add(new FrameManifest
            {
                Id = frame.Id.ToString("D", CultureInfo.InvariantCulture),
                DurationMilliseconds = frame.DurationMilliseconds,
            });
        }

        foreach (LayerNode layer in document.Root.Children)
        {
            manifest.Layers.Add(BuildLayerManifest(layer));
        }

        foreach (AnimationTag tag in document.Tags)
        {
            manifest.Tags.Add(new TagManifest
            {
                Id = tag.Id.ToString("D", CultureInfo.InvariantCulture),
                Name = tag.Name,
                From = tag.From,
                To = tag.To,
                Direction = tag.Direction.ToString(),
                RepeatCount = tag.RepeatCount,
                Color = tag.Color?.ToHex(),
                CoversWholeTimeline = tag.CoversWholeTimeline,
            });
        }

        foreach (PixelColor color in document.Palette.Colors)
        {
            manifest.Palette.Colors.Add(color.ToHex());
        }

        foreach ((string key, string value) in document.Metadata)
        {
            manifest.Metadata[key] = value;
        }

        // Cel entries are numbered in a stable order so that saving the same document twice
        // produces byte-comparable archives, which makes the format friendlier to version
        // control and to diffing during debugging.
        orderedCels = [.. document.Cels.All];
        orderedCels.Sort(static (a, b) =>
        {
            int byLayer = a.LayerId.CompareTo(b.LayerId);
            return byLayer != 0 ? byLayer : a.FrameId.CompareTo(b.FrameId);
        });

        for (int i = 0; i < orderedCels.Count; i++)
        {
            Cel cel = orderedCels[i];
            manifest.Cels.Add(new CelManifest
            {
                LayerId = cel.LayerId.ToString("D", CultureInfo.InvariantCulture),
                FrameId = cel.FrameId.ToString("D", CultureInfo.InvariantCulture),
                Image = string.Create(CultureInfo.InvariantCulture, $"{ProjectSchema.CelFolderName}{i}.png"),
                OffsetX = cel.Offset.X,
                OffsetY = cel.Offset.Y,
                Opacity = cel.Opacity,
            });
        }

        return manifest;
    }

    private static LayerManifest BuildLayerManifest(LayerNode layer)
    {
        LayerManifest entry = new()
        {
            Id = layer.Id.ToString("D", CultureInfo.InvariantCulture),
            Type = layer switch
            {
                GroupLayer => ProjectSchema.GroupLayerType,
                TextLayer => ProjectSchema.TextLayerType,
                _ => ProjectSchema.RasterLayerType,
            },
            Name = layer.Name,
            IsVisible = layer.IsVisible,
            IsLocked = layer.IsLocked,
            IsContinuous = layer.IsContinuous,
            Opacity = layer.Opacity,
            BlendMode = layer.BlendMode.ToString(),
            TagColor = layer.TagColor?.ToHex(),
        };

        if (layer is GroupLayer group)
        {
            foreach (LayerNode child in group.Children)
            {
                entry.Children.Add(BuildLayerManifest(child));
            }
        }

        if (layer is TextLayer text)
        {
            entry.Text = BuildTextManifest(text);
        }

        return entry;
    }

    private static TextManifest BuildTextManifest(TextLayer layer)
    {
        TextStyle style = layer.Style;

        return new TextManifest
        {
            Content = layer.Text,
            OriginX = layer.Origin.X,
            OriginY = layer.Origin.Y,
            FontFamily = style.FontFamily,
            FontSize = style.FontSize,
            IsBold = style.IsBold,
            IsItalic = style.IsItalic,
            IsUnderline = style.IsUnderline,
            IsStrikethrough = style.IsStrikethrough,
            Alignment = style.Alignment.ToString(),
            LineSpacing = style.LineSpacing,
            LetterSpacing = style.LetterSpacing,
            IsAntiAliased = style.IsAntiAliased,
            Color = style.Color.ToHex(),
        };
    }

    private static void WriteManifest(ZipArchive archive, ProjectManifest manifest)
    {
        ZipArchiveEntry entry = archive.CreateEntry(ProjectSchema.ManifestEntryName, CompressionLevel.Optimal);
        using Stream stream = entry.Open();
        using Utf8JsonWriter writer = new(stream, new JsonWriterOptions { Indented = true });

        JsonSerializer.Serialize(writer, manifest, ProjectManifestJsonContext.Default.ProjectManifest);
    }

    private static void WriteCels(ZipArchive archive, ProjectManifest manifest, List<Cel> orderedCels)
    {
        for (int i = 0; i < orderedCels.Count; i++)
        {
            // The pixel data is already PNG-compressed, so asking the zip layer to deflate it
            // again would cost time and save nothing.
            ZipArchiveEntry entry = archive.CreateEntry(manifest.Cels[i].Image, CompressionLevel.NoCompression);
            using Stream stream = entry.Open();
            PngCodec.EncodeImage(stream, orderedCels[i].Pixels);
        }
    }

    private static void WriteThumbnail(ZipArchive archive, PixelDocument document)
    {
        PixelBuffer flattened = new LayerCompositor().ComposeToNewBuffer(document, document.Frames[0]);

        ZipArchiveEntry entry = archive.CreateEntry(ProjectSchema.ThumbnailEntryName, CompressionLevel.NoCompression);
        using Stream stream = entry.Open();
        PngCodec.EncodeImage(stream, flattened);
    }
}
