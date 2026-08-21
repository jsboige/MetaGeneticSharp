using System;
using System.Collections.Generic;
using System.Linq;
using GeneticSharp;
using MetaGeneticSharp;

namespace MetaGeneticSharp.Domain.Tests.Compound;

/// <summary>
///   Acceptance tests for the <see cref="ScatterSearch"/> geometric compound (Glover 1998;
/// Laguna &amp; Marti 2003). The structural tests verify the assembled primitive tree (a
/// MatchMetaHeuristic matching the individual with a RANDOM reference -- diverse mates, not
/// elite-only -- running a convex-combination geometric crossover, and a ScatterSearchReinsertion
/// carrying the b1-quality/b2-diversity split); the pure-math tests verify the combination
/// deterministically; the per-child-weight test proves every gene of one child shares a single
/// lambda through the evolution-context store; the reinsertion tests verify the reference-set
/// update on hand-built candidates; the keystone runs the built ScatterSearch against a real
/// <see cref="MetaGeneticAlgorithm"/> and asserts it actually optimises Sphere.
/// </summary>
public class ScatterSearchTests
{
    private static ScatterSearch NewSs(int maxGenerations = 20)
    {
        var ss = new ScatterSearch { MaxGenerations = maxGenerations };
        // A double<->double identity converter: FixedChromosome stores genes as bare doubles.
        ss.SetGeometricConverter(new GeometricConverter<double>
        {
            GeneToDoubleConverter = (_, v) => v,
            DoubleToGeneConverter = (_, d) => d,
        });
        return ss;
    }

    [Test]
    public void GetDefaultReinsertion_ReturnsScatterSearchReinsertion_WithForwardedFraction()
    {
        var ss = new ScatterSearch { QualityFraction = 0.5 };

        var reinsertion = ss.GetDefaultReinsertion() as ScatterSearchReinsertion;
        Assert.That(reinsertion, Is.Not.Null,
            "ScatterSearch must override the default reinsertion: the reference-set update IS the algorithm");
        Assert.That(reinsertion.QualityFraction, Is.EqualTo(0.5).Within(1e-12),
            "QualityFraction must be forwarded to the reinsertion");
    }

    [Test]
    public void Build_AssemblesMatchMetaHeuristicRootNamedAfterAlgorithm()
    {
        var built = NewSs().Build();

        Assert.That(built, Is.InstanceOf<MatchMetaHeuristic>());
        Assert.That(((NamedEntity)built).Name, Is.EqualTo("Scatter Search"));
    }

    [Test]
    public void Build_MatchesCurrentAndRandomReferences()
    {
        var root = (MatchMetaHeuristic)NewSs().Build();

        // geneValues = [x_a (Current), x_b (Random)]. The RANDOM mate is the distinguishing
        // Scatter Search choice: PSO pulls every particle toward the single best, Scatter
        // Search combines across the WHOLE reference set, diverse members included.
        var kinds = root.Picker.MatchPicks.Select(m => m.MatchingKind).ToArray();
        Assert.That(kinds, Is.EqualTo(new[]
        {
            MatchingKind.Current, MatchingKind.Random
        }));
    }

    /// <summary>
    /// The convex combination endpoints and midpoint: lambda = 1 returns x_a untouched, lambda = 0
    /// returns x_b, lambda = 0.5 the midpoint (an inverted convention swaps a and b).
    /// </summary>
    [Test]
    public void CombineGene_EndpointsAndMidpoint()
    {
        Assert.That(ScatterSearch.CombineGene(3.0, 9.0, 1.0), Is.EqualTo(3.0).Within(1e-12));
        Assert.That(ScatterSearch.CombineGene(3.0, 9.0, 0.0), Is.EqualTo(9.0).Within(1e-12));
        Assert.That(ScatterSearch.CombineGene(3.0, 9.0, 0.5), Is.EqualTo(6.0).Within(1e-12));
    }

