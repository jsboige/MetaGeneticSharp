using System.Drawing;
using GeneticSharp.Extensions.Mathematic.Functions;
using MetaGeneticSharp;

namespace MetaGeneticSharp.Extensions.Tests;

/// <summary>
/// Tests for the M1 additive sibling <see cref="BilinearHeightMapFunction"/> and its factory
/// <see cref="LandscapeMaps.CreateBilinearFunctionFromImage"/>. The keystone
/// (<see cref="OnGridLine_BilinearInterpolates_WhereVerbatimIdwStairSteps"/>) pins the exact
/// delta against jsboige's verbatim <see cref="ImageHeightMapFunction"/> (@ d05826fd): on a grid
/// line the original returns the nearest floored pixel (a stair-step) while bilinear returns the
/// 1-D linear blend of the two endpoints. Integer/corner samples must agree between the two
/// schemes (no regression of the shared contract), and the bilinear edge sampling must never
/// read out of bounds.
/// </summary>
[TestFixture]
public class BilinearHeightMapFunctionTests
{
    /// <summary>
    /// A horizontal brightness gradient (R = G = B = round(255 * x / (W-1))), constant along y.
    /// Grayscale conversion preserves a neutral gray, so the elevation field is a clean ramp in x.
    /// </summary>
    private static Bitmap GradientBitmap(int width = 3, int height = 3)
    {
        var bitmap = new Bitmap(width, height);
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int v = (int)System.Math.Round(255.0 * x / (width - 1));
                bitmap.SetPixel(x, y, Color.FromArgb(v, v, v));
            }
        }

        return bitmap;
    }

    [Test]
    public void CreateBilinearFunctionFromImage_RangesMatchImageDimensions()
    {
        using Bitmap source = GradientBitmap(16, 12);
        using BilinearHeightMapFunction function = LandscapeMaps.CreateBilinearFunctionFromImage(source, "grad");

        System.Collections.Generic.IList<(double min, double max)> ranges = function.Ranges(2);
        Assert.That(ranges, Has.Count.EqualTo(2));
        Assert.That(ranges[0], Is.EqualTo((0.0, 15.0))); // x in [0, W-1]
        Assert.That(ranges[1], Is.EqualTo((0.0, 11.0))); // y in [0, H-1]
        Assert.That(function.Name, Is.EqualTo("grad"));
    }

    [Test]
    public void CreateBilinearFunctionFromImage_DefaultsNameAndEvaluatesInByteRange()
    {
        using Bitmap source = GradientBitmap(16, 12);
        using BilinearHeightMapFunction function = LandscapeMaps.CreateBilinearFunctionFromImage(source);

        Assert.That(function.Name, Is.EqualTo("CustomImageBilinear"));
        double value = function.Function(new[] { 7.5, 6.0 });
        Assert.That(value, Is.InRange(0.0, 255.0));
    }

    [Test]
    public void IntegerCoordinates_AgreeWithVerbatimImageHeightMapFunction()
    {
        // At pixel-exact (integer) coordinates both schemes return the same stored pixel value:
        // bilinear has fx = fy = 0, and the original's interpolation guard (x > xDraw && y > yDraw)
        // is false. This proves bilinear does not regress the shared contract at grid nodes.
        using Bitmap source = GradientBitmap(5, 4);
        using BilinearHeightMapFunction bilinear = LandscapeMaps.CreateBilinearFunctionFromImage(source);
        using ImageHeightMapFunction verbatim = LandscapeMaps.CreateFunctionFromImage(source);

        for (int y = 0; y < 4; y++)
        {
            for (int x = 0; x < 5; x++)
            {
                double b = bilinear.Function(new double[] { x, y });
                double v = verbatim.Function(new double[] { x, y });
                Assert.That(b, Is.EqualTo(v).Within(1e-9), $"mismatch at integer ({x}, {y})");
            }
        }
    }

    [Test]
    public void OnGridLine_BilinearInterpolates_WhereVerbatimIdwStairSteps()
    {
        // KEYSTONE: sample exactly on the y = 0 grid line, halfway along x (0.5, 0.0).
        //  - Verbatim ImageHeightMapFunction: the guard (x > xDraw && y > yDraw) is FALSE because
        //    y == yDraw, so it returns the nearest floored pixel = b00 (a stair-step).
        //  - BilinearHeightMapFunction: continuous along the grid line = midpoint of the two
        //    x-endpoints (b00 + b10) / 2.
        using Bitmap source = GradientBitmap(3, 3);
        using BilinearHeightMapFunction bilinear = LandscapeMaps.CreateBilinearFunctionFromImage(source);
        using ImageHeightMapFunction verbatim = LandscapeMaps.CreateFunctionFromImage(source);

        // Endpoint pixel values via integer samples (both schemes agree there).
        double g00 = bilinear.Function(new[] { 0.0, 0.0 });
        double g10 = bilinear.Function(new[] { 1.0, 0.0 });
        Assert.That(g00, Is.Not.EqualTo(g10), "gradient endpoints must differ for the delta to be visible");

        double bilinearOnLine = bilinear.Function(new[] { 0.5, 0.0 });
        double verbatimOnLine = verbatim.Function(new[] { 0.5, 0.0 });

        Assert.That(bilinearOnLine, Is.EqualTo((g00 + g10) / 2.0).Within(1e-9),
            "bilinear must be the 1-D linear blend on the grid line");
        Assert.That(verbatimOnLine, Is.EqualTo(g00).Within(1e-9),
            "verbatim IDW falls back to the nearest floored pixel on the grid line");
        Assert.That(bilinearOnLine, Is.Not.EqualTo(verbatimOnLine).Within(1e-6),
            "the two interpolations must genuinely differ on the grid line (the M1 delta)");
    }

    [Test]
    public void CellCenter_BilinearEqualsAverageOfFourCorners()
    {
        using Bitmap source = GradientBitmap(3, 3);
        using BilinearHeightMapFunction bilinear = LandscapeMaps.CreateBilinearFunctionFromImage(source);

        double c00 = bilinear.Function(new[] { 0.0, 0.0 });
        double c10 = bilinear.Function(new[] { 1.0, 0.0 });
        double c01 = bilinear.Function(new[] { 0.0, 1.0 });
        double c11 = bilinear.Function(new[] { 1.0, 1.0 });

        double center = bilinear.Function(new[] { 0.5, 0.5 });
        Assert.That(center, Is.EqualTo((c00 + c10 + c01 + c11) / 4.0).Within(1e-9));
    }

    [Test]
    public void RightAndBottomEdges_SampleWithoutOutOfBounds()
    {
        // The +1 neighbour clamp must keep edge sampling in bounds (fraction is 0 there).
        using Bitmap source = GradientBitmap(4, 5);
        using BilinearHeightMapFunction function = LandscapeMaps.CreateBilinearFunctionFromImage(source);

        Assert.DoesNotThrow(() =>
        {
            _ = function.Function(new[] { 3.0, 2.5 });  // right edge x = W-1, y fractional
            _ = function.Function(new[] { 1.5, 4.0 });  // bottom edge y = H-1, x fractional
            _ = function.Function(new[] { 3.0, 4.0 });  // far corner (W-1, H-1)
        });
    }

    [Test]
    public void CoordinatesOutsideImage_Throw()
    {
        using Bitmap source = GradientBitmap(4, 4);
        using BilinearHeightMapFunction function = LandscapeMaps.CreateBilinearFunctionFromImage(source);

        Assert.Throws<System.ArgumentException>(() => function.Function(new[] { -0.1, 1.0 }));
        Assert.Throws<System.ArgumentException>(() => function.Function(new[] { 1.0, 3.5 }));
    }

    [Test]
    public void CreateBilinearFunctionFromImage_DoesNotDisposeCallerImage()
    {
        using Bitmap source = GradientBitmap();
        using (BilinearHeightMapFunction function = LandscapeMaps.CreateBilinearFunctionFromImage(source))
        {
            _ = function.Function(new[] { 1.0, 1.0 });
        }

        Assert.DoesNotThrow(() => source.GetPixel(0, 0));
        Assert.That(source.Width, Is.EqualTo(3));
    }

    [Test]
    public void CreateBilinearFunctionFromImage_NullImage_Throws()
    {
        Assert.Throws<System.ArgumentNullException>(() => LandscapeMaps.CreateBilinearFunctionFromImage(null!));
    }
}
