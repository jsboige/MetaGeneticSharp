using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using GeneticSharp.Extensions.Mathematic.Functions;
using GeneticSharp.Infrastructure.Framework.Images;

namespace MetaGeneticSharp;

/// <summary>
/// The three landscape sources of the original GeneticSharp Metaheuristics
/// <c>LandscapeExplorerSampleController</c> (authored by jsboige). Verbatim from
/// <c>LandscapeExplorerSampleController.cs</c> lines 26-30 @ d05826fd
/// (MyIntelligenceAgency/GeneticSharp, branch Metaheuristics).
/// </summary>
public enum LandscapeMode
{
    KnownFunction,
    KnownHeightMap,
    CustomImage,
}

/// <summary>
/// The four original height maps shipped with the LandscapeExplorer sample, recovered
/// byte-exact (sha256 == source git blob) and embedded as assembly resources. Verbatim
/// enum from <c>LandscapeExplorerSampleController.cs</c> lines 33-38 @ d05826fd.
/// </summary>
public enum KnownHeightMap
{
    EverestMount,
    NepalBhoutan,
    TibetanPlateau,
    World,
}

/// <summary>
/// Thin pedagogical wrapper around the recovered-verbatim landscape library
/// (<see cref="ImageHeightMapFunction"/>, <see cref="DirectBitmap"/>,
/// <see cref="ImageExtensions"/>). It loads the four original height maps from embedded
/// resources and renders a GRAPHIC heatmap (image/png) of any fitness landscape, with the
/// GA population superimposed.
///
/// Every color/marker formula here is extracted verbatim from jsboige's
/// <c>LandscapeExplorerSampleController.cs</c> @ d05826fd; only the orchestration
/// (resource loading, MemoryStream PNG export, range mapping) is authored. The original
/// controller was Gtk#-bound and cannot be ported as-is; the rendering math is unchanged.
/// </summary>
public static class LandscapeMaps
{
    // Population marker colors, verbatim from the controller (lines 50-51 @ d05826fd):
    //   indColor  = Color.BlueViolet;  // ordinary individuals
    //   bestColor = Color.Aqua;        // current best
    public static readonly Color IndividualColor = Color.BlueViolet;
    public static readonly Color BestColor = Color.Aqua;

    private const string ResourcePrefix = "MetaGeneticSharp.LandscapeMaps.";

    /// <summary>
    /// Streams one of the four original height maps from its embedded resource. The bytes
    /// are the verbatim PNG authored by jsboige (no re-encode).
    /// </summary>
    public static Image Load(KnownHeightMap map)
    {
        string name = ResourcePrefix + map + ".png";
        Assembly asm = typeof(LandscapeMaps).Assembly;
        using Stream? stream = asm.GetManifestResourceStream(name);
        if (stream is null)
        {
            throw new InvalidOperationException(
                $"Embedded height map '{name}' not found. Available: {string.Join(", ", asm.GetManifestResourceNames())}");
        }

        return Image.FromStream(stream);
    }

    /// <summary>
    /// Builds an <see cref="ImageHeightMapFunction"/> (the verbatim fitness function:
    /// grayscale pixel intensity with inverse-distance interpolation) over one of the four
    /// original maps. The returned function maximizes elevation = pixel brightness.
    /// </summary>
    public static ImageHeightMapFunction CreateFunction(KnownHeightMap map)
    {
        var function = new ImageHeightMapFunction { TargetImage = Load(map), Name = map.ToString() };
        return function;
    }

    /// <summary>
    /// Builds an <see cref="ImageHeightMapFunction"/> over an arbitrary in-memory image — the
    /// <see cref="LandscapeMode.CustomImage"/> source, now first-class and symmetric with
    /// <see cref="CreateFunction(KnownHeightMap)"/>. Any bitmap becomes an elevation field:
    /// grayscale pixel intensity (R channel) read with the verbatim inverse-distance
    /// interpolation of <see cref="ImageHeightMapFunction"/>.
    ///
    /// The <see cref="ImageHeightMapFunction.TargetImage"/> setter copies <paramref name="image"/>
    /// into an internal grayscale bitmap, so the caller keeps ownership of the argument and may
    /// dispose it after this call returns.
    /// </summary>
    /// <param name="image">Any image to read as a height field (its R channel after grayscale).</param>
    /// <param name="name">Optional label for the function (defaults to "CustomImage").</param>
    public static ImageHeightMapFunction CreateFunctionFromImage(Image image, string? name = null)
    {
        ArgumentNullException.ThrowIfNull(image);
        return new ImageHeightMapFunction { TargetImage = image, Name = name ?? "CustomImage" };
    }

