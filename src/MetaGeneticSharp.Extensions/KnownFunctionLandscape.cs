using System.Diagnostics.CodeAnalysis;
using GeneticSharp;

namespace MetaGeneticSharp;

/// <summary>
/// Bridges the standard benchmark fitness functions (<c>KnownFunctions.cs</c>: Sphere,
/// Rastrigin, Rosenbrock, ... Dixon-Price) to the graphic heatmap renderer.
///
/// The benchmark functions are <see cref="IFitness"/> instances that maximize the negated
/// objective over an <see cref="IChromosome"/>; they have no direct link to
/// <see cref="LandscapeRenderer"/>, which samples a <c>Func&lt;double[], double&gt;</c> over
/// explicit ranges. This helper closes that gap: it adapts a 2D <c>(x, y)</c> point into the
/// geometry-agnostic chromosome the fitness reads (<see cref="KnownFunctionGenes"/>), looks up
/// the recommended 2D search box from <see cref="KnownFunctionsBounds"/>, and renders the
/// surface as a real PNG heatmap (red = high fitness, cyan = low) via
/// <see cref="LandscapeRenderer"/>.
///
/// Because the engine maximizes the negated objective, the heatmap's Black maximum marker
/// sits on the function's global optimum and the White minimum on its worst sampled point —
/// the same convention as the verbatim height-map heatmaps, now available for the analytic
/// benchmark surfaces too (LandscapeMode.KnownFunction).
/// </summary>
public static class KnownFunctionLandscape
{
    /// <summary>
    /// Renders a benchmark fitness over its recommended 2D bounds (from
    /// <see cref="KnownFunctionsBounds"/>). Use this for the ten standard functions whose type
    /// is registered there. For a wrapped or shifted fitness whose type is not registered (e.g.
    /// <see cref="ShiftedFitness"/>), pass explicit ranges with the overload below — the
    /// registry would otherwise fall back to its default box.
    /// </summary>
    /// <param name="fitness">One of the standard benchmark functions.</param>
    /// <param name="width">Heatmap canvas width in pixels (>= 2).</param>
    /// <param name="height">Heatmap canvas height in pixels (>= 2).</param>
    public static LandscapeHeatmap RenderHeatmap(IFitness fitness, int width = 400, int height = 300)
    {
        ArgumentNullException.ThrowIfNull(fitness);
        (double min, double max) = KnownFunctionsBounds.For(fitness.GetType());
        return RenderHeatmap(fitness, (min, max), (min, max), width, height);
    }

    /// <summary>
    /// Renders a benchmark fitness over explicit per-axis ranges. Symmetric with the
    /// <see cref="LandscapeRenderer.RenderHeatmap(Func{double[], double}, ValueTuple{double, double}, ValueTuple{double, double}, int, int)"/>
    /// delegate overload; use it when the recommended box does not apply (a shifted optimum, a
    /// zoomed view, or a fitness type not registered in <see cref="KnownFunctionsBounds"/>).
    /// </summary>
    public static LandscapeHeatmap RenderHeatmap(
        IFitness fitness,
        (double min, double max) xRange,
        (double min, double max) yRange,
        int width = 400,
        int height = 300)
    {
        ArgumentNullException.ThrowIfNull(fitness);
        return LandscapeRenderer.RenderHeatmap(
            point => fitness.Evaluate(new PointChromosome(point)),
            xRange,
            yRange,
            width,
            height);
    }