    /// <summary>
    /// Hand-computed interior point: 0.25 * 10 + 0.75 * 2 = 4.
    /// </summary>
    [Test]
    public void CombineGene_InteriorPoint_MatchesHandComputation()
    {
        Assert.That(ScatterSearch.CombineGene(10.0, 2.0, 0.25), Is.EqualTo(4.0).Within(1e-12));
    }

    /// <summary>
    /// KEYSTONE for the per-child weight: the child must lie ON the segment between its
    /// references -- every gene shares ONE lambda drawn through the store. Gene 0 reads
    /// (x_a=0, x_b=10) giving g0 = 10 * (1 - lambda); gene 1 reads (x_a=4, x_b=6) giving
    /// g1 = 6 - 2 * lambda. Eliminating lambda: g1 must equal 6 - 2 * (1 - g0/10) exactly. A
    /// per-gene redraw would place the child inside the box spanned by the parents and fail
    /// this relation with overwhelming probability.
    /// </summary>
    [Test]
    public void DefaultCombineOperator_AllGenesOfAChildShareOneLambda()
    {
        var ss = new GatedScatterSearch();
        var converter = new IdentityConverter();
        var ctx = new StubEvolutionContext();
        ctx.SelectedParents = new List<IChromosome>
        {
            new FixedChromosome(0.0), // x_a: gene0 = 0, gene1 = 4 via per-gene inputs below.
            new FixedChromosome(10.0) // x_b.
        };

        double g0 = (double)ss.CombineOperator(0, new object[] { 0.0, 10.0 }, converter, ctx);
        double g1 = (double)ss.CombineOperator(1, new object[] { 4.0, 6.0 }, converter, ctx);

        Assert.That(g0, Is.GreaterThan(0.0).And.LessThan(10.0), "sanity: the draw moved the child");
        double predictedG1 = 6.0 - 2.0 * (1.0 - g0 / 10.0);
        Assert.That(g1, Is.EqualTo(predictedG1).Within(1e-12),
            $"both genes must share one lambda; per-gene redraw would break the segment relation (g0={g0}, g1={g1}, expected {predictedG1})");
    }

    /// <summary>
    /// The RMS gene distance: identical chromosomes are at distance 0, and double-convertible
    /// genes contribute their absolute separation (two genes at |5| each give RMS 5).
    /// </summary>
    [Test]
    public void Distance_IdenticalIsZero_AndNumericPairsUseSeparation()
    {
        Assert.That(ScatterSearchReinsertion.Distance(new FixedChromosome(3.0), new FixedChromosome(3.0)),
            Is.EqualTo(0.0).Within(1e-12));
        Assert.That(ScatterSearchReinsertion.Distance(new FixedChromosome(0.0), new FixedChromosome(5.0)),
            Is.EqualTo(5.0).Within(1e-12));
    }

    /// <summary>
    /// The b1-quality + b2-diversity split on hand-built candidates (MinSize 4, fraction 0.5):
    /// parents A(fit 9, at 0) and B(fit 8, at 0) fill the two quality slots; the diversity pass
    /// must then admit D (at 10, min-distance 10) BEFORE C (at 5, min-distance 5) -- the
    /// max-min rule, not the fitness order (which would take C first).
    /// </summary>
    [Test]
    public void Reinsertion_KeepsQualitySlotsThenFillsByMaxMinDiversity()
    {
        var adam = new FixedChromosome(0.0);
        var population = new Population(4, 4, adam);
        var reinsertion = new ScatterSearchReinsertion(qualityFraction: 0.5);

        var a = new FixedChromosome(0.0) { Fitness = 9.0 };
        var b = new FixedChromosome(0.0) { Fitness = 8.0 };
        var c = new FixedChromosome(5.0) { Fitness = 7.0 };
        var d = new FixedChromosome(10.0) { Fitness = 6.0 };

        var selected = reinsertion.SelectChromosomes(population, new List<IChromosome> { c, d }, new List<IChromosome> { a, b });

        Assert.That(selected.Select(ch => ch.Fitness!.Value).ToArray(),
            Is.EqualTo(new[] { 9.0, 8.0, 6.0, 7.0 }),
            "expected [A, B] by quality then [D, C] by max-min distance (D is farther from the selected set than C)");
    }

