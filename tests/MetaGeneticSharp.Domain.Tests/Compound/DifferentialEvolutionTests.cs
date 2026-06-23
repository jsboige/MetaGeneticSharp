using GeneticSharp;
using MetaGeneticSharp;
using MetaGeneticSharp.Domain.Tests.Geometric;

namespace MetaGeneticSharp.Domain.Tests.Compound;

/// <summary>
/// Acceptance tests for the <see cref="DifferentialEvolution"/> geometric compound (DE/rand/1/bin).
/// The structural tests verify the assembled primitive tree (a MatchMetaHeuristic that matches the
/// target plus three random donors, a four-parent geometric crossover, the inherited
/// FitnessBasedElitistReinsertion); the operator tests prove the differential math directly; the
/// keystone runs the built DE against a real <see cref="MetaGeneticAlgorithm"/> and asserts it
/// actually optimises Sphere (the population needs a randomising chromosome so the donors are not
/// all identical clones with a zero difference vector).
/// </summary>
public class DifferentialEvolutionTests
{
    private static DifferentialEvolution NewDe(int maxGenerations = 20)
    {
        var de = new DifferentialEvolution { MaxGenerations = maxGenerations };
        // A double<->double identity converter: DoubleArrayChromosome stores genes as bare doubles.
        de.SetGeometricConverter(new GeometricConverter<double>
        {
            GeneToDoubleConverter = (_, v) => v,
            DoubleToGeneConverter = (_, d) => d,
        });
        return de;
    }

    [Test]
    public void GetDefaultReinsertion_ReturnsFitnessBasedElitist()
    {
        var de = new DifferentialEvolution();

        // DE does not override GetDefaultReinsertion: the base FitnessBasedElitistReinsertion
        // (best-N of parents+offspring) is exactly DE's greedy "keep the trial only if no worse".
        Assert.That(de.GetDefaultReinsertion(), Is.InstanceOf<FitnessBasedElitistReinsertion>());
    }

    [Test]
    public void Build_AssemblesMatchMetaHeuristicRootNamedAfterAlgorithm()
    {
        var de = NewDe();

        var built = de.Build();

        Assert.That(built, Is.InstanceOf<MatchMetaHeuristic>());
        Assert.That(((NamedEntity)built).Name, Is.EqualTo("Differential Evolution"));
    }

    [Test]
    public void Build_MatchesTargetAndThreeRandomDonors()
    {
        var de = NewDe();
        var root = (MatchMetaHeuristic)de.Build();

        // geneValues = [target x_i, donor r1, donor r2, donor r3] = Current + three Random.
        var kinds = root.Picker.MatchPicks.Select(m => m.MatchingKind).ToArray();
        Assert.That(kinds, Is.EqualTo(new[]
        {
            MatchingKind.Current, MatchingKind.Random, MatchingKind.Random, MatchingKind.Random
        }));
    }

    [Test]
    public void Build_WrapsMainWithNoMutationAndForcedReinsertion()
    {
        var de = NewDe();
        var built = de.Build();

        var reinsertionWrapper = built.SubMetaHeuristic as ReinsertionMetaHeuristic;
        Assert.That(reinsertionWrapper, Is.Not.Null, "Build should wrap with a ReinsertionMetaHeuristic");
        Assert.That(reinsertionWrapper.StaticOperator, Is.InstanceOf<FitnessBasedElitistReinsertion>(),
            "DE inherits the base FitnessBasedElitistReinsertion default (greedy selection)");

        var noMutationLayer = reinsertionWrapper.SubMetaHeuristic as ScopedMetaHeuristic;
        Assert.That(noMutationLayer, Is.Not.Null, "the reinsertion wrapper's sub should be the No-Mutation scoped layer");
        Assert.That(((NamedEntity)noMutationLayer).Name, Does.Contain("No-Mutation"));
    }

    /// <summary>
    /// Operator math, no-pendulum proof: with CR = 1.0 every gene takes the mutant, so the trial
    /// is exactly the differential mutant v = r1 + F * (r2 - r3). (GetDouble() in [0,1) is always
    /// &lt; 1.0, so no RNG stub is needed for this branch.)
    /// </summary>
    [Test]
    public void DefaultTrialOperator_FullCrossover_ComputesDifferentialMutant()
    {
        double F = 0.7;
        // geneValues = [target, r1, r2, r3] for a single gene.
        var geneValues = new object[] { 10.0, 1.0, 5.0, 3.0 };

        object result = DifferentialEvolution.DefaultTrialOperator(0, geneValues, new IdentityConverter(), scale: F, crossoverRate: 1.0);

        // v = r1 + F * (r2 - r3) = 1.0 + 0.7 * (5.0 - 3.0) = 1.0 + 1.4 = 2.4
        Assert.That((double)result, Is.EqualTo(2.4).Within(1e-12));
    }

