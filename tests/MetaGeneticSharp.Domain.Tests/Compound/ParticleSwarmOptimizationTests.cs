using GeneticSharp;
using MetaGeneticSharp;
using MetaGeneticSharp.Domain.Tests.Geometric;

namespace MetaGeneticSharp.Domain.Tests.Compound;

/// <summary>
///   Acceptance tests for the <see cref="ParticleSwarmOptimization"/> geometric compound (Shi &
///   Eberhart 1998). The structural tests verify the assembled primitive tree (a MatchMetaHeuristic
///   matching the particle's own position plus the global best, a two-parent geometric crossover
///   running the velocity/position recurrence, the inherited FitnessBasedElitistReinsertion); the
///   pure-math tests verify the clamped recurrence deterministically (no RNG, no store); the two
///   memory tests prove the per-particle state -- velocity persistence across generations and a
///   personal best that survives a fitness regression -- through the evolution-context store with a
///   controlled generation clock; the keystone runs the built PSO against a real
///   <see cref="MetaGeneticAlgorithm"/> and asserts it actually optimises Sphere.
/// </summary>
public class ParticleSwarmOptimizationTests
{
    private static ParticleSwarmOptimization NewPso(int maxGenerations = 20)
    {
        var pso = new ParticleSwarmOptimization { MaxGenerations = maxGenerations };
        // A double<->double identity converter: FixedChromosome stores genes as bare doubles.
        pso.SetGeometricConverter(new GeometricConverter<double>
        {
            GeneToDoubleConverter = (_, v) => v,
            DoubleToGeneConverter = (_, d) => d,
        });
        return pso;
    }

    [Test]
    public void GetDefaultReinsertion_ReturnsFitnessBasedElitist()
    {
        var pso = new ParticleSwarmOptimization();

        // PSO does not override GetDefaultReinsertion: the base FitnessBasedElitistReinsertion
        // (best-N of parents+offspring) is the greedy keep-the-best selection the swarm assumes.
        Assert.That(pso.GetDefaultReinsertion(), Is.InstanceOf<FitnessBasedElitistReinsertion>());
    }

    [Test]
    public void Build_AssemblesMatchMetaHeuristicRootNamedAfterAlgorithm()
    {
        var pso = NewPso();

        var built = pso.Build();

        Assert.That(built, Is.InstanceOf<MatchMetaHeuristic>());
        Assert.That(((NamedEntity)built).Name, Is.EqualTo("Particle Swarm Optimisation"));
    }

    [Test]
    public void Build_MatchesCurrentPositionAndGlobalBest()
    {
        var pso = NewPso();
        var root = (MatchMetaHeuristic)pso.Build();

        // geneValues = [current position x_i (Current), global best (Best)]. The personal best and
        // the velocity are NOT matches: they live in the per-particle context store.
        var kinds = root.Picker.MatchPicks.Select(m => m.MatchingKind).ToArray();
        Assert.That(kinds, Is.EqualTo(new[]
        {
            MatchingKind.Current, MatchingKind.Best
        }));
    }

    /// <summary>
    /// Freeze at convergence, no-pendulum proof: when the particle IS both attractors
    /// (x == pbest == gbest) the self-scaling clamp collapses to zero, so the velocity is exactly
    /// zero regardless of the inertia term -- the same convergence property as the bare-bones
    /// variant, verified without the RNG.
    /// </summary>
    [Test]
    public void ComputeVelocity_AtBothAttractors_ClampsToZero()
    {
        // x = pbest = gbest = 10 with a nonzero incoming velocity: the span is 0 so vMax = 0.
        double v = ParticleSwarmOptimization.ComputeVelocity(
            position: 10.0, personalBest: 10.0, globalBest: 10.0,
            previousVelocity: 3.0, r1: 0.7, r2: 0.3,
            inertiaWeight: 0.7298, cognitiveCoefficient: 1.49618, socialCoefficient: 1.49618,
            maxVelocityRatio: 2.0);

        Assert.That(v, Is.EqualTo(0.0).Within(1e-12));
    }

