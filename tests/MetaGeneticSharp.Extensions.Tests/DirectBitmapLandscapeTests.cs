using System.Drawing;
using GeneticSharp.Extensions.Mathematic.Functions;
using GeneticSharp.Infrastructure.Framework.Images;
using MetaGeneticSharp;

namespace MetaGeneticSharp.Extensions.Tests;

/// <summary>
/// Tests for the GDI+-free image sources of the landscapes: <see cref="LandscapeMaps.LoadBitmap"/>
/// and <see cref="LandscapeMaps.DecodeBitmap"/> (SkiaSharp decode), and the
/// <see cref="DirectBitmap"/> overloads <see cref="LandscapeMaps.CreateFunctionFromBitmap"/> /
/// <see cref="LandscapeMaps.CreateBilinearFunctionFromBitmap"/>. They run on Linux and macOS as
/// well as Windows. The <c>Gdi_*</c> tests are the keystone: on Windows they check that the new
/// path yields, pixel for pixel, the grayscale field of the original GDI+ path
/// (<see cref="LandscapeMaps.Load"/> then jsboige's <c>MakeGrayscaleImage()</c> @ d05826fd), on the
/// four shipped maps and on a colored image.
/// </summary>
[TestFixture]
public class DirectBitmapLandscapeTests
{
    private const string GdiReason = "GDI+ (System.Drawing.Common) is Windows-only since .NET 7";