    /// <summary>
    /// With CR = 0.0 every gene keeps the target value (GetDouble() &gt;= 0 is never &lt; 0): the
    /// trial equals the target, so DE cannot make a move this turn (a sound degenerate boundary).
    /// </summary>
    [Test]
    public void DefaultTrialOperator_ZeroCrossover_KeepsTarget()
    {
        double F = 0.7;
        var geneValues = new object[] { 10.0, 1.0, 5.0, 3.0 };

        object result = DifferentialEvolution.DefaultTrialOperator(0, geneValues, new IdentityConverter(), scale: F, crossoverRate: 0.0);

        Assert.That((double)result, Is.EqualTo(10.0).Within(1e-12));
    }

    /// <summary>
    /// KEYSTONE: the built DE drives a real <see cref="MetaGeneticAlgorithm"/> end-to-end and
    /// optimises the Sphere function (minimise sum of squares -> fitness = -sum of squares, higher
    /// is better / closer to the origin). The population uses a randomising chromosome so the three
    /// donors are not identical clones (a clone population has a zero difference vector and DE could
    /// not move); we assert the run completes, reaches the origin region, and beats the initial best.
    /// </summary>
    [Test]
    public void Build_DrivesMetaGeneticAlgorithm_EndToEnd_AndOptimises()
    {
        // Seeded RNG so the randomising chromosome + donor draws are reproducible.
        BasicRandomization.ResetSeed(12345);

        var de = NewDe(maxGenerations: 40);
        var metaHeuristic = de.Build();

        var chromosome = new RandomDoubleChromosome(min: -10.0, max: 10.0, length: 5);
        var fitness = new FuncFitness(c =>
        {
            var values = ((RandomDoubleChromosome)c).GetDoubleValues();
            double s = 0.0;
            for (int i = 0; i < values.Length; i++) s += values[i] * values[i];
            return -s;
        });

        var population = new MetaPopulation(30, 30, chromosome);
        var ga = new MetaGeneticAlgorithm(
            population,
            fitness,
            new EliteSelection(),
            new UniformCrossover(0.5f),
            new UniformMutation(true),
            metaHeuristic)
        {
            Termination = new GenerationNumberTermination(40)
        };

        ga.Start();

        // BestChromosome.Fitness is null unless the run evaluated offspring; guard explicitly.
        Assert.That(ga.BestChromosome, Is.Not.Null, "DE produced no best chromosome");
        Assert.That(ga.BestChromosome.Fitness, Is.Not.Null,
            "DE produced no evaluated offspring (crossover returned nothing)");
        double finalSumSq = -ga.BestChromosome.Fitness!.Value;

        Assert.Multiple(() =>
        {
            Assert.That(ga.GenerationsNumber, Is.EqualTo(40));
            Assert.That(ga.State, Is.EqualTo(GeneticAlgorithmState.TerminationReached));
            // DE drives Sphere(5) from a random [-10,10]^5 start (expected sum-sq ~166) into the
            // origin region: sum of squares well under 1 proves the differential actually optimises.
            Assert.That(finalSumSq, Is.LessThan(1.0),
                $"DE should reach the origin region; got sum-of-squares {finalSumSq}");
        });
    }

    /// <summary>A bare double&lt;-&gt;double converter for direct operator tests (no embedding).</summary>
    private sealed class IdentityConverter : IGeometricConverter
    {
        public bool IsOrdered => false;
        public double GeneToDouble(int geneIndex, object geneValue) => (double)geneValue;
        public object DoubleToGene(int geneIndex, double metricValue) => metricValue;
        public IGeometryEmbedding<object> GetEmbedding() => null!;
    }

    /// <summary>
    /// A chromosome that randomises each gene in [min, max] on CreateNew, so the initial population
    /// is diverse (required for DE: identical donors give a zero difference vector).
    /// </summary>
    private sealed class RandomDoubleChromosome : ChromosomeBase
    {
        private readonly double _min;
        private readonly double _max;

        public RandomDoubleChromosome(double min, double max, int length) : base(length)
        {
            _min = min;
            _max = max;
            Seed();
        }

        private void Seed()
        {
            var rnd = RandomizationProvider.Current;
            for (int i = 0; i < Length; i++)
                ReplaceGene(i, new Gene(_min + rnd.GetDouble() * (_max - _min)));
        }

        public override IChromosome CreateNew() => new RandomDoubleChromosome(_min, _max, Length);

        public override Gene GenerateGene(int geneIndex) =>
            new Gene(_min + RandomizationProvider.Current.GetDouble() * (_max - _min));

        public double[] GetDoubleValues() => GetGenes().Select(g => (double)g.Value).ToArray();
    }
}
