using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using GeneticSharp.Domain.Metaheuristics;
using GeneticSharp.Extensions.Mathematic.Functions;
using GeneticSharp.Infrastructure.Framework.Images;

namespace MetaGeneticSharp;

/// <summary>
/// An additive, smoother sibling of jsboige's verbatim
/// <see cref="ImageHeightMapFunction"/> (@ d05826fd, MyIntelligenceAgency/GeneticSharp,
/// branch Metaheuristics): it reads the same grayscale elevation field (R channel of
/// <c>MakeGrayscaleImage()</c>) over the same ranges (<c>x in [0, W-1]</c>, <c>y in [0, H-1]</c>),
/// but samples it with <b>true bilinear interpolation</b> instead of the original's
/// inverse-distance scheme.
///
/// <para><b>Why this exists (the delta).</b> The verbatim
/// <see cref="ImageHeightMapFunction"/> only interpolates <em>strictly inside a cell</em>
/// — its <c>ComputeValue</c> guards the interpolation with <c>if (x &gt; xDraw &amp;&amp; y &gt; yDraw)</c>
/// (line 49 @ d05826fd). On a grid line (<c>x</c> or <c>y</c> exactly integer) the guard is
/// false and it falls back to the nearest floored pixel, so elevation is piecewise-constant
/// (a stair-step) along every grid line and the surface is discontinuous there. Inside a cell
/// it uses inverse-distance weighting (IDW) of the four corners — a different surface from
/// standard bilinear. This sibling removes that grid-line asymmetry: bilinear is continuous
/// everywhere, including on the grid lines, where it reduces to the natural 1-D linear blend of
/// the two endpoints.</para>
///
/// <para><b>What is preserved.</b> The original is <em>untouched</em> and stays the default; this
/// is a strictly additive option (co-evolution, like the H1/H2/H3 helpers). Same grayscale load,
/// same <see cref="Ranges"/>, same <see cref="Fitness"/>, same out-of-range guard, same
/// <see cref="IDisposable"/> contract — only the per-point blend changes. Credit: jsboige
/// @ d05826fd for the original height-map function and the grayscale infrastructure
/// (<see cref="DirectBitmap"/>, <c>ImageExtensions.MakeGrayscaleImage</c>).</para>
///
/// <para><b>Bilinear formula.</b> With <c>fx = x - floor(x)</c>, <c>fy = y - floor(y)</c> and the
/// four neighboring R values <c>b00, b10, b01, b11</c> (neighbor indices clamped to the image
/// edge so the right/bottom border never reads out of bounds — there the corresponding fraction
/// is 0, so the clamped neighbor contributes nothing):</para>
/// <code>
/// value = b00 (1-fx)(1-fy) + b10 fx (1-fy) + b01 (1-fx) fy + b11 fx fy
/// </code>
/// </summary>
public sealed class BilinearHeightMapFunction : NamedEntity, IKnownFunction, IDisposable
{
    private DirectBitmap _targetImage;

    /// <summary>
    /// The elevation source. The setter copies the image into an internal grayscale
    /// <see cref="DirectBitmap"/> via the verbatim <c>MakeGrayscaleImage()</c>, exactly like
    /// <see cref="ImageHeightMapFunction.TargetImage"/>; the caller keeps ownership of the
    /// argument and may dispose it after assignment.
    /// </summary>
    public Image TargetImage
    {
        get => _targetImage.Bitmap;
        set => _targetImage = value.MakeGrayscaleImage();
    }

    public Func<double[], double> Function => ComputeValue;

    /// <summary>
    /// Identical to <see cref="ImageHeightMapFunction.Ranges"/>: the 2D box is the pixel grid
    /// (<c>x in [0, W-1]</c>, <c>y in [0, H-1]</c>); extra dimensions get the verbatim
    /// <c>(-1000, 1000)</c> padding range.
    /// </summary>
    public Func<int, IList<(double min, double max)>> Ranges => i =>
    {
        var drawRange = new[] { (0.0, (double)_targetImage.Width - 1), (0.0, (double)_targetImage.Height - 1) };
        if (i <= 2)
        {
            return drawRange.ToList();
        }

        var extraCoordsRanges = Enumerable.Repeat((-1000.0, 1000.0), i - 2);
        return drawRange.Union(extraCoordsRanges).ToList();
    };

    public Func<double[], double, double> Fitness => (coords, d) => d;

    private double ComputeValue(double[] coords)
    {
        (double x, double y) = (coords[0], coords[1]);
        if (x < 0 || y < 0 || x > _targetImage.Width - 1 || y > _targetImage.Height - 1)
        {
            throw new ArgumentException("coords outside of image size range");
        }

        (int xDraw, int yDraw) = ((int)Math.Floor(x), (int)Math.Floor(y));
        double fx = x - xDraw;
        double fy = y - yDraw;

        // Clamp the +1 neighbours to the image edge: on the right/bottom border the floored
        // index is already W-1 / H-1 and the matching fraction is 0, so the clamped duplicate
        // contributes zero weight (no out-of-bounds read, unlike a raw xDraw+1 / yDraw+1).
        int x1 = Math.Min(xDraw + 1, _targetImage.Width - 1);
        int y1 = Math.Min(yDraw + 1, _targetImage.Height - 1);

        double b00 = _targetImage.GetPixel(xDraw, yDraw).R;
        double b10 = _targetImage.GetPixel(x1, yDraw).R;
        double b01 = _targetImage.GetPixel(xDraw, y1).R;
        double b11 = _targetImage.GetPixel(x1, y1).R;

        return b00 * (1 - fx) * (1 - fy)
             + b10 * fx * (1 - fy)
             + b01 * (1 - fx) * fy
             + b11 * fx * fy;
    }

    public void Dispose()
    {
        _targetImage?.Dispose();
    }
}
