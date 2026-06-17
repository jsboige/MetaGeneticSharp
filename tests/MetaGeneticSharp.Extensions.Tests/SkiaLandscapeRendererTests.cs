using GeneticSharp.Extensions.Mathematic.Functions;
using GeneticSharp.Infrastructure.Framework.Images;
using MetaGeneticSharp;
using SkiaSharp;

namespace MetaGeneticSharp.Extensions.Tests;

/// <summary>
/// Tests for the H3 SkiaSharp backend (<see cref="SkiaLandscapeRenderer"/>): the heatmap PNG
/// export no longer requires the Windows-only GDI+ encoder, and a function landscape renders
/// end-to-end with no <see cref="System.Drawing"/> GDI+ surface at all. The keystone is
/// <see cref="RenderHeatmapPng_MarksMaximumBlackAtOptimum"/> (the verbatim "max = Black" marker
/// lands on the function's true optimum, proving the Skia path reproduces the original ramp), and
/// <see cref="EncodePng_RoundTripsThroughSkia"/> (the encoded bytes are a real PNG that Skia
/// re-decodes to the same dimensions and pixels).
/// </summary>
[TestFixture]
public class SkiaLandscapeRendererTests
{
    /// <summary>A paraboloid with a single maximum at (0, 0): f(x, y) = -(x^2 + y^2).</summary>
    private static double Paraboloid(double[] coords) => -(coords[0] * coords[0] + coords[1] * coords[1]);

    private static void AssertPngMagic(byte[] png)
    {
        Assert.That(png.Length, Is.GreaterThan(100));
        Assert.That(png[0], Is.EqualTo(0x89));
        Assert.That(png[1], Is.EqualTo(0x50)); // 'P'
        Assert.That(png[2], Is.EqualTo(0x4E)); // 'N'
        Assert.That(png[3], Is.EqualTo(0x47)); // 'G'
    }

    [Test]
    public void EncodePng_ProducesValidPngFromDirectBitmap()
    {
        using var bitmap = new DirectBitmap(24, 16);
        bitmap.SetPixel(3, 5, System.Drawing.Color.FromArgb(255, 200, 100, 50));

        byte[] png = SkiaLandscapeRenderer.EncodePng(bitmap);
        AssertPngMagic(png);
    }

    [Test]
    public void EncodePng_RoundTripsThroughSkia()
    {
        // A DirectBitmap pixel re-decodes through Skia with the same color (BGRA<->ARGB layout).
        using var bitmap = new DirectBitmap(8, 6);
        var colour = System.Drawing.Color.FromArgb(255, 10, 220, 130);
        for (int y = 0; y < bitmap.Height; y++)
        {
            for (int x = 0; x < bitmap.Width; x++)
            {
                bitmap.SetPixel(x, y, colour);
            }
        }

        byte[] png = SkiaLandscapeRenderer.EncodePng(bitmap);
        using SKBitmap decoded = SKBitmap.Decode(png);

        Assert.That(decoded.Width, Is.EqualTo(8));
        Assert.That(decoded.Height, Is.EqualTo(6));
        SKColor sample = decoded.GetPixel(4, 3);
        Assert.That(sample.Red, Is.EqualTo(10));
        Assert.That(sample.Green, Is.EqualTo(220));
        Assert.That(sample.Blue, Is.EqualTo(130));
    }

    [Test]
    public void RenderHeatmapPng_ProducesGraphicPngOfRequestedSize()
    {
        byte[] png = SkiaLandscapeRenderer.RenderHeatmapPng(
            Paraboloid, (-5.0, 5.0), (-5.0, 5.0), width: 120, height: 90);

        AssertPngMagic(png);
        using SKBitmap decoded = SKBitmap.Decode(png);
        Assert.That(decoded.Width, Is.EqualTo(120));
        Assert.That(decoded.Height, Is.EqualTo(90));
    }

