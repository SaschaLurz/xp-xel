using System.IO.Compression;
using System.Text;
using XPXel.Core.Documents;
using XPXel.Core.Imaging;
using XPXel.Core.Primitives;
using XPXel.Core.Text;
using XPXel.Formats.Png;
using XPXel.Formats.Project;

namespace XPXel.Formats.Tests;

/// <summary>
/// Tests for the native <c>.xpxel</c> project format.
/// </summary>
/// <remarks>
/// The round-trip test is the important one: it is the guarantee that an artist's work
/// survives save and reload intact, and it is what will catch a future change to the
/// document model that silently drops a field from the manifest.
/// </remarks>
public sealed class ProjectSerializerTests
{
    private static readonly PixelColor Red = new(255, 0, 0);
    private static readonly PixelColor Blue = new(0, 0, 255);

    [Fact]
    public void RoundTripsEverythingInADocument()
    {
        PixelDocument original = BuildRichDocument();

        PixelDocument reloaded = RoundTrip(original);

        Assert.Equal(original.Size, reloaded.Size);
        Assert.Equal(2, reloaded.Frames.Count);
        Assert.Equal(250, reloaded.Frames[1].DurationMilliseconds);

        Assert.Equal(2, reloaded.Root.Children.Count);

        LayerNode bottom = reloaded.Root.Children[0];
        Assert.Equal("Background", bottom.Name);
        Assert.Equal(BlendMode.Normal, bottom.BlendMode);

        LayerNode top = reloaded.Root.Children[1];
        Assert.Equal("Shading", top.Name);
        Assert.Equal(BlendMode.Multiply, top.BlendMode);
        Assert.Equal(0.5, top.Opacity, 3);
        Assert.False(top.IsVisible);
        Assert.True(top.IsLocked);
        Assert.Equal(Red, top.TagColor);

        // A layer that carries no label must reload without one, rather than picking up a
        // default colour.
        Assert.Null(bottom.TagColor);

        Assert.Equal("Test Palette", reloaded.Palette.Name);
        Assert.Equal(2, reloaded.Palette.Colors.Count);
        Assert.Equal(Red, reloaded.Palette.Colors[0]);

        Assert.Equal("16", reloaded.Metadata["export.unity.pixelsPerUnit"]);
    }

    [Fact]
    public void RoundTripsPixelDataOnEveryFrame()
    {
        PixelDocument original = BuildRichDocument();
        PixelDocument reloaded = RoundTrip(original);

        LayerNode layer = reloaded.Root.Children[0];

        Assert.Equal(Red, reloaded.Cels.Find(layer.Id, reloaded.Frames[0].Id)!.Pixels.GetPixel(0, 0));
        Assert.Equal(Blue, reloaded.Cels.Find(layer.Id, reloaded.Frames[1].Id)!.Pixels.GetPixel(0, 0));
    }

    [Fact]
    public void RoundTripsALayerThatHasBeenMovedPartlyOffTheCanvas()
    {
        PixelDocument original = new(new ImageSize(8, 8));
        LayerNode layer = original.CreateRasterLayer("Moved");

        Cel cel = original.GetOrCreateCel(layer, original.Frames[0]);
        cel.Pixels.SetPixel(1, 1, Red);

        // Four to the left: the one painted pixel goes from (1, 1) to (-3, 1), and the buffer
        // grows leftwards to keep it, so its top-left corner ends up outside the document. The
        // empty margin it left behind on the right is trimmed, which is why the buffer is
        // eleven wide rather than twelve.
        original.Cels.Set(cel.CreateMoved(new ImagePoint(-4, 0), original.Bounds));

        PixelDocument reloaded = RoundTrip(original);
        Cel restored = reloaded.Cels.Find(layer.Id, reloaded.Frames[0].Id)!;

        // The promise is that nothing is lost, and a file that dropped the hidden part or
        // forgot where the buffer sat would break it the moment the artist saved and reopened.
        Assert.Equal(new ImagePoint(-3, 0), restored.Offset);
        Assert.Equal(new ImageSize(11, 8), restored.Pixels.Size);

        // Still at (-3, 1) in the document, which is off the left-hand edge and invisible until
        // the canvas grows or the layer is moved back.
        Assert.Equal(Red, restored.Pixels.GetPixel(-3 - restored.Offset.X, 1 - restored.Offset.Y));
    }

