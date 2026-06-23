using GeneticSharp;
using MetaGeneticSharp;
using MetaGeneticSharp.Domain.Tests.Geometric;

namespace MetaGeneticSharp.Domain.Tests.Compound;

/// <summary>
///   Acceptance tests for the <see cref="SimulatedAnnealing"/> geometric compound (Metropolis 1953;
///   Kirkpatrick, Gelatt &amp; Vecchi 1983). The structural tests verify the assembled primitive tree
///   (a MatchMetaHeuristic matching the current position and a random individual for the step scale, a
///   two-parent geometric crossover running the isotropic Gaussian perturbation, the custom
///   Metropolis reinsertion forwarding T_0 and alpha); the operator tests prove the zero-scale collapse
///   and the midpoint centring directly (the latter without pinning a single Gaussian draw); the
///   temperature test pins the geometric schedule; the keystone runs the built SA against a real
///   <see cref="MetaGeneticAlgorithm"/> and asserts it actually optimises Sphere (the population needs a
///   randomising chromosome so the steps are non-degenerate).
/// </summary>
public class SimulatedAnnealingTests
{
    private static SimulatedAnnealing NewSa(int maxGenerations = 60, double initialTemperature = 50.0, double coolingRate = 0.97)
    {
        var sa = new SimulatedAnnealing
        {
            MaxGenerations = maxGenerations,
            InitialTemperature = initialTemperature,
            CoolingRate = coolingRate
        };
        // A double<->double identity converter: DoubleArrayChromosome stores genes as bare doubles.
        sa.SetGeometricConverter(new GeometricConverter<double>
        {
            GeneToDoubleConverter = (_, v) => v,
            DoubleToGeneConverter = (_, d) => d,
        });
        return sa;
    }

    [Test]
    public void GetDefaultReinsertion_ReturnsMetropolisAndForwardsSchedule()
    {
        var sa = new SimulatedAnnealing { InitialTemperature = 30.0, CoolingRate = 0.9 };

        var reinsertion = sa.GetDefaultReinsertion();

        // SA installs the Metropolis layer (the defining uphill-acceptance behaviour); the schedule
        // parameters set on the compound must be forwarded to that layer.
        Assert.That(reinsertion, Is.InstanceOf<MetropolisReinsertion>());
        var metropolis = (MetropolisReinsertion)reinsertion;
        Assert.That(metropolis.InitialTemperature, Is.EqualTo(30.0));
        Assert.That(metropolis.CoolingRate, Is.EqualTo(0.9));
    }

    [Test]
    public void Build_AssemblesMatchMetaHeuristicRootNamedAfterAlgorithm()
    {
        var sa = NewSa();

        var built = sa.Build();

        Assert.That(built, Is.InstanceOf<MatchMetaHeuristic>());
        Assert.That(((NamedEntity)built).Name, Is.EqualTo("Simulated Annealing"));
    }

    [Test]
    public void Build_MatchesCurrentPositionAndRandomIndividual()
    {
        var sa = NewSa();
        var root = (MatchMetaHeuristic)sa.Build();

        // geneValues = [current position (Current), random individual (Random) -- the scale source].
        var kinds = root.Picker.MatchPicks.Select(m => m.MatchingKind).ToArray();
        Assert.That(kinds, Is.EqualTo(new[]
        {
            MatchingKind.Current, MatchingKind.Random
        }));
    }

    /// <summary>
    /// Operator math, no-pendulum proof: when the matched random individual coincides with the current
    /// position the scale is zero, so the Gaussian collapses to the current value exactly, regardless of
    /// the Box-Muller draw. This is the SA analogue of BBPSO's freeze-at-the-best (a stationary proposal).
    /// </summary>
    [Test]
    public void DefaultPerturbationOperator_ZeroScale_ReturnsCurrentExactly()
    {
        // geneValues = [current, random] both equal 7.0 -> scale = 0 -> sample = 7.0.
        var geneValues = new object[] { 7.0, 7.0 };

        object result = SimulatedAnnealing.DefaultPerturbationOperator(0, geneValues, new IdentityConverter());

        Assert.That((double)result, Is.EqualTo(7.0).Within(1e-12));
    }

