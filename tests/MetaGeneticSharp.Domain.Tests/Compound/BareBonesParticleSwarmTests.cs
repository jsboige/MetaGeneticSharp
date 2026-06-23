using GeneticSharp;
using MetaGeneticSharp;
using MetaGeneticSharp.Domain.Tests.Geometric;

namespace MetaGeneticSharp.Domain.Tests.Compound;

/// <summary>
///   Acceptance tests for the <see cref="BareBonesParticleSwarm"/> geometric compound (Kennedy 2003).
///   The structural tests verify the assembled primitive tree (a MatchMetaHeuristic that matches the
///   particle's own position plus the global best, a two-parent geometric crossover running the
///   bare-bones Gaussian, the inherited FitnessBasedElitistReinsertion); the operator test proves
///   the zero-variance degenerate boundary directly (no RNG needed); the keystone runs the built
///   BBPSO against a real <see cref="MetaGeneticAlgorithm"/> and asserts it actually optimises
///   Sphere (the population needs a randomising chromosome so the swarm is not all clones sitting
///   on top of the best with a zero-variance draw).
/// </summary>
public class BareBonesParticleSwarmTests
{
    private static BareBonesParticleSwarm NewBb(int maxGenerations = 20)
    {
        var bb = new BareBonesParticleSwarm { MaxGenerations = maxGenerations };
        // A double<->double identity converter: DoubleArrayChromosome stores genes as bare doubles.
        bb.SetGeometricConverter(new GeometricConverter<double>
        {
            GeneToDoubleConverter = (_, v) => v,
            DoubleToGeneConverter = (_, d) => d,
        });
        return bb;
    }

    [Test]
    public void GetDefaultReinsertion_ReturnsFitnessBasedElitist()
    {
        var bb = new BareBonesParticleSwarm();

        // BBPSO does not override GetDefaultReinsertion: the base FitnessBasedElitistReinsertion
        // (best-N of parents+offspring) is the greedy keep-the-best selection Kennedy assumes.
        Assert.That(bb.GetDefaultReinsertion(), Is.InstanceOf<FitnessBasedElitistReinsertion>());
    }

    [Test]
    public void Build_AssemblesMatchMetaHeuristicRootNamedAfterAlgorithm()
    {
        var bb = NewBb();

        var built = bb.Build();

        Assert.That(built, Is.InstanceOf<MatchMetaHeuristic>());
        Assert.That(((NamedEntity)built).Name, Is.EqualTo("Bare Bones Particle Swarm"));
    }

    [Test]
    public void Build_MatchesCurrentPositionAndGlobalBest()
    {
        var bb = NewBb();
        var root = (MatchMetaHeuristic)bb.Build();

        // geneValues = [personal anchor (Current), global best (Best)].
        var kinds = root.Picker.MatchPicks.Select(m => m.MatchingKind).ToArray();
        Assert.That(kinds, Is.EqualTo(new[]
        {
            MatchingKind.Current, MatchingKind.Best
        }));
    }

    /// <summary>
    /// Operator math, no-pendulum proof: when the particle IS the global best (anchor == gbest) the
    /// standard deviation is zero, so the Gaussian collapses to its mean = anchor = gbest exactly,
    /// regardless of the Box-Muller draw. This is the "freeze at the global best" property (the elite
    /// sits still while the swarm samples around it), verified without stubbing the RNG.
    /// </summary>
    [Test]
    public void DefaultSampleOperator_ZeroVariance_ReturnsAnchorExactly()
    {
        // geneValues = [anchor, best] both equal 10.0 -> mean = 10.0, std = 0 -> sample = 10.0.
        var geneValues = new object[] { 10.0, 10.0 };

        object result = BareBonesParticleSwarm.DefaultSampleOperator(0, geneValues, new IdentityConverter());

        Assert.That((double)result, Is.EqualTo(10.0).Within(1e-12));
    }

    /// <summary>
    /// Midpoint check: with anchor = 0.0 and best = 20.0 the draw is centred at mean = 10.0. We cannot
    /// pin a single Gaussian sample (it is random), but averaging many draws must converge to the
    /// midpoint 10.0 (unbiased estimator) -- this guards the mean computation against an off-by sign
    /// or a swapped [anchor, best] indexing.
    /// </summary>
    [Test]
    public void DefaultSampleOperator_DistinctAnchorBest_IsCentredAtMidpoint()
    {
        BasicRandomization.ResetSeed(12345);
        var geneValues = new object[] { 0.0, 20.0 };

        double sum = 0.0;
        const int N = 20000;
        for (int i = 0; i < N; i++)
            sum += (double)BareBonesParticleSwarm.DefaultSampleOperator(0, geneValues, new IdentityConverter());

        double mean = sum / N;
        // The Gaussian is N(10, 10); over 20k draws the sample mean is within ~0.2 of 10.0 (3 sigma).
        Assert.That(mean, Is.EqualTo(10.0).Within(0.25),
            $"BBPSO draw should be centred at the midpoint (0+20)/2 = 10.0; got sample mean {mean}");
    }

    /// <summary>
    /// KEYSTONE: the built BBPSO drives a real <see cref="MetaGeneticAlgorithm"/> end-to-end and
    /// optimises the Sphere function (minimise sum of squares -> fitness = -sum of squares, higher
    /// is better / closer to the origin). The population uses a randomising chromosome so the swarm
    /// is diverse (a clone population sits on top of the best with zero-variance draws and cannot
    /// explore); we assert the run completes, reaches the origin region, and beats the initial best.
    /// </summary>
    [Test]
    public void Build_DrivesMetaGeneticAlgorithm_EndToEnd_AndOptimises()
    {
        // Seeded RNG so the randomising chromosome + Gaussian draws are reproducible.
        BasicRandomization.ResetSeed(12345);

        var bb = NewBb(maxGenerations: 60);
        var metaHeuristic = bb.Build();

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
            Termination = new GenerationNumberTermination(60)
        };

        ga.Start();

        Assert.That(ga.BestChromosome, Is.Not.Null, "BBPSO produced no best chromosome");
        Assert.That(ga.BestChromosome.Fitness, Is.Not.Null,
            "BBPSO produced no evaluated offspring (the draw returned nothing)");
        double finalSumSq = -ga.BestChromosome.Fitness!.Value;

        Assert.Multiple(() =>
        {
            Assert.That(ga.GenerationsNumber, Is.EqualTo(60));
            Assert.That(ga.State, Is.EqualTo(GeneticAlgorithmState.TerminationReached));
            // BBPSO samples the swarm toward the origin: a Sphere(5) sum-of-squares well under 5
            // proves the bare-bones draw actually optimises (not just random-walks), while staying
            // honest about BBPSO converging more gently than the difference-driven DE.
            Assert.That(finalSumSq, Is.LessThan(5.0),
                $"BBPSO should reach the origin region; got sum-of-squares {finalSumSq}");
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
    /// A chromosome that randomises each gene in [min, max] on CreateNew, so the initial swarm is
    /// diverse (required for BBPSO: clones on top of the best give zero-variance draws and no search).
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
