using GeneticSharp;
using MetaGeneticSharp;
using MetaGeneticSharp.Domain.Tests.Geometric;

namespace MetaGeneticSharp.Domain.Tests.Compound;

/// <summary>
/// Phase 4 slice 5 acceptance tests: the Equilibrium Optimizer compound metaheuristic.
/// The structural tests verify the assembled primitive tree (a MatchMetaHeuristic root with the
/// equilibrium/centroid custom-match steps and the geometric crossover operators); the end-to-end
/// keystone runs the built EO against a real <see cref="MetaGeneticAlgorithm"/>, proving every
/// composed primitive (Evolution/Generation/Individual-scoped params, the 4-best + centroid custom
/// match with child crossover, the equilibrium generation operator) executes without fault.
/// </summary>
public class EquilibriumOptimizerTests
{
    private static EquilibriumOptimizer NewEo(int maxGenerations = 20)
    {
        var eo = new EquilibriumOptimizer { MaxGenerations = maxGenerations };
        // A double↔double identity converter: DoubleArrayChromosome stores genes as bare doubles.
        eo.SetGeometricConverter(new GeometricConverter<double>
        {
            GeneToDoubleConverter = (_, v) => v,
            DoubleToGeneConverter = (_, d) => d,
        });
        return eo;
    }

    [Test]
    public void GetDefaultReinsertion_ReturnsFitnessBasedElitist()
    {
        var eo = new EquilibriumOptimizer();

        // EO does not override GetDefaultReinsertion, so it inherits the base FitnessBasedElitistReinsertion.
        Assert.That(eo.GetDefaultReinsertion(), Is.InstanceOf<FitnessBasedElitistReinsertion>());
    }

    [Test]
    public void Build_AssemblesMatchMetaHeuristicRootNamedAfterAlgorithm()
    {
        var eo = NewEo();

        // Build() returns the assembled main heuristic itself. EO's root is a MatchMetaHeuristic
        // (it uses custom-match steps for the 4-best + centroid selection), carrying the algorithm name + citation.
        var built = eo.Build();

        Assert.That(built, Is.InstanceOf<MatchMetaHeuristic>());
        Assert.That(((NamedEntity)built).Name, Is.EqualTo("Equilibrium Optimizer"));
    }

    /// <summary>
    /// mealpy draws the equilibrium candidate c_eq uniformly over the 5-member pool
    /// (4 best + centroid): c_pool[generator.integers(0, len(c_pool))]. The port's final
    /// custom-match step must therefore draw uniformly too. With RandomIncludesCurrent left
    /// to its struct default (false), the random pick excludes index 0 (the current best of
    /// the pool) and remaps it onto the last index (the centroid): the port would NEVER draw
    /// its own best chromosome as donor and would over-draw the centroid (25% instead of 20%).
    /// </summary>
    [Test]
    public void Build_EquilibriumPoolDraw_IsUniformOverFiveCandidates()
    {
        var eo = NewEo();
        var root = (MatchMetaHeuristic)eo.Build();

        // The third custom-match step is the pool draw over [best1..best4, centroid].
        var drawStep = root.Picker.CustomMatch[2];
        Assert.That(drawStep.Count, Is.EqualTo(1));
        Assert.That(drawStep[0].MatchingKind, Is.EqualTo(MatchingKind.Random));
        Assert.That(drawStep[0].RandomIncludesCurrent, Is.True,
            "the pool draw must include the first candidate (the best): mealpy samples uniformly over the 5 members");

        // Empirical distribution of the draw over a 5-member pool (seeded RNG): each member ~1/5.
        FastRandomRandomization.ResetSeed(12345);
        var pool = Enumerable.Range(0, 5)
            .Select(i => (IChromosome)new DoubleArrayChromosome(new[] { (double)i, (double)i }))
            .ToList();
        var counts = new int[5];
        for (int k = 0; k < 5000; k++)
        {
            var picked = root.Picker.SelectMatches(drawStep, null, null, 0, null, pool);
            Assert.That(picked.Count, Is.EqualTo(1));
            counts[(int)((DoubleArrayChromosome)picked[0]).GetDoubleValues()[0]]++;
        }

        Assert.That(counts[0], Is.EqualTo(1000).Within(150),
            "the current best must be drawable as donor (mealpy: uniform 1/5)");
        foreach (var c in counts)
            Assert.That(c, Is.EqualTo(1000).Within(150), "uniform draw over the 5 equilibrium-pool candidates");
    }

    [Test]
    public void Build_WrapsMainWithNoMutationAndForcedReinsertion()
    {
        var eo = NewEo();

        // Build() (from GeometricMetaHeuristicBase) wraps the main heuristic with the No-Mutation scope
        // then a forced reinsertion layer, exposed through the returned object's SubMetaHeuristic chain.
        var built = eo.Build();

        var reinsertionWrapper = built.SubMetaHeuristic as ReinsertionMetaHeuristic;
        Assert.That(reinsertionWrapper, Is.Not.Null, "Build should wrap with a ReinsertionMetaHeuristic");
        Assert.That(reinsertionWrapper.StaticOperator, Is.InstanceOf<FitnessBasedElitistReinsertion>(),
            "EO inherits the base FitnessBasedElitistReinsertion default");

        var noMutationLayer = reinsertionWrapper.SubMetaHeuristic as ScopedMetaHeuristic;
        Assert.That(noMutationLayer, Is.Not.Null, "the reinsertion wrapper's sub should be the No-Mutation scoped layer");
        Assert.That(((NamedEntity)noMutationLayer).Name, Does.Contain("No-Mutation"));
    }

    /// <summary>
    /// KEYSTONE: the built EO drives a real <see cref="MetaGeneticAlgorithm"/> end-to-end. This
    /// exercises every composed primitive — the Evolution-scoped pbSize, the Generation-scoped t
    /// (Eq. 9), the Individual-scoped lambda/r/f/r1/r2/gcp arrays, the custom-match 4-best +
    /// centroid selection with the child crossover heuristic, and the equilibrium generation
    /// operator over a GeometricCrossover<object> — against a live evolution.
    /// </summary>
    [Test]
    public void Build_DrivesMetaGeneticAlgorithm_EndToEnd()
    {
        var eo = NewEo(maxGenerations: 15);
        var metaHeuristic = eo.Build();

        // Fitness: minimize the sum of squares (target is the origin), so higher fitness = closer to (0,0).
        // Population must be >= 4 for the EO's 4-best custom-match selection.
        var chromosome = new DoubleArrayChromosome(new double[] { 50.0, 50.0 });
        var fitness = new FuncFitness(c =>
        {
            var values = ((DoubleArrayChromosome)c).GetDoubleValues();
            return -(values[0] * values[0] + values[1] * values[1]);
        });

        var population = new MetaPopulation(20, 20, chromosome);
        var ga = new MetaGeneticAlgorithm(
            population,
            fitness,
            new EliteSelection(),
            new UniformCrossover(0.5f),
            new UniformMutation(true),
            metaHeuristic)
        {
            Termination = new GenerationNumberTermination(15)
        };

        ga.Start();

        Assert.Multiple(() =>
        {
            Assert.That(ga.GenerationsNumber, Is.EqualTo(15));
            Assert.That(ga.State, Is.EqualTo(GeneticAlgorithmState.TerminationReached));
            Assert.That(ga.BestChromosome, Is.Not.Null);
            Assert.That(ga.BestChromosome.Fitness, Is.Not.Null);
        });
    }
}
