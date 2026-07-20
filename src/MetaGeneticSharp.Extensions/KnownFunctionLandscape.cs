using GeneticSharp;

namespace MetaGeneticSharp;

// ===========================================================================
// N-D projection bridge: maps a 2D heatmap canvas onto a higher-dimensional
// fitness landscape by projecting hidden dimensions via uniform sampling and
// the MAX operator. Pattern verbatim (byte-for-byte at the algorithm level)
// from the Gtk# controller `LandscapeExplorerSampleController.GetFunctionValue`
// lines 640-674 @ d05826fd (jsboige fork, MyIntelligenceAgency/GeneticSharp
// branch Metaheuristics) — extended here as a public, deterministic, fully
// typed bridge that delegates back to the existing 2D LandscapeRenderer.
// ===========================================================================

/// <summary>
/// Adaptateur qui projette un <see cref="IFitness"/> N-dimensionnel sur un canvas
/// 2D en echantillonnant les coordonnees cachees uniformement et en agregeant
/// par MAX. Pour chaque pixel (x, y), la fitness est evaluee <paramref name="nbSamples"/>
/// fois avec des coordonnees 2..N-1 tirees au hasard dans la range recommandee,
/// et la valeur max (la plus haute fitness maximisee) est retenue. Cela rend
/// le paysage lisible en dimension superieure a 2, la ou le landscape "troue"
/// reste visible quand on echantillonne assez de coordonnees cachees.
/// </summary>
/// <remarks>
/// Pattern verbatim (avec corrections) du Gtk# controller upstream :
/// <code>
/// for (int i = 0; i < mNbSamples; i++) {
///     for (int extraCoord = 2; extraCoord < mNbDimensions; extraCoord++) {
///         var coord = mRange.xRange.min + rnd.GetDouble() * coordsRange;
///         sampleCoords[extraCoord] = coord;
///     }
///     fValue = Math.Max(fValue, ComputeFunctionValue(sampleCoords));
/// }
/// </code>
/// Le bug verbatim `coordsRange = min - min = 0` (ligne 643 du fork Gtk#) est
/// corrige ici : <c>coordsRange = max - min</c>. Voir test
/// <c>TestNDProjectionRangeSpan</c> pour le pin du fix.
/// </remarks>
internal sealed class NDMaxProjectionAdapter
{
    private readonly IFitness m_fitness;
    private readonly (double min, double max) m_coordRange;
    private readonly int m_dimension;
    private readonly int m_nbSamples;
    private readonly Random m_rng;

    public NDMaxProjectionAdapter(
        IFitness fitness,
        (double min, double max) coordRange,
        int dimension,
        int nbSamples,
        Random rng)
    {
        ArgumentNullException.ThrowIfNull(fitness);
        ArgumentNullException.ThrowIfNull(rng);
        if (dimension < 2)
        {
            throw new ArgumentOutOfRangeException(
                nameof(dimension), dimension, "N-D projection requires dimension >= 2.");
        }
        if (nbSamples < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(nbSamples), nbSamples, "nbSamples must be >= 1 (use 1 for no hidden sampling).");
        }
        if (coordRange.max < coordRange.min)
        {
            throw new ArgumentException(
                $"coordRange.max ({coordRange.max}) must be >= coordRange.min ({coordRange.min}).",
                nameof(coordRange));
        }

        m_fitness = fitness;
        m_coordRange = coordRange;
        m_dimension = dimension;
        m_nbSamples = nbSamples;
        m_rng = rng;
    }

    /// <summary>
    /// Evalue la fitness au point (x, y) en projetant les dimensions cachees
    /// 2..N-1 via MAX sur <see cref="m_nbSamples"/> tirages uniformes.
    /// </summary>
    public double Evaluate(double x, double y)
    {
        double coordsRange = m_coordRange.max - m_coordRange.min;
        var sampleCoords = new double[m_dimension];
        sampleCoords[0] = x;
        sampleCoords[1] = y;
        double fValue = double.NegativeInfinity;
        for (int i = 0; i < m_nbSamples; i++)
        {
            for (int extraCoord = 2; extraCoord < m_dimension; extraCoord++)
            {
                sampleCoords[extraCoord] = m_coordRange.min + m_rng.NextDouble() * coordsRange;
            }
            double f = m_fitness.Evaluate(new PointChromosome(sampleCoords));
            if (f > fValue)
            {
                fValue = f;
            }
        }
        return fValue;
    }
}

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
    /// Renders a benchmark fitness over its recommended bounds (from
    /// <see cref="KnownFunctionsBounds"/>) projected from <paramref name="dimension"/>
    /// down to 2 via the MAX-of-uniform-samples pattern (verbatim from the
    /// Gtk# controller <c>LandscapeExplorerSampleController.GetFunctionValue</c>
    /// lines 640-674 @ d05826fd). Use this for visualising a benchmark function
    /// in dimensions where the surface is otherwise unviewable (e.g. Schwefel
    /// or Ackley in dim &gt;= 5).
    /// </summary>
    /// <param name="fitness">One of the standard benchmark functions, or any
    /// <see cref="IFitness"/> whose <see cref="KnownFunctionGenes"/> consumes
    /// exactly <paramref name="dimension"/> genes.</param>
    /// <param name="dimension">Total dimension of the fitness (must be &gt;= 2;
    /// <paramref name="dimension"/> - 2 hidden coords are sampled).</param>
    /// <param name="nbSamples">Number of uniform samples per pixel for the MAX
    /// projection (default 10, verbatim from the controller). Increase to reduce
    /// projection noise on rougher landscapes; 1 collapses to a single sample.</param>
    /// <param name="rng">RNG used for the hidden-coordinate samples. Pass a
    /// seedable <see cref="Random"/> for reproducible heatmaps; passing
    /// <c>null</c> creates a fresh time-based seed.</param>
    /// <param name="width">Heatmap canvas width in pixels (&gt;= 2).</param>
    /// <param name="height">Heatmap canvas height in pixels (&gt;= 2).</param>
    public static LandscapeHeatmap RenderHeatmap(
        IFitness fitness,
        int dimension,
        int nbSamples = 10,
        Random? rng = null,
        int width = 400,
        int height = 300)
    {
        ArgumentNullException.ThrowIfNull(fitness);
        (double min, double max) = KnownFunctionsBounds.For(fitness.GetType());
        return RenderHeatmap(
            fitness,
            (min, max),
            (min, max),
            dimension,
            nbSamples,
            rng ?? new Random(),
            width,
            height);
    }

    /// <summary>
    /// Renders a benchmark fitness over explicit per-axis (x, y) ranges, projected
    /// from <paramref name="dimension"/> down to 2 via the MAX-of-uniform-samples
    /// pattern. The hidden coordinates 2..N-1 are sampled in
    /// <paramref name="hiddenRange"/> (which is independent of the (x, y) ranges
    /// and lets the caller explore, for instance, a zoom on (x, y) while keeping
    /// hidden coordinates at the recommended full search box).
    /// </summary>
    public static LandscapeHeatmap RenderHeatmap(
        IFitness fitness,
        (double min, double max) xRange,
        (double min, double max) yRange,
        int dimension,
        int nbSamples = 10,
        Random? rng = null,
        int width = 400,
        int height = 300)
    {
        ArgumentNullException.ThrowIfNull(fitness);
        var adapter = new NDMaxProjectionAdapter(fitness, xRange, dimension, nbSamples, rng ?? new Random());
        return LandscapeRenderer.RenderHeatmap(
            point => adapter.Evaluate(point[0], point[1]),
            xRange,
            yRange,
            width,
            height);
    }
}