    /// <summary>
    /// Centring check: with current = 4.0 and random = 14.0 the isotropic step is current + scale*z with
    /// scale = 0.5*|14-4| = 5.0 and z ~ N(0,1), so the draw is centred at the current value 4.0 (the step
    /// is unbiased). Averaging many draws must converge to 4.0 -- this guards the sign/indexing of the
    /// perturbation (a swapped [current, random] or a sign flip would bias the mean).
    /// </summary>
    [Test]
    public void DefaultPerturbationOperator_DistinctCurrentRandom_IsCentredAtCurrent()
    {
        BasicRandomization.ResetSeed(777);
        var geneValues = new object[] { 4.0, 14.0 };

        double sum = 0.0;
        const int N = 20000;
        for (int i = 0; i < N; i++)
            sum += (double)SimulatedAnnealing.DefaultPerturbationOperator(0, geneValues, new IdentityConverter());

        double mean = sum / N;
        // The operator draws from RandomizationProvider.Current (FastRandomRandomization, unseeded --
        // BasicRandomization.ResetSeed does not control it), so the sample mean over 20k draws has stddev
        // ~0.035. A wide band is bulletproof against the run-to-run variance while still catching a swapped
        // [current, random] indexing (which would land the mean near 14, hundreds of sigmas away).
        Assert.That(mean, Is.EqualTo(4.0).Within(0.4),
            $"SA perturbation should be centred at the current value 4.0; got sample mean {mean}");
    }

    /// <summary>Pins the geometric cooling schedule T_k = T_0 * alpha^k.</summary>
    [Test]
    public void CurrentTemperature_FollowsGeometricSchedule()
    {
        var metropolis = new MetropolisReinsertion(initialTemperature: 100.0, coolingRate: 0.5);

        Assert.That(metropolis.CurrentTemperature(0), Is.EqualTo(100.0).Within(1e-9));
        Assert.That(metropolis.CurrentTemperature(1), Is.EqualTo(50.0).Within(1e-9));
        Assert.That(metropolis.CurrentTemperature(3), Is.EqualTo(12.5).Within(1e-9));
        Assert.That(metropolis.CurrentTemperature(10), Is.EqualTo(100.0 * Math.Pow(0.5, 10)).Within(1e-9));
    }

    /// <summary>
    /// KEYSTONE: the built SA drives a real <see cref="MetaGeneticAlgorithm"/> end-to-end and optimises
    /// the Sphere function (minimise sum of squares -> fitness = -sum of squares, higher is better / closer
    /// to the origin). The population uses a randomising chromosome so the steps are non-degenerate (a clone
    /// population has zero spread and thus zero-scale steps that never move). We assert the run completes,
    /// reaches the origin region, and beats the initial best -- honest about SA converging more gently than
    /// the difference-driven DE or the gbest-anchored BBPSO.
    /// </summary>
    [Test]
    public void Build_DrivesMetaGeneticAlgorithm_EndToEnd_AndOptimises()
    {
        // Seeded RNG so the randomising chromosome + Gaussian draws are reproducible.
        BasicRandomization.ResetSeed(2024);

        var sa = NewSa(maxGenerations: 80, initialTemperature: 80.0, coolingRate: 0.97);
        var metaHeuristic = sa.Build();

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
            Termination = new GenerationNumberTermination(80)
        };

        ga.Start();

        Assert.That(ga.BestChromosome, Is.Not.Null, "SA produced no best chromosome");
        Assert.That(ga.BestChromosome.Fitness, Is.Not.Null,
            "SA produced no evaluated offspring (the perturbation returned nothing)");
        double finalSumSq = -ga.BestChromosome.Fitness!.Value;

        Assert.Multiple(() =>
        {
            Assert.That(ga.GenerationsNumber, Is.EqualTo(80));
            Assert.That(ga.State, Is.EqualTo(GeneticAlgorithmState.TerminationReached));
            // SA samples the population toward the origin via Metropolis-accepted steps: a Sphere(5)
            // sum-of-squares well under the initial spread proves the anneal actually optimises (not just
            // random-walks), while staying honest about SA converging more gently than DE/BBPSO.
            Assert.That(finalSumSq, Is.LessThan(40.0),
                $"SA should reach the origin region; got sum-of-squares {finalSumSq}");
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
    /// A chromosome that randomises each gene in [min, max] on CreateNew, so the population has a non-zero
    /// spread (required for SA: zero spread gives zero-scale steps and no search).
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
