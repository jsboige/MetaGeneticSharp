using GeneticSharp;

namespace MetaGeneticSharp.Domain.Tests;

/// <summary>
/// Smoke tests proving the reference chain
/// MetaGeneticSharp.Domain -> GeneticSharp.Domain (submodule, tag 3.1.4)
/// compiles and runs an actual GA end-to-end.
/// </summary>
[TestFixture]
public class SmokeTests
{
    [Test]
    public void UpstreamGeneticAlgorithm_TenGenerations_ProducesBestChromosome()
    {
        var chromosome = new FloatingPointChromosome(
            new double[] { 0 },
            new double[] { 10 },
            new int[] { 16 },
            new int[] { 2 });
        var population = new Population(20, 40, chromosome);
        var fitness = new FuncFitness(c => ((FloatingPointChromosome)c).ToFloatingPoints()[0]);
        var ga = new GeneticAlgorithm(
            population,
            fitness,
            new EliteSelection(),
            new UniformCrossover(),
            new FlipBitMutation())
        {
            Termination = new GenerationNumberTermination(10)
        };

        ga.Start();

        Assert.That(ga.GenerationsNumber, Is.EqualTo(10));
        Assert.That(ga.BestChromosome, Is.Not.Null);
        Assert.That(ga.BestChromosome.Fitness, Is.GreaterThanOrEqualTo(0));
    }
}
