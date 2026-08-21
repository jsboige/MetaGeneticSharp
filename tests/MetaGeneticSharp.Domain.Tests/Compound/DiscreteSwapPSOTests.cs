using System;
using System.Collections.Generic;
using System.Linq;
using GeneticSharp;
using MetaGeneticSharp;

namespace MetaGeneticSharp.Domain.Tests.Compound;

/// <summary>
///   Acceptance tests for the <see cref="DiscreteSwapPSO"/> compound (#12049 axe 3, the
///   graduation of the Tranche-4 swap-PSO validated on notebook #12050). The algebra tests pin
///   the ygmh Move/Minus/Times operators pure and deterministic; the update test drives the
///   default operator on hand-built parents with a real store (per-particle memory, permutation
///   preservation); the keystone runs the built compound against a real
///   <see cref="MetaGeneticAlgorithm"/> on a masked-target permutation problem and asserts it
///   recovers the target exactly.
/// </summary>
public class DiscreteSwapPSOTests
{
    // --- Fixtures -----------------------------------------------------------

    /// <summary>A chromosome whose genes are a permutation of 0..n-1, shuffled on CreateNew.</summary>
    private sealed class RandomPermutationChromosome : ChromosomeBase
    {
        private readonly int _length;

        public RandomPermutationChromosome(int length) : base(length)
        {
            _length = length;
            Seed();
        }

        private object[] Shuffled()
        {
            var values = Enumerable.Range(0, _length).Cast<object>().ToArray();
            var rnd = RandomizationProvider.Current;
            for (int i = values.Length - 1; i > 0; i--)
            {
                int k = rnd.GetInt(0, i + 1);
                (values[i], values[k]) = (values[k], values[i]);
            }

            return values;
        }

        private void Seed()
        {
            var values = Shuffled();
            for (int i = 0; i < _length; i++)
                ReplaceGene(i, new Gene(values[i]));
        }

        public override IChromosome CreateNew() => new RandomPermutationChromosome(_length);

        // A single gene of a permutation chromosome is meaningless in isolation (the constraint
        // binds the WHOLE vector); a fresh shuffle keeps this answer consistent with CreateNew.
        public override Gene GenerateGene(int geneIndex) => new Gene(Shuffled()[geneIndex]);
    }

    /// <summary>A chromosome pinned to an explicit vector of gene values.</summary>
    private sealed class FixedChromosome : ChromosomeBase
    {
        private readonly object[] _values;

        public FixedChromosome(params object[] values) : base(values.Length)
        {
            _values = values;
            for (int i = 0; i < values.Length; i++)
                ReplaceGene(i, new Gene(values[i]));
        }

        public override IChromosome CreateNew() => new FixedChromosome(_values);
        public override Gene GenerateGene(int geneIndex) => new Gene(_values[geneIndex]);
    }

    /// <summary>A minimal evolution context backed by a real <see cref="EvolutionContext"/> store.</summary>
    private sealed class StubEvolutionContext : IEvolutionContext
    {
        private readonly EvolutionContext _store = new();

        public IGeneticAlgorithm GeneticAlgorithm { get; set; } = null!;
        public IPopulation Population { get; set; } = null!;
        public int OriginalIndex { get; set; } = 3;
        public int LocalIndex { get; set; } = -1;
        public EvolutionStage CurrentStage { get; set; } = EvolutionStage.Crossover;
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

    // Fixed randomization for the deterministic update tests: every GetDouble() returns the
    // same value, so the SAME draw serves as the r multiplier of the coefficient (rate =
    // c·v, clamped) and as the per-swap threshold of Times (keep iff v < c·v, i.e. 1 < c).
    // With a coefficient above 1 the retention is total, with 0 it is nil -- no run-to-run
    // variance. Only the three RandomizationBase abstractions need overriding.
    private sealed class FixedRandomization : RandomizationBase
    {
        private readonly double _value;
        public FixedRandomization(double value) => _value = value;
        public override int GetInt(int min, int max) => min;
        public override float GetFloat() => (float)_value;
        public override double GetDouble() => _value;
    }

    private IRandomization _originalRandomization = null!;

    [SetUp]
    public void SetUp() => _originalRandomization = RandomizationProvider.Current;