    [Test]
    public void RenderHeatmapPng_MarksMaximumBlackAtOptimum()
    {
        // KEYSTONE: the verbatim "maximum = Black" marker lands on the paraboloid optimum (0, 0),
        // i.e. the center of the canvas. Proves the Skia path reproduces the original extrema math.
        const int width = 81, height = 81; // odd -> the center pixel maps exactly to (0, 0)
        byte[] png = SkiaLandscapeRenderer.RenderHeatmapPng(
            Paraboloid, (-4.0, 4.0), (-4.0, 4.0), width, height);

        using SKBitmap decoded = SKBitmap.Decode(png);
        SKColor center = decoded.GetPixel(width / 2, height / 2);
        Assert.That(center.Red, Is.EqualTo(0));
        Assert.That(center.Green, Is.EqualTo(0));
        Assert.That(center.Blue, Is.EqualTo(0));
    }

    [Test]
    public void RenderHeatmapPng_PlotsBestMarkerInAqua()
    {
        // The best individual at the optimum is drawn as an Aqua (0, 255, 255) diamond.
        const int width = 81, height = 81;
        var best = new[] { 0.0, 0.0 };
        byte[] png = SkiaLandscapeRenderer.RenderHeatmapPng(
            Paraboloid, (-4.0, 4.0), (-4.0, 4.0), width, height,
            population: new[] { new[] { 2.0, 2.0 }, new[] { -3.0, 1.0 } }, best: best);

        using SKBitmap decoded = SKBitmap.Decode(png);
        // Diamond arm two pixels from center stays Aqua (the very center is the Black max marker
        // painted before the population, then overdrawn by the Aqua diamond body — check an arm).
        SKColor arm = decoded.GetPixel(width / 2 + 2, height / 2);
        Assert.That(arm.Red, Is.EqualTo(0));
        Assert.That(arm.Green, Is.EqualTo(255));
        Assert.That(arm.Blue, Is.EqualTo(255));
    }

    [Test]
    public void RenderHeatmapPng_FromKnownFunction_RendersImageHeightMap()
    {
        // The IKnownFunction overload renders an ImageHeightMapFunction (CustomImage source).
        using var source = new DirectBitmap(20, 14);
        for (int y = 0; y < source.Height; y++)
        {
            for (int x = 0; x < source.Width; x++)
            {
                int v = (int)(255.0 * x / (source.Width - 1));
                source.SetPixel(x, y, System.Drawing.Color.FromArgb(255, v, v, v));
            }
        }

        using ImageHeightMapFunction function = LandscapeMaps.CreateFunctionFromImage(source.Bitmap, "grad");
        byte[] png = SkiaLandscapeRenderer.RenderHeatmapPng(function, width: 100, height: 70);

        AssertPngMagic(png);
        using SKBitmap decoded = SKBitmap.Decode(png);
        Assert.That(decoded.Width, Is.EqualTo(100));
        Assert.That(decoded.Height, Is.EqualTo(70));
    }

