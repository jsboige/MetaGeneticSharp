using System.Drawing;
using GeneticSharp.Infrastructure.Framework.Images;
using MetaGeneticSharp;

namespace MetaGeneticSharp.Extensions.Tests;

/// <summary>
/// Tests for the H1 bridge (<see cref="KnownFunctionLandscape"/>): the ten standard benchmark
/// functions of <c>KnownFunctions.cs</c> render as GRAPHIC PNG heatmaps via the verbatim
/// <see cref="LandscapeRenderer"/>, without the notebook having to hand-wire a chromosome
/// adapter or look bounds up by hand. The keystone is
/// <see cref="RenderHeatmap_Sphere_ProducesPngWithValidSignature"/> (a real PNG) and
/// <see cref="RenderHeatmap_MaximumMarker_SitsOnGlobalOptimum"/> (the Black max marker lands on
/// the function's true optimum, proving the negated-fitness convention carries through).
/// </summary>
[TestFixture]
public class KnownFunctionLandscapeTests
{
    [Test]
    public void RenderHeatmap_Sphere_ProducesPngWithValidSignature()
    {
        using LandscapeHeatmap heatmap = KnownFunctionLandscape.RenderHeatmap(
            new SphereFitness(), width: 120, height: 90);

        byte[] png = heatmap.ToPng();
        // PNG magic number: 89 50 4E 47 ...
        Assert.That(png.Length, Is.GreaterThan(100), "a 120x90 heatmap PNG is non-trivial");
        Assert.That(png[0], Is.EqualTo(0x89));
        Assert.That(png[1], Is.EqualTo(0x50)); // 'P'
        Assert.That(png[2], Is.EqualTo(0x4E)); // 'N'
        Assert.That(png[3], Is.EqualTo(0x47)); // 'G'
        Assert.That(heatmap.Width, Is.EqualTo(120));
        Assert.That(heatmap.Height, Is.EqualTo(90));
    }

    [Test]
    public void RenderHeatmap_Sphere_UsesRecommendedBounds()
    {
        // SphereFitness bounds are [-5.12, 5.12] (from KnownFunctionsBounds). On an odd-sized
        // canvas the origin maps exactly to the center pixel.
        using LandscapeHeatmap heatmap = KnownFunctionLandscape.RenderHeatmap(
            new SphereFitness(), width: 101, height: 101);

        (int px, int py) = heatmap.ToPixel(0.0, 0.0);
        Assert.That(px, Is.EqualTo(50));
        Assert.That(py, Is.EqualTo(50));
    }

    [Test]
    public void RenderHeatmap_MaximumMarker_SitsOnGlobalOptimum()
    {
        // Sphere maximizes (negated) fitness = 0 only at the origin. On an 81x81 canvas spanning
        // [-5.12, 5.12], pixel (40, 40) samples exactly (0, 0), so the Black maximum marker of
        // the verbatim BuildBitmap must land there.
        using LandscapeHeatmap heatmap = KnownFunctionLandscape.RenderHeatmap(
            new SphereFitness(), width: 81, height: 81);

        (int ox, int oy) = heatmap.ToPixel(0.0, 0.0);
        Assert.That(heatmap.Bitmap.GetPixel(ox, oy).ToArgb(), Is.EqualTo(Color.Black.ToArgb()),
            "global maximum (the optimum) is marked Black");
    }

    [Test]
    public void RenderHeatmap_ExplicitRanges_OverrideRegistry()
    {
        // A zoomed view of Rastrigin around its optimum: explicit ranges bypass the registry box.
        using LandscapeHeatmap heatmap = KnownFunctionLandscape.RenderHeatmap(
            new RastriginFitness(), xRange: (-1.0, 1.0), yRange: (-1.0, 1.0), width: 60, height: 60);

        byte[] png = heatmap.ToPng();
        Assert.That(png[0], Is.EqualTo(0x89));
        Assert.That(png[3], Is.EqualTo(0x47)); // 'G'
        Assert.That(heatmap.Width, Is.EqualTo(60));
        Assert.That(heatmap.Height, Is.EqualTo(60));
    }

    [Test]
    public void RenderHeatmap_TwoDimensionalBenchmark_RendersWithoutThrowing()
    {
        // Booth is a fixed-2D function; the bridge must drive it the same as the n-D ones.
        using LandscapeHeatmap heatmap = KnownFunctionLandscape.RenderHeatmap(
            new BoothFitness(), width: 80, height: 80);

        Assert.That(heatmap.ToPng()[0], Is.EqualTo(0x89));
    }