    [Fact]
    public void RoundTripsNestedGroups()
    {
        PixelDocument original = new(new ImageSize(4, 4));
        GroupLayer group = new(Guid.NewGuid(), "Folder") { Opacity = 0.25 };
        original.InsertLayer(group, original.Root);
        RasterLayer inside = original.CreateRasterLayer("Inside", group);
        original.GetOrCreateCel(inside, original.Frames[0]).Pixels.Fill(Red);

        PixelDocument reloaded = RoundTrip(original);

        GroupLayer reloadedGroup = Assert.IsType<GroupLayer>(reloaded.Root.Children[0]);
        Assert.Equal(0.25, reloadedGroup.Opacity, 3);
        Assert.Single(reloadedGroup.Children);
        Assert.Equal("Inside", reloadedGroup.Children[0].Name);
        Assert.Equal(Red, reloaded.Cels.Find(reloadedGroup.Children[0].Id, reloaded.Frames[0].Id)!.Pixels.GetPixel(0, 0));
    }

    [Fact]
    public void PreservesLayerAndFrameIdentitiesSoCelsStayAttached()
    {
        PixelDocument original = BuildRichDocument();
        Guid layerId = original.Root.Children[0].Id;
        Guid frameId = original.Frames[0].Id;

        PixelDocument reloaded = RoundTrip(original);

        Assert.Equal(layerId, reloaded.Root.Children[0].Id);
        Assert.Equal(frameId, reloaded.Frames[0].Id);
    }

    [Fact]
    public void ArchiveContainsTheExpectedEntries()
    {
        using MemoryStream stream = new();
        new XpxelProjectSerializer().Write(stream, BuildRichDocument(), ExportOptions.Default);
        stream.Position = 0;

        using ZipArchive archive = new(stream, ZipArchiveMode.Read);

        Assert.Equal(ProjectSchema.ManifestEntryName, archive.Entries[0].FullName);
        Assert.NotNull(archive.GetEntry(ProjectSchema.ThumbnailEntryName));
        Assert.Contains(archive.Entries, e => e.FullName.StartsWith(ProjectSchema.CelFolderName, StringComparison.Ordinal));
    }

    [Fact]
    public void ContentSniffingDistinguishesProjectsFromOtherZipFiles()
    {
        using MemoryStream project = new();
        new XpxelProjectSerializer().Write(project, BuildRichDocument(), ExportOptions.Default);

        using MemoryStream plainZip = new();
        using (ZipArchive archive = new(plainZip, ZipArchiveMode.Create, leaveOpen: true))
        {
            using Stream entry = archive.CreateEntry("readme.txt").Open();
            entry.Write("hello"u8);
        }

        XpxelProjectSerializer serializer = new();
        Assert.True(serializer.CanRead(project.ToArray()));
        Assert.False(serializer.CanRead(plainZip.ToArray()));
    }

