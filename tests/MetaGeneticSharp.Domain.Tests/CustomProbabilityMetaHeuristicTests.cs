using GeneticSharp;
using MetaGeneticSharp;
using MetaGeneticSharp.Domain.Tests.Geometric;

namespace MetaGeneticSharp.Domain.Tests;

/// <summary>
/// Unit tests for <see cref="CustomProbabilityMetaHeuristic"/> and its probability
/// machinery (<see cref="ProbabilityConfig"/>, <see cref="OperatorsProbabilityConfig"/>,
/// <see cref="ProbabilityStrategy"/>).
///
/// The behaviour under test is the documented "a base probability greater than 1 yields
/// multiple operator applications (one full run per unit, then a probabilistic run for the
/// remainder)" rule — the multi-application loop driven by <c>ShouldRun</c>. It had no
/// direct unit test: it was only exercised indirectly through concrete compounds. These
/// tests pin it via a minimal recording subclass that records each operator invocation and
/// the sub-probability it received, with a fixed randomization so the probabilistic
/// remainder branch is deterministic. The class itself is reused unchanged — the subclass
/// only records, it does not reimplement the loop.
/// </summary>
[TestFixture]
public class CustomProbabilityMetaHeuristicTests
{
    // A deterministic CustomProbabilityMetaHeuristic that records every Do* invocation and
    // the sub-probability handed to it, and emits one fresh child per crossover run so that
    // child accumulation across runs is observable.
    private sealed class RecordingProbabilityMetaHeuristic : CustomProbabilityMetaHeuristic
    {
        public List<float> CrossoverSubProbabilities { get; } = new();
        public List<float> MutationSubProbabilities { get; } = new();
        public int CrossoverRuns => CrossoverSubProbabilities.Count;
        public int MutationRuns => MutationSubProbabilities.Count;

        protected override IList<IChromosome> DoMatchParentsAndCross(
            IEvolutionContext ctx, ICrossover crossover, float crossoverProbability, IList<IChromosome> parents)
        {
            CrossoverSubProbabilities.Add(crossoverProbability);
            return new List<IChromosome> { new DoubleArrayChromosome(new[] { 0.0, 0.0 }) };
        }

        protected override void DoMutateChromosome(
            IEvolutionContext ctx, IMutation mutation, float mutationProbability, IList<IChromosome> offSprings)
        {
            MutationSubProbabilities.Add(mutationProbability);
        }

        // The selection and reinsertion stages are not under test here; pass through trivially
        // so the abstract MetaHeuristicBase contract is satisfied without affecting the
        // crossover/mutation loop being measured.
        public override IList<IChromosome> SelectParentPopulation(IEvolutionContext ctx, ISelection selection) => null!;

        public override IList<IChromosome> Reinsert(
            IEvolutionContext ctx, IReinsertion reinsertion, IList<IChromosome> offspring, IList<IChromosome> parents) => offspring;
    }

    // Fixed randomization: GetDouble() always returns the same value, so the probabilistic
    // remainder branch of ShouldRun is deterministic. Only the three RandomizationBase
    // abstractions need overriding; the rest derive from them.
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

    private static RecordingProbabilityMetaHeuristic WithCrossover(ProbabilityStrategy strategy, float staticProbability)
    {
        return new RecordingProbabilityMetaHeuristic
        {
            ProbabilityConfig = new OperatorsProbabilityConfig
            {
                Crossover = new ProbabilityConfig { Strategy = strategy, StaticProbability = staticProbability },
            },
        };
    }

    private static IList<IChromosome> Cross(RecordingProbabilityMetaHeuristic mh, float initialProbability)
        => mh.MatchParentsAndCross(new EvolutionContext(), crossover: null!, initialProbability, new List<IChromosome>());

    // =========================================================================
    // ProbabilityConfig.GetProbability: how the base probability is resolved.
    // =========================================================================
    [Test]
    public void GetProbability_PassToDescendents_ReturnsInitialProbabilityUnchanged()
    {
        var config = new ProbabilityConfig { Strategy = ProbabilityStrategy.PassToDescendents };
        Assert.That(config.GetProbability(new EvolutionContext(), 0.7f), Is.EqualTo(0.7f));
    }

    [Test]
    public void GetProbability_Overwrite_NoDynamic_UsesStaticProbability()
    {
        var config = new ProbabilityConfig { Strategy = ProbabilityStrategy.OverwriteProbability, StaticProbability = 0.3f };
        // The initial 0.9 is discarded in favour of the configured static value.
        Assert.That(config.GetProbability(new EvolutionContext(), 0.9f), Is.EqualTo(0.3f));
    }

    [Test]
    public void GetProbability_Overwrite_WithDynamic_UsesDynamicProbability()
    {
        var config = new ProbabilityConfig
        {
            Strategy = ProbabilityStrategy.OverwriteProbability,
            DynamicProbability = (_, initial) => initial * 2f,
        };
        Assert.That(config.GetProbability(new EvolutionContext(), 0.25f), Is.EqualTo(0.5f));
    }

    // =========================================================================
    // ShouldRun via MatchParentsAndCross: the multi-application loop.
    // =========================================================================
    [Test]
    public void Crossover_ZeroProbability_DoesNotRun_AndReturnsNull()
    {
        var mh = WithCrossover(ProbabilityStrategy.PassToDescendents, staticProbability: 1f);
        IList<IChromosome> result = Cross(mh, initialProbability: 0f);
        Assert.That(mh.CrossoverRuns, Is.EqualTo(0));
        Assert.That(result, Is.Null);
    }

