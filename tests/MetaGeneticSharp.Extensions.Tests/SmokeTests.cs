using GeneticSharp.Extensions;

namespace MetaGeneticSharp.Extensions.Tests;

/// <summary>
/// Smoke tests proving the reference chain
/// MetaGeneticSharp.Extensions -> GeneticSharp.Extensions (submodule, tag 3.1.4)
/// compiles and runs.
/// </summary>
[TestFixture]
public class SmokeTests
{
    [Test]
    public void UpstreamTspFitness_EvaluatesChromosome()
    {
        var fitness = new TspFitness(10, 0, 100, 0, 100);
        var chromosome = new TspChromosome(10);

        var value = fitness.Evaluate(chromosome);

        Assert.That(value, Is.GreaterThan(0));
        Assert.That(fitness.Cities, Has.Count.EqualTo(10));
    }
}
