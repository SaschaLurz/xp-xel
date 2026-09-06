using System.Globalization;
using System.Reflection;
using System.Text.Json;
using XPXel.Core.Documents;
using XPXel.Core.Imaging;
using XPXel.Core.Primitives;
using XPXel.Formats.Png;

namespace XPXel.Formats.Animation;

/// <summary>
/// Writes an animation as a sprite sheet: one PNG holding every frame on a uniform grid,
/// plus a JSON sidecar describing them.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is the format 2D game animation is actually shipped in.</b> An engine loads one
/// texture and draws a different rectangle of it each frame, which costs one texture bind
/// instead of one per frame and is why every 2D engine's animation system is built around a
/// sheet. The uniform grid is what lets Unity's Sprite Editor slice it with "Grid By Cell
/// Size", and the JSON is what lets Godot, Phaser and custom importers pick up the frame
/// timings and the animation names that a bare image cannot carry.
/// </para>
/// <para>
/// <b>The loop direction is recorded, not baked in.</b> A ping-pong animation is written out
/// once, forwards, with its direction in the metadata — storing the return leg would put the
/// same drawing in the texture twice. That is the opposite of what the animated image formats
/// do, and the reason <see cref="AnimationExportOptions.ExpandLoop"/> exists.
/// </para>
/// </remarks>
public sealed class SpriteSheetExporter : IAnimationExporter
{
    private static readonly FormatDescriptor SheetDescriptor = new(
        "xpxel.format.spritesheet",
        "Sprite Sheet (PNG + JSON)",
        [".png"],
        "image/png",
        FormatCapabilities.Animation | FormatCapabilities.Alpha | FormatCapabilities.Metadata);

    /// <inheritdoc />
    public FormatDescriptor Descriptor => SheetDescriptor;

    /// <inheritdoc />
    public string Summary =>
        "One image holding every frame on a uniform grid, with a .json listing the frames, "
        + "their timings and the animation names. The standard way to ship 2D animation: "
        + "Unity slices it with Grid By Cell Size, and Godot, Phaser and most importers read the JSON.";

    /// <inheritdoc />
    public bool ProducesSingleFile => false;

    /// <inheritdoc />
    public bool UsesSheetLayout => true;

    /// <inheritdoc />
    public IReadOnlyList<ExportArtifact> Export(PixelDocument document, AnimationExportOptions options)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(options);

        IReadOnlyList<AnimationClip> clips = AnimationClip.Build(document, options);

