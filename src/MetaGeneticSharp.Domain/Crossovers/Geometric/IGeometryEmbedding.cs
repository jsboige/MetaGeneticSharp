#nullable disable
using System.Collections.Generic;
using GeneticSharp;

namespace MetaGeneticSharp
{
    /// <summary>
    /// A general interface to define geometry embeddings. They are responsible for
    /// mapping gene-space into a target metric-space, in order to use a geometric operator.
    /// Non-generic facade over <see cref="IGeometryEmbedding{TValue}"/>.
    /// Ported from GeneticSharp.Domain.Crossovers.Geometric (PR giacomelli/GeneticSharp#87).
    /// </summary>
    public interface IGeometryEmbedding : IGeometryEmbedding<object> { }

    /// <summary>
    /// A general interface to define geometry embeddings. They are responsible for
    /// mapping gene-space into a target metric-space, in order to use a geometric operator.
    /// </summary>
    /// <typeparam name="TValue">The base type of the metric space (typically a .NET value type).</typeparam>
    public interface IGeometryEmbedding<TValue>
    {
        /// <summary>
        /// Converts offspring values in metric space into an offspring individual in gene space.
        /// </summary>
        /// <param name="parents">The original offspring's parent individuals.</param>
        /// <param name="offSpringValues">The metric-space values for the offspring to create.</param>
        /// <returns>The converted offspring chromosome individual.</returns>
        IChromosome MapFromGeometry(IList<IChromosome> parents, IList<TValue> offSpringValues);

        /// <summary>
        /// Imports parent individuals from gene space into metric-space.
        /// </summary>
        /// <param name="parents">The parents to convert.</param>
        /// <returns>The converted metric-space vectors representing the parents.</returns>
        IList<IList<TValue>> MapToGeometry(IList<IChromosome> parents);
    }
}