    /// <summary>
    /// Builds an <see cref="ImageHeightMapFunction"/> from an image file on disk (PNG, JPEG, ...).
    /// Completes the three-mode symmetry: <see cref="CreateFunction(KnownHeightMap)"/> for the
    /// four shipped maps, <see cref="CreateFunctionFromImage"/> for an in-memory bitmap, and this
    /// overload for a file path. The source image is loaded, copied to grayscale by the
    /// <see cref="ImageHeightMapFunction.TargetImage"/> setter, then disposed here — only the
    /// internal grayscale copy is retained (no source-image leak).
    /// </summary>
    /// <param name="path">Path to an image file readable by <see cref="Image.FromFile(string)"/>.</param>
    /// <param name="name">Optional label (defaults to the file name without extension).</param>
    public static ImageHeightMapFunction CreateFunctionFromFile(string path, string? name = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        using Image source = Image.FromFile(path);
        return new ImageHeightMapFunction
        {
            TargetImage = source,
            Name = name ?? Path.GetFileNameWithoutExtension(path),
        };
    }
}

/// <summary>
/// A rendered heatmap of a fitness landscape plus the range mapping needed to superimpose
/// population points consistently. Holds a <see cref="DirectBitmap"/> and exports it as a
/// PNG byte array for inline display (image/png) in a .NET Interactive notebook.
/// </summary>
public sealed class LandscapeHeatmap : IDisposable
{
    private readonly (double min, double max) _xRange;
    private readonly (double min, double max) _yRange;

    internal LandscapeHeatmap(DirectBitmap bitmap, (double min, double max) xRange, (double min, double max) yRange)
    {
        Bitmap = bitmap;
        _xRange = xRange;
        _yRange = yRange;
    }

    public DirectBitmap Bitmap { get; }

    public int Width => Bitmap.Width;

    public int Height => Bitmap.Height;

    /// <summary>Maps a fitness-space coordinate (x, y) to the heatmap pixel that represents it.</summary>
    public (int px, int py) ToPixel(double x, double y)
    {
        double fx = (_xRange.max - _xRange.min) <= double.Epsilon ? 0.0 : (x - _xRange.min) / (_xRange.max - _xRange.min);
        double fy = (_yRange.max - _yRange.min) <= double.Epsilon ? 0.0 : (y - _yRange.min) / (_yRange.max - _yRange.min);
        int px = (int)Math.Round(fx * (Width - 1));
        int py = (int)Math.Round(fy * (Height - 1));
        return (Math.Clamp(px, 0, Width - 1), Math.Clamp(py, 0, Height - 1));
    }

    /// <summary>
    /// Superimposes the GA population: ordinary individuals in BlueViolet, the best in Aqua.
    /// Marker shape is the verbatim 5x5 diamond of the controller's <c>DrawFunctionPoint</c>.
    /// </summary>
    public void Plot(IEnumerable<double[]> population, double[]? best = null)
    {
        ArgumentNullException.ThrowIfNull(population);
        foreach (double[] individual in population)
        {
            (int px, int py) = ToPixel(individual[0], individual[1]);
            LandscapeRenderer.DrawFunctionPoint(Bitmap, px, py, LandscapeMaps.IndividualColor);
        }

        if (best is not null)
        {
            (int px, int py) = ToPixel(best[0], best[1]);
            LandscapeRenderer.DrawFunctionPoint(Bitmap, px, py, LandscapeMaps.BestColor);
        }
    }

    /// <summary>Exports the heatmap as a PNG byte array (image/png) for inline notebook display.</summary>
    public byte[] ToPng()
    {
        using var stream = new MemoryStream();
        Bitmap.Bitmap.Save(stream, ImageFormat.Png);
        return stream.ToArray();
    }

    public void Dispose() => Bitmap.Dispose();
}

