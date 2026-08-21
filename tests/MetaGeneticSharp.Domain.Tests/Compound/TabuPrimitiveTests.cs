using System;
using System.Collections.Generic;
using System.Linq;
using GeneticSharp;
using MetaGeneticSharp;

namespace MetaGeneticSharp.Domain.Tests.Compound;

/// <summary>
///   Acceptance tests for the tabu primitive (#12049 axe 4): three decoupled dimensions --
///   <see cref="ITabuProjection"/> (what a move IS), <see cref="ITabuMemory"/> (how it is
///   stored: recency / frequency / reactive / hybrid), <see cref="ITabuFilter"/> (when the
///   interdict yields: strict / aspiration-on-best) -- composed by <see cref="TabuHillClimb"/>,
///   the improvement operator for the memetic layer. The KEYSTONE is deterministic plateau
///   cycling: on a plateau where every equal-fitness move is admissible, the tabu-free walk
///   cycles between two states forever while the interdicted walk must traverse to the optimum.
/// </summary>
public class TabuPrimitiveTests
{
    // --- Fixtures -----------------------------------------------------------

    /// <summary>A chromosome of bare-double genes, constructible from an explicit vector.</summary>
    private sealed class VectorChromosome : ChromosomeBase
    {
        private readonly double[] _values;

        public VectorChromosome(params double[] values) : base(values.Length)
        {
            _values = values;
            for (int i = 0; i < values.Length; i++)
                ReplaceGene(i, new Gene(values[i]));
        }

        public override IChromosome CreateNew() => new VectorChromosome(_values);
        public override Gene GenerateGene(int geneIndex) => new Gene(_values[geneIndex]);
        public double[] GetValues() => GetGenes().Select(g => (double)g.Value).ToArray();
    }

    private sealed class IdentityConverter : IGeometricConverter
    {
        public bool IsOrdered => false;
        public double GeneToDouble(int geneIndex, object geneValue) => (double)geneValue;
        public object DoubleToGene(int geneIndex, double metricValue) => metricValue;
        public IGeometryEmbedding<object> GetEmbedding() => null!;
    }

    /// <summary>
    /// A minimal evolution context backed by a real <see cref="EvolutionContext"/> store -- the
    /// GetOrAdd semantics (memory cross-generation caching) is under test.
    /// </summary>
    private sealed class StubEvolutionContext : IEvolutionContext
    {
        private readonly EvolutionContext _store = new();

        public IGeneticAlgorithm GeneticAlgorithm { get; set; } = null!;
        public IPopulation Population { get; set; } = null!;
        public int OriginalIndex { get; set; } = -1;
        public int LocalIndex { get; set; } = -1;
        public EvolutionStage CurrentStage { get; set; } = EvolutionStage.Selection;
        public IList<IChromosome> SelectedParents { get; set; } = null!;
        public IList<IChromosome> GeneratedOffsprings { get; set; } = null!;

        public IEvolutionContext GetIndividual(int index) => this;
        public IEvolutionContext GetLocal(int index) => this;

        public TItemType GetOrAdd<TItemType>((string key, int generation, EvolutionStage stage, IMetaHeuristic heuristic, int individual) contextKey, Func<TItemType> factory)
            => _store.GetOrAdd(contextKey, factory);

        public TItemType GetParam<TItemType>(IMetaHeuristic h, string paramName) => throw new NotSupportedException();
        public void RegisterParameter(string paramName, IMetaHeuristicParameter param) { }
        public IMetaHeuristicParameter GetParameterDefinition(string paramName) => throw new NotSupportedException();
    }

    /// <summary>The plateau fitness: 1 on [+,+,+], 0 everywhere else -- only the full ascent is strict.</summary>
    private static double? PlateauFitness(IChromosome c) =>
        c.GetGenes().Count(g => (double)g.Value > 0) == 3 ? 1.0 : 0.0;

    // --- Projection ---------------------------------------------------------

    [Test]
    public void GeneMoveProjection_ForwardAndCommittedAreReversePairs()
    {
        var projection = new GeneMoveProjection();
        var a = new VectorChromosome(0.0, 7.0, 3.0);
        var b = new VectorChromosome(1.0, 7.0, 4.0);

        Assert.That(projection.Forbidden(a, b).ToArray(), Is.EqualTo(new[] { "(0:0->1)", "(2:3->4)" }));
        Assert.That(projection.Committed(a, b).ToArray(), Is.EqualTo(new[] { "(0:1->0)", "(2:4->3)" }),
            "committing the move must interdict its REVERSE (Glover's reverse attribution)");
        Assert.That(projection.Forbidden(a, (IChromosome)a.Clone()), Is.Empty, "no move, no attribute");
    }

