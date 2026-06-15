using System.Drawing;
using GeneticSharp.Extensions.Mathematic.Functions;
using GeneticSharp.Infrastructure.Framework.Images;
using MetaGeneticSharp;

namespace MetaGeneticSharp.Extensions.Tests;

/// <summary>
/// Tests for the LandscapeExplorer revival: the four original height maps (recovered
/// byte-exact from jsboige's GeneticSharp Metaheuristics sample @ d05826fd) load as embedded
/// resources, the verbatim <see cref="ImageHeightMapFunction"/> evaluates them as fitness
/// landscapes, and <see cref="LandscapeRenderer"/> produces a GRAPHIC heatmap (image/png),
/// not ASCII. The keystone is <see cref="RenderHeatmap_ProducesPngWithValidSignature"/>:
/// the rendered bitmap exports as a real PNG, proving the graphic-output requirement.
/// </summary>
[TestFixture]
public class LandscapeRendererTests
{
    // =========================================================================
    // The four original maps load from embedded resources.
    // =========================================================================
    [TestCase(KnownHeightMap.EverestMount)]
    [TestCase(KnownHeightMap.NepalBhoutan)]
    [TestCase(KnownHeightMap.TibetanPlateau)]
    [TestCase(KnownHeightMap.World)]
    public void Load_EachOriginalMap_ReturnsNonEmptyImage(KnownHeightMap map)
    {
        using Image image = LandscapeMaps.Load(map);
        Assert.That(image.Width, Is.GreaterThan(0));
        Assert.That(image.Height, Is.GreaterThan(0));
    }

    [Test]
    public void Load_AllFourMaps_AreEmbeddedUnderExpectedNames()
    {
        string[] names = typeof(LandscapeMaps).Assembly.GetManifestResourceNames();
        Assert.That(names, Contains.Item("MetaGeneticSharp.LandscapeMaps.EverestMount.png"));
        Assert.That(names, Contains.Item("MetaGeneticSharp.LandscapeMaps.NepalBhoutan.png"));
        Assert.That(names, Contains.Item("MetaGeneticSharp.LandscapeMaps.TibetanPlateau.png"));
        Assert.That(names, Contains.Item("MetaGeneticSharp.LandscapeMaps.World.png"));
    }

    // =========================================================================
    // The verbatim ImageHeightMapFunction reads each map as an elevation field.
    // =========================================================================
    [Test]
    public void CreateFunction_EverestMount_EvaluatesPixelBrightnessInByteRange()
    {
        using ImageHeightMapFunction function = LandscapeMaps.CreateFunction(KnownHeightMap.EverestMount);
        System.Collections.Generic.IList<(double min, double max)> ranges = function.Ranges(2);

        Assert.That(ranges, Has.Count.EqualTo(2));
        // The function maps the image: x in [0, W-1], y in [0, H-1] (verbatim Ranges).
        Assert.That(ranges[0].min, Is.EqualTo(0.0));
        Assert.That(ranges[1].min, Is.EqualTo(0.0));

        // A coordinate at the grayscale image is a pixel intensity (R channel), 0..255.
        double value = function.Function(new[] { ranges[0].max / 2.0, ranges[1].max / 2.0 });
        Assert.That(value, Is.InRange(0.0, 255.0));
    }

    [Test]
    public void HeightMapFunction_OutsideImageRange_Throws()
    {
        using ImageHeightMapFunction function = LandscapeMaps.CreateFunction(KnownHeightMap.World);
        Assert.Throws<ArgumentException>(() => function.Function(new[] { -1.0, 0.0 }));
    }

    // =========================================================================
    // M2: CreateFunction(KnownHeightMap) disposes the loaded source Image after the
    // TargetImage setter has grayscale-copied it (no GDI+ handle leak). These guard that
    // the disposal leaves a fully self-contained grayscale copy: if the source were still
    // a live dependency of the returned function, evaluating it (or re-rendering) after the
    // internal dispose would throw ObjectDisposedException.
    // =========================================================================
    [TestCase(KnownHeightMap.EverestMount)]
    [TestCase(KnownHeightMap.NepalBhoutan)]
    [TestCase(KnownHeightMap.TibetanPlateau)]
    [TestCase(KnownHeightMap.World)]
    public void CreateFunction_AfterSourceDisposal_RetainedGrayscaleCopyStillEvaluates(KnownHeightMap map)
    {
        // The loaded source Image is disposed inside CreateFunction (M2); the returned function
        // must evaluate entirely from its own retained grayscale DirectBitmap.
        using ImageHeightMapFunction function = LandscapeMaps.CreateFunction(map);
        System.Collections.Generic.IList<(double min, double max)> ranges = function.Ranges(2);

        Assert.DoesNotThrow(() =>
        {
            foreach (double fx in new[] { 0.0, 0.25, 0.5, 0.75, 1.0 })
            {
                foreach (double fy in new[] { 0.0, 0.5, 1.0 })
                {
                    double value = function.Function(new[] { ranges[0].max * fx, ranges[1].max * fy });
                    Assert.That(value, Is.InRange(0.0, 255.0), $"({fx}, {fy}) sample out of byte range");
                }
            }
        });
    }

    [Test]
    public void CreateFunction_AfterSourceDisposal_RendersHeatmapPng()
    {
        // End-to-end guard: build (source disposed inside), then render a graphic heatmap from
        // the retained grayscale copy. A leaked-then-disposed dependency would surface here.
        using ImageHeightMapFunction function = LandscapeMaps.CreateFunction(KnownHeightMap.TibetanPlateau);
        using LandscapeHeatmap heatmap = LandscapeRenderer.RenderHeatmap(function, width: 80, height: 60);

        byte[] png = heatmap.ToPng();
        Assert.That(png.Length, Is.GreaterThan(100));
        Assert.That((png[0], png[1], png[2], png[3]), Is.EqualTo(((byte)0x89, (byte)0x50, (byte)0x4E, (byte)0x47)));
    }

