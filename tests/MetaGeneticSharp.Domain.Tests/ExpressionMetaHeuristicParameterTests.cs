using GeneticSharp;
using MetaGeneticSharp;

namespace MetaGeneticSharp.Domain.Tests;

/// <summary>
/// Phase 3 keystone tests: expression-tree parameter fusion. Verifies that an
/// <see cref="ExpressionMetaHeuristicParameter{TParamType, TArg1}"/> whose lambda references
/// another named parameter is reduced by <see cref="ParameterReplacer"/> into a single closed
/// expression tree, producing the same value as the hand-wired equivalent.
/// </summary>
public class ExpressionMetaHeuristicParameterTests
{
    [Test]
    public void ExpressionParameter_SimpleLambda_CompilesAndGeneratesValue()
    {
        var ctx = new EvolutionContext();
        var heuristic = new NoOpMetaHeuristic();
        var param = new ExpressionMetaHeuristicParameter<int>
        {
            Scope = ParamScope.None,
            DynamicGenerator = (h, c) => 7
        };

        var value = param.Get<int>(heuristic, ctx, "p");

        Assert.That(value, Is.EqualTo(7));
    }

    [Test]
    public void ExpressionParameter_WithArg_FusesNamedDependency()
    {
        // A dependency parameter "base" producing 10, registered under its name.
        var ctx = new EvolutionContext();
        var heuristic = new NoOpMetaHeuristic();
        var baseParam = new ExpressionMetaHeuristicParameter<int>
        {
            Scope = ParamScope.None,
            DynamicGenerator = (h, c) => 10
        };
        // The lambda parameter name ("baseValue") is what ParameterReplacer looks up, so the
        // dependency must be registered under that exact name.
        ctx.RegisterParameter("baseValue", baseParam);

        // A depending parameter whose lambda has one extra parameter "baseValue" that the replacer
        // must fuse away, yielding a closed (h, c) => baseValue * 2 tree.
        var derived = new ExpressionMetaHeuristicParameter<int, int>
        {
            Scope = ParamScope.None,
            DynamicGeneratorWithArg = (h, c, baseValue) => baseValue * 2
        };

        var value = derived.Get<int>(heuristic, ctx, "derived");

        Assert.That(value, Is.EqualTo(20));
    }

    [Test]
    public void ExpressionParameter_WithArg_MatchesHandWiredEquivalent()
    {
        // Acceptance criterion: the fused expression must behave identically to a hand-written
        // closed generator computing the same thing.
        var ctx = new EvolutionContext();
        var heuristic = new NoOpMetaHeuristic();

        var baseParam = new ExpressionMetaHeuristicParameter<int>
        {
            Scope = ParamScope.None,
            DynamicGenerator = (h, c) => 3
        };
        ctx.RegisterParameter("baseValue", baseParam);

        var fused = new ExpressionMetaHeuristicParameter<int, int>
        {
            Scope = ParamScope.None,
            DynamicGeneratorWithArg = (h, c, baseValue) => baseValue * baseValue + 1
        };

        var handWired = new MetaHeuristicParameter<int>
        {
            Scope = ParamScope.None,
            Generator = (h, c) => 10
        };

        Assert.Multiple(() =>
        {
            Assert.That(fused.Get<int>(heuristic, ctx, "fused"), Is.EqualTo(10));
            Assert.That(handWired.Get<int>(heuristic, ctx, "wired"), Is.EqualTo(fused.Get<int>(heuristic, ctx, "fused")));
        });
    }

    [Test]
    public void ExpressionParameter_Scoped_CachesAcrossCallsWithinScope()
    {
        // A scope-masked parameter should compute once per scope key and return the cached value
        // on subsequent calls within the same generation/stage/individual. We count computations
        // through a captured mutable holder (expression trees forbid statement bodies / assignment,
        // so the side effect rides on a method call inside the expression).
        var ctx = new EvolutionContext();
        ctx.CurrentStage = EvolutionStage.Crossover;
        var heuristic = new NoOpMetaHeuristic();

        var counter = new ComputeCounter();
        var param = new ExpressionMetaHeuristicParameter<int>
        {
            Scope = ParamScope.Generation | ParamScope.Stage,
            DynamicGenerator = (h, c) => counter.TallyAndReturn(42)
        };

        var first = param.Get<int>(heuristic, ctx, "p");
        var second = param.Get<int>(heuristic, ctx, "p");

        Assert.Multiple(() =>
        {
            Assert.That(first, Is.EqualTo(42));
            Assert.That(second, Is.EqualTo(42));
            Assert.That(counter.Calls, Is.EqualTo(1), "Scoped parameter must compute once and cache within scope.");
        });
    }

    private class ComputeCounter
    {
        public int Calls { get; private set; }

        public int TallyAndReturn(int value)
        {
            Calls++;
            return value;
        }
    }
}