    [Test]
    public void SolutionHashProjection_CommitsBothEndpoints_DigestsDistinct()
    {
        var projection = new SolutionHashProjection();
        var a = new VectorChromosome(1.0, 2.0);
        var b = new VectorChromosome(2.0, 1.0);

        Assert.That(projection.Forbidden(a, b), Is.EqualTo(new[] { SolutionHashProjection.Digest(b) }));
        var committed = projection.Committed(a, b).ToArray();
        Assert.That(committed, Is.EquivalentTo(new[] { SolutionHashProjection.Digest(a), SolutionHashProjection.Digest(b) }),
            "both the left and the entered solution become visited/forbidden");
        Assert.That(SolutionHashProjection.Digest(a), Is.Not.EqualTo(SolutionHashProjection.Digest(b)));
    }

    // --- Memory -------------------------------------------------------------

    [Test]
    public void RecencyTabuMemory_InterdictsThenExpiresByQueueDepth()
    {
        var memory = new RecencyTabuMemory(tenure: 2);

        memory.Remember(new[] { "a", "b" });
        Assert.That(memory.IsTabu("a") && memory.IsTabu("b"), Is.True);

        memory.Remember(new[] { "c" }); // queue depth 3 > tenure 2: "a" expires (FIFO)
        Assert.That(memory.IsTabu("a"), Is.False, "the oldest attribute must expire first");
        Assert.That(memory.IsTabu("b") && memory.IsTabu("c"), Is.True);
        Assert.That(memory.Frequency("a"), Is.EqualTo(0), "expired attribute is fully gone");
    }

    [Test]
    public void FrequencyTabuMemory_CountsButNeverInterdicts()
    {
        var memory = new FrequencyTabuMemory();
        memory.Remember(new[] { "a" });
        memory.Remember(new[] { "a" });

        Assert.That(memory.IsTabu("a"), Is.False, "long-term memory never carries the interdict");
        Assert.That(memory.Frequency("a"), Is.EqualTo(2));
        Assert.That(memory.Frequency("never-seen"), Is.EqualTo(0));
    }

    [Test]
    public void ReactiveTabuMemory_RepetitionExtendsTheStay()
    {
        var memory = new ReactiveTabuMemory(baseTenure: 1);

        memory.Remember(new[] { "a" });
        memory.Tick();
        Assert.That(memory.IsTabu("a"), Is.False, "base tenure 1 is exhausted by one tick");

        var persisted = new ReactiveTabuMemory(baseTenure: 1);
        persisted.Remember(new[] { "a" });
        persisted.Remember(new[] { "a" }); // re-committed while interdicted: stay extended
        persisted.Tick();
        Assert.That(persisted.IsTabu("a"), Is.True,
            "a repeated attribute must outlast the base tenure (anti-cycling reaction)");
    }

    [Test]
    public void HybridTabuMemory_OrsTheInterdict_SumsTheFrequency()
    {
        var recency = new RecencyTabuMemory(4);
        var frequency = new FrequencyTabuMemory();
        var hybrid = new HybridTabuMemory(recency, frequency);

        recency.Remember(new[] { "r" });
        frequency.Remember(new[] { "f", "f", "f" });

        Assert.That(hybrid.IsTabu("r"), Is.True, "interdict comes from the recency half");
        Assert.That(hybrid.IsTabu("f"), Is.False);
        Assert.That(hybrid.Frequency("f"), Is.EqualTo(3));
        Assert.That(hybrid.Frequency("r"), Is.EqualTo(1), "frequency flows through the recency half too");
    }

    // --- Filter -------------------------------------------------------------

    [Test]
    public void StrictTabuFilter_InterdictsOnAnyForbiddenAttributeHit()
    {
        var projection = new GeneMoveProjection();
        var memory = new RecencyTabuMemory(8);
        var current = new VectorChromosome(0.0, 0.0);
        var candidate = new VectorChromosome(1.0, 0.0) { Fitness = 5.0 };
        var filter = new StrictTabuFilter();

        Assert.That(filter.IsAdmissible(current, candidate, projection, memory, bestFitness: 0.0), Is.True);

        memory.Remember(new[] { "(0:0->1)" }); // the reverse of the committed move (0:1->0)
        Assert.That(filter.IsAdmissible(current, candidate, projection, memory, bestFitness: 0.0), Is.False,
            "the move whose reverse was committed is interdicted");
    }

