#nullable disable
using System;
using System.ComponentModel;

namespace MetaGeneticSharp
{
    /// <summary>
    /// The default geometric converter leverages standard .NET converters to convert gene
    /// values to and from metric space. Non-generic facade delegating to the typed default.
    /// Ported from GeneticSharp.Domain.Crossovers.Geometric (PR giacomelli/GeneticSharp#87).
    /// </summary>
    public class DefaultGeometricConverter : IGeometricConverter
    {
        private readonly TypedGeometricConverter _typedConverter;

        /// <summary>Builds a default converter bound to <see cref="object"/> gene values.</summary>
        public DefaultGeometricConverter()
        {
            var converter = new TypedGeometricConverter();
            converter.SetTypedConverter<object>(new DefaultGeometricConverter<object>());
            _typedConverter = converter;
        }

        /// <inheritdoc />
        public object DoubleToGene(int geneIndex, double metricValue)
        {
            return _typedConverter.DoubleToGene(geneIndex, metricValue);
        }

        /// <inheritdoc />
        IGeometryEmbedding<object> IGeometricConverter<object>.GetEmbedding()
        {
            return _typedConverter.GetEmbedding();
        }

        /// <inheritdoc />
        public bool IsOrdered
        {
            get => _typedConverter.IsOrdered;
            set => _typedConverter.IsOrdered = value;
        }

        /// <inheritdoc />
        public double GeneToDouble(int geneIndex, object geneValue)
        {
            return _typedConverter.GeneToDouble(geneIndex, geneValue);
        }
    }

    /// <summary>
    /// The default geometric converter leverages standard .NET converters to convert gene
    /// values to and from metric space.
    /// </summary>
    /// <typeparam name="TGeneValue">The gene-value type.</typeparam>
    public class DefaultGeometricConverter<TGeneValue> : IGeometricConverter<TGeneValue>
    {
        private static readonly TypeConverter _converter = TypeDescriptor.GetConverter(typeof(TGeneValue));

        /// <inheritdoc />
        public bool IsOrdered { get; set; }

        /// <inheritdoc />
        public double GeneToDouble(int geneIndex, TGeneValue geneValue) =>
            Convert.ToDouble(geneValue);

        /// <inheritdoc />
        public TGeneValue DoubleToGene(int geneIndex, double metricValue)
        {
            return (TGeneValue)_converter.ConvertFrom(metricValue);
        }

        /// <inheritdoc />
        public IGeometryEmbedding<TGeneValue> GetEmbedding()
        {
            return null;
        }
    }
}