    /// <summary>A horizontal gray ramp, R = G = B = round(255 * x / (W-1)), constant along y.</summary>
    private static DirectBitmap GradientBitmap(int width, int height)
    {
        var bitmap = new DirectBitmap(width, height);
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int v = (int)Math.Round(255.0 * x / (width - 1));
                bitmap.SetPixel(x, y, Color.FromArgb(v, v, v));
            }
        }

        return bitmap;
    }

    [TestCase(KnownHeightMap.EverestMount, 2560, 1383)]
    [TestCase(KnownHeightMap.NepalBhoutan, 2560, 1383)]
    [TestCase(KnownHeightMap.TibetanPlateau, 2560, 1383)]
    [TestCase(KnownHeightMap.World, 2560, 1440)]
    public void LoadBitmap_EachOriginalMap_DecodesOpaqueBitmapOfExpectedSize(KnownHeightMap map, int width, int height)
    {
        using DirectBitmap bitmap = LandscapeMaps.LoadBitmap(map);

        Assert.That(bitmap.Width, Is.EqualTo(width));
        Assert.That(bitmap.Height, Is.EqualTo(height));
        Assert.That(bitmap.Bits, Has.Length.EqualTo(width * height));
        Assert.That(bitmap.GetPixel(width / 2, height / 2).A, Is.EqualTo(255));
    }

    [Test]
    public void DecodeBitmap_RoundTripsSkiaEncodedPng()
    {
        using DirectBitmap source = GradientBitmap(9, 4);
        using var stream = new MemoryStream(SkiaLandscapeRenderer.EncodePng(source));

        using DirectBitmap decoded = LandscapeMaps.DecodeBitmap(stream);

        Assert.That(decoded.Width, Is.EqualTo(9));
        Assert.That(decoded.Height, Is.EqualTo(4));
        Assert.That(decoded.Bits, Is.EqualTo(source.Bits).AsCollection);
    }

    [Test]
    public void DecodeBitmap_NotAnImage_ThrowsArgumentException()
    {
        using var stream = new MemoryStream(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 });
        Assert.Throws<ArgumentException>(() => LandscapeMaps.DecodeBitmap(stream));
    }

    [Test]
    public void CreateFunctionFromBitmap_RangesNameAndValues()
    {
        using DirectBitmap source = GradientBitmap(16, 12);
        using ImageHeightMapFunction function = LandscapeMaps.CreateFunctionFromBitmap(source, "grad");

        IList<(double min, double max)> ranges = function.Ranges(2);
        Assert.That(ranges[0], Is.EqualTo((0.0, 15.0)));
        Assert.That(ranges[1], Is.EqualTo((0.0, 11.0)));
        Assert.That(function.Name, Is.EqualTo("grad"));
        Assert.That(function.Function(new[] { 0.0, 3.0 }), Is.EqualTo(0.0));
        Assert.That(function.Function(new[] { 15.0, 3.0 }), Is.EqualTo(255.0));
    }

    [Test]
    public void CreateFunctionFromBitmap_DefaultName_AndCallerKeepsOwnership()
    {
        using DirectBitmap source = GradientBitmap(4, 3);
        int[] before = (int[])source.Bits.Clone();
        using (ImageHeightMapFunction function = LandscapeMaps.CreateFunctionFromBitmap(source))
        {
            Assert.That(function.Name, Is.EqualTo("CustomImage"));
            _ = function.Function(new[] { 1.5, 1.5 });
        }

        // The setter copied to grayscale; disposing the function leaves the caller's bitmap intact.
        Assert.That(source.Bits, Is.EqualTo(before).AsCollection);
        Assert.DoesNotThrow(() => source.GetPixel(3, 2));
    }

    [Test]
    public void CreateFunctionFromBitmap_RendersGraphicHeatmapPng()
    {
        using DirectBitmap source = GradientBitmap(32, 24);
        using ImageHeightMapFunction function = LandscapeMaps.CreateFunctionFromBitmap(source);
        using LandscapeHeatmap heatmap = LandscapeRenderer.RenderHeatmap(function, width: 120, height: 90);

        byte[] png = heatmap.ToPng();
        Assert.That(png[0], Is.EqualTo(0x89));
        Assert.That(png[1], Is.EqualTo(0x50));
        Assert.That(heatmap.Width, Is.EqualTo(120));
        Assert.That(heatmap.Height, Is.EqualTo(90));
    }

    [Test]
    public void Bilinear_FromBitmap_AgreesWithIdwAtNodes_AndBlendsOnGridLines()
    {
        // Same delta as BilinearHeightMapFunctionTests' keystone, over a DirectBitmap source.
        using DirectBitmap source = GradientBitmap(3, 3);
        using BilinearHeightMapFunction bilinear = LandscapeMaps.CreateBilinearFunctionFromBitmap(source);
        using ImageHeightMapFunction verbatim = LandscapeMaps.CreateFunctionFromBitmap(source);

        Assert.That(bilinear.Name, Is.EqualTo("CustomImageBilinear"));
        for (int y = 0; y < 3; y++)
        {
            for (int x = 0; x < 3; x++)
            {
                Assert.That(bilinear.Function(new double[] { x, y }),
                    Is.EqualTo(verbatim.Function(new double[] { x, y })).Within(1e-9), $"node ({x}, {y})");
            }
        }

        double g00 = bilinear.Function(new[] { 0.0, 0.0 });
        double g10 = bilinear.Function(new[] { 1.0, 0.0 });
        Assert.That(bilinear.Function(new[] { 0.5, 0.0 }), Is.EqualTo((g00 + g10) / 2.0).Within(1e-9));
        Assert.That(verbatim.Function(new[] { 0.5, 0.0 }), Is.EqualTo(g00).Within(1e-9));
    }

    [Test]
    public void CreateFunctionFromBitmap_NullBitmap_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => LandscapeMaps.CreateFunctionFromBitmap(null!));
        Assert.Throws<ArgumentNullException>(() => LandscapeMaps.CreateBilinearFunctionFromBitmap(null!));
    }

    [TestCase(KnownHeightMap.EverestMount)]
    [TestCase(KnownHeightMap.NepalBhoutan)]
    [TestCase(KnownHeightMap.TibetanPlateau)]
    [TestCase(KnownHeightMap.World)]
    [Platform("Win", Reason = GdiReason)]
    public void Gdi_KnownHeightMap_NewPathMatchesGdiPath_PixelForPixel(KnownHeightMap map)
    {
        // KEYSTONE: CreateFunction(map) now decodes with SkiaSharp and converts in managed code.
        // Its grayscale field must be the one the GDI+ path produced before the change.
        using Image image = LandscapeMaps.Load(map);
        using DirectBitmap gdi = image.MakeGrayscaleImage();
        using DirectBitmap decoded = LandscapeMaps.LoadBitmap(map);
        using DirectBitmap managed = decoded.MakeGrayscaleImage();

        Assert.That(managed.Width, Is.EqualTo(gdi.Width));
        Assert.That(managed.Height, Is.EqualTo(gdi.Height));
        int mismatches = 0, first = -1;
        for (int i = 0; i < gdi.Bits.Length; i++)
        {
            if (managed.Bits[i] != gdi.Bits[i])
            {
                if (first < 0) first = i;
                mismatches++;
            }
        }

        Assert.That(mismatches, Is.Zero,
            first < 0 ? "" : $"first mismatch at pixel {first}: GDI+ {gdi.Bits[first]:X8}, new path {managed.Bits[first]:X8}");
    }

    [Test]
    [Platform("Win", Reason = GdiReason)]
    public void Gdi_ColorImage_FromBitmapMatchesFromImage()
    {
        // A colored opaque source through both public entry points gives the same field.
        using var source = new DirectBitmap(40, 30);
        for (int y = 0; y < 30; y++)
        {
            for (int x = 0; x < 40; x++)
            {
                source.SetPixel(x, y, Color.FromArgb(255, (x * 37) % 256, (y * 53) % 256, ((x + y) * 29) % 256));
            }
        }

        using ImageHeightMapFunction fromImage = LandscapeMaps.CreateFunctionFromImage(source.Bitmap);
        using ImageHeightMapFunction fromBitmap = LandscapeMaps.CreateFunctionFromBitmap(source);

        for (double y = 0; y <= 29; y += 0.75)
        {
            for (double x = 0; x <= 39; x += 0.75)
            {
                Assert.That(fromBitmap.Function(new[] { x, y }), Is.EqualTo(fromImage.Function(new[] { x, y })),
                    $"({x}, {y})");
            }
        }
    }
}
