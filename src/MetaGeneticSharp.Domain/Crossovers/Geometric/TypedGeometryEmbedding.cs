#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using GeneticSharp;

namespace MetaGeneticSharp
{
    /// <summary>
    /// A helper class to build an untyped geometry embedding from a generic typed definition.
    /// Ported from GeneticSharp.Domain.Crossovers.Geometric (PR giacomelli/GeneticSharp#87).
    /// </summary>
    public class TypedGeometryEmbedding : IGeometryEmbedding
    {
        /// <summary>The wrapped typed embedding.</summary>
        public object TypedEmbedding { get; set; }

        /// <summary>
        /// Binds a typed embedding and caches its MapFromGeometry / MapToGeometry delegates.
        /// </summary>
        public void SetTypedEmbedding<TValue>(IGeometryEmbedding<TValue> embedding)
        {
            ArgumentNullException.ThrowIfNull(embedding);
            TypedEmbedding = embedding;
            MapFromGeometryFunction = (parents, offSpringValues) => embedding.MapFromGeometry(parents, offSpringValues.Cast<TValue>().ToArray());
            MapToGeometryFunction = parents => embedding.MapToGeometry(parents).Select(c => (IList<Object>)c.Cast<object>().ToArray()).ToArray();
        }

        /// <summary>Cached typed-erased MapFromGeometry delegate.</summary>
        public Func<IList<IChromosome>, IList<object>, IChromosome> MapFromGeometryFunction { get; set; }

        /// <summary>Cached typed-erased MapToGeometry delegate.</summary>
        public Func<IList<IChromosome>, IList<IList<object>>> MapToGeometryFunction { get; set; }

        /// <inheritdoc />
        public IChromosome MapFromGeometry(IList<IChromosome> parents, IList<object> offSpringValues)
        {
            return MapFromGeometryFunction(parents, offSpringValues);
        }

        /// <inheritdoc />
        public IList<IList<object>> MapToGeometry(IList<IChromosome> parents)
        {
            return MapToGeometryFunction(parents);
        }
    }
}
