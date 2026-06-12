using GeneticSharp;
using MetaGeneticSharp;

namespace MetaGeneticSharp.Domain.Tests;

public class ControlFlowPrimitivesTests
{
    private static FloatingPointChromosome CreateChromosome()
    {
        return new FloatingPointChromosome(
            new double[] { 0, 0 },
            new double[] { 100, 100 },
            new int[] { 16, 16 },
            new int[] { 2, 2 });
    }

    private static MetaGeneticAlgorithm CreateAlgorithm(int generations, IMetaHeuristic metaHeuristic)
    {
        var fitness = new FuncFitness(c =>
        {
            var values = ((FloatingPointChromosome)c).ToFloatingPoints();
            return -(Math.Abs(values[0] - 42) + Math.Abs(values[1] - 13));
        });

        var population = new MetaPopulation(40, 40, CreateChromosome());

        var ga = new MetaGeneticAlgorithm(population, fitness, new EliteSelection(), new UniformCrossover(0.5f), new FlipBitMutation(), metaHeuristic)
        {
            Termination = new GenerationNumberTermination(generations)
        };
        return ga;
    }

    [Test]
    public void EnumeratedPhases_GetPhaseIndex_MapsContiguousRangesAndWraps()
    {
        var phases = new SizeBasedMetaHeuristic.EnumeratedPhases(new[] { 2, 3 });

        Assert.Multiple(() =>
        {
            Assert.That(phases.TotalPhaseSize, Is.EqualTo(5));
            Assert.That(phases.GetPhaseIndex(0, out var local0), Is.EqualTo(0));
            Assert.That(local0, Is.EqualTo(0));
            Assert.That(phases.GetPhaseIndex(1, out _), Is.EqualTo(0));
            Assert.That(phases.GetPhaseIndex(2, out var local2), Is.EqualTo(1));
            Assert.That(local2, Is.EqualTo(0));
            Assert.That(phases.GetPhaseIndex(4, out var local4), Is.EqualTo(1));
            Assert.That(local4, Is.EqualTo(2));
            Assert.That(phases.GetPhaseIndex(5, out _), Is.EqualTo(0), "Index past the total size should wrap around.");
            Assert.That(phases.GetPhaseIndex(7, out _), Is.EqualTo(1));
        });
    }

    [Test]
    public void SwitchMetaHeuristic_DispatchesByKey_AndThrowsOnMissingKey()
    {
        var selector = 1;
        var switchHeuristic = new SwitchMetaHeuristic<int>
        {
            DynamicParameter = new MetaHeuristicParameter<int>
            {
                Scope = ParamScope.None,
                Generator = (h, ctx) => selector
            }
        };
        switchHeuristic.PhaseHeuristics[1] = new EmptyMetaHeuristic();
        var ctx = new EvolutionContext();

        Assert.Multiple(() =>
        {
            // EmptyMetaHeuristic is the null-returning branch: reaching it proves dispatch.
            Assert.That(switchHeuristic.SelectParentPopulation(ctx, new EliteSelection()), Is.Null);
            selector = 2;
            Assert.That(() => switchHeuristic.SelectParentPopulation(ctx, new EliteSelection()),
                Throws.InvalidOperationException);
        });
    }

    [Test]
    public void Start_GenerationMetaHeuristic_AlternatingPhases_RunsToTermination()
    {
        var heuristic = new GenerationMetaHeuristic(5, new DefaultMetaHeuristic(), new DefaultMetaHeuristic());
        var ga = CreateAlgorithm(20, heuristic);

        ga.Start();

        Assert.Multiple(() =>
        {
            Assert.That(ga.State, Is.EqualTo(GeneticAlgorithmState.TerminationReached));
            Assert.That(ga.GenerationsNumber, Is.EqualTo(20));
            Assert.That(ga.BestChromosome.Fitness, Is.Not.Null);
        });
    }

    [Test]
    public void Start_PopulationMetaHeuristic_TwoIndividualGroups_RunsToTermination()
    {
        var heuristic = new PopulationMetaHeuristic(20, new DefaultMetaHeuristic(), new DefaultMetaHeuristic());
        var ga = CreateAlgorithm(20, heuristic);

        ga.Start();

        Assert.That(ga.State, Is.EqualTo(GeneticAlgorithmState.TerminationReached));
    }