    [Test]
    public void RenderHeatmapPng_TooSmallCanvas_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => SkiaLandscapeRenderer.RenderHeatmapPng(Paraboloid, (-1.0, 1.0), (-1.0, 1.0), width: 1, height: 90));
    }

    [Test]
    public void EncodePng_NullBitmap_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => SkiaLandscapeRenderer.EncodePng(null!));
    }

    [Test]
    public void RenderHeatmapPng_NullFunction_Throws()
    {
        Assert.Throws<ArgumentNullException>(
            () => SkiaLandscapeRenderer.RenderHeatmapPng((Func<double[], double>)null!, (0, 1), (0, 1)));
    }

    // ---- Colored-islands overlay (per-individual marker colors) ----

    /// <summary>
    /// A flat-zero fitness makes the whole canvas a single ramp color, so the only non-ramp pixels
    /// are the population diamonds — convenient for asserting marker colors in isolation.
    /// </summary>
    private static double FlatZero(double[] coords) => 0.0;

    private static SKColor PixelAt(byte[] png, int x, int y)
    {
        using SKBitmap decoded = SKBitmap.Decode(png);
        return decoded.GetPixel(x, y);
    }

    [Test]
    public void RenderHeatmapPng_NullIndividualColors_FallsBackToSingleColor()
    {
        // NO-PENDULUM: a null palette renders byte-identical to the no-colors overload — every
        // individual marker is the verbatim BlueViolet (138, 43, 226), nothing changes.
        const int width = 41, height = 41;
        var population = new[] { new[] { 2.0, 0.0 }, new[] { -2.0, 0.0 } };

        byte[] withoutColors = SkiaLandscapeRenderer.RenderHeatmapPng(
            FlatZero, (-4.0, 4.0), (-4.0, 4.0), width, height, population, best: null);
        byte[] withNullColors = SkiaLandscapeRenderer.RenderHeatmapPng(
            FlatZero, (-4.0, 4.0), (-4.0, 4.0), width, height, population, best: null, individualColors: null);

        Assert.That(withNullColors, Is.EqualTo(withoutColors));
    }

    [Test]
    public void RenderHeatmapPng_PerIndividualColors_TintsEachMarker()
    {
        // Each individual takes its own palette color: island 0 = Red, island 1 = Lime.
        const int width = 81, height = 81;
        var population = new[] { new[] { 2.0, 0.0 }, new[] { -2.0, 0.0 } };
        var colors = new List<System.Drawing.Color>
        {
            System.Drawing.Color.Red,    // (255, 0, 0)
            System.Drawing.Color.Lime,   // (0, 255, 0)
        };

        byte[] png = SkiaLandscapeRenderer.RenderHeatmapPng(
            FlatZero, (-4.0, 4.0), (-4.0, 4.0), width, height, population, best: null, individualColors: colors);

        // Island 0 at x=2.0 -> pixel (width * 0.75, height/2); its diamond center is Red.
        int px0 = (int)Math.Round(0.75 * (width - 1));
        SKColor marker0 = PixelAt(png, px0, height / 2);
        Assert.That(marker0.Red, Is.EqualTo(255));
        Assert.That(marker0.Green, Is.EqualTo(0));
        Assert.That(marker0.Blue, Is.EqualTo(0));

        // Island 1 at x=-2.0 -> pixel (width * 0.25, height/2); its diamond center is Lime.
        int px1 = (int)Math.Round(0.25 * (width - 1));
        SKColor marker1 = PixelAt(png, px1, height / 2);
        Assert.That(marker1.Red, Is.EqualTo(0));
        Assert.That(marker1.Green, Is.EqualTo(255));
        Assert.That(marker1.Blue, Is.EqualTo(0));
    }

    [Test]
    public void RenderHeatmapPng_ColorCountMismatch_FallsBackToSingleColor()
    {
        // A palette whose count does not match the population falls back to single-color (no crash,
        // no partial coloring): both markers are the verbatim BlueViolet.
        const int width = 81, height = 81;
        var population = new[] { new[] { 2.0, 0.0 }, new[] { -2.0, 0.0 } };
        var mismatched = new List<System.Drawing.Color> { System.Drawing.Color.Red }; // 1 != 2

        byte[] png = SkiaLandscapeRenderer.RenderHeatmapPng(
            FlatZero, (-4.0, 4.0), (-4.0, 4.0), width, height, population, best: null, individualColors: mismatched);

        int px0 = (int)Math.Round(0.75 * (width - 1));
        SKColor marker0 = PixelAt(png, px0, height / 2);
        // BlueViolet = (138, 43, 226).
        Assert.That(marker0.Red, Is.EqualTo(138));
        Assert.That(marker0.Green, Is.EqualTo(43));
        Assert.That(marker0.Blue, Is.EqualTo(226));
    }

    [Test]
    public void RenderHeatmapPng_PerIndividualColors_BestStaysAqua()
    {
        // Even with per-individual colors, the best individual is always the verbatim Aqua marker.
        const int width = 81, height = 81;
        var best = new[] { 0.0, 0.0 };
        var colors = new List<System.Drawing.Color> { System.Drawing.Color.Red };

        byte[] png = SkiaLandscapeRenderer.RenderHeatmapPng(
            Paraboloid, (-4.0, 4.0), (-4.0, 4.0), width, height,
            population: new[] { new[] { 2.0, 2.0 } }, best: best, individualColors: colors);

        // Aqua arm two pixels right of center (center is the Black max, overdrawn by the Aqua body).
        SKColor arm = PixelAt(png, width / 2 + 2, height / 2);
        Assert.That(arm.Red, Is.EqualTo(0));
        Assert.That(arm.Green, Is.EqualTo(255));
        Assert.That(arm.Blue, Is.EqualTo(255));
    }
}
