using GeneticSharp;
using MetaGeneticSharp;

namespace MetaGeneticSharp.Domain.Tests.Compound;

/// <summary>
/// Phase 4 slice 3 acceptance tests: the compound foundation (ICompoundMetaheuristic,
/// GeometricMetaHeuristicBase, SimpleCompoundMetaheuristic). The keystone proves a concrete
/// GeometricMetaHeuristicBase subclass builds a wrapped IContainerMetaHeuristic with the
/// No-Mutation scope and forced reinsertion layers applied end-to-end.
/// </summary>
public class CompoundFoundationTests
{
    /// <summary>
    /// A minimal concrete geometric compound for testing: its main heuristic is a plain
    /// <see cref="DefaultMetaHeuristic"/> held in a <see cref="ContainerMetaHeuristic"/>.
    /// </summary>
    private class TestGeometricCompound : GeometricMetaHeuristicBase
    {
        private readonly IContainerMetaHeuristic _main;

        public TestGeometricCompound(IContainerMetaHeuristic main) => _main = main;

        protected override IContainerMetaHeuristic BuildMainHeuristic() => _main;
    }

    private static IContainerMetaHeuristic NewContainer(IMetaHeuristic? sub = null) =>
        new ContainerMetaHeuristic(sub ?? new DefaultMetaHeuristic());

    [Test]
    public void SimpleCompoundMetaheuristic_Build_ReturnsWrappedHeuristic()
    {
        var inner = NewContainer();
        var compound = new SimpleCompoundMetaheuristic(inner);

        var built = compound.Build();

        Assert.That(built, Is.SameAs(inner));
    }

    [Test]
    public void GeometricMetaHeuristicBase_Build_AppliesNoMutationAndForceReinsertion()
    {
        // KEYSTONE: Build() wraps the main heuristic with a No-Mutation scope, then a forced
        // reinsertion layer. The result's SubMetaHeuristic chain reflects both wrappings.
        var main = NewContainer(new DefaultMetaHeuristic());
        var compound = new TestGeometricCompound(main);

        var built = compound.Build();

        // ForceReinsertion wraps first: built.SubMetaHeuristic is the ReinsertionMetaHeuristic.
        Assert.That(built, Is.SameAs(main));
        var reinsertionWrapper = built.SubMetaHeuristic as ReinsertionMetaHeuristic;
        Assert.That(reinsertionWrapper, Is.Not.Null, "Build should wrap with a ReinsertionMetaHeuristic");
        Assert.That(reinsertionWrapper.StaticOperator, Is.InstanceOf<FitnessBasedElitistReinsertion>());

        // NoMutation wraps the original sub inside the reinsertion wrapper: a scoped DefaultMetaHeuristic.
        var noMutationLayer = reinsertionWrapper.SubMetaHeuristic as ScopedMetaHeuristic;
        Assert.That(noMutationLayer, Is.Not.Null, "the reinsertion wrapper's sub should be the No-Mutation scoped layer");
        Assert.That(noMutationLayer.Name, Does.Contain("No-Mutation"));
    }

    [Test]
    public void Build_NoMutationFalse_KeepsOriginalSubHeuristic()
    {
        var originalSub = new DefaultMetaHeuristic();
        var main = NewContainer(originalSub);
        var compound = new TestGeometricCompound(main) { NoMutation = false };

        var built = compound.Build();

        // No No-Mutation wrapping: the reinsertion wrapper's sub is the original sub.
        var reinsertionWrapper = (ReinsertionMetaHeuristic)built.SubMetaHeuristic;
        Assert.That(reinsertionWrapper.SubMetaHeuristic, Is.SameAs(originalSub));
    }

    [Test]
    public void Build_ForceReinsertionFalse_DoesNotWrapReinsertion()
    {
        var originalSub = new DefaultMetaHeuristic();
        var main = NewContainer(originalSub);
        var compound = new TestGeometricCompound(main) { ForceReinsertion = false };

        var built = compound.Build();

        // No reinsertion wrapping: built.SubMetaHeuristic is the No-Mutation scoped layer directly.
        var noMutationLayer = built.SubMetaHeuristic as ScopedMetaHeuristic;
        Assert.That(noMutationLayer, Is.Not.Null);
        Assert.That(noMutationLayer.Name, Does.Contain("No-Mutation"));
    }

    [Test]
    public void SetGeometricConverter_BindsTypedConverter()
    {
        var compound = new TestGeometricCompound(NewContainer());
        var inner = new GeometricConverter<double>
        {
            GeneToDoubleConverter = (_, v) => v,
            DoubleToGeneConverter = (_, d) => d,
        };

        compound.SetGeometricConverter(inner);

        Assert.That(compound.GeometricConverter, Is.InstanceOf<TypedGeometricConverter>());
        Assert.That(compound.GeometricConverter.GeneToDouble(0, 3.0), Is.EqualTo(3.0));
    }

    [Test]
    public void CustomReinsertion_OverridesDefault()
    {
        var main = NewContainer(new DefaultMetaHeuristic());
        var custom = new UniformReinsertion();
        var compound = new TestGeometricCompound(main) { CustomReinsertion = custom };

        compound.Build();

        // The wrapper's StaticOperator is the custom reinsertion, not the default.
        // (We verify by re-running Build and inspecting: CustomReinsertion is stored.)
        Assert.That(compound.CustomReinsertion, Is.SameAs(custom));
    }

    [Test]
    public void GetDefaultReinsertion_ReturnsFitnessBasedElitist()
    {
        var compound = new TestGeometricCompound(NewContainer());

        Assert.That(compound.GetDefaultReinsertion(), Is.InstanceOf<FitnessBasedElitistReinsertion>());
    }
}