    /// <summary>
    /// Pure social step: with w = 0, c1 = 0, c2 = 1 and r2 = 1 the velocity is exactly
    /// gbest - x, so x_new = x + v lands on the global best in one step (a swapped attractor
    /// indexing or a sign flip would land on 2x - gbest instead).
    /// </summary>
    [Test]
    public void ComputeVelocity_PureSocialStep_IsGlobalBestMinusPosition()
    {
        double v = ParticleSwarmOptimization.ComputeVelocity(
            position: 0.0, personalBest: 0.0, globalBest: 8.0,
            previousVelocity: 5.0, r1: 1.0, r2: 1.0,
            inertiaWeight: 0.0, cognitiveCoefficient: 0.0, socialCoefficient: 1.0,
            maxVelocityRatio: 2.0);

        Assert.That(v, Is.EqualTo(8.0).Within(1e-12));
    }

    /// <summary>
    /// Inertia persistence: with c1 = c2 = 0 the velocity is exactly w * v_prev (the attractors
    /// only provide the clamp scale here: span = 8, vMax = 16 stays well above |0.5 * 4|).
    /// </summary>
    [Test]
    public void ComputeVelocity_ZeroAttractorPull_IsDampedInertiaExactly()
    {
        double v = ParticleSwarmOptimization.ComputeVelocity(
            position: 0.0, personalBest: 0.0, globalBest: 8.0,
            previousVelocity: 4.0, r1: 0.9, r2: 0.9,
            inertiaWeight: 0.5, cognitiveCoefficient: 0.0, socialCoefficient: 0.0,
            maxVelocityRatio: 2.0);

        Assert.That(v, Is.EqualTo(2.0).Within(1e-12));
    }

    /// <summary>
    /// The clamp actually binds: a full-attractor pull of 16 against a span of 8 and ratio 0.5
    /// caps the velocity at vMax = 0.5 * 8 = 4.
    /// </summary>
    [Test]
    public void ComputeVelocity_Overspeed_ClampsToRatioTimesSpan()
    {
        double v = ParticleSwarmOptimization.ComputeVelocity(
            position: 0.0, personalBest: 0.0, globalBest: 8.0,
            previousVelocity: 0.0, r1: 1.0, r2: 1.0,
            inertiaWeight: 0.0, cognitiveCoefficient: 0.0, socialCoefficient: 2.0,
            maxVelocityRatio: 0.5);

        Assert.That(v, Is.EqualTo(4.0).Within(1e-12));
    }

    /// <summary>
    /// Full recurrence, hand-computed: v = 0.5*(-1) + 1*0.25*(6-2) + 1*0.5*(10-2) = 4.5, under
    /// vMax = 2 * max(4, 8) = 16 so the clamp does not interfere.
    /// </summary>
    [Test]
    public void ComputeVelocity_FullRecurrence_MatchesHandComputation()
    {
        double v = ParticleSwarmOptimization.ComputeVelocity(
            position: 2.0, personalBest: 6.0, globalBest: 10.0,
            previousVelocity: -1.0, r1: 0.25, r2: 0.5,
            inertiaWeight: 0.5, cognitiveCoefficient: 1.0, socialCoefficient: 1.0,
            maxVelocityRatio: 2.0);

        Assert.That(v, Is.EqualTo(4.5).Within(1e-12));
    }

    /// <summary>
    /// KEYSTONE for the velocity memory: generation 0 draws a social pull v0 = r2 * (gbest - x)
    /// (random r2, observed through the returned gene); generation 1 switches the social pull off
    /// and keeps pure inertia w = 1. Only if v0 was stored per-particle and read back at the next
    /// generation does the second update return x + v0 -- without memory the seeded v_prev = 0
    /// would return exactly x. The returned gene is compared bit-for-bit (same arithmetic path).
    /// </summary>
    [Test]
    public void UpdateOperator_VelocityPersistsAcrossGenerations()
    {
        var pso = new GatedPso();
        pso.InertiaWeight = 1.0;
        pso.CognitiveCoefficient = 0.0;
        pso.MaxVelocityRatio = 2.0;
        var converter = new IdentityConverter();
        var ctx = new StubEvolutionContext();
        ctx.SelectedParents = new List<IChromosome>
        {
            new FixedChromosome(0.0) { Fitness = 1.0 }, // particle x = 0.
            new FixedChromosome(8.0) { Fitness = 99.0 } // gbest = 8.
        };
        var geneValues = new object[] { 0.0, 8.0 };

        // Generation 0: v0 = 1 * r2 * (8 - 0), gene0 = 0 + v0 in (0, 8).
        pso.SocialCoefficient = 1.0;
        pso.Generation = 0;
        double gene0 = (double)pso.UpdateOperator(0, geneValues, converter, ctx);
        Assert.That(gene0, Is.GreaterThan(0.0).And.LessThan(8.0), "sanity: the social draw moved the particle");

        // Generation 1: social pull OFF, pure inertia w = 1 -> v1 = v0 read from the store.
        pso.SocialCoefficient = 0.0;
        pso.Generation = 1;
        double gene1 = (double)pso.UpdateOperator(0, geneValues, converter, ctx);

        Assert.That(gene1, Is.EqualTo(gene0),
            $"velocity must persist: without memory v_prev would reseed to 0 and return x = 0, got {gene1} vs {gene0}");
    }

