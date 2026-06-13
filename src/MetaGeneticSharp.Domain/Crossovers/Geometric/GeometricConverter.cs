#nullable disable
using System;
using System.ComponentModel;

namespace MetaGeneticSharp
{
    /// <summary>
    /// Non-generic facade over <see cref="GeometricConverter{TGeneValue}"/>.
    /// Ported from GeneticSharp.Domain.Crossovers.Geometric (PR giacomelli/GeneticSharp#87).
    /// </summary>
    public class GeometricConverter : GeometricConverter<object> { }

    /// <summary>
    /// The standard geometric converter allows defining chromosome-specific gene-value
    /// converters to and from metric space.
    /// </summary>
    /// <typeparam name="TGeneValue">The gene-value type.</typeparam>
    public class GeometricConverter<TGeneValue> : IGeometricConverter<TGeneValue>
    {
        /// <summary>
        /// A default .NET <see cref="TypeConverter"/> for <typeparamref name="TGeneValue"/>.
        /// </summary>
        public static readonly TypeConverter DefaultTypeConverter = TypeDescriptor.GetConverter(typeof(TGeneValue));

        /// <summary>Function converting a gene value at an index into a metric-space double.</summary>
        public Func<int, TGeneValue, double> GeneToDoubleConverter { get; set; }

        /// <summary>Function converting a metric-space double at an index into a gene value.</summary>
        public Func<int, double, TGeneValue> DoubleToGeneConverter { get; set; }

        /// <summary>The embedding associated with this converter.</summary>
        public IGeometryEmbedding<TGeneValue> Embedding { get; set; }

        /// <summary>Whether the embedding handles ordered chromosomes/crossover.</summary>
        public bool IsOrdered { get; set; }

        /// <inheritdoc />
        public double GeneToDouble(int geneIndex, TGeneValue geneValue) => GeneToDoubleConverter(geneIndex, geneValue);

        /// <inheritdoc />
        public TGeneValue DoubleToGene(int geneIndex, double metricValue) => DoubleToGeneConverter(geneIndex, metricValue);

        /// <inheritdoc />
        public IGeometryEmbedding<TGeneValue> GetEmbedding() => Embedding;
    }
}
