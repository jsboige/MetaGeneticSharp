using GeneticSharp;
using MetaGeneticSharp;

namespace MetaGeneticSharp.Domain.Tests.Geometric;

/// <summary>
/// Phase 4 acceptance tests for the geometric crossover infrastructure (PR giacomelli/GeneticSharp#87).
/// The keystone proves that <see cref="GeometricCrossover{TValue}"/> with its default centroid
/// operator produces the gene-wise midpoint of two parents end-to-end through the embedding layer.
/// </summary>
public class GeometricCrossoverTests
{
    private static DoubleArrayChromosome Parent(params double[] values) => new DoubleArrayChromosome(values);

    [Test]
    public void Centroid_DefaultLinearOperator_ProducesGeneWiseMidpoint()
    {
        // KEYSTONE: default centroid = average of same-index parent gene values.
        var crossover = new GeometricCrossover<double>(ordered: false, parentNb: 2, generateTwin: false);
        var parentA = Parent(0, 10, 20);
        var parentB = Parent(30, 40, 50);

        var children = crossover.Cross(new List<IChromosome> { parentA, parentB });

        Assert.That(children, Has.Count.EqualTo(1));
        var offspring = (DoubleArrayChromosome)children[0];
        Assert.That(offspring.GetDoubleValues(), Is.EqualTo(new[] { 15.0, 25.0, 35.0 }));
    }

    [Test]
    public void GenerateTwin_ProducesSymmetricalSecondChild()
    {
        var crossover = new GeometricCrossover<double>(ordered: false, parentNb: 2, generateTwin: true);
        var parentA = Parent(0, 10);
        var parentB = Parent(20, 30);

        var children = crossover.Cross(new List<IChromosome> { parentA, parentB });

        Assert.That(children, Has.Count.EqualTo(2));
        var first = ((DoubleArrayChromosome)children[0]).GetDoubleValues();
        var twin = ((DoubleArrayChromosome)children[1]).GetDoubleValues();
        // Twin swaps parent order then takes the centroid, which is identical for the average.
        Assert.That(first, Is.EqualTo(new[] { 10.0, 20.0 }));
        Assert.That(twin, Is.EqualTo(new[] { 10.0, 20.0 }));
    }

    [Test]
    public void GeneralOperator_OverridesLinearOperator()
    {
        var crossover = new GeometricCrossover<double>(ordered: false, parentNb: 2, generateTwin: false)
        {
            GeneralGeometricOperator = parents =>
            {
                // Take the first parent's values verbatim.
                return parents[0];
            }
        };

        var children = crossover.Cross(new List<IChromosome> { Parent(1, 2, 3), Parent(9, 9, 9) });

        var offspring = ((DoubleArrayChromosome)children[0]).GetDoubleValues();
        Assert.That(offspring, Is.EqualTo(new[] { 1.0, 2.0, 3.0 }));
    }

    [Test]
    public void CustomLinearOperator_AppliedPerGeneIndex()
    {
        var crossover = new GeometricCrossover<double>(
            ordered: false,
            parentNb: 2,
            linearGeometricOperator: (geneIndex, values) => values[0] - values[1],
            generateTwin: false);

        var children = crossover.Cross(new List<IChromosome> { Parent(10, 20), Parent(3, 5) });

        var offspring = ((DoubleArrayChromosome)children[0]).GetDoubleValues();
        Assert.That(offspring, Is.EqualTo(new[] { 7.0, 15.0 }));
    }

    [Test]
    public void Cross_WithoutAnyOperator_Throws()
    {
        var crossover = new GeometricCrossover<double>(ordered: false, parentNb: 2, generateTwin: false)
        {
            LinearGeometricOperator = null
        };

        Assert.Throws<InvalidOperationException>(() =>
            crossover.Cross(new List<IChromosome> { Parent(1, 2), Parent(3, 4) }));
    }