    [Test]
    public void Start_StageSwitchMetaHeuristic_PerStageDispatch_RunsToTermination()
    {
        var heuristic = new StageSwitchMetaHeuristic();
        heuristic.PhaseHeuristics[EvolutionStage.Selection] = new DefaultMetaHeuristic();
        heuristic.PhaseHeuristics[EvolutionStage.Crossover] = new MatchMetaHeuristic().WithMatches(MatchingKind.Current, MatchingKind.Random);
        heuristic.PhaseHeuristics[EvolutionStage.Mutation] = new DefaultMetaHeuristic();
        heuristic.PhaseHeuristics[EvolutionStage.Reinsertion] = new DefaultMetaHeuristic();

        var ga = CreateAlgorithm(20, heuristic);

        ga.Start();

        Assert.That(ga.State, Is.EqualTo(GeneticAlgorithmState.TerminationReached));
    }

    [Test]
    public void Start_OperatorWrappers_NestedStaticSubstitutions_RunsToTermination()
    {
        // Each wrapper substitutes one stage's operator and passes the other stages
        // through to its sub-metaheuristic, so the chain covers all three wrappers.
        var heuristic = new SelectionMetaHeuristic
        {
            StaticOperator = new TournamentSelection(2),
            SubMetaHeuristic = new MutationMetaHeuristic
            {
                StaticOperator = new UniformMutation(true),
                SubMetaHeuristic = new CrossoverMetaHeuristic
                {
                    StaticOperator = new OnePointCrossover()
                }
            }
        };

        // GA configured with different operators: the run only works through the substitutes.
        var ga = CreateAlgorithm(20, heuristic);

        ga.Start();

        Assert.Multiple(() =>
        {
            Assert.That(ga.State, Is.EqualTo(GeneticAlgorithmState.TerminationReached));
            Assert.That(ga.BestChromosome.Fitness, Is.Not.Null);
        });
    }

    [Test]
    public void CrossoverMetaHeuristic_ConstantScope_GeneratesOperatorOnce()
    {
        var generatorCalls = 0;
        var wrapper = new CrossoverMetaHeuristic
        {
            DynamicParameter = new MetaHeuristicParameter<ICrossover>
            {
                Scope = ParamScope.Constant,
                Generator = (h, ctx) =>
                {
                    generatorCalls++;
                    return new OnePointCrossover();
                }
            }
        };
        var parents = new List<IChromosome> { CreateChromosome(), CreateChromosome() };
        var ctx = new EvolutionContext { LocalIndex = 0 };

        var firstChildren = wrapper.MatchParentsAndCross(ctx, new UniformCrossover(), 1f, parents);
        var secondChildren = wrapper.MatchParentsAndCross(ctx, new UniformCrossover(), 1f, parents);

        Assert.Multiple(() =>
        {
            Assert.That(firstChildren, Is.Not.Empty);
            Assert.That(secondChildren, Is.Not.Empty);
            Assert.That(generatorCalls, Is.EqualTo(1), "Constant scope should promote the generated operator to StaticOperator.");
        });
    }

    [Test]
    public void EmptyMetaHeuristic_AllStages_ReturnNullOrNoOp()
    {
        var empty = new EmptyMetaHeuristic();
        var ctx = new EvolutionContext();
        var chromosomes = new List<IChromosome> { CreateChromosome() };

        Assert.Multiple(() =>
        {
            Assert.That(empty.SelectParentPopulation(ctx, new EliteSelection()), Is.Null);
            Assert.That(empty.MatchParentsAndCross(ctx, new UniformCrossover(), 1f, chromosomes), Is.Null);
            Assert.That(empty.Reinsert(ctx, new FitnessBasedElitistReinsertion(), chromosomes, chromosomes), Is.Null);
            Assert.That(() => empty.MutateChromosome(ctx, new FlipBitMutation(), 1f, chromosomes), Throws.Nothing);
        });
    }
}