    [TearDown]
    public void TearDown() => RandomizationProvider.Current = _originalRandomization;

    private static GatedDiscreteSwapPSO GatedPso(int generation)
    {
        return new GatedDiscreteSwapPSO { FixedGeneration = generation };
    }

    // --- The algebra: Minus / Move / Times ----------------------------------

    /// <summary>
    /// The defining property: Move(from, Minus(from, to)) == to. The difference IS the path.
    /// </summary>
    [Test]
    public void MinusThenMove_ReachesTheTarget()
    {
        object[] from = { 3, 0, 1, 4, 2 };
        object[] to = { 0, 1, 2, 3, 4 };

        var swaps = DiscreteSwapPSO.Minus(from, to);
        var arrived = DiscreteSwapPSO.Move(from, swaps);

        Assert.That(arrived, Is.EqualTo(to), "applying the difference must land exactly on the target");
        Assert.That(swaps.Count, Is.LessThanOrEqualTo(from.Length - 1),
            "the greedy sequence needs at most n-1 transpositions");
    }

    [Test]
    public void Minus_IdenticalPermutationsGiveNoSwap()
    {
        Assert.That(DiscreteSwapPSO.Minus(new object[] { 1, 2, 3 }, new object[] { 1, 2, 3 }), Is.Empty);
    }

    /// <summary>
    /// The honest degradation: when the target holds a gene absent from the source (multisets
    /// differ), no transposition sequence exists -- the sequence stops at the longest common
    /// prefix instead of inventing an inapplicable swap.
    /// </summary>
    [Test]
    public void Minus_MultisetMismatch_StopsAtTheCommonPrefix()
    {
        object[] from = { 1, 2 };
        object[] to = { 1, 3 };

        Assert.That(DiscreteSwapPSO.Minus(from, to), Is.Empty,
            "gene 3 does not exist in the source: no swap can proceed, none is fabricated");
    }

    [Test]
    public void Move_EmptyIsIdentity_AndASwapIsAnInvolution()
    {
        object[] genes = { 3, 0, 1, 4, 2 };
        Assert.That(DiscreteSwapPSO.Move(genes, Array.Empty<DiscreteSwapPSO.Swap>()), Is.EqualTo(genes));

        var swap = new DiscreteSwapPSO.Swap(1, 3);
        var once = DiscreteSwapPSO.Move(genes, new[] { swap });
        var twice = DiscreteSwapPSO.Move(once, new[] { swap });
        Assert.That(twice, Is.EqualTo(genes), "applying the same transposition twice restores the source");
        Assert.That(once, Is.Not.EqualTo(genes), "sanity: the swap moved something");
    }

    /// <summary>
    /// Times is the scalar product: rate 0 kills the velocity, rate 1 keeps it whole, and
    /// out-of-range rates clamp (a coefficient above 1 must not throw).
    /// </summary>
    [Test]
    public void Times_ZeroDropsAll_OneKeepsAll_OutOfRangeClamps()
    {
        var swaps = new[] { new DiscreteSwapPSO.Swap(0, 1), new DiscreteSwapPSO.Swap(2, 3), new DiscreteSwapPSO.Swap(4, 5) };

        Assert.That(DiscreteSwapPSO.Times(swaps, 0.0), Is.Empty);
        Assert.That(DiscreteSwapPSO.Times(swaps, 1.0), Is.EqualTo(swaps));
        Assert.That(DiscreteSwapPSO.Times(swaps, 5.0), Is.EqualTo(swaps), "rate above 1 clamps to keep-all");
        Assert.That(DiscreteSwapPSO.Times(swaps, -1.0), Is.Empty, "negative rate clamps to drop-all");
    }

    // --- The update: memory + permutation preservation ----------------------

