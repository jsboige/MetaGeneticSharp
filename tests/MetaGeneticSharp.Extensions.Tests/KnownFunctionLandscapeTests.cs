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
}
