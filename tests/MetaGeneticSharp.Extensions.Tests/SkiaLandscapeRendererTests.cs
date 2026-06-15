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
}
