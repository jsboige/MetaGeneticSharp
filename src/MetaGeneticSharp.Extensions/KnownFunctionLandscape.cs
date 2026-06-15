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
}
