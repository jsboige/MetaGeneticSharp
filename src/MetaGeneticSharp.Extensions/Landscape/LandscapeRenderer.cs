using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
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
/// Per-channel quantization used when an RGB color is built from a normalized [0, 1] component
/// (see <see cref="LandscapeRenderer.GetColorFromHSV"/>). The verbatim controller @ d05826fd
/// truncates (<c>(int)(r * 255)</c>); <see cref="Round"/> is an additive, opt-in alternative.
/// </summary>
public enum ColorQuantization
{
    /// <summary>
    /// Verbatim controller behavior (@ d05826fd): cast <c>r * 255</c> to <see cref="int"/>, which
    /// truncates toward zero. This is the default so the rendered ramp stays byte-identical to
    /// jsboige's original output.
    /// </summary>
    Truncate,

    /// <summary>
    /// Round <c>r * 255</c> to the nearest byte (away-from-zero on the .5 tie). Removes the
    /// systematic downward bias of truncation (a normalized channel of e.g. 0.999 maps to 255
    /// instead of 254). An opt-in deviation from the verbatim ramp — never the default.
    /// </summary>
    Round,
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
    ///
    /// <para><b>Image lifetime (M2).</b> <see cref="Load"/> returns a freshly decoded
    /// <see cref="Image"/> that this method owns. The
    /// <see cref="ImageHeightMapFunction.TargetImage"/> setter copies it into an internal grayscale
    /// <see cref="DirectBitmap"/> (jsboige's <c>MakeGrayscaleImage()</c> @ d05826fd), so the loaded
    /// source is no longer needed once assigned and is disposed here — only the retained grayscale
    /// copy survives. This mirrors the <see cref="CreateFunctionFromFile"/> pattern and removes a
    /// GDI+ handle leak (the source <see cref="Image"/> was previously never disposed). The
    /// grayscale infrastructure is jsboige's @ d05826fd; only this wrapper's disposal is authored.</para>
    /// </summary>
    public static ImageHeightMapFunction CreateFunction(KnownHeightMap map)
    {
        using Image source = Load(map);
        return new ImageHeightMapFunction { TargetImage = source, Name = map.ToString() };
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

    /// <summary>
    /// Builds a <see cref="BilinearHeightMapFunction"/> over an arbitrary in-memory image — the
    /// smoother, additive sibling of <see cref="CreateFunctionFromImage"/>. Same grayscale
    /// elevation field and same ranges, but sampled with true bilinear interpolation, which is
    /// continuous on the grid lines where the verbatim inverse-distance scheme of
    /// <see cref="ImageHeightMapFunction"/> falls back to the nearest floored pixel (a stair-step).
    /// Use it side by side with <see cref="CreateFunctionFromImage"/> to compare the two
    /// interpolations on the same source (the MGS-7 "IDW vs bilinéaire" exercise).
    ///
    /// The original is unchanged and remains the default; this only adds a choice. The
    /// <see cref="BilinearHeightMapFunction.TargetImage"/> setter copies <paramref name="image"/>
    /// to grayscale, so the caller keeps ownership of the argument. Credit: jsboige @ d05826fd.
    /// </summary>
    /// <param name="image">Any image to read as a height field (its R channel after grayscale).</param>
    /// <param name="name">Optional label (defaults to "CustomImageBilinear").</param>
    public static BilinearHeightMapFunction CreateBilinearFunctionFromImage(Image image, string? name = null)
    {
        ArgumentNullException.ThrowIfNull(image);
        return new BilinearHeightMapFunction { TargetImage = image, Name = name ?? "CustomImageBilinear" };
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

    /// <summary>
    /// Superimposes the GA population with optional <em>per-individual</em> marker colors for the
    /// "colored islands" overlay. When <paramref name="individualColors"/> is supplied and its count
    /// matches the materialized population count, individual <c>i</c> is drawn in
    /// <c>individualColors[i]</c> instead of the default BlueViolet — visualizing a population
    /// structured into islands/sub-populations (each island a distinct color) and the basin of
    /// attraction each converges toward. When <c>null</c> or a count mismatch, the render falls
    /// back to the verbatim single-color BlueViolet markers (byte-identical to the no-colors
    /// <see cref="Plot(IEnumerable{double[]}, double[])"/> overload). Marker shape is the verbatim
    /// 5x5 diamond of the controller's <c>DrawFunctionPoint</c>. The best individual is always Aqua.
    /// </summary>
    public void Plot(IEnumerable<double[]> population, double[]? best, IReadOnlyList<System.Drawing.Color>? individualColors)
    {
        ArgumentNullException.ThrowIfNull(population);
        IList<double[]> materialized = population as IList<double[]> ?? new List<double[]>(population);
        bool usePerIndividualColors = individualColors is not null && individualColors.Count == materialized.Count;
        for (int i = 0; i < materialized.Count; i++)
        {
            double[] individual = materialized[i];
            (int px, int py) = ToPixel(individual[0], individual[1]);
            System.Drawing.Color marker = usePerIndividualColors ? individualColors![i] : LandscapeMaps.IndividualColor;
            LandscapeRenderer.DrawFunctionPoint(Bitmap, px, py, marker);
        }

        if (best is not null)
        {
            (int px, int py) = ToPixel(best[0], best[1]);
            LandscapeRenderer.DrawFunctionPoint(Bitmap, px, py, LandscapeMaps.BestColor);
        }
    }

    /// <summary>
    /// Exports the heatmap as a PNG byte array (image/png) for inline notebook display.
    /// Cross-platform by default: delegates to <see cref="SkiaLandscapeRenderer.EncodePng(DirectBitmap)"/>
    /// (SkiaSharp), so the same graphic heatmap exports without the GDI+ encoder that is Windows-only
    /// on .NET 6+. The verbatim GDI+ path remains available as <see cref="ToPngGdi"/> for parity.
    /// </summary>
    public byte[] ToPng() => SkiaLandscapeRenderer.EncodePng(Bitmap);

    /// <summary>
    /// The original verbatim GDI+ PNG export (<c>Bitmap.Save(stream, ImageFormat.Png)</c>).
    /// Windows-only (System.Drawing.Common, .NET 6+); kept for parity and credit. Prefer
    /// <see cref="ToPng"/> for cross-platform rendering.
    /// </summary>
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    public byte[] ToPngGdi()
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
    ///
    /// <para><b>Parallel evaluation (L1).</b> The original GTK# controller (jsboige @ d05826fd)
    /// sampled the canvas in a single sequential double loop. This authored renderer evaluates
    /// the <c>width * height</c> fitness samples (~120k at the 400x300 default) with
    /// <see cref="Parallel.For"/> over the rows: each row writes a disjoint slice of
    /// <c>values</c> (<c>index = px + py * width</c>), so the writes never race, and the global
    /// min/max reduction uses per-thread (<c>localInit</c>/<c>localFinally</c>) accumulators
    /// merged under a lock. Reads of any image-backed function go through
    /// <see cref="DirectBitmap.GetPixel"/>, a pure read of the pinned <c>Bits</c> array, so the
    /// grayscale source is read-only during the render and safe to share across threads. The
    /// merge keeps the <em>lowest</em> index on equal extrema, exactly reproducing the
    /// first-occurrence tie-break of the sequential scan, so the rendered bitmap is
    /// byte-identical to the single-threaded result. Only the parallel scheduling is authored;
    /// the color ramp, extrema markers and sampling math remain jsboige's @ d05826fd.</para>
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
        object reduceLock = new();

        Parallel.For(
            0,
            height,
            () => (fMin: double.PositiveInfinity, minIndex: int.MaxValue, fMax: double.NegativeInfinity, maxIndex: int.MaxValue),
            (py, _, local) =>
            {
                double y = Map(py, height, yRange);
                int rowStart = py * width;
                for (int px = 0; px < width; px++)
                {
                    double x = Map(px, width, xRange);
                    double f = function(new[] { x, y });
                    int index = rowStart + px;
                    values[index] = f;
                    // Strict comparisons keep the lowest index within this thread's rows
                    // (scanned in increasing index order), matching the sequential first-win.
                    if (f < local.fMin) { local.fMin = f; local.minIndex = index; }
                    if (f > local.fMax) { local.fMax = f; local.maxIndex = index; }
                }

                return local;
            },
            local =>
            {
                lock (reduceLock)
                {
                    // On equal extrema across threads, prefer the lowest index so the result
                    // matches the sequential scan's first-occurrence tie-break exactly.
                    if (local.fMin < fMin || (local.fMin == fMin && local.minIndex < minIndex))
                    {
                        fMin = local.fMin;
                        minIndex = local.minIndex;
                    }

                    if (local.fMax > fMax || (local.fMax == fMax && local.maxIndex < maxIndex))
                    {
                        fMax = local.fMax;
                        maxIndex = local.maxIndex;
                    }
                }
            });

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
    /// <paramref name="quantization"/> defaults to <see cref="ColorQuantization.Truncate"/> so the
    /// ramp stays byte-identical to jsboige's original; pass <see cref="ColorQuantization.Round"/>
    /// to opt into the de-biased rounding (see <see cref="GetColorFromHSV"/>).
    /// </summary>
    public static Color GetColor(double fValue, double fMin, double fMax,
        ColorQuantization quantization = ColorQuantization.Truncate)
    {
        double ratio = (fMax - fMin) <= double.Epsilon
            ? 0.5
            : 0.5 - ((fValue - fMin) / (2 * (fMax - fMin)));
        return GetColorFromHSV(ratio, quantization: quantization);
    }

    /// <summary>
    /// Builds an RGB color from a hue using the recovered <see cref="ImageExtensions.HsvToRgb"/>
    /// helper. With the default <see cref="ColorQuantization.Truncate"/> this is <b>verbatim</b>
    /// from the controller (@ d05826fd): <c>Color.FromArgb((int)(r * 255), ...)</c>, a truncating
    /// cast. <see cref="ColorQuantization.Round"/> is an additive opt-in (L2 co-evolution) that
    /// rounds to the nearest byte instead, removing truncation's systematic downward bias; it is
    /// <b>never</b> the default, so it never silently alters jsboige's original ramp.
    /// </summary>
    public static Color GetColorFromHSV(double hue, double saturation = 1.0, double value = 1.0,
        ColorQuantization quantization = ColorQuantization.Truncate)
    {
        (double r, double g, double b) = (hue, saturation, value).HsvToRgb();
        return quantization == ColorQuantization.Round
            ? Color.FromArgb(ToByteRounded(r), ToByteRounded(g), ToByteRounded(b))
            : Color.FromArgb((int)(r * 255), (int)(g * 255), (int)(b * 255));
    }

    /// <summary>
    /// Rounds a normalized [0, 1] channel to the nearest byte (away-from-zero on .5), clamped to
    /// [0, 255] so a boundary <see cref="ImageExtensions.HsvToRgb"/> result can never overflow
    /// <see cref="Color.FromArgb(int, int, int)"/>. Used only by <see cref="ColorQuantization.Round"/>.
    /// </summary>
    private static int ToByteRounded(double channel)
    {
        int scaled = (int)Math.Round(channel * 255, MidpointRounding.AwayFromZero);
        return Math.Clamp(scaled, 0, 255);
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