    [Test]
    public void AspirationOnBestFilter_LiftsTheInterdictOnlyForStrictlyBetter()
    {
        var projection = new GeneMoveProjection();
        var memory = new RecencyTabuMemory(8);
        var current = new VectorChromosome(0.0, 0.0);
        var candidate = new VectorChromosome(1.0, 0.0) { Fitness = 5.0 };
        memory.Remember(new[] { "(0:0->1)" }); // interdicts the very move current -> candidate
        var filter = new AspirationOnBestFilter();

        Assert.That(filter.IsAdmissible(current, candidate, projection, memory, bestFitness: 5.0), Is.False,
            "equal to the best does not aspire");
        Assert.That(filter.IsAdmissible(current, candidate, projection, memory, bestFitness: 4.0), Is.True,
            "strictly better than the best lifts the interdict (Glover's aspiration)");
        Assert.That(filter.IsAdmissible(current, candidate, projection, memory, bestFitness: double.NaN), Is.False,
            "no reference best yet (NaN): nothing aspires");
    }

    // --- The walk -----------------------------------------------------------

    /// <summary>
    /// KEYSTONE: deterministic plateau cycling. Three ±1 genes, fitness 1 only on [+,+,+],
    /// every other state 0 -- the optimum is reachable ONLY through equal-fitness moves.
    /// Without the interdict the lateral walk bounces [-,-,-] ↔ [+,-,-] forever; with the
    /// solution-hash interdict the return is forbidden, the walk must traverse, and it ends on
    /// the optimum.
    /// </summary>
    [Test]
    public void Walk_TabuBreaksDeterministicPlateauCycling()
    {
        var converter = new IdentityConverter();
        var start = new VectorChromosome(-1.0, -1.0, -1.0);

        // Tabu-free: RecencyTabuMemory(0) commits then immediately evicts everything.
        var free = TabuHillClimb.Walk(start, new StubEvolutionContext(), converter, PlateauFitness,
            TabuHillClimb.Flip, new SolutionHashProjection(), () => new RecencyTabuMemory(0),
            new StrictTabuFilter(), maxMoves: 12, acceptLateral: true);
        Assert.That(free.Fitness, Is.EqualTo(0.0),
            "without the interdict the lateral walk cycles on the plateau and never ascends");

        // Interdicted: solutions, once visited, cannot be re-entered.
        var interdicted = TabuHillClimb.Walk(start, new StubEvolutionContext(), converter, PlateauFitness,
            TabuHillClimb.Flip, new SolutionHashProjection(), () => new RecencyTabuMemory(10),
            new StrictTabuFilter(), maxMoves: 12, acceptLateral: true);
        Assert.That(interdicted.Fitness, Is.EqualTo(1.0),
            "the interdict forces plateau traversal; the walk must reach [+,+,+]");
        Assert.That(((VectorChromosome)interdicted).GetValues(), Is.EqualTo(new[] { 1.0, 1.0, 1.0 }));
    }

    /// <summary>
    /// The lateral-free configuration stops on the plateau edge: without lateral acceptance the
    /// walk halts at the first state with no strict improvement (documented behavior, the
    /// tabu memory is not a substitute for lateral moves on plateaus).
    /// </summary>
    [Test]
    public void Walk_WithoutLateralAcceptance_StopsAtThePlateauEdge()
    {
        var converter = new IdentityConverter();
        var start = new VectorChromosome(-1.0, -1.0, -1.0);

        var walk = TabuHillClimb.Walk(start, new StubEvolutionContext(), converter, PlateauFitness,
            TabuHillClimb.Flip, new SolutionHashProjection(), () => new RecencyTabuMemory(10),
            new StrictTabuFilter(), maxMoves: 12, acceptLateral: false);

        Assert.That(walk.Fitness, Is.EqualTo(0.0), "no strict neighbor exists: the walk makes no move at all");
        Assert.That(((VectorChromosome)walk).GetValues(), Is.EqualTo(new[] { -1.0, -1.0, -1.0 }));
    }