    /// <summary>
    /// The default update on hand-built parents (x, gbest) with default coefficients: whatever
    /// the sampled velocity, the child is a PERMUTATION of x's genes (same multiset) -- the
    /// search never leaves the discrete space, no repair step exists to call.
    /// </summary>
    [Test]
    public void DefaultUpdateOperator_ChildStaysAPermutation()
    {
        var pso = GatedPso(generation: 5);
        object[] xGenes = { 3, 0, 1, 4, 2 };
        object[] gbestGenes = { 0, 1, 2, 3, 4 };
        var x = new FixedChromosome(xGenes) { Fitness = 2.0 };
        var gbest = new FixedChromosome(gbestGenes) { Fitness = 9.0 };
        var ctx = new StubEvolutionContext { SelectedParents = new List<IChromosome> { x } };

        var children = pso.DefaultUpdateOperator(new List<IChromosome> { x, gbest }, ctx);

        Assert.That(children, Has.Count.EqualTo(1));
        var childGenes = children[0].GetGenes().Select(g => g.Value).ToArray();
        Assert.That(childGenes.OrderBy(v => v), Is.EqualTo(xGenes.OrderBy(v => v)),
            "swaps preserve the multiset: the child is a permutation of the parent, no repair needed");
    }

    /// <summary>
    /// Deterministic boundary of the social term: with w = 0, c1 = 0 and c2 above 1 under a
    /// fixed RNG (every draw = 0.999999 &lt; c·0.999999), Times keeps EVERY swap of
    /// (gbest ⊖ x), so Move lands EXACTLY on gbest regardless of the run.
    /// </summary>
    [Test]
    public void DefaultUpdateOperator_SocialOnly_ConvergesToGbestExactly()
    {
        RandomizationProvider.Current = new FixedRandomization(0.999999);
        var pso = GatedPso(generation: 5);
        pso.InertiaWeight = 0.0;
        pso.CognitiveCoefficient = 0.0;
        pso.SocialCoefficient = 1.49618;
        object[] xGenes = { 3, 0, 1, 4, 2 };
        object[] gbestGenes = { 0, 1, 2, 3, 4 };
        var x = new FixedChromosome(xGenes) { Fitness = 2.0 };
        var gbest = new FixedChromosome(gbestGenes) { Fitness = 9.0 };
        var ctx = new StubEvolutionContext { SelectedParents = new List<IChromosome> { x } };

        var child = pso.DefaultUpdateOperator(new List<IChromosome> { x, gbest }, ctx)[0];

        Assert.That(child.GetGenes().Select(g => g.Value).ToArray(), Is.EqualTo(gbestGenes),
            "full retention of the social sequence must land on the global best, deterministically");
    }

    /// <summary>
    /// The per-particle memory across generations, verified BEHAVIOURALLY (the store keys are
    /// private): at generation 5 a particle records its position (fitness 2) as personal best;
    /// at generation 6 the same particle slot sits on a WORSE position (fitness 0). With only
    /// the cognitive term active the update must pull the child to the GENERATION-5 RECORD, not
    /// to the current position -- that pull can only come from the store.
    /// </summary>
    [Test]
    public void DefaultUpdateOperator_PbestMemorySurvivesGenerations()
    {
        RandomizationProvider.Current = new FixedRandomization(0.999999);
        var pso = GatedPso(generation: 5);
        pso.InertiaWeight = 0.0;
        pso.CognitiveCoefficient = 1.49618;
        pso.SocialCoefficient = 0.0;
        object[] x1Genes = { 3, 0, 1, 4, 2 };
        object[] x2Genes = { 2, 4, 0, 3, 1 };
        var x1 = new FixedChromosome(x1Genes) { Fitness = 2.0 };
        var x2 = new FixedChromosome(x2Genes) { Fitness = 0.0 };
        var ctx = new StubEvolutionContext { SelectedParents = new List<IChromosome> { x1 } };

        // Generation 5: the particle's own position seeds and becomes the personal-best record.
        pso.DefaultUpdateOperator(new List<IChromosome> { x1, x1.Clone() }, ctx);

        // Generation 6: a worse occupant of the same slot (OriginalIndex 3 in both calls).
        pso.FixedGeneration = 6;
        ctx.SelectedParents = new List<IChromosome> { x2 };
        var child = pso.DefaultUpdateOperator(new List<IChromosome> { x2, x2.Clone() }, ctx)[0];

        Assert.That(child.GetGenes().Select(g => g.Value).ToArray(), Is.EqualTo(x1Genes),
            "the cognitive pull must target the remembered personal best (generation 5), not the current position");
    }

