using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using GeneticSharp.Extensions.Mathematic.Functions;
using GeneticSharp.Infrastructure.Framework.Images;
using SkiaSharp;

namespace MetaGeneticSharp;

/// <summary>
/// Cross-platform (SkiaSharp) PNG rendering for the fitness landscapes, realizing the
/// recommendation jsboige left in <c>ImageExtensions.cs</c> line 46 @ d05826fd
/// (MyIntelligenceAgency/GeneticSharp, branch Metaheuristics):
/// <para>
/// <c>"... a move to SharpImage or other libs is recommended rather than the compatibility
/// System.Drawing"</c>.
/// </para>
/// <see cref="System.Drawing.Common"/> (GDI+) is Windows-only on .NET 6+, so the original
/// <c>Bitmap.Save(stream, ImageFormat.Png)</c> and the <see cref="DirectBitmap"/> canvas throw
/// under a Linux notebook kernel. This backend produces the SAME graphic PNG heatmaps with no
/// GDI+ dependency, behind the same <c>byte[]</c> contract.
///
/// <para>Two entry points, both additive (the byte-exact originals
/// <see cref="DirectBitmap"/> / <c>ImageExtensions</c> are untouched):</para>
/// <list type="bullet">
///   <item><see cref="EncodePng(DirectBitmap)"/> — encodes an already-rendered
///   <see cref="DirectBitmap"/> (used by <c>LandscapeHeatmap.ToPng</c>); the GDI+ <em>encoder</em>
///   is no longer required for export.</item>
///   <item><see cref="RenderHeatmapPng(Func{double[],double},ValueTuple{double,double},ValueTuple{double,double},int,int,IEnumerable{double[]},double[])"/>
///   — a fully GDI-free render+encode for <em>function</em> landscapes (the
///   <see cref="LandscapeMode.KnownFunction"/> mode needs no image loading), so it runs end-to-end
///   on Linux.</item>
/// </list>
///
/// <para>Honesty note: this does not make the image-backed modes
/// (<see cref="LandscapeMode.KnownHeightMap"/> / <see cref="LandscapeMode.CustomImage"/>)
/// cross-platform — their grayscale load path still goes through the verbatim
/// <c>ImageExtensions.MakeGrayscaleImage</c> (GDI+). Migrating that decode/grayscale path to
/// SkiaSharp is the broader SharpImage move jsboige flagged, tracked as a follow-up.</para>
///
/// <para>Every color/marker formula reused here is the verbatim ramp of jsboige's
/// <c>LandscapeExplorerSampleController.cs</c> @ d05826fd (via <see cref="LandscapeRenderer.GetColor"/>
/// and the recovered <see cref="ImageExtensions.HsvToRgb"/>); only the SkiaSharp canvas plumbing is
/// authored. Credit: jsboige @ d05826fd.</para>
/// </summary>
public static partial class SkiaLandscapeRenderer
{
    /// <summary>
    /// Encodes an already-rendered <see cref="DirectBitmap"/> as a PNG byte array using SkiaSharp.
    /// The bitmap stores premultiplied ARGB ints (<c>PixelFormat.Format32bppPArgb</c>); on a
    /// little-endian machine each int lays out in memory as B, G, R, A — matching
    /// <see cref="SKColorType.Bgra8888"/> with <see cref="SKAlphaType.Premul"/>, so the pixels copy
    /// across without any per-pixel conversion. No GDI+ is touched.
    /// </summary>
    public static byte[] EncodePng(DirectBitmap bitmap)
    {
        ArgumentNullException.ThrowIfNull(bitmap);
        var info = new SKImageInfo(bitmap.Width, bitmap.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var skBitmap = new SKBitmap(info);
        int[] bits = bitmap.Bits;
        Marshal.Copy(bits, 0, skBitmap.GetPixels(), bits.Length);
        return EncodePng(skBitmap);
    }

    /// <summary>
    /// Renders any 2D fitness function over the given input ranges directly onto a SkiaSharp
    /// surface and returns the PNG bytes — no <see cref="DirectBitmap"/>, no GDI+, so the whole
    /// path runs on a Linux kernel. The coloring is the verbatim ramp (minimum pixel White,
    /// maximum Black; <see cref="LandscapeRenderer.GetColor"/>), and the population markers reuse
    /// the verbatim 5x5 diamond (<c>|i| + |j| &lt; 4</c>) in BlueViolet (individuals) / Aqua (best).
    /// </summary>
    /// <param name="function">The fitness function (x, y) -> value.</param>
    /// <param name="xRange">Input range mapped across the canvas width.</param>
    /// <param name="yRange">Input range mapped across the canvas height.</param>
    /// <param name="width">Canvas width in pixels (>= 2).</param>
    /// <param name="height">Canvas height in pixels (>= 2).</param>
    /// <param name="population">Optional GA population to superimpose (BlueViolet diamonds).</param>
    /// <param name="best">Optional current-best individual to superimpose (Aqua diamond).</param>
    public static byte[] RenderHeatmapPng(
        Func<double[], double> function,
        (double min, double max) xRange,
        (double min, double max) yRange,
        int width = 400,
        int height = 300,
        IEnumerable<double[]>? population = null,
        double[]? best = null)
    {
        ArgumentNullException.ThrowIfNull(function);
        if (width < 2 || height < 2)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "Heatmap canvas must be at least 2x2.");
        }

