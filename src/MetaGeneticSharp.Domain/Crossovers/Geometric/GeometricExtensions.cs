#nullable disable
using System;
using GeneticSharp;

namespace MetaGeneticSharp
{
    /// <summary>
    /// Value-conversion and gene-swap helpers used by the geometric crossover layer.
    /// </summary>
    /// <remarks>
    /// The PR#87 source relied on two GeneticSharp.Infrastructure.Framework helpers
    /// that are not present in the pinned 3.1.4 upstream:
    /// <list type="bullet">
    /// <item><description><c>.To&lt;TTarget&gt;()</c> value converter (used by
    /// <see cref="GeometricCrossover{TValue}"/> to compute centroids).</description></item>
    /// <item><description>A two-argument <c>FlipGene(int, int)</c> swap extension
    /// (used by <see cref="OrderedEmbedding{TValue}"/>).</description></item>
    /// </list>
    /// They are reproduced here so the geometric layer is self-contained.
    /// </remarks>
    public static class GeometricExtensions
    {
        /// <summary>
        /// Converts the source value to the target type using <see cref="Convert.ChangeType(object, Type)"/>.
        /// </summary>
        public static TTarget To<TTarget>(this object source)
        {
            if (source == null)
            {
                return default;
            }

            var targetType = typeof(TTarget);
            if (targetType == typeof(object))
            {
                return (TTarget)source;
            }

            return (TTarget)Convert.ChangeType(source, targetType);
        }

        /// <summary>
        /// Swaps the genes at <paramref name="firstIndex"/> and <paramref name="secondIndex"/>
        /// and returns the chromosome (fluent). Ported from the PR#87 ChromosomeExtensions.
        /// </summary>
        public static TChromosome FlipGene<TChromosome>(this TChromosome chromosome, int firstIndex, int secondIndex)
            where TChromosome : IChromosome
        {
            var firstGene = chromosome.GetGene(firstIndex);
            var secondGene = chromosome.GetGene(secondIndex);

            chromosome.ReplaceGene(firstIndex, secondGene);
            chromosome.ReplaceGene(secondIndex, firstGene);
            return chromosome;
        }
    }
}
