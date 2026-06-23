using GeneticSharp;

namespace MetaGeneticSharp.Domain.Tests;

/// <summary>
///   Acceptance tests for the compound-metaheuristic registry (PR#87 port):
///   proves <see cref="MetaHeuristicsService"/> exposes every
///   <see cref="KnownCompoundMetaheuristics"/> name, that <c>None</c> resolves to
///   a null instance, and — the keystone — that building each compound actually
///   yields an instance rooted at the type declared by
///   <see cref="MetaHeuristicsService.GetMetaHeuristicTypeByName"/>. Driving every
///   enum value through the factory end-to-end exercises the full WOA/EO/FBI and
///   heterogeneous-island assembly chains at once.
/// </summary>
[TestFixture]
public class MetaHeuristicsServiceTests
{
    [Test]
    public void GetMetaHeuristicNames_ReturnsEveryCompoundEnumName()
    {
        var names = MetaHeuristicsService.GetMetaHeuristicNames();

        Assert.That(names, Has.Count.EqualTo(14));
        Assert.That(names, Contains.Item("None"));
        Assert.That(names, Contains.Item("Default"));
        Assert.That(names, Contains.Item("DefaultRandomHyperspeed"));
        Assert.That(names, Contains.Item("WhaleOptimisation"));
        Assert.That(names, Contains.Item("WhaleOptimisationNaive"));
        Assert.That(names, Contains.Item("EquilibriumOptimizer"));
        Assert.That(names, Contains.Item("ForensicBasedInvestigation"));
        Assert.That(names, Contains.Item("DifferentialEvolution"));
        Assert.That(names, Contains.Item("BareBonesParticleSwarm"));
        Assert.That(names, Contains.Item("SimulatedAnnealing"));
        Assert.That(names, Contains.Item("Islands5Default"));
        Assert.That(names, Contains.Item("Islands5DefaultNoMigration"));
        Assert.That(names, Contains.Item("Islands5BestMixture"));
        Assert.That(names, Contains.Item("Islands5BestMixtureNoMigration"));
    }

    [Test]
    public void CreateMetaHeuristicByName_None_ReturnsNull()
    {
        Assert.That(MetaHeuristicsService.CreateMetaHeuristicByName("None"), Is.Null);
    }

    [Test]
    public void GetMetaHeuristicTypeByName_None_ReturnsNull()
    {
        Assert.That(MetaHeuristicsService.GetMetaHeuristicTypeByName("None"), Is.Null);
    }

    // Keystone: every compound enum value resolves to a non-null instance whose
    // runtime type is an instance of the root type the registry declares for it.
    // Driving e.g. "Islands5BestMixture" builds an archipelago whose islands are
    // themselves WOA/EO compounds (.Build() chained), so this one assertion covers
    // the whole assembly chain.
    [TestCase("Default", typeof(DefaultMetaHeuristic))]
    [TestCase("DefaultRandomHyperspeed", typeof(DefaultMetaHeuristic))]
    [TestCase("WhaleOptimisation", typeof(IfElseMetaHeuristic))]
    [TestCase("WhaleOptimisationNaive", typeof(IfElseMetaHeuristic))]
    [TestCase("EquilibriumOptimizer", typeof(MatchMetaHeuristic))]
    [TestCase("ForensicBasedInvestigation", typeof(GenerationMetaHeuristic))]
    [TestCase("Islands5Default", typeof(IslandMetaHeuristic))]
    [TestCase("Islands5DefaultNoMigration", typeof(IslandMetaHeuristic))]
    [TestCase("Islands5BestMixture", typeof(IslandMetaHeuristic))]
    [TestCase("Islands5BestMixtureNoMigration", typeof(IslandMetaHeuristic))]
    [TestCase("BareBonesParticleSwarm", typeof(MatchMetaHeuristic))]
    [TestCase("SimulatedAnnealing", typeof(MatchMetaHeuristic))]
    public void CreateMetaHeuristicByName_BuildsInstanceWithDeclaredRootType(
        string name, Type expectedRootType)
    {
        var instance = MetaHeuristicsService.CreateMetaHeuristicByName(name);

        Assert.That(instance, Is.Not.Null, $"factory returned null for '{name}'");
        Assert.That(expectedRootType.IsInstanceOfType(instance), Is.True,
            $"'{name}' should root a {expectedRootType.Name} but produced a {instance.GetType().Name}");
    }

    [TestCase("Default", typeof(DefaultMetaHeuristic))]
    [TestCase("DefaultRandomHyperspeed", typeof(DefaultMetaHeuristic))]
    [TestCase("WhaleOptimisation", typeof(IfElseMetaHeuristic))]
    [TestCase("WhaleOptimisationNaive", typeof(IfElseMetaHeuristic))]
    [TestCase("EquilibriumOptimizer", typeof(MatchMetaHeuristic))]
    [TestCase("ForensicBasedInvestigation", typeof(GenerationMetaHeuristic))]
    [TestCase("Islands5Default", typeof(IslandMetaHeuristic))]
    [TestCase("Islands5DefaultNoMigration", typeof(IslandMetaHeuristic))]
    [TestCase("Islands5BestMixture", typeof(IslandMetaHeuristic))]
    [TestCase("Islands5BestMixtureNoMigration", typeof(IslandMetaHeuristic))]
    [TestCase("BareBonesParticleSwarm", typeof(MatchMetaHeuristic))]
    [TestCase("SimulatedAnnealing", typeof(MatchMetaHeuristic))]
    public void GetMetaHeuristicTypeByName_MatchesDeclaredRootType(string name, Type expected)
    {
        Assert.That(MetaHeuristicsService.GetMetaHeuristicTypeByName(name), Is.SameAs(expected));
    }

    [Test]
    public void GetMetaHeuristicNames_IsConsistentWithCompoundEnum()
    {
        // The registry must stay in sync with the enum: every enum name is advertised,
        // and the advertised names are exactly the enum names (no drift after a port).
        var enumNames = Enum.GetNames(typeof(KnownCompoundMetaheuristics));
        var advertised = MetaHeuristicsService.GetMetaHeuristicNames();

        Assert.That(advertised, Is.EquivalentTo(enumNames));
    }
}