    [Fact]
    public void RefusesAProjectFromANewerVersionWithAClearMessage()
    {
        byte[] archive = BuildArchiveWithManifest($$"""
            { "schemaVersion": {{ProjectSchema.CurrentVersion + 1}}, "width": 4, "height": 4 }
            """);

        using MemoryStream stream = new(archive);
        ImageFormatException error = Assert.Throws<ImageFormatException>(
            () => new XpxelProjectSerializer().Read(stream, "test"));

        Assert.Contains("newer version", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ReportsAMissingCelImageRatherThanCrashing()
    {
        byte[] archive = BuildArchiveWithManifest($$"""
            {
              "schemaVersion": {{ProjectSchema.CurrentVersion}},
              "width": 4,
              "height": 4,
              "frames": [ { "id": "11111111-1111-1111-1111-111111111111", "durationMilliseconds": 100 } ],
              "layers": [ { "id": "22222222-2222-2222-2222-222222222222", "type": "raster", "name": "L" } ],
              "cels": [ {
                "layerId": "22222222-2222-2222-2222-222222222222",
                "frameId": "11111111-1111-1111-1111-111111111111",
                "image": "cels/missing.png"
              } ]
            }
            """);

        using MemoryStream stream = new(archive);
        ImageFormatException error = Assert.Throws<ImageFormatException>(
            () => new XpxelProjectSerializer().Read(stream, "test"));

        Assert.Contains("missing.png", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void UnknownBlendModeFallsBackToNormal()
    {
        byte[] archive = BuildArchiveWithManifest($$"""
            {
              "schemaVersion": {{ProjectSchema.CurrentVersion}},
              "width": 2,
              "height": 2,
              "frames": [ { "id": "11111111-1111-1111-1111-111111111111", "durationMilliseconds": 100 } ],
              "layers": [ {
                "id": "22222222-2222-2222-2222-222222222222",
                "type": "raster",
                "name": "L",
                "blendMode": "SomeModeFromTheFuture"
              } ]
            }
            """);

        using MemoryStream stream = new(archive);
        PixelDocument document = new XpxelProjectSerializer().Read(stream, "test");

        Assert.Equal(BlendMode.Normal, document.Root.Children[0].BlendMode);
    }

    [Fact]
    public void RoundTripsNamedAnimations()
    {
        PixelDocument original = PixelDocument.CreateNew(new ImageSize(8, 8));

        while (original.Frames.Count < 6)
        {
            original.CreateFrame();
        }

        original.Frames[2].DurationMilliseconds = 250;

        AnimationTag idle = original.CreateTag("idle", 0, 1);
        idle.Color = Red;

        AnimationTag walk = original.CreateTag("walk", 2, 5);
        walk.Direction = LoopDirection.PingPong;
        walk.RepeatCount = 3;

        PixelDocument reloaded = RoundTrip(original);

        Assert.Equal(6, reloaded.Frames.Count);
        Assert.Equal(250, reloaded.Frames[2].DurationMilliseconds);
        Assert.Equal(2, reloaded.Tags.Count);

        AnimationTag reloadedIdle = reloaded.Tags[0];
        Assert.Equal("idle", reloadedIdle.Name);
        Assert.Equal(0, reloadedIdle.From);
        Assert.Equal(1, reloadedIdle.To);
        Assert.Equal(LoopDirection.Forward, reloadedIdle.Direction);
        Assert.Equal(Red, reloadedIdle.Color);

        AnimationTag reloadedWalk = reloaded.Tags[1];
        Assert.Equal("walk", reloadedWalk.Name);
        Assert.Equal(2, reloadedWalk.From);
        Assert.Equal(5, reloadedWalk.To);
        Assert.Equal(LoopDirection.PingPong, reloadedWalk.Direction);
        Assert.Equal(3, reloadedWalk.RepeatCount);

        // A tag with no colour must reload without one rather than picking up a default.
        Assert.Null(reloadedWalk.Color);
    }

    [Fact]
    public void AnAnimationRangeBeyondTheTimelineIsClampedRatherThanFailing()
    {
        byte[] archive = BuildArchiveWithManifest($$"""
            {
              "schemaVersion": {{ProjectSchema.CurrentVersion}},
              "width": 4,
              "height": 4,
              "frames": [ { "id": "11111111-1111-1111-1111-111111111111", "durationMilliseconds": 100 } ],
              "layers": [ { "id": "22222222-2222-2222-2222-222222222222", "type": "raster", "name": "L" } ],
              "tags": [ {
                "id": "33333333-3333-3333-3333-333333333333",
                "name": "walk",
                "from": 4,
                "to": 40,
                "direction": "SomeDirectionFromTheFuture"
              } ]
            }
            """);

        using MemoryStream stream = new(archive);
        PixelDocument document = new XpxelProjectSerializer().Read(stream, "test");

        // A hand-edited or truncated file should open showing what it can, rather than
        // presenting an animation that would export as an empty sheet.
        AnimationTag tag = Assert.Single(document.Tags);
        Assert.Equal(0, tag.From);
        Assert.Equal(0, tag.To);
        Assert.Equal(LoopDirection.Forward, tag.Direction);
    }

    [Fact]
    public void RoundTripsATextLayerAsBothWordsAndPixels()
    {
        PixelDocument original = PixelDocument.CreateNew(new ImageSize(16, 16));

        TextLayer text = original.CreateTextLayer("Caption");
        text.Text = "Level 1";
        text.Origin = new ImagePoint(3, 4);
        text.Style.FontFamily = "Consolas";
        text.Style.FontSize = 11;
        text.Style.IsBold = true;
        text.Style.IsUnderline = true;
        text.Style.Alignment = TextAlignment.Center;
        text.Style.LineSpacing = 1.25;
        text.Style.IsAntiAliased = true;
        text.Style.Color = Blue;

        // Stand in for the glyphs a font engine would have produced. They are stored as an
        // ordinary cel, which is what lets a build with no font for "Consolas" - or an older
        // build that does not know text layers at all - still show the caption correctly.
        Cel cel = original.GetOrCreateCel(text, original.Frames[0]);
        cel.Pixels.SetPixel(3, 4, Blue);

        PixelDocument reloaded = RoundTrip(original);

        TextLayer restored = Assert.IsType<TextLayer>(reloaded.Root.Children[^1]);
        Assert.Equal("Caption", restored.Name);
        Assert.Equal("Level 1", restored.Text);
        Assert.Equal(new ImagePoint(3, 4), restored.Origin);
        Assert.Equal("Consolas", restored.Style.FontFamily);
        Assert.Equal(11, restored.Style.FontSize);
        Assert.True(restored.Style.IsBold);
        Assert.True(restored.Style.IsUnderline);
        Assert.False(restored.Style.IsItalic);
        Assert.Equal(TextAlignment.Center, restored.Style.Alignment);
        Assert.Equal(1.25, restored.Style.LineSpacing, 3);
        Assert.True(restored.Style.IsAntiAliased);
        Assert.Equal(Blue, restored.Style.Color);

        Cel restoredCel = reloaded.Cels.Find(restored.Id, reloaded.Frames[0].Id)!;
        Assert.Equal(Blue, restoredCel.Pixels.GetPixel(3, 4));
    }

    [Fact]
    public void AnUnrecognisedLayerKindLoadsAsARasterLayerRatherThanFailing()
    {
        PixelDocument document = PixelDocument.CreateNew(new ImageSize(4, 4));

        using MemoryStream stream = new();
        XpxelProjectSerializer serializer = new();
        serializer.Write(stream, document, ExportOptions.Default);

        byte[] rewritten = ReplaceInManifest(stream.ToArray(), "\"type\": \"raster\"", "\"type\": \"hologram\"");

        using MemoryStream reopened = new(rewritten);
        PixelDocument reloaded = serializer.Read(reopened, "reloaded");

        // A file written by a newer build has to open in this one, showing what it can. That
        // rule is what let text layers ship without a schema version bump.
        Assert.IsType<RasterLayer>(Assert.Single(reloaded.Root.Children));
    }

    /// <summary>Rewrites the manifest inside a project archive, for compatibility tests.</summary>
    private static byte[] ReplaceInManifest(byte[] project, string find, string replaceWith)
    {
        using MemoryStream source = new(project);
        using ZipArchive reader = new(source, ZipArchiveMode.Read);

        Dictionary<string, byte[]> entries = [];

        foreach (ZipArchiveEntry entry in reader.Entries)
        {
            using Stream content = entry.Open();
            using MemoryStream copy = new();
            content.CopyTo(copy);
            entries[entry.FullName] = copy.ToArray();
        }

        string manifest = Encoding.UTF8.GetString(entries["manifest.json"]);
        entries["manifest.json"] = Encoding.UTF8.GetBytes(manifest.Replace(find, replaceWith, StringComparison.Ordinal));

        using MemoryStream output = new();

        using (ZipArchive writer = new(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach ((string name, byte[] bytes) in entries)
            {
                using Stream target = writer.CreateEntry(name).Open();
                target.Write(bytes);
            }
        }

        return output.ToArray();
    }

    private static PixelDocument RoundTrip(PixelDocument document)
    {
        using MemoryStream stream = new();
        XpxelProjectSerializer serializer = new();

        serializer.Write(stream, document, ExportOptions.Default);
        stream.Position = 0;

        return serializer.Read(stream, "reloaded");
    }

    private static PixelDocument BuildRichDocument()
    {
        PixelDocument document = new(new ImageSize(8, 8));

        Frame second = document.CreateFrame();
        second.DurationMilliseconds = 250;

        RasterLayer background = document.CreateRasterLayer("Background");
        document.GetOrCreateCel(background, document.Frames[0]).Pixels.Fill(Red);
        document.GetOrCreateCel(background, second).Pixels.Fill(Blue);

        RasterLayer shading = document.CreateRasterLayer("Shading");
        shading.BlendMode = BlendMode.Multiply;
        shading.Opacity = 0.5;
        shading.IsVisible = false;
        shading.IsLocked = true;
        shading.TagColor = Red;
        document.GetOrCreateCel(shading, document.Frames[0]).Pixels.FillRect(new ImageRect(0, 0, 4, 4), Blue);

        document.Palette.Name = "Test Palette";
        document.Palette.ReplaceAll([Red, Blue]);
        document.Metadata["export.unity.pixelsPerUnit"] = "16";

        return document;
    }

    /// <summary>Builds a minimal archive around a hand-written manifest.</summary>
    private static byte[] BuildArchiveWithManifest(string manifestJson)
    {
        using MemoryStream stream = new();

        using (ZipArchive archive = new(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            using Stream entry = archive.CreateEntry(ProjectSchema.ManifestEntryName).Open();
            entry.Write(Encoding.UTF8.GetBytes(manifestJson));
        }

        return stream.ToArray();
    }
}