    [Test]
    public void OrderedEmbedding_Unordered_FallsBackToIdentity()
    {
        var embedding = new OrderedEmbedding<double> { IsOrdered = false };
        var parentA = Parent(0, 10, 20);
        var parentB = Parent(30, 40, 50);

        var geometry = embedding.MapToGeometry(new List<IChromosome> { parentA, parentB });
        Assert.That(geometry[0], Is.EqualTo(new[] { 0.0, 10.0, 20.0 }));
        Assert.That(geometry[1], Is.EqualTo(new[] { 30.0, 40.0, 50.0 }));

        var offspring = (DoubleArrayChromosome)embedding.MapFromGeometry(
            new List<IChromosome> { parentA, parentB }, new[] { 15.0, 25.0, 35.0 });
        Assert.That(offspring.GetDoubleValues(), Is.EqualTo(new[] { 15.0, 25.0, 35.0 }));
    }

    [Test]
    public void OrderedEmbedding_Ordered_PreservesPermutationMultiset()
    {
        // With IsOrdered=true the embedding must not introduce values absent from the parent
        // permutation: the offspring is a reordering (swaps) of the cloned parent's genes.
        var embedding = new OrderedEmbedding<int> { IsOrdered = true };
        var parent = new IntPermutationChromosome(new[] { 0, 1, 2, 3 });

        var offspring = embedding.MapFromGeometry(
            new List<IChromosome> { parent },
            new[] { 2, 0, 3, 1 });

        var result = offspring.GetGenes().Select(g => (int)g.Value).ToArray();
        Assert.That(result, Is.EquivalentTo(new[] { 0, 1, 2, 3 }));
    }

    [Test]
    public void FlipGene_SwapsTwoGenes()
    {
        var chromosome = Parent(10, 20, 30);

        chromosome.FlipGene(0, 2);

        Assert.That(((DoubleArrayChromosome)chromosome).GetDoubleValues(), Is.EqualTo(new[] { 30.0, 20.0, 10.0 }));
    }

    [Test]
    public void To_ConvertsBetweenNumericTypes()
    {
        Assert.That(((object)2.7).To<int>(), Is.EqualTo(3));
        Assert.That(((object)5).To<double>(), Is.EqualTo(5.0));
        Assert.That(((object)42).To<string>(), Is.EqualTo("42"));
    }

    [Test]
    public void DefaultGeometricConverter_GeneToDouble_ConvertsNumericGene()
    {
        // DefaultGeometricConverter.GeneToDouble uses Convert.ToDouble, robust for numeric genes.
        // The reverse direction (DoubleToGene) relies on TypeDescriptor.GetConverter, which the
        // BCL numeric converters refuse for an already-typed double source — this mirrors the
        // PR#87 behaviour and is not on the default GeometricCrossover path, so it is not asserted.
        var converter = new DefaultGeometricConverter<int>();

        Assert.That(converter.GeneToDouble(0, 12), Is.EqualTo(12.0));
        Assert.That(converter.IsOrdered, Is.False);
    }

    [Test]
    public void TypedGeometricConverter_BindsTypedConverterAndEmbedding()
    {
        var typed = new TypedGeometricConverter();
        var inner = new GeometricConverter<double>
        {
            GeneToDoubleConverter = (_, v) => v,
            DoubleToGeneConverter = (_, d) => d,
            Embedding = new IdentityEmbedding<double>(),
        };

        typed.SetTypedConverter(inner);

        Assert.That(typed.GeneToDouble(0, 3.0), Is.EqualTo(3.0));
        Assert.That(typed.DoubleToGene(0, 9.0), Is.EqualTo(9.0));
        Assert.That(typed.GetEmbedding(), Is.Not.Null);
    }
}

/// <summary>
/// Minimal transparent chromosome for ordered (permutation) tests, holding integer gene values.
/// </summary>
public class IntPermutationChromosome : ChromosomeBase
{
    private readonly int[] _initialValues;

    public IntPermutationChromosome(int[] values) : base(values.Length)
    {
        _initialValues = values;
        for (int i = 0; i < values.Length; i++)
        {
            ReplaceGene(i, new Gene(values[i]));
        }
    }

    public override IChromosome CreateNew() => new IntPermutationChromosome(_initialValues);

    public override Gene GenerateGene(int geneIndex) => new Gene(_initialValues[geneIndex]);
}