        return options.SeparateFilePerTag && clips.Count > 1
            ? [.. clips.SelectMany(clip => BuildSheet(document, options, [clip], $"{options.BaseName}_{clip.Name}"))]
            : BuildSheet(document, options, clips, options.BaseName);
    }

    /// <summary>Draws one sheet and its metadata.</summary>
    private static List<ExportArtifact> BuildSheet(
        PixelDocument document,
        AnimationExportOptions options,
        IReadOnlyList<AnimationClip> clips,
        string baseName)
    {
        SpriteSheetPlan plan = SpriteSheetPlan.Create(clips, options.Layout, options.Padding);

        PixelBuffer sheet = new(plan.SheetSize);

        foreach (SheetCell cell in plan.Cells)
        {
            PixelBuffer frame = clips[cell.ClipIndex].Images[cell.FrameIndex];
            frame.CopyRegionTo(frame.Bounds, sheet, cell.Bounds.Origin);
        }

        using MemoryStream image = new();
        PngCodec.EncodeImage(image, sheet);

        string imageName = $"{baseName}.png";
        List<ExportArtifact> artifacts =
        [
            new ExportArtifact(imageName, image.ToArray(), "image/png"),
        ];

        if (options.IncludeMetadata)
        {
            byte[] json = BuildMetadata(document, options, clips, plan, imageName);
            artifacts.Add(new ExportArtifact($"{baseName}.json", json, "application/json"));
        }

        return artifacts;
    }

    private static byte[] BuildMetadata(
        PixelDocument document,
        AnimationExportOptions options,
        IReadOnlyList<AnimationClip> clips,
        SpriteSheetPlan plan,
        string imageName)
    {
        SpriteSheetManifest manifest = new()
        {
            Meta = new SheetMetaEntry
            {
                Version = typeof(SpriteSheetExporter).Assembly
                    .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                    ?? string.Empty,
                Image = imageName,
                Size = new SheetSize { W = plan.SheetSize.Width, H = plan.SheetSize.Height },
                Scale = options.Scale.ToString(CultureInfo.InvariantCulture),
                CellSize = new SheetSize { W = plan.CellSize.Width, H = plan.CellSize.Height },
                Grid = new SheetSize { W = plan.Columns, H = plan.Rows },
            },
        };

        // Frames are listed in sheet order, and each animation's range is expressed as
        // indices into that list — which is what an importer walks.
        int index = 0;
        int[] clipStart = new int[clips.Count];

        foreach (SheetCell cell in plan.Cells)
        {
            AnimationClip clip = clips[cell.ClipIndex];

            if (cell.FrameIndex == 0)
            {
                clipStart[cell.ClipIndex] = index;
            }

            manifest.Frames.Add(new SheetFrameEntry
            {
                Filename = $"{clip.Name} {cell.FrameIndex}.png",
                Frame = ToRect(cell.Bounds),
                SpriteSourceSize = new SheetRect { X = 0, Y = 0, W = plan.CellSize.Width, H = plan.CellSize.Height },
                SourceSize = new SheetSize { W = plan.CellSize.Width, H = plan.CellSize.Height },
                Duration = clip.Durations[cell.FrameIndex],
            });

            index++;
        }

        // Two ways a sheet can carry its animations. When each clip came from a tag, the
        // ranges are worked out from where that clip landed on the sheet. When the sheet is
        // the whole timeline in order — which is what a shared sheet is — the document's own
        // tags already describe it, and their indices are the sheet's frame indices.
        bool clipsAreTagged = clips.Any(c => c.Tag is not null);

        if (clipsAreTagged)
        {
            for (int i = 0; i < clips.Count; i++)
            {
                AnimationClip clip = clips[i];
                if (clip.Count == 0)
                {
                    continue;
                }

                manifest.Meta.FrameTags.Add(new SheetTagEntry
                {
                    Name = clip.Name,
                    From = clipStart[i],
                    To = clipStart[i] + clip.Count - 1,
                    Direction = DirectionName(clip.Tag?.Direction ?? LoopDirection.Forward),
                    Repeat = clip.Tag is { RepeatCount: > 0 } tag
                        ? tag.RepeatCount.ToString(CultureInfo.InvariantCulture)
                        : string.Empty,
                });
            }
        }
        else
        {
            foreach (AnimationTag tag in document.Tags)
            {
                manifest.Meta.FrameTags.Add(new SheetTagEntry
                {
                    Name = tag.Name,
                    From = tag.From,
                    To = tag.To,
                    Direction = DirectionName(tag.Direction),
                    Repeat = tag.RepeatCount > 0
                        ? tag.RepeatCount.ToString(CultureInfo.InvariantCulture)
                        : string.Empty,
                });
            }
        }

        foreach (LayerNode layer in document.EnumerateLayers())
        {
            manifest.Meta.Layers.Add(new SheetLayerEntry
            {
                Name = layer.Name,
                Opacity = (int)Math.Round(layer.Opacity * 255),
                BlendMode = layer.BlendMode.ToString().ToLowerInvariant(),
            });
        }

        return JsonSerializer.SerializeToUtf8Bytes(manifest, SpriteSheetJsonContext.Default.SpriteSheetManifest);
    }

    private static SheetRect ToRect(ImageRect rect) =>
        new() { X = rect.X, Y = rect.Y, W = rect.Width, H = rect.Height };

    /// <summary>
    /// The direction names Aseprite's JSON uses, which importers match on literally.
    /// </summary>
    private static string DirectionName(LoopDirection direction) => direction switch
    {
        LoopDirection.Reverse => "reverse",
        LoopDirection.PingPong => "pingpong",
        _ => "forward",
    };
}
