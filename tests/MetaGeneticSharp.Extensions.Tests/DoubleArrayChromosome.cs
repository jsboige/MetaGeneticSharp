using GeneticSharp;

namespace MetaGeneticSharp.Extensions.Tests;

/// <summary>
/// Minimal chromosome exposing gene values transparently as <see cref="double"/>,
/// mirroring <c>MetaGeneticSharp.Domain.Tests.Geometric.DoubleArrayChromosome</c>.
/// Used to feed <see cref="KnownFunctionGenes.AsDoubles"/> a concrete representation
/// (no binary encoding) so the benchmark functions can be unit-tested at known points.
/// </summary>
public class DoubleArrayChromosome : ChromosomeBase
{
    private readonly double[] _initialValues;

    public DoubleArrayChromosome(double[] values) : base(values.Length)
    {
        _initialValues = values;
        for (int i = 0; i < values.Length; i++)
        {
            ReplaceGene(i, new Gene(values[i]));
        }
    }

    public override IChromosome CreateNew() => new DoubleArrayChromosome(_initialValues);

    public override Gene GenerateGene(int geneIndex) => new Gene(_initialValues[geneIndex]);

    public double[] GetDoubleValues() => GetGenes().Select(g => (double)g.Value).ToArray();
}