    // --- The assembly -------------------------------------------------------

    [Test]
    public void Build_AssemblesMatchMetaHeuristicRoot_CurrentAndBest()
    {
        var built = GatedPso(0).Build();

        Assert.That(built, Is.InstanceOf<MatchMetaHeuristic>());
        Assert.That(((NamedEntity)built).Name, Is.EqualTo("Discrete Swap PSO"));
        var kinds = ((MatchMetaHeuristic)built).Picker.MatchPicks.Select(m => m.MatchingKind).ToArray();
        Assert.That(kinds, Is.EqualTo(new[] { MatchingKind.Current, MatchingKind.Best }),
            "the whole-chromosome update needs the current position and the global best");
    }

    // --- KEYSTONE end-to-end ------------------------------------------------

    /// <summary>
    /// KEYSTONE: a masked-target permutation problem ("permuted one-max"): the fitness counts
    /// the positions matching a secret target permutation; the initial population is shuffled.
    /// The built DiscreteSwapPSO drives a real <see cref="MetaGeneticAlgorithm"/> and must
    /// recover the target EXACTLY (every position matched) -- the discrete analogue of the
    /// canonical PSO keystone reaching the origin region on Sphere.
    /// </summary>
    [Test]
    public void Build_DrivesMetaGeneticAlgorithm_AndRecoversTheMaskedTarget()
    {
        const int n = 10;
        // A fixed non-trivial target (not the identity, not reversed).
        object[] target = { 7, 2, 9, 4, 0, 8, 1, 6, 3, 5 };

        // The REAL compound (not gated): the generation clock and the per-particle store are
        // driven by the live population, so this exercises the actual PSO recurrence.
        // Mutation is LEFT ON (NoMutation = false) with a permutation-preserving SwapMutation:
        // once the swarm freezes onto the global best, the social and cognitive differences are
        // empty and the inertial churn only recycles STALE swaps -- it cannot invent the one
        // missing transposition. A low-rate swap mutation is the diversification the discrete
        // recurrence needs at convergence (the ygmh DiscretePSO gets it from its engine's
        // mutation; the canonical continuous PSO gets the equivalent for free from the
        // never-empty velocity in R^n). TworsMutation swaps two genes: permutation-preserving.
        var metaHeuristic = new DiscreteSwapPSO { NoMutation = false }.Build();

        var chromosome = new RandomPermutationChromosome(n);
        var fitness = new FuncFitness(c =>
        {
            var genes = c.GetGenes().Select(g => g.Value).ToArray();
            int matches = 0;
            for (int i = 0; i < n; i++)
                if (Equals(genes[i], target[i]))
                    matches++;
            return matches;
        });

        var population = new MetaPopulation(60, 60, chromosome);
        var ga = new MetaGeneticAlgorithm(
            population,
            fitness,
            new EliteSelection(),
            new UniformCrossover(0.5f),
            new TworsMutation(),
            metaHeuristic)
        {
            Termination = new GenerationNumberTermination(150)
        };

        ga.Start();

        Assert.That(ga.BestChromosome, Is.Not.Null, "the swap-PSO produced no best chromosome");
        Assert.That(ga.BestChromosome.Fitness, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(ga.GenerationsNumber, Is.EqualTo(150));
            Assert.That(ga.State, Is.EqualTo(GeneticAlgorithmState.TerminationReached));
            Assert.That(ga.BestChromosome.Fitness!.Value, Is.EqualTo(n),
                $"the swap-PSO must recover the masked target exactly; best fitness {ga.BestChromosome.Fitness!.Value}/{n}");
            Assert.That(ga.BestChromosome.GetGenes().Select(g => g.Value).ToArray(), Is.EqualTo(target));
        });
    }

    /// <summary>
    /// A DiscreteSwapPSO whose generation clock is test-controlled, so the per-particle store
    /// can be driven without a live population (same pattern as GatedScatterSearch).
    /// </summary>
    private sealed class GatedDiscreteSwapPSO : DiscreteSwapPSO
    {
        public int FixedGeneration { get; set; }

        protected override int GetCurrentGeneration(IEvolutionContext ctx) => FixedGeneration;
    }
}
