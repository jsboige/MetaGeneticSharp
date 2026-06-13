#nullable disable
using System;

namespace MetaGeneticSharp
{
    /// <summary>
    /// A helper class to build an untyped geometry converter from a generic type definition one.
    /// Ported from GeneticSharp.Domain.Crossovers.Geometric (PR giacomelli/GeneticSharp#87).
    /// </summary>
    public class TypedGeometricConverter : IGeometricConverter
    {
        private TypedGeometryEmbedding _embedding;

        /// <summary>
        /// Binds a typed converter and caches its gene/double conversion delegates.
        /// </summary>
        public void SetTypedConverter<TValue>(IGeometricConverter<TValue> converter)
        {
            ArgumentNullException.ThrowIfNull(converter);
            GeneToDoubleFunction = (geneIndex, geneValue) => converter.GeneToDouble(geneIndex, (TValue)geneValue);
            DoubleToGeneFunction = (geneIndex, metricValue) => converter.DoubleToGene(geneIndex, metricValue);
            IsOrdered = converter.IsOrdered;
            var embedding = converter.GetEmbedding();
            if (embedding != null)
            {
                var untypedEmbedding = new TypedGeometryEmbedding();
                untypedEmbedding.SetTypedEmbedding(embedding);
                _embedding = untypedEmbedding;
            }
        }

        /// <summary>Cached gene-to-double function.</summary>
        public Func<int, object, double> GeneToDoubleFunction { get; set; }

        /// <summary>Cached double-to-gene function.</summary>
        public Func<int, double, object> DoubleToGeneFunction { get; set; }

        /// <inheritdoc />
        public bool IsOrdered { get; set; }

        /// <inheritdoc />
        public double GeneToDouble(int geneIndex, object geneValue)
        {
            return GeneToDoubleFunction(geneIndex, geneValue);
        }

        /// <inheritdoc />
        public object DoubleToGene(int geneIndex, double metricValue)
        {
            return DoubleToGeneFunction(geneIndex, metricValue);
        }

        /// <inheritdoc />
        public IGeometryEmbedding<object> GetEmbedding()
        {
            return _embedding;
        }
    }
}
