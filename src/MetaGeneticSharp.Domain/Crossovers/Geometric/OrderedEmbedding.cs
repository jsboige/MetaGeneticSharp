#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using GeneticSharp;

namespace MetaGeneticSharp
{
    /// <summary>
    /// The ordered embedding provides a pass-through for unordered chromosomes, and a
    /// swap-preserving conversion for ordered ones (permutations). Ported from
    /// GeneticSharp.Domain.Crossovers.Geometric (PR giacomelli/GeneticSharp#87).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Honest caveat (G.9). The 2-parent centroid operator of <see cref="GeometricCrossover{TValue}"/>
    /// maps permutations to a metric-space target that is NOT itself a permutation (e.g. the gene-wise
    /// mean of [0,1,2,3,4] and [2,0,1,3,4] is [1,0.5,1.5,3,4]). The back-walk interprets that target as
    /// a desired ranking and swaps toward it, so the swap-metric DISTINCTION is guaranteed in one step
    /// only when the metric-space target is itself a permutation (passed directly to
    /// <see cref="MapFromGeometry"/>); under a naive centroid it degrades symmetrically across the
    /// positional embeddings — the same "naive centroid is inadequate on city-label indices" limit
    /// documented in MGS-7. See <see cref="InsertionEmbedding{TValue}"/> and
    /// <see cref="KendallTauEmbedding{TValue}"/> for the sibling metrics with the identical limit.
    /// </para>
    /// </remarks>
    public class OrderedEmbedding<TValue> : IdentityEmbedding<TValue>
    {
        private Func<IChromosome, int, int, bool> _validateSwapFunction;

        /// <summary>Whether the target chromosome is ordered (permutation).</summary>
        public virtual bool IsOrdered { get; set; }

        /// <summary>
        /// How candidate offspring metric-space values are walked when converting back
        /// to gene-space swaps.
        /// </summary>
        public GeneSelectionMode GeneSelectionMode { get; set; } = GeneSelectionMode.AllIndexed;

        /// <summary>
        /// The default CreateOffspring method is split into 2 cases depending on whether
        /// the chromosome is ordered.
        /// </summary>
        public override IChromosome MapFromGeometry(IList<IChromosome> parents, IList<TValue> offSpringValues)
        {
            if (IsOrdered)
            {
                return MapFromGeometryOrdered(parents, offSpringValues);
            }

            return base.MapFromGeometry(parents, offSpringValues);
        }

        /// <summary>
        /// The ordered embedding walks some or all candidate offspring metric-space values,
        /// and for each value to update it identifies the swap gene index and uses
        /// <see cref="ValidateSwapFunction"/> to validate or reject the swap.
        /// </summary>
        public virtual IChromosome MapFromGeometryOrdered(IList<IChromosome> parents, IList<TValue> values)
        {
            var offspring = parents.First().Clone();
            var valuesCount = values.Count();
            IEnumerable<int> selectedIndices;
            if ((GeneSelectionMode & GeneSelectionMode.RandomOrder) == GeneSelectionMode.RandomOrder)
            {
                selectedIndices = new List<int>(RandomizationProvider.Current.GetUniqueInts(valuesCount, 0, valuesCount));
            }
            else
            {
                selectedIndices = Enumerable.Range(0, valuesCount);
            }

            // We precomputed swap indices for easier lookup.
            var offspringIndexes = offspring.GetGenes().Select((g, i) => (g, i)).ToDictionary(gi => (TValue)gi.g.Value, gi => gi.i);
            foreach (var i in selectedIndices)
            {
                var replacement = (TValue)offspring.GetGene(i).Value;
                if (!replacement.Equals(values[i]))
                {
                    // We find the swap index in the precomputed index.
                    var indexVal = offspringIndexes[values[i]];
                    if (indexVal < 0)
                    {
                        throw new ArgumentException($"value {values[i]} not found in ordered chromosome {offspring}");
                    }
                    if (ValidateSwapFunction(offspring, i, indexVal))
                    {
                        offspring.FlipGene(i, indexVal);
                        // If the gene selection method is SingleFirstAllowed, we return the
                        // corresponding offspring with just one accepted swap.
                        if ((GeneSelectionMode & GeneSelectionMode.SingleFirstAllowed) == GeneSelectionMode.SingleFirstAllowed)
                        {
                            return offspring;
                        }
                    }
                }
            }
            return offspring;
        }

        /// <summary>
        /// A swap-validation function specific to the problem to solve.
        /// </summary>
        public Func<IChromosome, int, int, bool> ValidateSwapFunction
        {
            get
            {
                if (_validateSwapFunction == null)
                {
                    _validateSwapFunction = GetDefaultSwapValidationFunction();
                }
                return _validateSwapFunction;
            }
            set => _validateSwapFunction = value;
        }

        /// <summary>
        /// The default swap validation function; can be overwritten with problem-specific embeddings.
        /// </summary>
        protected virtual Func<IChromosome, int, int, bool> GetDefaultSwapValidationFunction()
        {
            return (chromosome, indexToSwap1, indexToSwap2) => true;
        }
    }
}
