using System.Text;
using System.Xml;
using Avalonia.Headless.XUnit;
using MapHelper.Desktop;
using SkiaSharp;
using Xunit;

namespace MapHelper.UiTests;

public sealed class IconTests
{
    private const string Square = "<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 32 32'><rect width='32' height='32' fill='currentColor'/></svg>";
    private static SKBitmap Render(string svg, string color = "#00FF00", int width = 100, int height = 100)
    {
        using var picture = SvgIcon.Load(svg, color);
        var bitmap = new SKBitmap(width, height);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Transparent);
        canvas.DrawPicture(picture);
        return bitmap;
    }

    [Theory]
    [InlineData("Aircraft")]
    [InlineData("Ground")]
    [InlineData("Objective")]
    [InlineData("Player")]
    [InlineData("Ship")]
    public void EveryBundledIconRendersVisiblePixelsAndUsesTheUnitColor(string name)
    {
        var xml = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Assets", "icons", name + ".svg"));
        using var bitmap = Render(xml, width: 32, height: 32);
        Assert.Contains(bitmap.Pixels, pixel => pixel.Alpha > 200 && pixel.Green > 200 && pixel.Red < 20);
        Assert.Contains(bitmap.Pixels, pixel => pixel.Alpha == 0);
    }

    [Theory]
    [InlineData("xMidYMid meet", false, false)]
    [InlineData("xMinYMin meet", true, false)]
    [InlineData("none", true, true)]
    [InlineData("xMidYMid slice", true, true)]
    public void ViewBoxRespectsNonzeroOriginAndAspectRatio(string ratio, bool topVisible, bool bottomVisible)
    {
        var xml = $"<svg width='100' height='100' viewBox='10 20 20 10' preserveAspectRatio='{ratio}'><rect x='10' y='20' width='20' height='10' fill='currentColor'/></svg>";
        using var bitmap = Render(xml);
        Assert.Equal(SKColors.Lime, bitmap.GetPixel(50, 40));
        Assert.Equal(topVisible, bitmap.GetPixel(50, 10).Alpha > 200);
        Assert.Equal(bottomVisible, bitmap.GetPixel(50, 90).Alpha > 200);
    }

    [Fact]
    public void NestedTransformsApplyInSvgOrderAndStylesInherit()
    {
        using var bitmap = Render("<svg width='100' height='100'><g style='fill: rgb(255, 0, 0)' transform='translate(20 30) scale(2)'><rect width='10' height='10'/></g></svg>");
        Assert.Equal(SKColors.Red, bitmap.GetPixel(30, 40));
        Assert.Equal(0, bitmap.GetPixel(10, 10).Alpha);
        Assert.Equal(0, bitmap.GetPixel(50, 70).Alpha);
    }

    [Theory]
    [InlineData("0 0 10 10", 0, 0)]
    [InlineData("10 20 10 10", 10, 20)]
    public void RootTransformIsOutsideTheViewBoxTransform(string viewBox, int x, int y)
    {
        var xml = $"<svg width='100' height='100' viewBox='{viewBox}' transform='translate(5 0)'><rect x='{x}' y='{y}' width='5' height='5' fill='red'/></svg>";
        using var bitmap = Render(xml);
        for (var row = 0; row < bitmap.Height; row++)
            for (var column = 0; column < bitmap.Width; column++)
            {
                var pixel = bitmap.GetPixel(column, row);
                if (column >= 5 && column < 55 && row < 50) Assert.Equal(SKColors.Red, pixel);
                else Assert.Equal(0, pixel.Alpha);
            }
    }

    [Theory]
    [InlineData("<rect x='50' y='20' width='0' height='60'/>")]
    [InlineData("<rect x='20' y='50' width='60' height='0' rx='10'/>")]
    [InlineData("<rect x='50' y='50' width='0' height='0'/>")]
    [InlineData("<ellipse cx='50' cy='50' rx='0' ry='30'/>")]
    [InlineData("<ellipse cx='50' cy='50' rx='30' ry='0'/>")]
    [InlineData("<circle cx='50' cy='50' r='0'/>")]
    public void ZeroSizeShapesPaintNothingEvenWithAStroke(string shape)
    {
        using var bitmap = Render("<svg width='100' height='100' fill='red' stroke='blue' stroke-width='10' stroke-linecap='round'>" + shape + "</svg>");
        Assert.Equal(0, bitmap.Pixels.Count(pixel => pixel.Alpha != 0));
    }

    [Theory]
    [InlineData("<rect x='NaN' width='0' height='10'/>")]
    [InlineData("<rect width='0' height='-1'/>")]
    [InlineData("<rect width='0' height='10' rx='-1'/>")]
    [InlineData("<ellipse cx='NaN' rx='0' ry='10'/>")]
    [InlineData("<ellipse rx='0' ry='-1'/>")]
    [InlineData("<circle cx='1e20' r='0'/>")]
    [InlineData("<rect width='0' height='10' stroke='url(https://example.invalid/image)'/>")]
    public void ZeroSizeShapesStillValidateAllAttributes(string shape) =>
        Assert.Throws<InvalidDataException>(() => IconRepository.ValidateSvg("<svg width='100' height='100'>" + shape + "</svg>"));

    [Fact]
    public void GroupOpacityCompositesOverlappingShapesOnce()
    {
        using var bitmap = Render("<svg width='100' height='100'><g opacity='0.5' fill='red'><rect width='20' height='20'/><rect x='10' width='20' height='20'/></g></svg>");
        Assert.InRange(bitmap.GetPixel(5, 5).Alpha, (byte)127, (byte)128);
        Assert.Equal(bitmap.GetPixel(5, 5), bitmap.GetPixel(15, 5));
    }

    [Fact]
    public void EvenOddFillAndCssRgbaOrderArePreserved()
    {
        using var bitmap = Render("<svg width='100' height='100'><path d='M0 0H80V80H0Z M20 20H60V60H20Z' fill='#FF000080' fill-rule='evenodd'/></svg>");
        Assert.Equal(0, bitmap.GetPixel(40, 40).Alpha);
        var red = bitmap.GetPixel(10, 10);
        Assert.True(red.Red > 250 && red.Green == 0 && red.Blue == 0);
        Assert.InRange(red.Alpha, (byte)127, (byte)128);
    }

    [Theory]
    [InlineData("<linearGradient id='gradient'/>")]
    [InlineData("<defs/>")]
    [InlineData("<filter/>")]
    [InlineData("<mask/>")]
    [InlineData("<text>Label</text>")]
    [InlineData("<style>.icon { fill: red; }</style>")]
    [InlineData("<script>alert(1)</script>")]
    [InlineData("<foreignObject/>")]
    [InlineData("<image href='file:///private.png'/>")]
    [InlineData("<use href='#self' id='self'/>")]
    [InlineData("<path d='M0 0H10V10Z' onload='alert(1)'/>")]
    [InlineData("<path d='M0 0H10V10Z' fill='url(https://example.invalid/image)'/>")]
    [InlineData("<path d='M0 0H10V10Z' style='fill: u\\72l(https://example.invalid/image)'/>")]
    [InlineData("<path d='M0 0H10V10Z' style='filter: blur(2px)'/>")]
    [InlineData("<path d='M0 0H10V10Z' clip-path='url(#clip)'/>")]
    [InlineData("<path d='M0 0H10V10Z' class='icon'/>")]
    public void UnsupportedOrActiveSvgIsRejectedWithPngGuidance(string body)
    {
        var exception = Assert.Throws<InvalidDataException>(() => IconRepository.ValidateSvg("<svg viewBox='0 0 32 32'>" + body + "</svg>"));
        Assert.Contains("PNG", exception.Message);
    }

    [Fact]
    public void ExternalEntityAndStylesheetInstructionsAreRejected()
    {
        Assert.Throws<XmlException>(() => IconRepository.ValidateSvg("<!DOCTYPE svg [<!ENTITY secret SYSTEM 'file:///private.txt'>]><svg viewBox='0 0 32 32'><title>&secret;</title></svg>"));
        Assert.Throws<InvalidDataException>(() => IconRepository.ValidateSvg("<?xml-stylesheet href='https://example.invalid/style.css'?>" + Square));
    }

    [Theory]
    [InlineData("<svg/>")]
    [InlineData("<svg viewBox='0 0 -1 10'/>")]
    [InlineData("<svg viewBox='0 0 NaN 10'/>")]
    [InlineData("<svg viewBox='0 0 1e20 10'/>")]
    [InlineData("<svg width='1e6%' height='10' viewBox='0 0 1000000 10'/>")]
    [InlineData("<svg viewBox='0 0 32 32'><path d='M10'/></svg>")]
    [InlineData("<svg viewBox='0 0 32 32'><path d='M0 0H10V10Z' transform='rotate()'/></svg>")]
    [InlineData("<svg viewBox='0 0 32 32'><path d='M0 0H10V10Z' transform='scale(1e6) scale(1e6)'/></svg>")]
    [InlineData("<svg viewBox='0 0 32 32'><rect width='10%' height='10'/></svg>")]
    [InlineData("<svg viewBox='0 0 32 32'><polygon points='0 0 1'/></svg>")]
    public void MalformedOrUnboundedGeometryIsRejected(string xml) => Assert.Throws<InvalidDataException>(() => IconRepository.ValidateSvg(xml));

    [Fact]
    public void DocumentDepthElementCountAndSizeAreBounded()
    {
        var deep = "<svg viewBox='0 0 32 32'>" + string.Concat(Enumerable.Repeat("<g>", 40)) + string.Concat(Enumerable.Repeat("</g>", 40)) + "</svg>";
        var broad = "<svg viewBox='0 0 32 32'>" + string.Concat(Enumerable.Repeat("<circle r='1'/>", 2050)) + "</svg>";
        Assert.Throws<InvalidDataException>(() => IconRepository.ValidateSvg(deep));
        Assert.Throws<InvalidDataException>(() => IconRepository.ValidateSvg(broad));
        Assert.Throws<InvalidDataException>(() => IconRepository.ValidateSvg(new string(' ', 4_000_001)));
    }

    [AvaloniaFact]
    public async Task PortalStreamImportCopiesContentAndNeverOverwritesExistingFiles()
    {
        using var icons = new IconRepository(new());
        var name = "portal-" + Guid.NewGuid().ToString("N") + ".svg";
        using var first = new MemoryStream([.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(Square)]);
        using var second = new MemoryStream([.. Encoding.Unicode.GetPreamble(), .. Encoding.Unicode.GetBytes(Square)]);
        var imported = await icons.ImportAsync(first, name);
        var duplicate = await icons.ImportAsync(second, name);
        try
        {
            Assert.Equal(name, imported);
            Assert.NotEqual(imported, duplicate);
            Assert.True(first.CanRead);
            Assert.Equal(Square, File.ReadAllText(Path.Combine(AppPaths.Icons, imported)));
            Assert.Equal(Square, File.ReadAllText(Path.Combine(AppPaths.Icons, duplicate)));
            Assert.NotNull(icons.Load(imported, "#00FF00"));
        }
        finally
        {
            icons.Dispose();
            File.Delete(Path.Combine(AppPaths.Icons, imported));
            File.Delete(Path.Combine(AppPaths.Icons, duplicate));
        }
    }

    [AvaloniaFact]
    public async Task PngImportPreservesBytesAndRemainsLoadable()
    {
        using var bitmap = new SKBitmap(3, 2);
        bitmap.Erase(SKColors.Red);
        using var image = SKImage.FromBitmap(bitmap);
        using var png = image.Encode(SKEncodedImageFormat.Png, 100);
        var bytes = png.ToArray();
        using var source = new MemoryStream(bytes);
        using var icons = new IconRepository(new());
        var name = await icons.ImportAsync(source, "png-" + Guid.NewGuid().ToString("N") + ".png");
        try
        {
            Assert.Equal(bytes, File.ReadAllBytes(Path.Combine(AppPaths.Icons, name)));
            Assert.NotNull(icons.Load(name, "#00FF00"));
        }
        finally { icons.Dispose(); File.Delete(Path.Combine(AppPaths.Icons, name)); }
    }

    [AvaloniaFact]
    public async Task InvalidOversizedAndCancelledImportsDoNotCreateFiles()
    {
        using var icons = new IconRepository(new());
        var name = "rejected-" + Guid.NewGuid().ToString("N") + ".svg";
        using var unsupported = new MemoryStream(Encoding.UTF8.GetBytes("<svg viewBox='0 0 32 32'><filter/></svg>"));
        await Assert.ThrowsAsync<InvalidDataException>(() => icons.ImportAsync(unsupported, name));
        using var oversized = new MemoryStream(new byte[4_000_001]);
        await Assert.ThrowsAsync<InvalidDataException>(() => icons.ImportAsync(oversized, name));
        using var cancelled = new MemoryStream(Encoding.UTF8.GetBytes(Square));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => icons.ImportAsync(cancelled, name, new CancellationToken(true)));
        Assert.False(File.Exists(Path.Combine(AppPaths.Icons, name)));
    }

    [AvaloniaFact]
    public void UnsupportedSavedIconFallsBackAndLogsTheReason()
    {
        var name = "unsupported-" + Guid.NewGuid().ToString("N") + ".svg";
        File.WriteAllText(Path.Combine(AppPaths.Icons, name), "<svg viewBox='0 0 32 32'><linearGradient/></svg>");
        try
        {
            using var icons = new IconRepository(new());
            Assert.Null(icons.Load(name, "#00FF00"));
            var initialLog = File.ReadAllText(Path.Combine(AppPaths.Root, "app.log"));
            Assert.Null(icons.Load(name, "#00FF00"));
            Assert.Equal(initialLog, File.ReadAllText(Path.Combine(AppPaths.Root, "app.log")));
            var settings = new AppSettings();
            using var mapped = new IconRepository(settings);
            settings.IconOverrides["Fighter"] = name;
            Assert.NotNull(mapped.Get("Fighter", "Aircraft", "#00FF00"));
            var log = File.ReadAllText(Path.Combine(AppPaths.Root, "app.log"));
            Assert.Contains("Unsupported SVG element: linearGradient", log);
            Assert.Contains("PNG", log);
            File.WriteAllText(Path.Combine(AppPaths.Icons, name), Square);
            icons.Reload();
            Assert.NotNull(icons.Load(name, "#00FF00"));
        }
        finally { File.Delete(Path.Combine(AppPaths.Icons, name)); }
    }
}
