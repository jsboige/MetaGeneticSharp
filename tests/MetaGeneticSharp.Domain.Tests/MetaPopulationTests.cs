using GeneticSharp;
using MetaGeneticSharp;

namespace MetaGeneticSharp.Domain.Tests;

public class MetaPopulationTests
{
    private static MetaPopulation CreatePopulationWithFitnesses(double[] fitnesses)
    {
        var adam = new FloatingPointChromosome(
            new double[] { 0 },
            new double[] { 100 },
            new int[] { 16 },
            new int[] { 2 });

        var population = new MetaPopulation(2, fitnesses.Length, adam);

        var chromosomes = new List<IChromosome>();
        foreach (var fitness in fitnesses)
        {
            var chromosome = adam.CreateNew();
            chromosome.Fitness = fitness;
            chromosomes.Add(chromosome);
        }

        population.CreateInitialGeneration();
        population.CreateNewGeneration(chromosomes);
        return population;
    }

    [Test]
    public void EndCurrentGeneration_PreservesChromosomeOrder()
    {
        var population = CreatePopulationWithFitnesses(new[] { 0.1, 0.9, 0.5, 0.3 });
        var before = population.CurrentGeneration.Chromosomes.ToList();

        population.EndCurrentGeneration();

        var after = population.CurrentGeneration.Chromosomes;

        Assert.That(after, Has.Count.EqualTo(before.Count));
        for (int i = 0; i < before.Count; i++)
        {
            Assert.That(ReferenceEquals(after[i], before[i]), Is.True,
                $"Chromosome at index {i} moved: order must be stable (no implicit fitness sort).");
        }

        Assert.That(population.BestChromosome, Is.SameAs(before[1]),
            "BestChromosome should be the 0.9-fitness chromosome regardless of position.");
    }

    [Test]
    public void EndCurrentGeneration_MissingFitness_Throws()
    {
        var adam = new FloatingPointChromosome(
            new double[] { 0 },
            new double[] { 100 },
            new int[] { 16 },
            new int[] { 2 });

        var population = new MetaPopulation(2, 10, adam);
        population.CreateInitialGeneration();

        Assert.Throws<InvalidOperationException>(() => population.EndCurrentGeneration());
    }
}