    /// <summary>
    /// Renders a benchmark fitness projected from an N-dimensional space onto the 2D canvas.
    /// The first two coordinates <c>(x, y)</c> map to the heatmap axes; the remaining
    /// <c>dimension - 2</c> coordinates are sampled uniformly at random inside
    /// <paramref name="extraRange"/> (or <paramref name="xRange"/> when no extra range is
    /// given), <paramref name="nbSamples"/> times, and the maximum fitness over those samples
    /// is reported as the pixel's value. This is the verbatim projection pattern used by the
    /// upstream GTK# controller
    /// (<c>LandscapeExplorerSampleController.GetFunctionValue</c> @ d05826fd, lines 638-672 in
    /// the source tree at
    /// <c>src/GeneticSharp.Runner.GtkApp/Samples/LandscapeExplorerSampleController.cs</c>):
    /// any pixel where the optimizer converges in dim &gt; 2 still earns a high-fitness pixel
    /// on the 2D canvas as long as some extra-coordinate sample pushes the underlying N-D
    /// surface to a comparable value.
    ///
    /// <para>Why this matters for MGS calibration. The MGS notebooks (MGS-6..MGS-19) sample
    /// the benchmark surfaces in <c>dim = 5</c> by default. Showing the user a flat 2D slice
    /// of a 5-D Schwefel does not teach them anything; the optimum hides behind unseen
    /// coordinates. Porting the upstream projection here lets MGS-7b render Schwefel /
    /// Rastrigin @ dim &amp;isin; &#123;2, 5, 10, 30&#125; with the same heatmap, and the
    /// user can see how the deceptive optimum becomes invisible at dim &gt; 2 unless
    /// <paramref name="nbSamples"/> grows fast enough to compensate.</para>
    ///
    /// <para>Sub-grain preserved verbatim: the controller uses <c>xRange</c> (not
    /// <c>yRange</c>) to draw the extra-coord random distribution, and reports the MAX over
    /// the <paramref name="nbSamples"/> draws (not the mean). Both choices are kept on
    /// purpose — this is a port of the upstream visualization, and diverging from the
    /// original would hide the calibration question this overload was added to answer. See
    /// the MGS-7b notebook for an empirical convergence study.</para>
    /// </summary>
    /// <param name="fitness">One of the standard benchmark functions.</param>
    /// <param name="dimension">Total dimensionality of the underlying N-D space
    /// (<c>&gt;= 2</c>). For <c>dimension == 2</c> the call forwards to the per-axis
    /// overload (no random extra coords).</param>
    /// <param name="nbSamples">Number of random extra-coordinate draws per canvas pixel.
    /// Controller default is 10; UI SpinButton 1..10000. More samples smooth the projection
    /// but multiply the per-pixel cost (roughly <c>width * height * nbSamples</c> evaluations).</param>
    /// <param name="width">Heatmap canvas width in pixels (>= 2).</param>
    /// <param name="height">Heatmap canvas height in pixels (>= 2).</param>
    public static LandscapeHeatmap RenderHeatmap(
        IFitness fitness,
        int dimension,
        int nbSamples = 10,
        int width = 400,
        int height = 300)
    {
        ArgumentNullException.ThrowIfNull(fitness);
        if (dimension < 2)
        {
            throw new ArgumentOutOfRangeException(
                nameof(dimension),
                dimension,
                "Multi-dim projection requires dimension >= 2.");
        }

        if (nbSamples < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(nbSamples),
                nbSamples,
                "nbSamples must be >= 1 (controller SpinButton default is 10).");
        }

        (double min, double max) box = KnownFunctionsBounds.For(fitness.GetType());

        if (dimension == 2)
        {
            // No random extra coords: forward to the existing 2D renderer so we don't duplicate
            // the parallel-for, min/max reduction, or extrema markers from LandscapeRenderer.
            return RenderHeatmap(fitness, box, box, width, height);
        }

        return RenderHeatmap(fitness, dimension, nbSamples, box, box, width, height);
    }

    /// <summary>
    /// Renders an N-D benchmark fitness projected onto the 2D canvas with explicit per-axis
    /// ranges. The first two coordinates map to <paramref name="xRange"/> / <paramref name="yRange"/>;
    /// the extra dimensions sample inside <paramref name="extraRange"/> (defaults to
    /// <paramref name="xRange"/> when <c>null</c> is passed, matching the upstream GTK# controller).
    /// </summary>
    [SuppressMessage(
        "Design",
        "CA1062:Validate arguments of public methods",
        Justification = "Forwarded to RenderHeatmap overload which already validates.")]
    public static LandscapeHeatmap RenderHeatmap(
        IFitness fitness,
        int dimension,
        int nbSamples,
        (double min, double max) xRange,
        (double min, double max) yRange,
        int width = 400,
        int height = 300,
        (double min, double max)? extraRange = null)
    {
        ArgumentNullException.ThrowIfNull(fitness);
        if (dimension < 2)
        {
            throw new ArgumentOutOfRangeException(
                nameof(dimension),
                dimension,
                "Multi-dim projection requires dimension >= 2.");
        }

        if (nbSamples < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(nbSamples),
                nbSamples,
                "nbSamples must be >= 1.");
        }

        if (dimension == 2)
        {
            return RenderHeatmap(fitness, xRange, yRange, width, height);
        }

        // Verbatim from LandscapeExplorerSampleController.GetFunctionValue @ d05826fd
        // (lines 644-666 in the upstream source). The controller uses the X range as the
        // distribution for the extra coordinates' random draws; we keep the same convention
        // so the projection on screen matches what the GTK# sample would have shown.
        // The randomization source is GeneticSharp's RandomizationProvider.Current (the
        // upstream controller's choice) — a notebook can override it via
        // `RandomizationProvider.Current = new FastRandomRandomization(seed)` to make a
        // run reproducible across launches.
        (double eMin, double eMax) = extraRange ?? xRange;
        double eRange = eMax - eMin;
        var rnd = RandomizationProvider.Current;

        return LandscapeRenderer.RenderHeatmap(
            point =>
            {
                // Verbatim: sampleCoords[0] = x; sampleCoords[1] = y; the remaining
                // dimensions are re-sampled on every nbSamples draw and we keep the MAX.
                var sampleCoords = new double[dimension];
                sampleCoords[0] = point[0];
                sampleCoords[1] = point[1];
                double fValue = double.MinValue;
                for (int i = 0; i < nbSamples; i++)
                {
                    for (int extraCoord = 2; extraCoord < dimension; extraCoord++)
                    {
                        sampleCoords[extraCoord] = eMin + rnd.GetDouble() * eRange;
                    }

                    fValue = Math.Max(fValue, fitness.Evaluate(new PointChromosome(sampleCoords)));
                }

                return fValue;
            },
            xRange,
            yRange,
            width,
            height);
    }
}