    /// <summary>
    /// The memory is shared across walks through the context store (one population-wide
    /// instance, not a fresh one per call): the second walk from the same start sees the
    /// first walk's visited solutions as interdicted.
    /// </summary>
    [Test]
    public void Walk_MemoryIsSharedAcrossCallsThroughTheContextStore()
    {
        var converter = new IdentityConverter();
        var ctx = new StubEvolutionContext();
        var start = new VectorChromosome(-1.0, -1.0, -1.0);
        Func<ITabuMemory> factory = () => new RecencyTabuMemory(50);

        var first = TabuHillClimb.Walk(start, ctx, converter, PlateauFitness,
            TabuHillClimb.Flip, new SolutionHashProjection(), factory,
            new StrictTabuFilter(), maxMoves: 1, acceptLateral: true);
        // One lateral move: [-,-,-] -> [+,-,-]; both digests now interdicted, tenure 50.

        var second = TabuHillClimb.Walk(start, ctx, converter, PlateauFitness,
            TabuHillClimb.Flip, new SolutionHashProjection(), factory,
            new StrictTabuFilter(), maxMoves: 1, acceptLateral: true);
        // The first lateral (g0 flip) is now interdicted: the walk must take g1 instead.
        Assert.That(((VectorChromosome)second).GetValues(), Is.EqualTo(new[] { -1.0, 1.0, -1.0 }),
            "the shared memory must forbid the state the first walk entered");
        Assert.That(((VectorChromosome)first).GetValues(), Is.EqualTo(new[] { 1.0, -1.0, -1.0 }));
    }

    /// <summary>
    /// Steepest on improvement: with a stepped neighborhood and Sphere, the walk greedily
    /// descends every gene -- the returned candidate must be strictly better than the start.
    /// </summary>
    [Test]
    public void Walk_SteppedNeighborhood_DescendsSphere()
    {
        var converter = new IdentityConverter();
        var start = new VectorChromosome(2.0, -3.0, 4.0);
        double? Sphere(IChromosome c) => -c.GetGenes().Sum(g => (double)g.Value * (double)g.Value);

        var walk = TabuHillClimb.Walk(start, new StubEvolutionContext(), converter, Sphere,
            TabuHillClimb.Stepped(0.5), new GeneMoveProjection(), () => new RecencyTabuMemory(64),
            new AspirationOnBestFilter(), maxMoves: 3, acceptLateral: true);

        Assert.That(walk.Fitness, Is.GreaterThan(Sphere(start).Value),
            "each move strictly improves (or laterals); three 0.5-steps must improve Sphere");
    }

    /// <summary>
    /// KEYSTONE end-to-end: the tabu operator wired as the improvement operator of the memetic
    /// layer (axe 2) driving a real <see cref="MetaGeneticAlgorithm"/>. Memetic PSO + tabu hill
    /// climb on Sphere: converges like the bare/memetic keystones do.
    /// </summary>
    [Test]
    public void TabuMemeticPSO_DrivesMetaGeneticAlgorithm_AndOptimises()
    {
        var pso = new ParticleSwarmOptimization { MaxGenerations = 60 };
        pso.SetGeometricConverter(new GeometricConverter<double>
        {
            GeneToDoubleConverter = (_, v) => v,
            DoubleToGeneConverter = (_, d) => d,
        });

        MetaGeneticAlgorithm ga = null;
        var improve = TabuHillClimb.Improvement(
            new IdentityConverter(),
            c => ga?.Fitness?.Evaluate(c),
            TabuHillClimb.Stepped(0.5),
            new SolutionHashProjection(),
            () => new RecencyTabuMemory(64),
            new AspirationOnBestFilter(),
            maxMoves: 3);

        var memetic = new MemeticAlgorithm(pso) { ImprovementCount = 2, ImproveOperator = improve };

        var chromosome = new RandomDoubleChromosome(min: -10.0, max: 10.0, length: 5);
        var fitness = new FuncFitness(c =>
        {
            var values = ((RandomDoubleChromosome)c).GetDoubleValues();
            double s = 0.0;
            for (int i = 0; i < values.Length; i++) s += values[i] * values[i];
            return -s;
        });

        var population = new MetaPopulation(30, 30, chromosome);
        ga = new MetaGeneticAlgorithm(
            population,
            fitness,
            new EliteSelection(),
            new UniformCrossover(0.5f),
            new UniformMutation(true),
            memetic.Build())
        {
            Termination = new GenerationNumberTermination(60)
        };

        ga.Start();

        Assert.That(ga.BestChromosome, Is.Not.Null);
        Assert.That(ga.BestChromosome.Fitness, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(ga.GenerationsNumber, Is.EqualTo(60));
            Assert.That(ga.State, Is.EqualTo(GeneticAlgorithmState.TerminationReached));
            Assert.That(-ga.BestChromosome.Fitness!.Value, Is.LessThan(1.0),
                $"the tabu-memetic PSO should reach the origin region; got sum-of-squares {-ga.BestChromosome.Fitness!.Value}");
        });
    }

    /// <summary>A chromosome that randomises each gene in [min, max] on CreateNew (same fixture as the memetic keystone).</summary>
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
