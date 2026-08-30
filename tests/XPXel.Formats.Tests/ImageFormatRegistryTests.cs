using XPXel.Core.Documents;
using XPXel.Core.Imaging;
using XPXel.Core.Primitives;
using XPXel.Formats.Png;
using XPXel.Formats.Project;

namespace XPXel.Formats.Tests;

/// <summary>
/// Tests for the registry that makes new file formats additive. If these hold, adding a
/// codec really is one class plus one registration.
/// </summary>
public sealed class ImageFormatRegistryTests
{
    private static ImageFormatRegistry CreateRegistry()
    {
        PngCodec png = new();
        XpxelProjectSerializer project = new();
        return new ImageFormatRegistry([project, png], [project, png]);
    }

    [Fact]
    public void ResolvesImportersByExtension()
    {
        ImageFormatRegistry registry = CreateRegistry();

        Assert.IsType<PngCodec>(registry.FindImporterByPath("sprite.png"));
        Assert.IsType<XpxelProjectSerializer>(registry.FindImporterByPath(@"C:\art\hero.xpxel"));
        Assert.Null(registry.FindImporterByPath("notes.txt"));
    }

    [Fact]
    public void ExtensionMatchingIsCaseInsensitive()
    {
        Assert.IsType<PngCodec>(CreateRegistry().FindImporterByPath("SPRITE.PNG"));
    }

    [Fact]
    public void ResolvesImportersByContentWhenTheExtensionIsWrong()
    {
        using MemoryStream stream = new();
        PngCodec.EncodeImage(stream, new PixelBuffer(2, 2));
        stream.Position = 0;

        // A PNG saved with a misleading name still opens correctly.
        IImageImporter? importer = CreateRegistry().ResolveImporter("actually-a-png.xpxel", stream);

        Assert.IsType<PngCodec>(importer);
    }

    [Fact]
    public void ContentSniffingRewindsTheStream()
    {
        using MemoryStream stream = new();
        PngCodec.EncodeImage(stream, new PixelBuffer(2, 2));
        stream.Position = 0;

        ImageFormatRegistry registry = CreateRegistry();
        registry.FindImporterByContent(stream);

        Assert.Equal(0, stream.Position);
    }

    [Fact]
    public void SniffingHandlesAFileShorterThanTheSniffWindow()
    {
        using MemoryStream stream = new([1, 2, 3]);

        Assert.Null(CreateRegistry().FindImporterByContent(stream));
    }

    [Fact]
    public void CapabilitiesDescribeWhatEachFormatPreserves()
    {
        Assert.False(PngCodec.Format.SupportsLayers);
        Assert.True(XpxelProjectSerializer.Format.SupportsLayers);
        Assert.True(XpxelProjectSerializer.Format.SupportsAnimation);
    }

    [Fact]
    public void RegistryExposesFormatsInRegistrationOrderForFileDialogs()
    {
        ImageFormatRegistry registry = CreateRegistry();

        Assert.Equal(
            [XpxelProjectSerializer.Format.DisplayName, PngCodec.Format.DisplayName],
            registry.Importers.Select(i => i.Descriptor.DisplayName));
    }

    [Fact]
    public void ResolvedImporterCanReadTheDocumentItClaims()
    {
        PixelDocument document = PixelDocument.CreateNew(new ImageSize(3, 3));

        using MemoryStream stream = new();
        new XpxelProjectSerializer().Write(stream, document, ExportOptions.Default);
        stream.Position = 0;

        ImageFormatRegistry registry = CreateRegistry();
        IImageImporter importer = registry.ResolveImporter("hero.xpxel", stream)!;
        PixelDocument reloaded = importer.Read(stream, "hero");

        Assert.Equal(new ImageSize(3, 3), reloaded.Size);
    }
}
