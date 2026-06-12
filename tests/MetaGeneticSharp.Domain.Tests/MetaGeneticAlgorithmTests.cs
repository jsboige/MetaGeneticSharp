using GeneticSharp;
using MetaGeneticSharp;

namespace MetaGeneticSharp.Domain.Tests;

public class MetaGeneticAlgorithmTests
{
    private static MetaGeneticAlgorithm CreateAlgorithm(int generations, IMetaHeuristic? metaHeuristic = null)
    {
        var chromosome = new FloatingPointChromosome(
            new double[] { 0, 0 },
            new double[] { 100, 100 },
            new int[] { 16, 16 },
            new int[] { 2, 2 });

        var fitness = new FuncFitness(c =>
        {
            var values = ((FloatingPointChromosome)c).ToFloatingPoints();
            return -(Math.Abs(values[0] - 42) + Math.Abs(values[1] - 13));
        });

        var population = new MetaPopulation(40, 40, chromosome);

        var ga = metaHeuristic == null
            ? new MetaGeneticAlgorithm(population, fitness, new EliteSelection(), new UniformCrossover(0.5f), new FlipBitMutation())
            : new MetaGeneticAlgorithm(population, fitness, new EliteSelection(), new UniformCrossover(0.5f), new FlipBitMutation(), metaHeuristic);

        ga.Termination = new GenerationNumberTermination(generations);
        return ga;
    }

    [Test]
    public void Start_DefaultMetaHeuristic_RunsToTermination()
    {
        var ga = CreateAlgorithm(30);

        ga.Start();

        Assert.Multiple(() =>
        {
            Assert.That(ga.GenerationsNumber, Is.EqualTo(30));
            Assert.That(ga.State, Is.EqualTo(GeneticAlgorithmState.TerminationReached));
            Assert.That(ga.BestChromosome, Is.Not.Null);
            Assert.That(ga.BestChromosome.Fitness, Is.Not.Null);
            Assert.That(ga.TimeEvolving, Is.GreaterThan(TimeSpan.Zero));
        });
    }

    [Test]
    public void Start_FitnessBasedElitistReinsertion_BestFitnessNeverDecreases()
    {
        var ga = CreateAlgorithm(30);
        var bests = new List<double>();

        ga.GenerationRan += (s, e) => bests.Add(ga.BestChromosome.Fitness!.Value);

        ga.Start();

        for (int i = 1; i < bests.Count; i++)
        {
            Assert.That(bests[i], Is.GreaterThanOrEqualTo(bests[i - 1]),
                $"Best fitness decreased at generation {i + 1}: {bests[i]} < {bests[i - 1]}");
        }
    }

    [Test]
    public void Start_ContextLifecycle_FollowsKeepContextInPopulation()
    {
        var ga = CreateAlgorithm(5);
        ga.KeepContextInPopulation = false;

        ga.Start();

        var population = (MetaPopulation)ga.Population;
        Assert.That(population.Parameters.ContainsKey(nameof(IEvolutionContext)), Is.False,
            "Context should be removed from the population store after termination by default.");

        var gaKeeping = CreateAlgorithm(5);
        gaKeeping.KeepContextInPopulation = true;

        gaKeeping.Start();

        var populationKeeping = (MetaPopulation)gaKeeping.Population;
        Assert.That(populationKeeping.Parameters.ContainsKey(nameof(IEvolutionContext)), Is.True,
            "Context should remain in the population store when KeepContextInPopulation is true.");
    }

    [Test]
    public void Start_TplOperatorsStrategy_RunsToTermination()
    {
        var ga = CreateAlgorithm(30);
        ga.OperatorsStrategy = new TplMetaOperatorsStrategy();

        ga.Start();

        Assert.Multiple(() =>
        {
            Assert.That(ga.GenerationsNumber, Is.EqualTo(30));
            Assert.That(ga.State, Is.EqualTo(GeneticAlgorithmState.TerminationReached));
            Assert.That(ga.BestChromosome.Fitness, Is.Not.Null);
        });
    }
}