    /// <summary>
    /// The QualityFraction = 1 limit is plain elitism: pure fitness order, no diversity pass.
    /// </summary>
    [Test]
    public void Reinsertion_FullQuality_IsPureElitism()
    {
        var adam = new FixedChromosome(0.0);
        var population = new Population(4, 4, adam);
        var reinsertion = new ScatterSearchReinsertion(qualityFraction: 1.0);

        var a = new FixedChromosome(0.0) { Fitness = 9.0 };
        var b = new FixedChromosome(0.0) { Fitness = 8.0 };
        var c = new FixedChromosome(5.0) { Fitness = 7.0 };
        var d = new FixedChromosome(10.0) { Fitness = 6.0 };

        var selected = reinsertion.SelectChromosomes(population, new List<IChromosome> { c, d }, new List<IChromosome> { a, b });

        Assert.That(selected.Select(ch => ch.Fitness!.Value).ToArray(),
            Is.EqualTo(new[] { 9.0, 8.0, 7.0, 6.0 }));
    }

    /// <summary>
    /// The registry wires the compound by name (also exercised by the service test cases).
    /// </summary>
    [Test]
    public void MetaHeuristicsService_BuildsScatterSearchByName()
    {
        var built = MetaHeuristicsService.CreateMetaHeuristicByName(nameof(KnownCompoundMetaheuristics.ScatterSearch));
        Assert.That(built, Is.InstanceOf<MatchMetaHeuristic>());

        var rootType = MetaHeuristicsService.GetMetaHeuristicTypeByName(nameof(KnownCompoundMetaheuristics.ScatterSearch));
        Assert.That(rootType, Is.EqualTo(typeof(MatchMetaHeuristic)));
    }

    /// <summary>
    /// KEYSTONE end-to-end: the built ScatterSearch drives a real <see cref="MetaGeneticAlgorithm"/>
    /// and optimises the Sphere function (minimise sum of squares -> fitness = -sum of squares).
    /// The convex combination is mean-seeking and Sphere is symmetric-unimodal, so the run
    /// converges comfortably; we assert it completes and reaches the origin region with the same
    /// threshold margin the PSO keystone uses (the draw is unseeded FastRandom).
    /// </summary>
    [Test]
    public void Build_DrivesMetaGeneticAlgorithm_EndToEnd_AndOptimises()
    {
        var ss = NewSs(maxGenerations: 60);
        var metaHeuristic = ss.Build();

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

        Assert.That(ga.BestChromosome, Is.Not.Null, "ScatterSearch produced no best chromosome");
        Assert.That(ga.BestChromosome.Fitness, Is.Not.Null,
            "ScatterSearch produced no evaluated offspring (the combination returned nothing)");
        double finalSumSq = -ga.BestChromosome.Fitness!.Value;

        Assert.Multiple(() =>
        {
            Assert.That(ga.GenerationsNumber, Is.EqualTo(60));
            Assert.That(ga.State, Is.EqualTo(GeneticAlgorithmState.TerminationReached));
            Assert.That(finalSumSq, Is.LessThan(1.0),
                $"ScatterSearch should reach the origin region; got sum-of-squares {finalSumSq}");
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
    /// A two-gene chromosome pinned to a value (ChromosomeBase enforces a 2-gene minimum); also
    /// the hand-built candidate for the reinsertion tests (fitness assigned by the test).
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
    /// A chromosome that randomises each gene in [min, max] on CreateNew, so the initial
    /// reference set is diverse.
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
    /// A ScatterSearch whose generation clock is test-controlled, so the per-individual lambda
    /// store can be driven without a live population.
    /// </summary>
    private sealed class GatedScatterSearch : ScatterSearch
    {
        protected override int GetCurrentGeneration(IEvolutionContext ctx) => 0;
    }

    /// <summary>
    /// A minimal evolution context for direct operator tests: a real backing
    /// <see cref="EvolutionContext"/> store (the GetOrAdd semantics under test) and controllable
    /// selected parents.
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