    /// <summary>
    /// KEYSTONE for the personal-best memory: the particle improves in generation 0 (x = 2,
    /// fitness 5 -> pbest = 2), then regresses in generation 1 (x = 6, fitness 1). The stored
    /// record must win: with w = 0, c2 = 0, c1 = 1 the pull is toward pbest = 2, strictly below
    /// both the regressed position (a pbest := x bug gives exactly 6) and the global best
    /// (a pbest := gbest bug gives a pull above 6).
    /// </summary>
    [Test]
    public void UpdateOperator_PersonalBestSurvivesFitnessRegression()
    {
        var pso = new GatedPso();
        pso.InertiaWeight = 0.0;
        pso.CognitiveCoefficient = 1.0;
        pso.SocialCoefficient = 0.0;
        pso.MaxVelocityRatio = 2.0;
        var converter = new IdentityConverter();
        var ctx = new StubEvolutionContext();

        // Generation 0: x = 2 (fitness 5) seeds the record pbest = 2, pull = 0.
        ctx.SelectedParents = new List<IChromosome>
        {
            new FixedChromosome(2.0) { Fitness = 5.0 },
            new FixedChromosome(8.0) { Fitness = 99.0 }
        };
        pso.Generation = 0;
        _ = pso.UpdateOperator(0, new object[] { 2.0, 8.0 }, converter, ctx);

        // Generation 1: the same particle regresses to x = 6 with fitness 1 (worse than the record 5).
        ctx.SelectedParents = new List<IChromosome>
        {
            new FixedChromosome(6.0) { Fitness = 1.0 },
            new FixedChromosome(8.0) { Fitness = 99.0 }
        };
        pso.Generation = 1;
        double gene1 = (double)pso.UpdateOperator(0, new object[] { 6.0, 8.0 }, converter, ctx);

        // v = r1 * (pbest - x) = r1 * (2 - 6) in (-4, 0) -> gene1 strictly inside (2, 6).
        Assert.That(gene1, Is.GreaterThan(2.0).And.LessThan(6.0),
            $"pbest must stay at the record 2.0 after the regression; pbest:=x gives exactly 6, pbest:=gbest gives > 6; got {gene1}");
    }

    /// <summary>
    /// The registry wires the compound by name: CreateMetaHeuristicByName builds the PSO root and
    /// GetMetaHeuristicTypeByName resolves it to the MatchMetaHeuristic root type.
    /// </summary>
    [Test]
    public void MetaHeuristicsService_BuildsParticleSwarmByName()
    {
        var built = MetaHeuristicsService.CreateMetaHeuristicByName(nameof(KnownCompoundMetaheuristics.ParticleSwarmOptimization));
        Assert.That(built, Is.InstanceOf<MatchMetaHeuristic>());

        var rootType = MetaHeuristicsService.GetMetaHeuristicTypeByName(nameof(KnownCompoundMetaheuristics.ParticleSwarmOptimization));
        Assert.That(rootType, Is.EqualTo(typeof(MatchMetaHeuristic)));
    }