    [Test]
    public void Crossover_PassToDescendents_UnitProbability_RunsExactlyOnce()
    {
        var mh = WithCrossover(ProbabilityStrategy.PassToDescendents, staticProbability: 1f);
        IList<IChromosome> result = Cross(mh, initialProbability: 1f);
        Assert.That(mh.CrossoverRuns, Is.EqualTo(1));
        Assert.That(result, Has.Count.EqualTo(1));
        // PassToDescendents hands the probability straight through to the descendant operator.
        Assert.That(mh.CrossoverSubProbabilities, Is.EqualTo(new[] { 1f }));
    }

    [Test]
    public void Crossover_IntegerProbabilityThree_RunsThreeTimes_AndAccumulatesChildren()
    {
        // The documented rule: a base probability of 3 yields three full operator applications.
        var mh = WithCrossover(ProbabilityStrategy.OverwriteProbability, staticProbability: 3f);
        IList<IChromosome> result = Cross(mh, initialProbability: 1f);
        Assert.That(mh.CrossoverRuns, Is.EqualTo(3));
        // Children from every run accumulate into a single returned list.
        Assert.That(result, Has.Count.EqualTo(3));
        // PassToDescendents (default within Overwrite, no TestProbability flag): the residual
        // probability decreases by one each run and is passed through unchanged.
        Assert.That(mh.CrossoverSubProbabilities, Is.EqualTo(new[] { 3f, 2f, 1f }));
    }

    [Test]
    public void Crossover_TestProbability_FractionalRemainder_RngBelowThreshold_RunsRemainder()
    {
        // base=2.5 with TestProbability: two full unit runs, then the 0.5 remainder run fires
        // because the fixed RNG draw (0.0) is below the residual 0.5. Three runs, each with
        // sub-probability forced to 1 (TestProbability: descendants run with probability 1).
        RandomizationProvider.Current = new FixedRandomization(0.0);
        var mh = WithCrossover(
            ProbabilityStrategy.OverwriteProbability | ProbabilityStrategy.TestProbability, staticProbability: 2.5f);
        Cross(mh, initialProbability: 1f);
        Assert.That(mh.CrossoverRuns, Is.EqualTo(3));
        Assert.That(mh.CrossoverSubProbabilities, Is.EqualTo(new[] { 1f, 1f, 1f }));
    }

    [Test]
    public void Crossover_TestProbability_FractionalRemainder_RngAboveThreshold_SkipsRemainder()
    {
        // Same base=2.5, but the fixed RNG draw (0.9) is above the residual 0.5, so the
        // probabilistic remainder run is skipped: only the two full unit runs happen.
        RandomizationProvider.Current = new FixedRandomization(0.9);
        var mh = WithCrossover(
            ProbabilityStrategy.OverwriteProbability | ProbabilityStrategy.TestProbability, staticProbability: 2.5f);
        Cross(mh, initialProbability: 1f);
        Assert.That(mh.CrossoverRuns, Is.EqualTo(2));
        Assert.That(mh.CrossoverSubProbabilities, Is.EqualTo(new[] { 1f, 1f }));
    }

    [Test]
    public void Crossover_TestProbability_SubUnitProbability_RngBelowThreshold_RunsOnceWithProbabilityOne()
    {
        // base=0.4 (< 1) with TestProbability: a single probabilistic run when the RNG draw
        // (0.1) is below 0.4, and the descendant receives probability 1 rather than 0.4.
        RandomizationProvider.Current = new FixedRandomization(0.1);
        var mh = WithCrossover(
            ProbabilityStrategy.OverwriteProbability | ProbabilityStrategy.TestProbability, staticProbability: 0.4f);
        Cross(mh, initialProbability: 1f);
        Assert.That(mh.CrossoverRuns, Is.EqualTo(1));
        Assert.That(mh.CrossoverSubProbabilities, Is.EqualTo(new[] { 1f }));
    }

    [Test]
    public void Crossover_TestProbability_SubUnitProbability_RngAboveThreshold_DoesNotRun()
    {
        // base=0.4 with TestProbability and an RNG draw (0.8) above 0.4: no run at all.
        RandomizationProvider.Current = new FixedRandomization(0.8);
        var mh = WithCrossover(
            ProbabilityStrategy.OverwriteProbability | ProbabilityStrategy.TestProbability, staticProbability: 0.4f);
        IList<IChromosome> result = Cross(mh, initialProbability: 1f);
        Assert.That(mh.CrossoverRuns, Is.EqualTo(0));
        Assert.That(result, Is.Null);
    }

    // =========================================================================
    // ShouldRun via MutateChromosome: same loop on the mutation side.
    // =========================================================================
    [Test]
    public void Mutation_IntegerProbabilityTwo_RunsTwice()
    {
        var mh = new RecordingProbabilityMetaHeuristic
        {
            ProbabilityConfig = new OperatorsProbabilityConfig
            {
                Mutation = new ProbabilityConfig
                {
                    Strategy = ProbabilityStrategy.OverwriteProbability,
                    StaticProbability = 2f,
                },
            },
        };
        mh.MutateChromosome(new EvolutionContext(), mutation: null!, 1f, new List<IChromosome>());
        Assert.That(mh.MutationRuns, Is.EqualTo(2));
        Assert.That(mh.MutationSubProbabilities, Is.EqualTo(new[] { 2f, 1f }));
    }

    [Test]
    public void Mutation_ZeroProbability_DoesNotRun()
    {
        var mh = WithCrossover(ProbabilityStrategy.PassToDescendents, 1f); // mutation config defaults to passthrough
        mh.MutateChromosome(new EvolutionContext(), mutation: null!, 0f, new List<IChromosome>());
        Assert.That(mh.MutationRuns, Is.EqualTo(0));
    }
}