        var values = new double[width * height];
        double fMin = double.PositiveInfinity, fMax = double.NegativeInfinity;
        int minIndex = 0, maxIndex = 0;

        for (int py = 0; py < height; py++)
        {
            double y = Map(py, height, yRange);
            for (int px = 0; px < width; px++)
            {
                double x = Map(px, width, xRange);
                double f = function(new[] { x, y });
                int index = px + py * width;
                values[index] = f;
                if (f < fMin) { fMin = f; minIndex = index; }
                if (f > fMax) { fMax = f; maxIndex = index; }
            }
        }

        var info = new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var skBitmap = new SKBitmap(info);
        for (int index = 0; index < values.Length; index++)
        {
            System.Drawing.Color c = LandscapeRenderer.GetColor(values[index], fMin, fMax);
            skBitmap.SetPixel(index % width, index / width, new SKColor(c.R, c.G, c.B));
        }

        // Mark the extrema: minimum White, maximum Black (controller BuildBitmap @ d05826fd).
        skBitmap.SetPixel(minIndex % width, minIndex / width, SKColors.White);
        skBitmap.SetPixel(maxIndex % width, maxIndex / width, SKColors.Black);

        return RenderHeatmapPng(function, xRange, yRange, width, height, population, best, individualColors: null);
    }

    /// <summary>
    /// Renders any 2D fitness function over the given input ranges directly onto a SkiaSharp
    /// surface and returns the PNG bytes — no <see cref="DirectBitmap"/>, no GDI+, so the whole
    /// path runs on a Linux kernel. The coloring is the verbatim ramp (minimum pixel White,
    /// maximum Black; <see cref="LandscapeRenderer.GetColor"/>), and the population markers reuse
    /// the verbatim 5x5 diamond (<c>|i| + |j| &lt; 4</c>).
    /// </summary>
    /// <param name="individualColors">
    /// Optional per-individual marker colors for the <paramref name="population"/>. When provided
    /// AND its count matches the materialized population count, individual <c>i</c> is drawn in
    /// <c>individualColors[i]</c> instead of the default BlueViolet — this is the "colored islands"
    /// overlay that visualizes a population structured into islands/sub-populations (each island a
    /// distinct color), revealing the basins of attraction each island converges toward. When
    /// <c>null</c> or a count mismatch, the render falls back to the verbatim single-color
    /// BlueViolet markers (byte-identical to the no-colors overload) — <em>no pendulum</em>: the
    /// original single-color behavior is preserved exactly.
    /// </param>
    /// <param name="function">The fitness function (x, y) -> value.</param>
    /// <param name="xRange">Input range mapped across the canvas width.</param>
    /// <param name="yRange">Input range mapped across the canvas height.</param>
    /// <param name="width">Canvas width in pixels (>= 2).</param>
    /// <param name="height">Canvas height in pixels (>= 2).</param>
    /// <param name="population">Optional GA population to superimpose.</param>
    /// <param name="best">Optional current-best individual to superimpose (Aqua diamond).</param>
    public static byte[] RenderHeatmapPng(
        Func<double[], double> function,
        (double min, double max) xRange,
        (double min, double max) yRange,
        int width,
        int height,
        IEnumerable<double[]>? population,
        double[]? best,
        IReadOnlyList<System.Drawing.Color>? individualColors)
    {
        ArgumentNullException.ThrowIfNull(function);
        if (width < 2 || height < 2)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "Heatmap canvas must be at least 2x2.");
        }

        var values = new double[width * height];
        double fMin = double.PositiveInfinity, fMax = double.NegativeInfinity;
        int minIndex = 0, maxIndex = 0;

        for (int py = 0; py < height; py++)
        {
            double y = Map(py, height, yRange);
            for (int px = 0; px < width; px++)
            {
                double x = Map(px, width, xRange);
                double f = function(new[] { x, y });
                int index = px + py * width;
                values[index] = f;
                if (f < fMin) { fMin = f; minIndex = index; }
                if (f > fMax) { fMax = f; maxIndex = index; }
            }
        }

        var info = new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var skBitmap = new SKBitmap(info);
        for (int index = 0; index < values.Length; index++)
        {
            System.Drawing.Color c = LandscapeRenderer.GetColor(values[index], fMin, fMax);
            skBitmap.SetPixel(index % width, index / width, new SKColor(c.R, c.G, c.B));
        }

        // Mark the extrema: minimum White, maximum Black (controller BuildBitmap @ d05826fd).
        skBitmap.SetPixel(minIndex % width, minIndex / width, SKColors.White);
        skBitmap.SetPixel(maxIndex % width, maxIndex / width, SKColors.Black);

        if (population is not null)
        {
            // Materialize once so the colors overlay can index by position; fall back to the
            // verbatim single-color marker when no per-individual palette is supplied or the
            // palette length does not line up with the population.
            IList<double[]> materialized = population as IList<double[]> ?? new List<double[]>(population);
            bool usePerIndividualColors = individualColors is not null && individualColors.Count == materialized.Count;
            for (int i = 0; i < materialized.Count; i++)
            {
                double[] individual = materialized[i];
                (int px, int py) = ToPixel(individual[0], individual[1], xRange, yRange, width, height);
                System.Drawing.Color marker = usePerIndividualColors ? individualColors![i] : LandscapeMaps.IndividualColor;
                DrawDiamond(skBitmap, px, py, ToSkColor(marker));
            }
        }

        if (best is not null)
        {
            (int px, int py) = ToPixel(best[0], best[1], xRange, yRange, width, height);
            DrawDiamond(skBitmap, px, py, ToSkColor(LandscapeMaps.BestColor));
        }

        return EncodePng(skBitmap);
    }

    /// <summary>
    /// Renders an <see cref="IKnownFunction"/> over its own declared 2D ranges (GDI-free).
    /// Convenience overload of
    /// <see cref="RenderHeatmapPng(Func{double[],double},ValueTuple{double,double},ValueTuple{double,double},int,int,IEnumerable{double[]},double[])"/>.
    /// </summary>
    public static byte[] RenderHeatmapPng(
        IKnownFunction function,
        int width = 400,
        int height = 300,
        IEnumerable<double[]>? population = null,
        double[]? best = null)
    {
        ArgumentNullException.ThrowIfNull(function);
        IList<(double min, double max)> ranges = function.Ranges(2);
        return RenderHeatmapPng(function.Function, ranges[0], ranges[1], width, height, population, best);
    }

    private static byte[] EncodePng(SKBitmap skBitmap)
    {
        using SKImage image = SKImage.FromBitmap(skBitmap);
        using SKData data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    private static SKColor ToSkColor(System.Drawing.Color color) => new(color.R, color.G, color.B);

    /// <summary>
    /// Verbatim 5x5 diamond marker of the controller's <c>DrawFunctionPoint</c>
    /// (lines 542-555 @ d05826fd): every offset (i, j) with |i| + |j| &lt; 4, inside bounds.
    /// </summary>
    private static void DrawDiamond(SKBitmap bitmap, int xDraw, int yDraw, SKColor color)
    {
        for (int i = -2; i <= 2; i++)
        {
            for (int j = -2; j <= 2; j++)
            {
                if (Math.Abs(i) + Math.Abs(j) < 4
                    && xDraw + i >= 0 && xDraw + i < bitmap.Width
                    && yDraw + j >= 0 && yDraw + j < bitmap.Height)
                {
                    bitmap.SetPixel(xDraw + i, yDraw + j, color);
                }
            }
        }
    }

    /// <summary>Maps a fitness-space coordinate to a canvas pixel (same mapping as LandscapeHeatmap.ToPixel).</summary>
    private static (int px, int py) ToPixel(
        double x, double y,
        (double min, double max) xRange, (double min, double max) yRange,
        int width, int height)
    {
        double fx = (xRange.max - xRange.min) <= double.Epsilon ? 0.0 : (x - xRange.min) / (xRange.max - xRange.min);
        double fy = (yRange.max - yRange.min) <= double.Epsilon ? 0.0 : (y - yRange.min) / (yRange.max - yRange.min);
        int px = (int)Math.Round(fx * (width - 1));
        int py = (int)Math.Round(fy * (height - 1));
        return (Math.Clamp(px, 0, width - 1), Math.Clamp(py, 0, height - 1));
    }

    private static double Map(int pixel, int extent, (double min, double max) range)
        => range.min + (range.max - range.min) * pixel / (extent - 1);
}