    // =========================================================================
    // KEYSTONE: a real PNG heatmap, not ASCII.
    // =========================================================================
    [Test]
    public void RenderHeatmap_ProducesPngWithValidSignature()
    {
        using ImageHeightMapFunction function = LandscapeMaps.CreateFunction(KnownHeightMap.EverestMount);
        using LandscapeHeatmap heatmap = LandscapeRenderer.RenderHeatmap(function, width: 160, height: 120);

        byte[] png = heatmap.ToPng();
        // PNG magic number: 89 50 4E 47 0D 0A 1A 0A.
        Assert.That(png.Length, Is.GreaterThan(100), "a 160x120 heatmap PNG is non-trivial");
        Assert.That(png[0], Is.EqualTo(0x89));
        Assert.That(png[1], Is.EqualTo(0x50)); // 'P'
        Assert.That(png[2], Is.EqualTo(0x4E)); // 'N'
        Assert.That(png[3], Is.EqualTo(0x47)); // 'G'
        Assert.That(heatmap.Width, Is.EqualTo(160));
        Assert.That(heatmap.Height, Is.EqualTo(120));
    }

    [Test]
    public void RenderHeatmap_AnalyticFunction_RendersAndMarksExtrema()
    {
        // A centered paraboloid maximized at the origin: f(x, y) = -(x^2 + y^2).
        static double Paraboloid(double[] c) => -(c[0] * c[0] + c[1] * c[1]);
        using LandscapeHeatmap heatmap = LandscapeRenderer.RenderHeatmap(
            Paraboloid, xRange: (-5.0, 5.0), yRange: (-5.0, 5.0), width: 120, height: 90);

        // The extrema markers are present: minimum White, maximum Black (verbatim BuildBitmap).
        bool hasWhite = false, hasBlack = false;
        for (int y = 0; y < heatmap.Height && !(hasWhite && hasBlack); y++)
        {
            for (int x = 0; x < heatmap.Width; x++)
            {
                int argb = heatmap.Bitmap.GetPixel(x, y).ToArgb();
                if (argb == Color.White.ToArgb()) hasWhite = true;
                if (argb == Color.Black.ToArgb()) hasBlack = true;
            }
        }

        Assert.That(hasWhite, Is.True, "global minimum marked White");
        Assert.That(hasBlack, Is.True, "global maximum marked Black");
    }

    [Test]
    public void Plot_BestIndividual_PaintsAquaMarker()
    {
        static double Paraboloid(double[] c) => -(c[0] * c[0] + c[1] * c[1]);
        using LandscapeHeatmap heatmap = LandscapeRenderer.RenderHeatmap(
            Paraboloid, xRange: (-5.0, 5.0), yRange: (-5.0, 5.0), width: 120, height: 90);

        double[] best = { 0.0, 0.0 };
        heatmap.Plot(new[] { new[] { 3.0, -3.0 } }, best);

        (int px, int py) = heatmap.ToPixel(best[0], best[1]);
        Assert.That(heatmap.Bitmap.GetPixel(px, py).ToArgb(), Is.EqualTo(LandscapeMaps.BestColor.ToArgb()));
    }

    // =========================================================================
    // Verbatim marker / color formulas (controller @ d05826fd).
    // =========================================================================
    [Test]
    public void DrawFunctionPoint_PaintsFivePointDiamond_NotCorners()
    {
        var bitmap = new DirectBitmap(11, 11);
        LandscapeRenderer.DrawFunctionPoint(bitmap, 5, 5, Color.Red);

        // Center and the four unit-neighbours are inside the |i|+|j| < 4 diamond.
        Assert.That(bitmap.GetPixel(5, 5).ToArgb(), Is.EqualTo(Color.Red.ToArgb()));
        Assert.That(bitmap.GetPixel(6, 5).ToArgb(), Is.EqualTo(Color.Red.ToArgb()));
        Assert.That(bitmap.GetPixel(5, 7).ToArgb(), Is.EqualTo(Color.Red.ToArgb()));
        // The (2, 2) corner has |2|+|2| = 4, which is NOT < 4: left blank.
        Assert.That(bitmap.GetPixel(7, 7).ToArgb(), Is.Not.EqualTo(Color.Red.ToArgb()));
        bitmap.Dispose();
    }

    [Test]
    public void GetColor_AtMaximum_IsRed_AtMinimum_IsCyan()
    {
        // ratio = 0.5 - (fValue - fMin) / (2 (fMax - fMin)): at fMax -> hue 0 (red),
        // at fMin -> hue 0.5 (cyan). Verbatim color ramp.
        Color atMax = LandscapeRenderer.GetColor(10.0, 0.0, 10.0);
        Color atMin = LandscapeRenderer.GetColor(0.0, 0.0, 10.0);

        Assert.That((atMax.R, atMax.G, atMax.B), Is.EqualTo(((byte)255, (byte)0, (byte)0)));
        Assert.That((atMin.R, atMin.G, atMin.B), Is.EqualTo(((byte)0, (byte)255, (byte)255)));
    }

    [Test]
    public void GetColor_DegenerateRange_DoesNotDivideByZero()
    {
        // fMin == fMax: ratio falls back to 0.5 (cyan) rather than dividing by zero.
        Color color = LandscapeRenderer.GetColor(5.0, 5.0, 5.0);
        Assert.That((color.R, color.G, color.B), Is.EqualTo(((byte)0, (byte)255, (byte)255)));
    }
}