    /// <summary>
    /// The factory default must preserve mutation for canonical PSO. Its attractor-scaled velocity
    /// clamp intentionally reaches zero when x == pbest == gbest; on a quantised objective that can
    /// happen before the optimum, so disabling the caller-provided mutation makes the plateau
    /// absorbing. Other compounds keep the historical no-mutation default.
    /// </summary>
    [Test]
    public void MetaHeuristicsService_DefaultParticleSwarm_PreservesMutationOnlyForPso()
    {
        var pso = MetaHeuristicsService.CreateMetaHeuristicByName(
            nameof(KnownCompoundMetaheuristics.ParticleSwarmOptimization));
        var de = MetaHeuristicsService.CreateMetaHeuristicByName(
            nameof(KnownCompoundMetaheuristics.DifferentialEvolution));
        var psoOptOut = MetaHeuristicsService.CreateMetaHeuristicByName(
            nameof(KnownCompoundMetaheuristics.ParticleSwarmOptimization), noMutation: true);

        Assert.Multiple(() =>
        {
            Assert.That(HasNoMutationLayer(pso), Is.False,
                "PSO needs bounded diversification after its velocity span collapses");
            Assert.That(HasNoMutationLayer(de), Is.True,
                "the PSO correction must not alter defaults for the other compounds");
            Assert.That(HasNoMutationLayer(psoOptOut), Is.True,
                "an explicit caller opt-out must override the PSO-specific default");
        });
    }

    /// <summary>
    /// Behavioural regression for the Sudoku-R1 failure mode: every particle starts in the same
    /// rounding cell, hence PSO has x == pbest == gbest and produces zero velocity. With mutation
    /// suppressed the best quantised distance cannot improve; with mutation preserved, the same
    /// deterministic mutation crosses the cell boundary and reaches the optimum.
    /// </summary>
    [TestCase(true, -1.0)]
    [TestCase(false, 0.0)]
    public void QuantisedPlateau_MutationDiversificationDeterminesWhetherPsoEscapes(
        bool noMutation, double expectedFitness)
    {
        var pso = NewPso(maxGenerations: 2);
        pso.NoMutation = noMutation;

        var chromosome = new PlateauChromosome(initialValue: 0.49, mutatedValue: 1.1);
        var fitness = new FuncFitness(c =>
        {
            double value = (double)c.GetGene(0).Value;
            return -Math.Abs(1.0 - Math.Round(value, MidpointRounding.AwayFromZero));
        });
        var ga = new MetaGeneticAlgorithm(
            new MetaPopulation(4, 4, chromosome),
            fitness,
            new EliteSelection(),
            new UniformCrossover(0.5f),
            new UniformMutation(true),
            pso.Build())
        {
            MutationProbability = 1.0f,
            Termination = new GenerationNumberTermination(2)
        };

        ga.Start();

        Assert.That(ga.BestChromosome.Fitness, Is.EqualTo(expectedFitness),
            noMutation
                ? "zero-span PSO without mutation must remain in the initial rounding cell"
                : "mutation-preserving PSO must cross the quantisation boundary");
    }

    /// <summary>
    /// KEYSTONE end-to-end: the built PSO drives a real <see cref="MetaGeneticAlgorithm"/> and
    /// optimises the Sphere function (minimise sum of squares -> fitness = -sum of squares). The
    /// population uses a randomising chromosome so the swarm is diverse (clones on top of the best
    /// give zero-span updates and cannot explore); we assert the run completes and reaches the
    /// origin region well inside the bare-bones threshold (the velocity recurrence with Clerc's
    /// constriction constants converges tighter than the bare-bones Gaussian).
    /// </summary>
    [Test]
    public void Build_DrivesMetaGeneticAlgorithm_EndToEnd_AndOptimises()
    {
        var pso = NewPso(maxGenerations: 60);
        var metaHeuristic = pso.Build();

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

        Assert.That(ga.BestChromosome, Is.Not.Null, "PSO produced no best chromosome");
        Assert.That(ga.BestChromosome.Fitness, Is.Not.Null,
            "PSO produced no evaluated offspring (the update returned nothing)");
        double finalSumSq = -ga.BestChromosome.Fitness!.Value;

        Assert.Multiple(() =>
        {
            Assert.That(ga.GenerationsNumber, Is.EqualTo(60));
            Assert.That(ga.State, Is.EqualTo(GeneticAlgorithmState.TerminationReached));
            // Sphere(5) sum-of-squares under 1.0 proves the velocity recurrence actually optimises
            // (not just random-walks) while staying honest about run-to-run RNG variance: the draw
            // comes from RandomizationProvider.Current (FastRandom, unseeded), so the threshold
            // keeps several sigmas of margin over the typical ~1e-2 converged value.
            Assert.That(finalSumSq, Is.LessThan(1.0),
                $"PSO should reach the origin region; got sum-of-squares {finalSumSq}");
        });
    }