/// <summary>
/// Renders fitness landscapes as graphic heatmaps. The color ramp, the min/max markers and
/// the population marker shape are all extracted verbatim from jsboige's
/// <c>LandscapeExplorerSampleController.cs</c> @ d05826fd; only the canvas sampling and the
/// PNG export are authored here (the original was Gtk#-bound).
/// </summary>
public static class LandscapeRenderer
{
    /// <summary>
    /// Renders any 2D fitness function over the given input ranges as a heatmap. Each canvas
    /// pixel is colored by <see cref="GetColor"/> from its fitness value; the global minimum
    /// pixel is marked White and the global maximum Black (verbatim from the controller's
    /// <c>BuildBitmap</c>, lines 581-595 @ d05826fd).
    /// </summary>
    public static LandscapeHeatmap RenderHeatmap(
        Func<double[], double> function,
        (double min, double max) xRange,
        (double min, double max) yRange,
        int width = 400,
        int height = 300)
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

        var bitmap = new DirectBitmap(width, height);
        for (int index = 0; index < values.Length; index++)
        {
            int px = index % width;
            int py = index / width;
            bitmap.SetPixel(px, py, GetColor(values[index], fMin, fMax));
        }

        // Mark the extrema: minimum White, maximum Black (controller BuildBitmap @ d05826fd).
        bitmap.SetPixel(minIndex % width, minIndex / width, Color.White);
        bitmap.SetPixel(maxIndex % width, maxIndex / width, Color.Black);

        return new LandscapeHeatmap(bitmap, xRange, yRange);
    }

    /// <summary>
    /// Renders an <see cref="IKnownFunction"/> (e.g. <see cref="ImageHeightMapFunction"/>)
    /// over its own declared 2D ranges.
    /// </summary>
    public static LandscapeHeatmap RenderHeatmap(IKnownFunction function, int width = 400, int height = 300)
    {
        ArgumentNullException.ThrowIfNull(function);
        IList<(double min, double max)> ranges = function.Ranges(2);
        return RenderHeatmap(function.Function, ranges[0], ranges[1], width, height);
    }

    /// <summary>
    /// The verbatim color ramp of the controller (lines 558-565 @ d05826fd):
    /// <code>
    /// var ratio = 0.5 - ((fValue - fMin) / (2 * (fMax - fMin)));
    /// var hue = ratio;
    /// return GetColorFromHSV(hue);
    /// </code>
    /// </summary>
    public static Color GetColor(double fValue, double fMin, double fMax)
    {
        double ratio = (fMax - fMin) <= double.Epsilon
            ? 0.5
            : 0.5 - ((fValue - fMin) / (2 * (fMax - fMin)));
        return GetColorFromHSV(ratio);
    }

    /// <summary>
    /// Verbatim from the controller (@ d05826fd): builds an RGB color from a hue using the
    /// recovered <see cref="ImageExtensions.HsvToRgb"/> helper.
    /// </summary>
    public static Color GetColorFromHSV(double hue, double saturation = 1.0, double value = 1.0)
    {
        (double r, double g, double b) = (hue, saturation, value).HsvToRgb();
        return Color.FromArgb((int)(r * 255), (int)(g * 255), (int)(b * 255));
    }

    /// <summary>
    /// Verbatim 5x5 diamond marker of the controller's <c>DrawFunctionPoint</c>
    /// (lines 542-555 @ d05826fd): every offset (i, j) with |i| + |j| &lt; 4, inside bounds.
    /// </summary>
    public static void DrawFunctionPoint(DirectBitmap image, int xDraw, int yDraw, Color color)
    {
        ArgumentNullException.ThrowIfNull(image);
        for (int i = -2; i <= 2; i++)
        {
            for (int j = -2; j <= 2; j++)
            {
                if (Math.Abs(i) + Math.Abs(j) < 4
                    && xDraw + i >= 0 && xDraw + i < image.Width
                    && yDraw + j >= 0 && yDraw + j < image.Height)
                {
                    image.SetPixel(xDraw + i, yDraw + j, color);
                }
            }
        }
    }

    private static double Map(int pixel, int extent, (double min, double max) range)
        => range.min + (range.max - range.min) * pixel / (extent - 1);
}
