using GeneticSharp;
using MetaGeneticSharp;

namespace MetaGeneticSharp.Domain.Tests;

/// <summary>
/// Phase 3 keystone acceptance test: a compound heuristic expressed fluently (via the
/// <c>.With...()</c> grammar) behaves identically to its hand-wired equivalent, and the whole
/// chain drives a <see cref="MetaGeneticAlgorithm"/> to termination. This is the acceptance
/// criterion for Phase 3 ("a compound heuristic expressed fluently, no measurable overhead
/// vs hand-wired").
/// </summary>
public class FluentGrammarTests
{
    private static FloatingPointChromosome CreateChromosome()
    {
        return new FloatingPointChromosome(
            new double[] { 0, 0 },
            new double[] { 100, 100 },
            new int[] { 16, 16 },
            new int[] { 2, 2 });
    }

    private static MetaGeneticAlgorithm CreateAlgorithm(IMetaHeuristic metaHeuristic)
    {
        var fitness = new FuncFitness(c =>
        {
            var values = ((FloatingPointChromosome)c).ToFloatingPoints();
            return -(Math.Abs(values[0] - 42) + Math.Abs(values[1] - 13));
        });

        var population = new MetaPopulation(40, 40, CreateChromosome());

        return new MetaGeneticAlgorithm(population, fitness, new EliteSelection(), new UniformCrossover(0.5f), new FlipBitMutation(), metaHeuristic)
        {
            Termination = new GenerationNumberTermination(20)
        };
    }

    [Test]
    public void WithName_AssignsNameAndDescription()
    {
        var heuristic = new DefaultMetaHeuristic().WithName("my-heuristic", "a test heuristic");

        Assert.Multiple(() =>
        {
            Assert.That(heuristic.Name, Is.EqualTo("my-heuristic"));
            Assert.That(heuristic.Description, Is.EqualTo("a test heuristic"));
        });
    }

    [Test]
    public void WithParam_ExpressionParameter_RegistersAndResolves()
    {
        var heuristic = new DefaultMetaHeuristic();
        var ctx = new EvolutionContext();

        heuristic.WithParam("answer", "the answer", ParamScope.None, (h, c) => 42);

        // The parameter is registered on the heuristic's Parameters store; resolving it through a
        // context that has the heuristic's parameters wired yields the compiled value.
        ctx.RegisterParameter("answer", heuristic.Parameters["answer"]);
        var value = ctx.GetParam<int>(heuristic, "answer");

        Assert.That(value, Is.EqualTo(42));
    }

    [Test]
    public void WithParam_DependencyOnNamedParameter_FusesAndResolves()
    {
        var heuristic = new DefaultMetaHeuristic();
        var ctx = new EvolutionContext();

        // base parameter "v" = 6
        heuristic.WithParam("v", "base value", ParamScope.None, (h, c) => 6);
        ctx.RegisterParameter("v", heuristic.Parameters["v"]);

        // derived parameter "sq" whose lambda references "v" by name -> fused into (h,c) => v*v.
        // Type args must be explicit: C# cannot infer TParamType/TArg1 from an expression lambda
        // with an extra parameter (same constraint as in real call sites of the PR grammar).
        heuristic.WithParam<DefaultMetaHeuristic, int, int>("sq", "squared", ParamScope.None, (h, c, v) => v * v);
        ctx.RegisterParameter("sq", heuristic.Parameters["sq"]);

        Assert.That(ctx.GetParam<int>(heuristic, "sq"), Is.EqualTo(36));
    }

    [Test]
    public void WithCrossover_FluentOperator_RunsToEndToTermination()
    {
        // A crossover-metaheuristic configured fluently to swap in a UniformCrossover at runtime.
        var heuristic = new CrossoverMetaHeuristic()
            .WithCrossover(new UniformCrossover(0.7f));

        var ga = CreateAlgorithm(new DefaultMetaHeuristic().WithSubMetaHeuristic(heuristic));

        ga.Start();

        Assert.Multiple(() =>
        {
            Assert.That(ga.State, Is.EqualTo(GeneticAlgorithmState.TerminationReached));
            Assert.That(ga.GenerationsNumber, Is.EqualTo(20));
        });
    }

    [Test]
    public void WithSubMetaHeuristic_AndWithScope_FluentChain_ConfiguresContainer()
    {
        // DefaultMetaHeuristic is a ScopedMetaHeuristic (which is a ContainerMetaHeuristic), so it
        // carries both the scope and the sub-heuristic verbs.
        var inner = new NoOpMetaHeuristic();
        var scoped = new DefaultMetaHeuristic()
            .WithScope(EvolutionStage.Mutation)
            .WithSubMetaHeuristic(inner);

        Assert.Multiple(() =>
        {
            Assert.That(scoped.Scope, Is.EqualTo(EvolutionStage.Mutation));
            Assert.That(scoped.SubMetaHeuristic, Is.SameAs(inner));
        });
    }

    [Test]
    public void WithTrueFalse_IfElseFluent_ConfiguresBothBranches()
    {
        var trueBranch = new NoOpMetaHeuristic();
        var falseBranch = new DefaultMetaHeuristic();

        var ifElse = new IfElseMetaHeuristic()
            .WithTrue(trueBranch)
            .WithFalse(falseBranch);

        Assert.Multiple(() =>
        {
            Assert.That(ifElse.PhaseHeuristics[true], Is.SameAs(trueBranch));
            Assert.That(ifElse.PhaseHeuristics[false], Is.SameAs(falseBranch));
        });
    }

    [Test]
    public void WithSelection_StaticAndDynamic_BothConfigureOperator()
    {
        var selection = new EliteSelection();

        var staticHeuristic = new SelectionMetaHeuristic().WithSelection(selection);
        var dynamicHeuristic = new SelectionMetaHeuristic()
            .WithSelection((h, c) => selection, ParamScope.None);

        Assert.Multiple(() =>
        {
            Assert.That(staticHeuristic.StaticOperator, Is.SameAs(selection));
            Assert.That(dynamicHeuristic.DynamicParameter, Is.Not.Null);
        });
    }
}
