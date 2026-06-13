using GeneticSharp;
using MetaGeneticSharp;

namespace MetaGeneticSharp.Domain.Tests.Geometric;

/// <summary>
/// Minimal chromosome exposing gene values transparently as <see cref="double"/>,
/// used to exercise the geometric crossover layer with a concrete, readable
/// representation (no binary encoding). Gene values are stored verbatim via <see cref="Gene"/>.
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

    public override IChromosome CreateNew()
    {
        // The geometric embedding overwrites every gene of the freshly-created offspring,
        // so the initial values are irrelevant for correctness; we reuse the same shape.
        return new DoubleArrayChromosome(_initialValues);
    }

    public override Gene GenerateGene(int geneIndex)
    {
        return new Gene(_initialValues[geneIndex]);
    }

    public double[] GetDoubleValues()
    {
        return GetGenes().Select(g => (double)g.Value).ToArray();
    }
}
