using GeneticSharp;
using MetaGeneticSharp;

namespace MetaGeneticSharp.Extensions.Tests;

/// <summary>
/// Unit tests for the mealpy-budget center-bias comparison harness:
/// <see cref="EvaluationBudget"/>, <see cref="CountingFitness"/>,
/// <see cref="RandomSearchOptimizer"/> and <see cref="CenterBiasBenchmark"/>.
///
/// The keystone is <see cref="CenterBiasBenchmark_DetectsBias_WithCenterOnlyOptimizer"/>:
/// a deliberately center-biased optimizer (it only ever samples the domain center)
/// scores perfectly on the centered function but poorly once the optimum is relocated,
/// so the harness reports a large positive delta — the bias signature. The contrast is
/// <see cref="CenterBiasBenchmark_UnbiasedRandomSearch_HasNearZeroDelta"/>: uniform
/// random search covers the box evenly and is about as good either way, so its delta
/// sits near zero. That centered/biased contrast is exactly what the harness measures.
/// </summary>
[TestFixture]
public class CenterBiasBenchmarkTests
{
    // =========================================================================
    // EvaluationBudget: the mealpy NFE shared by every optimizer.
    // =========================================================================
    [Test]
    public void EvaluationBudget_NonPositive_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new EvaluationBudget(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new EvaluationBudget(-5));
    }

    [Test]
    public void EvaluationBudget_GenerationsFor_IsFloorOfEvaluationsOverPopulation()
    {
        // 1000 evaluations / 30 per generation = 33 generations (floor).
        Assert.That(new EvaluationBudget(1000).GenerationsFor(30), Is.EqualTo(33));
    }

    [Test]
    public void EvaluationBudget_GenerationsFor_ClampsToAtLeastOne()
    {
        // A population larger than the whole budget still buys one generation.
        Assert.That(new EvaluationBudget(10).GenerationsFor(50), Is.EqualTo(1));
    }

    [Test]
    public void EvaluationBudget_GenerationsFor_NonPositivePopulation_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new EvaluationBudget(100).GenerationsFor(0));
    }

    // =========================================================================
    // CountingFitness: observe evaluation count, pass the value through.
    // =========================================================================
    [Test]
    public void CountingFitness_CountsEvaluations_AndPassesValueThrough()
    {
        var counting = new CountingFitness(new SphereFitness());
        double v = counting.Evaluate(new DoubleArrayChromosome(new[] { 1.0, 1.0 }));
        Assert.That(v, Is.EqualTo(-2.0));               // f(1,1)=2 -> fitness -2, unchanged.
        Assert.That(counting.Evaluations, Is.EqualTo(1));
        counting.Evaluate(new DoubleArrayChromosome(new[] { 0.0, 0.0 }));
        Assert.That(counting.Evaluations, Is.EqualTo(2));
    }

    [Test]
    public void CountingFitness_IsExhausted_WhenCountReachesBudget()
    {
        var counting = new CountingFitness(new SphereFitness());
        var budget = new EvaluationBudget(2);
        Assert.That(counting.IsExhausted(budget), Is.False);
        counting.Evaluate(new DoubleArrayChromosome(new[] { 0.0, 0.0 }));
        Assert.That(counting.IsExhausted(budget), Is.False);
        counting.Evaluate(new DoubleArrayChromosome(new[] { 0.0, 0.0 }));
        Assert.That(counting.IsExhausted(budget), Is.True);
    }

    [Test]
    public void CountingFitness_NullInner_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new CountingFitness(null!));
    }

    // =========================================================================
    // RandomSearchOptimizer: the unbiased control baseline.
    // =========================================================================
    [Test]
    public void RandomSearch_SameSeed_IsReproducible()
    {
        var request = new OptimizerRequest(new SphereFitness(), (-5.12, 5.12), 2, 500);
        double a = new RandomSearchOptimizer(7).Run(request);
        double b = new RandomSearchOptimizer(7).Run(request);
        Assert.That(b, Is.EqualTo(a));
    }

    [Test]
    public void RandomSearch_SpendsExactlyTheRequestedBudget()
    {
        var counting = new CountingFitness(new SphereFitness());
        new RandomSearchOptimizer(1).Run(new OptimizerRequest(counting, (-5.12, 5.12), 2, 750));
        Assert.That(counting.Evaluations, Is.EqualTo(750));
    }

    [Test]
    public void RandomSearch_ApproachesOptimum_OnCenteredSphere()
    {
        // With a few thousand uniform draws over a small box, the best Sphere sample is
        // close to the optimum (fitness near 0). Deterministic via the fixed seed.
        double best = new RandomSearchOptimizer(1).Run(new OptimizerRequest(new SphereFitness(), (-5.12, 5.12), 2, 5000));
        Assert.That(best, Is.GreaterThan(-0.5)); // objective < 0.5
    }

    [Test]
    public void RandomSearch_DimensionBelowTwo_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new RandomSearchOptimizer().Run(new OptimizerRequest(new SphereFitness(), (-5.12, 5.12), 1, 100)));
    }

    [Test]
    public void RandomSearch_NonPositiveEvaluations_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new RandomSearchOptimizer().Run(new OptimizerRequest(new SphereFitness(), (-5.12, 5.12), 2, 0)));
    }

    // =========================================================================
    // CenterBiasBenchmark: the centered-vs-shifted measurement under equal budget.
    // =========================================================================

    /// <summary>
    /// A deliberately center-biased optimizer: it ignores the domain and only ever
    /// samples the all-zero center, spending the full budget there. It is optimal on a
    /// centered function and blind to a relocated optimum — the worst-case bias.
    /// </summary>
    private static double CenterOnlyOptimizer(OptimizerRequest request)
    {
        double[] center = new double[request.Dimension]; // all zeros = domain center
        double best = double.NegativeInfinity;
        for (int e = 0; e < request.Evaluations; e++)
        {
            double f = request.Fitness.Evaluate(new DoubleArrayChromosome(center));
            if (f > best) best = f;
        }
        return best;
    }

    [Test]
    public void CenterBiasBenchmark_DetectsBias_WithCenterOnlyOptimizer()
    {
        // KEYSTONE: the center-only optimizer nails the centered Sphere (optimum at 0)
        // but is blind once the optimum is relocated, so the harness reports a large
        // positive delta = the center-bias signature.
        var budget = new EvaluationBudget(200);
        CenterBiasResult result = CenterBiasBenchmark.Run(
            new SphereFitness(), dimension: 2, budget, CenterOnlyOptimizer, shiftMagnitude: 2.0, seed: 42);

        Assert.That(result.CenteredObjective, Is.EqualTo(0.0).Within(1e-12)); // perfect at the center
        Assert.That(result.ShiftedObjective, Is.GreaterThan(0.5));            // blind off-center
        Assert.That(result.Delta, Is.GreaterThan(0.5));                       // bias detected
        // The shifted objective is exactly f_inner(-shift) = sum(shift^2): the squared
        // distance from the center to the relocated optimum.
        double[] shift = ShiftVectors.Seeded(2, 2.0, 42);
        double expected = shift[0] * shift[0] + shift[1] * shift[1];
        Assert.That(result.ShiftedObjective, Is.EqualTo(expected).Within(1e-9));
    }

    [Test]
    public void CenterBiasBenchmark_UnbiasedRandomSearch_HasNearZeroDelta()
    {
        // CONTRAST: uniform random search has no center preference, so it does about as
        // well centered as shifted and its delta sits near zero. This is what makes the
        // biased optimizer's positive delta meaningful rather than an artefact.
        var budget = new EvaluationBudget(4000);
        var search = new RandomSearchOptimizer(3);
        CenterBiasResult result = CenterBiasBenchmark.Run(
            new SphereFitness(), dimension: 2, budget, search.Run, shiftMagnitude: 2.0, seed: 42);

        Assert.That(Math.Abs(result.Delta), Is.LessThan(1.0));
        // And it is dramatically smaller than the worst-case biased delta on the same setup.
        CenterBiasResult biased = CenterBiasBenchmark.Run(
            new SphereFitness(), dimension: 2, budget, CenterOnlyOptimizer, shiftMagnitude: 2.0, seed: 42);
        Assert.That(Math.Abs(result.Delta), Is.LessThan(biased.Delta));
    }

    [Test]
    public void CenterBiasBenchmark_RecordsBudgetEvaluations_ForBothRuns()
    {
        var budget = new EvaluationBudget(128);
        CenterBiasResult result = CenterBiasBenchmark.Run(
            new SphereFitness(), dimension: 2, budget, CenterOnlyOptimizer, shiftMagnitude: 1.0, seed: 7);
        Assert.That(result.CenteredEvaluations, Is.EqualTo(128));
        Assert.That(result.ShiftedEvaluations, Is.EqualTo(128));
    }

    [Test]
    public void CenterBiasBenchmark_Shift_IsReproduciblePerSeed()
    {
        CenterBiasResult result = CenterBiasBenchmark.Run(
            new SphereFitness(), dimension: 3, new EvaluationBudget(50), CenterOnlyOptimizer, shiftMagnitude: 2.0, seed: 99);
        Assert.That(result.Shift, Is.EqualTo(ShiftVectors.Seeded(3, 2.0, 99)));
    }

    [Test]
    public void CenterBiasBenchmark_DimensionBelowTwo_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CenterBiasBenchmark.Run(
            new SphereFitness(), dimension: 1, new EvaluationBudget(10), CenterOnlyOptimizer, shiftMagnitude: 1.0, seed: 0));
    }

    [Test]
    public void CenterBiasBenchmark_NullArguments_Throw()
    {
        var budget = new EvaluationBudget(10);
        Assert.Throws<ArgumentNullException>(() => CenterBiasBenchmark.Run(null!, 2, budget, CenterOnlyOptimizer, 1.0, 0));
        Assert.Throws<ArgumentNullException>(() => CenterBiasBenchmark.Run(new SphereFitness(), 2, null!, CenterOnlyOptimizer, 1.0, 0));
        Assert.Throws<ArgumentNullException>(() => CenterBiasBenchmark.Run(new SphereFitness(), 2, budget, null!, 1.0, 0));
    }

    // =========================================================================
    // RunSuite: every function in both centered and shifted form, one row each.
    // =========================================================================
    [Test]
    public void RunSuite_ReturnsOneResultPerProblem_WithDistinctSeeds()
    {
        var problems = new (IFitness, int)[]
        {
            (new SphereFitness(), 2),
            (new RastriginFitness(), 2),
            (new AckleyFitness(), 2),
        };
        IReadOnlyList<CenterBiasResult> results = CenterBiasBenchmark.RunSuite(
            problems, new EvaluationBudget(100), CenterOnlyOptimizer, shiftMagnitude: 1.0, seed: 10);

        Assert.That(results, Has.Count.EqualTo(3));
        Assert.That(results[0].Function, Is.EqualTo(nameof(SphereFitness)));
        Assert.That(results[2].Function, Is.EqualTo(nameof(AckleyFitness)));
        // Distinct per-function seeds (seed + index): the relocations are not identical.
        Assert.That(results[0].Shift, Is.EqualTo(ShiftVectors.Seeded(2, 1.0, 10)));
        Assert.That(results[2].Shift, Is.EqualTo(ShiftVectors.Seeded(2, 1.0, 12)));
        Assert.That(results[0].Shift, Is.Not.EqualTo(results[2].Shift));
    }

    [Test]
    public void RunSuite_NullProblems_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => CenterBiasBenchmark.RunSuite(
            null!, new EvaluationBudget(10), CenterOnlyOptimizer, 1.0, 0));
    }
}
