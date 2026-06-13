#nullable disable
namespace MetaGeneticSharp
{
    /// <summary>
    /// A general interface to define geometry converters. Non-generic facade over
    /// <see cref="IGeometricConverter{TGeneValue}"/>.
    /// Ported from GeneticSharp.Domain.Crossovers.Geometric (PR giacomelli/GeneticSharp#87).
    /// </summary>
    public interface IGeometricConverter : IGeometricConverter<object> { }

    /// <summary>
    /// A general interface to define geometry converters.
    /// </summary>
    /// <typeparam name="TGeneValue">The base type of the gene space (typically a .NET value type).</typeparam>
    public interface IGeometricConverter<TGeneValue>
    {
        /// <summary>Whether the embedding handles ordered chromosomes/crossover.</summary>
        bool IsOrdered { get; }

        /// <summary>Converts a gene value at the given index into a metric-space double.</summary>
        double GeneToDouble(int geneIndex, TGeneValue geneValue);

        /// <summary>Converts a metric-space double back into a gene value at the given index.</summary>
        TGeneValue DoubleToGene(int geneIndex, double metricValue);

        /// <summary>The geometry embedding associated with this converter (may be null).</summary>
        IGeometryEmbedding<TGeneValue> GetEmbedding();
    }
}
