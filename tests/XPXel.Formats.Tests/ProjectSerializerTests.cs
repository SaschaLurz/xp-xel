using System.IO.Compression;
using System.Text;
using XPXel.Core.Documents;
using XPXel.Core.Imaging;
using XPXel.Core.Primitives;
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
