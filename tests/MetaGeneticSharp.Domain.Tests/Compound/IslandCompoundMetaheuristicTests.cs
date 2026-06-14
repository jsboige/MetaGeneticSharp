using GeneticSharp;
using MetaGeneticSharp;
using MetaGeneticSharp.Domain.Tests.Geometric;

namespace MetaGeneticSharp.Domain.Tests.Compound;

/// <summary>
/// Phase 4 slice 7 acceptance tests: the heterogeneous-island compound bridge.
/// IslandCompoundMetaheuristic builds an IslandMetaHeuristic from a list of compound metaheuristics
/// so that each island can run a different compound. The structural tests verify the three construction
/// shapes (fixed-size repeat, shared total, explicit list) and the Build() output kind; the end-to-end
/// keystone runs a built heterogeneous archipelago (two islands running distinct compounds) against a
/// real <see cref="MetaGeneticAlgorithm"/>, proving each island compound's Build() composes into the
/// island machinery without fault.
/// </summary>
public class IslandCompoundMetaheuristicTests
{
    private static SimpleCompoundMetaheuristic Compound(IContainerMetaHeuristic heuristic) => new SimpleCompoundMetaheuristic(heuristic);

    [Test]
    public void Ctor_FixedSize_RepeatsCompoundsEvenly()
    {
        var c1 = Compound(new DefaultMetaHeuristic().WithName("island-A"));
        var c2 = Compound(new DefaultMetaHeuristic().WithName("island-B"));

        // islandSize=10, islandNb=4, 2 compounds -> each compound repeated 4/2 = 2 times.
        var islandCompound = new IslandCompoundMetaheuristic(10, 4, c1, c2);

        Assert.That(islandCompound.IslandCompounds.Count, Is.EqualTo(4));
        Assert.That(islandCompound.IslandCompounds.Count(c => c.islandCompoundMetaheuristic == c1), Is.EqualTo(2));
        Assert.That(islandCompound.IslandCompounds.Count(c => c.islandCompoundMetaheuristic == c2), Is.EqualTo(2));
        Assert.That(islandCompound.IslandCompounds[0].islandSize, Is.EqualTo(10));
    }

    [Test]
    public void Ctor_SharedTotal_SplitsByShares()
    {
        var c1 = Compound(new DefaultMetaHeuristic().WithName("big-island"));
        var c2 = Compound(new DefaultMetaHeuristic().WithName("small-island"));

        // totalPopulation=20, shares (3,1) -> shareSize=5, so big=15, small=5.
        var islandCompound = new IslandCompoundMetaheuristic(20, (3, c1), (1, c2));

        Assert.That(islandCompound.IslandCompounds.Count, Is.EqualTo(2));
        Assert.That(islandCompound.IslandCompounds[0].islandSize, Is.EqualTo(15));
        Assert.That(islandCompound.IslandCompounds[1].islandSize, Is.EqualTo(5));
    }

    [Test]
    public void Defaults_MatchIslandMetaHeuristicCanonicalValues()
    {
        // Explicit two-island list (ctor 3), each island size 10 running a distinct compound.
        var islandCompound = new IslandCompoundMetaheuristic(
            (10, Compound(new DefaultMetaHeuristic())),
            (10, Compound(new DefaultMetaHeuristic())));

        Assert.Multiple(() =>
        {
            Assert.That(islandCompound.MigrationMode, Is.EqualTo(MigrationMode.RandomRing));
            Assert.That(islandCompound.MigrationsGenerationPeriod, Is.EqualTo(10));
            Assert.That(islandCompound.GlobalMigrationRate, Is.EqualTo(IslandMetaHeuristic.SmallMigrationRate));
        });
    }

    [Test]
    public void Build_AssemblesIslandMetaHeuristic()
    {
        var islandCompound = new IslandCompoundMetaheuristic(
            (10, Compound(new DefaultMetaHeuristic().WithName("island-A"))),
            (10, Compound(new DefaultMetaHeuristic().WithName("island-B"))));

        var built = islandCompound.Build();

        Assert.That(built, Is.InstanceOf<IslandMetaHeuristic>());
        var island = (IslandMetaHeuristic)built;
        Assert.That(island.MigrationMode, Is.EqualTo(MigrationMode.RandomRing));
    }

    /// <summary>
    /// KEYSTONE: a built heterogeneous archipelago drives a real <see cref="MetaGeneticAlgorithm"/>
    /// end-to-end. Two islands run distinct compounds (here two DefaultMetaHeuristic wrappers, proving
    /// the Build() pipeline composes each island compound into the island machinery); the whole
    /// archipelago evolves 15 generations to termination.
    /// </summary>
    [Test]
    public void Build_DrivesMetaGeneticAlgorithm_EndToEnd()
    {
        var islandCompound = new IslandCompoundMetaheuristic(
            (10, Compound(new DefaultMetaHeuristic().WithName("island-A"))),
            (10, Compound(new DefaultMetaHeuristic().WithName("island-B"))));

        var metaHeuristic = islandCompound.Build();

        // Fitness: minimize the sum of squares (target is the origin).
        var chromosome = new DoubleArrayChromosome(new double[] { 50.0, 50.0 });
        var fitness = new FuncFitness(c =>
        {
            var values = ((DoubleArrayChromosome)c).GetDoubleValues();
            return -(values[0] * values[0] + values[1] * values[1]);
        });

        // Two islands of 10 chromosomes each = population 20.
        var population = new MetaPopulation(20, 20, chromosome);
        var ga = new MetaGeneticAlgorithm(
            population,
            fitness,
            new EliteSelection(),
            new UniformCrossover(0.5f),
            new UniformMutation(true),
            metaHeuristic)
        {
            Termination = new GenerationNumberTermination(15)
        };

        ga.Start();

        Assert.Multiple(() =>
        {
            Assert.That(ga.GenerationsNumber, Is.EqualTo(15));
            Assert.That(ga.State, Is.EqualTo(GeneticAlgorithmState.TerminationReached));
            Assert.That(ga.BestChromosome, Is.Not.Null);
            Assert.That(ga.BestChromosome.Fitness, Is.Not.Null);
        });
    }
}
