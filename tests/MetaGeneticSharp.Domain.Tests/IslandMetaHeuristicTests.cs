using GeneticSharp;
using MetaGeneticSharp;

namespace MetaGeneticSharp.Domain.Tests;

public class IslandMetaHeuristicTests
{
    private static FloatingPointChromosome CreateChromosome()
    {
        return new FloatingPointChromosome(
            new double[] { 0, 0 },
            new double[] { 100, 100 },
            new int[] { 16, 16 },
            new int[] { 2, 2 });
    }

    private static FuncFitness CreateFitness()
    {
        return new FuncFitness(c =>
        {
            var values = ((FloatingPointChromosome)c).ToFloatingPoints();
            return -(Math.Abs(values[0] - 42) + Math.Abs(values[1] - 13));
        });
    }

    private class TestableIslandMetaHeuristic : IslandMetaHeuristic
    {
        public TestableIslandMetaHeuristic(int islandSize, params IMetaHeuristic[] phaseHeuristics)
            : base(islandSize, phaseHeuristics)
        {
        }

        public IList<IslandPopulation> GenerateIslands(IEvolutionContext ctx)
        {
            return GenerateSubPopulations(this, ctx);
        }
    }

    private static MetaPopulation CreateEvaluatedPopulation(int size)
    {
        var population = new MetaPopulation(size, size, CreateChromosome());
        population.CreateInitialGeneration();
        foreach (var c in population.CurrentGeneration.Chromosomes)
        {
            c.Fitness = 0.1;
        }

        return population;
    }

    [Test]
    public void GenerateSubPopulations_PartitionsPopulationInOrder()
    {
        var population = CreateEvaluatedPopulation(8);
        var metaHeuristic = new TestableIslandMetaHeuristic(4, new DefaultMetaHeuristic(), new DefaultMetaHeuristic());

        var islands = metaHeuristic.GenerateIslands(new EvolutionContext { Population = population });

        Assert.Multiple(() =>
        {
            Assert.That(islands, Has.Count.EqualTo(2));
            Assert.That(islands[0].ParentPopulation, Is.SameAs(population));
            Assert.That(islands[0].MinSize, Is.EqualTo(4));
            Assert.That(islands[0].CurrentGeneration.Chromosomes,
                Is.EqualTo(population.CurrentGeneration.Chromosomes.Take(4)),
                "Islands hold the parent individuals themselves (full chromosomes), in order.");
            Assert.That(islands[1].CurrentGeneration.Chromosomes,
                Is.EqualTo(population.CurrentGeneration.Chromosomes.Skip(4)));
        });
    }

    [Test]
    public void GenerateSubPopulations_PopulationLargerThanIslands_Throws()
    {
        var population = CreateEvaluatedPopulation(10);
        var metaHeuristic = new TestableIslandMetaHeuristic(4, new DefaultMetaHeuristic(), new DefaultMetaHeuristic());

        Assert.That(
            () => metaHeuristic.GenerateIslands(new EvolutionContext { Population = population }),
            Throws.InvalidOperationException);
    }

    [Test]
    public void Start_TwoIslands_RandomRingMigrationEveryGeneration_RunsToTermination()
    {
        var population = new MetaPopulation(40, 40, CreateChromosome());

        var metaHeuristic = new IslandMetaHeuristic(20, new DefaultMetaHeuristic(), new DefaultMetaHeuristic())
        {
            MigrationMode = MigrationMode.RandomRing,
            MigrationsGenerationPeriod = 1,
            GlobalMigrationRate = IslandMetaHeuristic.LargeMigrationRate
        };

        var ga = new MetaGeneticAlgorithm(population, CreateFitness(), new EliteSelection(), new UniformCrossover(0.5f), new FlipBitMutation(), metaHeuristic)
        {
            Termination = new GenerationNumberTermination(15)
        };

        ga.Start();

        Assert.Multiple(() =>
        {
            Assert.That(ga.State, Is.EqualTo(GeneticAlgorithmState.TerminationReached));
            Assert.That(ga.GenerationsNumber, Is.EqualTo(15));
            Assert.That(ga.BestChromosome.Fitness, Is.Not.Null);
            Assert.That(population.CurrentGeneration.Chromosomes, Has.Count.EqualTo(40),
                "Migrations replace individuals but never change island (hence population) sizes.");
        });
    }

    [Test]
    public void Start_FourIslands_StaticMigration_RunsToTermination()
    {
        var population = new MetaPopulation(40, 40, CreateChromosome());

        var metaHeuristic = new IslandMetaHeuristic(10, 4, new DefaultMetaHeuristic())
        {
            MigrationMode = MigrationMode.Static,
            MigrationsGenerationPeriod = 5
        };

        var ga = new MetaGeneticAlgorithm(population, CreateFitness(), new EliteSelection(), new UniformCrossover(0.5f), new FlipBitMutation(), metaHeuristic)
        {
            Termination = new GenerationNumberTermination(12)
        };

        ga.Start();

        Assert.Multiple(() =>
        {
            Assert.That(ga.State, Is.EqualTo(GeneticAlgorithmState.TerminationReached));
            Assert.That(ga.GenerationsNumber, Is.EqualTo(12));
            Assert.That(ga.BestChromosome.Fitness, Is.Not.Null);
        });
    }

    [Test]
    public void Start_TwoIslands_NoMigration_RunsToTermination()
    {
        var population = new MetaPopulation(40, 40, CreateChromosome());

        var metaHeuristic = new IslandMetaHeuristic(20, new DefaultMetaHeuristic(), new DefaultMetaHeuristic())
        {
            MigrationMode = MigrationMode.None,
            MigrationsGenerationPeriod = 1
        };

        var ga = new MetaGeneticAlgorithm(population, CreateFitness(), new EliteSelection(), new UniformCrossover(0.5f), new FlipBitMutation(), metaHeuristic)
        {
            Termination = new GenerationNumberTermination(10)
        };

        ga.Start();

        Assert.Multiple(() =>
        {
            Assert.That(ga.State, Is.EqualTo(GeneticAlgorithmState.TerminationReached));
            Assert.That(ga.GenerationsNumber, Is.EqualTo(10));
            Assert.That(ga.BestChromosome.Fitness, Is.Not.Null);
        });
    }
}