    private static bool HasNoMutationLayer(IMetaHeuristic heuristic)
    {
        IMetaHeuristic? current = heuristic;
        while (current != null)
        {
            if (current is ScopedMetaHeuristic scoped
                && scoped.Name?.Contains("No-Mutation") == true)
                return true;

            current = current is ContainerMetaHeuristic container
                ? container.SubMetaHeuristic
                : null;
        }

        return false;
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
    /// diverse (required for PSO: clones on top of the best give zero-span updates and no search).
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

    /// <summary>
    /// A two-gene chromosome whose initial clones all occupy one quantisation cell while mutation
    /// deterministically generates a value in the optimum cell.
    /// </summary>
    private sealed class PlateauChromosome : ChromosomeBase
    {
        private readonly double _initialValue;
        private readonly double _mutatedValue;

        public PlateauChromosome(double initialValue, double mutatedValue) : base(2)
        {
            _initialValue = initialValue;
            _mutatedValue = mutatedValue;
            ReplaceGene(0, new Gene(initialValue));
            ReplaceGene(1, new Gene(initialValue));
        }

        public override IChromosome CreateNew() =>
            new PlateauChromosome(_initialValue, _mutatedValue);

        public override Gene GenerateGene(int geneIndex) => new Gene(_mutatedValue);
    }

    /// <summary>
    /// A two-gene chromosome pinned to a value (ChromosomeBase enforces a 2-gene minimum), for
    /// driving SelectedParents directly; the operator under test reads gene 0.
    /// </summary>
    private sealed class FixedChromosome : ChromosomeBase
    {
        private readonly double _value;

        public FixedChromosome(double value) : base(2)
        {
            _value = value;
            ReplaceGene(0, new Gene(value));
            ReplaceGene(1, new Gene(value));
        }

        public override IChromosome CreateNew() => new FixedChromosome(_value);

        public override Gene GenerateGene(int geneIndex) => new Gene(_value);
    }

    /// <summary>
    /// A PSO whose generation clock is test-controlled, so the per-particle store can be driven
    /// across generations without a live population.
    /// </summary>
    private sealed class GatedPso : ParticleSwarmOptimization
    {
        public int Generation;

        protected override int GetCurrentGeneration(IEvolutionContext ctx) => Generation;
    }

    /// <summary>
    /// A minimal evolution context for direct operator tests: a real backing
    /// <see cref="EvolutionContext"/> store (the GetOrAdd semantics under test), a fixed particle
    /// index, and controllable selected parents.
    /// </summary>
    private sealed class StubEvolutionContext : IEvolutionContext
    {
        private readonly EvolutionContext _store = new();

        public IGeneticAlgorithm GeneticAlgorithm { get; set; } = null!;
        public IPopulation Population { get; set; } = null!;
        public int OriginalIndex { get; set; } = 7;
        public int LocalIndex { get; set; } = -1;
        public EvolutionStage CurrentStage { get; set; } = EvolutionStage.Crossover;
        public IList<IChromosome> SelectedParents { get; set; } = null!;
        public IList<IChromosome> GeneratedOffsprings { get; set; } = null!;

        public IEvolutionContext GetIndividual(int index) => this;

        public IEvolutionContext GetLocal(int index) => this;

        public TItemType GetOrAdd<TItemType>((string key, int generation, EvolutionStage stage, IMetaHeuristic heuristic, int individual) contextKey, Func<TItemType> factory)
        {
            return _store.GetOrAdd(contextKey, factory);
        }

        public TItemType GetParam<TItemType>(IMetaHeuristic h, string paramName) => throw new NotSupportedException();

        public void RegisterParameter(string paramName, IMetaHeuristicParameter param)
        {
        }

        public IMetaHeuristicParameter GetParameterDefinition(string paramName) => throw new NotSupportedException();
    }
}
