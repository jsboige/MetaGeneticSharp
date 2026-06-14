using GeneticSharp;
using MetaGeneticSharp;

namespace MetaGeneticSharp.Domain.Tests.Geometric;

/// <summary>
/// Phase 4 slice 2 acceptance tests: the Geometric fluent verbs (deferred from Phase 3 slice 3b)
/// wire the geometric crossover infrastructure into the grammar. The keystone proves the verbs
/// compose fluently to produce a configured <see cref="GeometricCrossover{TValue}"/> that crosses
/// parents correctly end-to-end.
/// </summary>
public class GeometricVerbsTests
{
    private static DoubleArrayChromosome Parent(params double[] values) => new DoubleArrayChromosome(values);

    [Test]
    public void WithLinearGeometricOperator_SetsOperatorAndCrosses()
    {
        var crossover = new GeometricCrossover<double>(ordered: false, parentNb: 2, generateTwin: false)
            .WithLinearGeometricOperator<double>((geneIndex, values) => values.Max());

        var children = crossover.Cross(new List<IChromosome> { Parent(1, 10), Parent(9, 3) });

        var offspring = ((DoubleArrayChromosome)children[0]).GetDoubleValues();
        // Max per index: max(1,9)=9, max(10,3)=10.
        Assert.That(offspring, Is.EqualTo(new[] { 9.0, 10.0 }));
    }

    [Test]
    public void WithGeneralGeometricOperator_OverridesLinearOperator()
    {
        var crossover = new GeometricCrossover<double>(ordered: false, parentNb: 2, generateTwin: false)
            .WithGeneralGeometricOperator<double>(parents =>
            {
                // Return the gene-wise minimum across parents.
                var length = parents[0].Count;
                var result = new double[length];
                for (int i = 0; i < length; i++)
                {
                    result[i] = parents.Min(p => p[i]);
                }
                return result;
            });

        var children = crossover.Cross(new List<IChromosome> { Parent(4, 20), Parent(8, 5) });

        var offspring = ((DoubleArrayChromosome)children[0]).GetDoubleValues();
        // Min per index: min(4,8)=4, min(20,5)=5.
        Assert.That(offspring, Is.EqualTo(new[] { 4.0, 5.0 }));
    }

    [Test]
    public void WithGeometryEmbedding_SetsCustomEmbedding()
    {
        var crossover = new GeometricCrossover<double>(ordered: false, parentNb: 2, generateTwin: false)
            .WithGeometryEmbedding<GeometricCrossover<double>, double>(new IdentityEmbedding<double>());

        Assert.That(crossover.GeometryEmbedding, Is.InstanceOf<IdentityEmbedding<double>>());

        // The explicit embedding overrides the default OrderedEmbedding set in PerformCross.
        var children = crossover.Cross(new List<IChromosome> { Parent(0, 10), Parent(20, 30) });
        var offspring = ((DoubleArrayChromosome)children[0]).GetDoubleValues();
        Assert.That(offspring, Is.EqualTo(new[] { 10.0, 20.0 }));
    }

    [Test]
    public void Verbs_ChainFluently()
    {
        // KEYSTONE: all three verbs chain fluently on one expression.
        var crossover = new GeometricCrossover<double>(ordered: false, parentNb: 2, generateTwin: false)
            .WithLinearGeometricOperator<double>((_, values) => values.Average())
            .WithGeometryEmbedding<GeometricCrossover<double>, double>(new IdentityEmbedding<double>());

        Assert.That(crossover.LinearGeometricOperator, Is.Not.Null);
        Assert.That(crossover.GeometryEmbedding, Is.Not.Null);

        var children = crossover.Cross(new List<IChromosome> { Parent(0, 40), Parent(20, 60) });
        var offspring = ((DoubleArrayChromosome)children[0]).GetDoubleValues();
        // Average per index: avg(0,20)=10, avg(40,60)=50.
        Assert.That(offspring, Is.EqualTo(new[] { 10.0, 50.0 }));
    }
}