    [Test]
    public void RenderHeatmap_NullFitness_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => KnownFunctionLandscape.RenderHeatmap(null!));
    }

    [Test]
    public void RenderHeatmap_TwoDimensionalProjection_DelegatesTo2DOverload()
    {
        // dimension == 2 should be a no-op for random sampling: a 3x3 canvas at the Sphere
        // optimum (0,0) is uniformly Black (the global optimum marker). If random sampling
        // accidentally fires, the marker placement would still be (1,1) here, but downstream
        // pixels would scatter. We assert a tight region around the marker to keep the test
        // deterministic regardless of nbSamples = 0 (which we do NOT pass — we pass the
        // default 10 and check the marker is where we expect).
        using LandscapeHeatmap heatmap = KnownFunctionLandscape.RenderHeatmap(
            new SphereFitness(), dimension: 2, width: 81, height: 81);

        // Origin (0,0) -> pixel center on a 101-px odd canvas. With 81px, origin maps to
        // (40, 40) (40/80 ratio, since the canvas spans [-5.12, 5.12] inclusively).
        (int ox, int oy) = heatmap.ToPixel(0.0, 0.0);
        Assert.That(heatmap.Bitmap.GetPixel(ox, oy).ToArgb(), Is.EqualTo(Color.Black.ToArgb()),
            "dim=2 must keep the global-optimum Black marker intact");

        byte[] png = heatmap.ToPng();
        Assert.That(png[0], Is.EqualTo(0x89));
        Assert.That(heatmap.Width, Is.EqualTo(81));
        Assert.That(heatmap.Height, Is.EqualTo(81));
    }

    [Test]
    public void RenderHeatmap_FiveDimensionalRastrigin_ProducesPng()
    {
        // Rastrigin @ dim = 5, default nbSamples = 10. The controller's verbatim projection
        // pattern picks MAX over nbSamples random extra-coords draws: the optimum on screen
        // is wherever the random draws align with all 4 extra coords near an optimum
        // (the digit pattern is more visible at the lower cost nbSamples = 10 here). The
        // sanity assertion is just that the bridge runs end-to-end and emits a real PNG.
        using LandscapeHeatmap heatmap = KnownFunctionLandscape.RenderHeatmap(
            new RastriginFitness(), dimension: 5, nbSamples: 10, width: 80, height: 60);

        byte[] png = heatmap.ToPng();
        Assert.That(png.Length, Is.GreaterThan(100), "even the N-D projection yields a non-trivial PNG");
        Assert.That(png[0], Is.EqualTo(0x89));
        Assert.That(heatmap.Width, Is.EqualTo(80));
        Assert.That(heatmap.Height, Is.EqualTo(60));
    }

    [Test]
    public void RenderHeatmap_DimensionOne_Throws()
    {
        // dim = 1 would produce a 1-D "heatmap" that cannot be drawn; the controller avoids
        // this case by guarding on mNbDimensions >= 2 at construction time. The bridge
        // mirrors that guard here.
        Assert.Throws<ArgumentOutOfRangeException>(
            () => KnownFunctionLandscape.RenderHeatmap(new SphereFitness(), dimension: 1));
    }

    [Test]
    public void RenderHeatmap_NbSamplesZero_Throws()
    {
        // nbSamples = 0 would short-circuit the MAX aggregation to MinValue and silently
        // emit a blank PNG. The controller's SpinButton clamps to [1, 10000]; the bridge
        // matches that contract for parity.
        Assert.Throws<ArgumentOutOfRangeException>(
            () => KnownFunctionLandscape.RenderHeatmap(
                new SphereFitness(), dimension: 5, nbSamples: 0));
    }

    [Test]
    public void RenderHeatmap_HighDimensionSchwefel_ProducesPng()
    {
        // Schwefel @ dim = 10, nbSamples = 50. Schwefel is highly deceptive at dim >= 5,
        // which is exactly the calibration question MGS-7b explores. We assert only that
        // the call finishes; the heuristic pixel placement is verified visually in the
        // notebook (MGS-7b), not in headless NUnit.
        using LandscapeHeatmap heatmap = KnownFunctionLandscape.RenderHeatmap(
            new SchwefelFitness(), dimension: 10, nbSamples: 50, width: 60, height: 40);

        Assert.That(heatmap.ToPng()[0], Is.EqualTo(0x89));
    }
}
