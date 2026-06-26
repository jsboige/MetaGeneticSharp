#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using GeneticSharp;

namespace MetaGeneticSharp
{
    /// <summary>
    /// An insertion-based geometric embedding for ordered chromosomes (permutations).
    /// Where <see cref="OrderedEmbedding{TValue}"/> walks the offspring toward the metric-space
    /// target through gene SWAPS (transpositions of two positions — the Cayley/swap metric), this
    /// embedding walks it through gene INSERTIONS (move one element to another position, shifting
    /// the intermediate genes — the Ulam/insertion metric). Both are valid geometric crossovers in
    /// the sense of Moraglio (a descendant lies on a geodesic of a permutation metric toward the
    /// parent(s)), but they explore different neighbourhoods: a single insertion can relocate an
    /// element arbitrarily far in one move, whereas a single swap only exchanges two positions.
    /// Different metrics => different geodesic segments => different offspring reachable in one
    /// crossover step. See MGS-7 (Config 4) for the pedagogical illustration.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Honest caveat (G.9). The 2-parent centroid operator of <see cref="GeometricCrossover{TValue}"/>
    /// maps permutations to a metric-space target that is NOT itself a permutation (e.g. the gene-wise
    /// mean of [0,1,2,3,4] and [2,0,1,3,4] is [1,0.5,1.5,3,4]). The back-walk interprets that target as
    /// a desired ranking and inserts toward it, so the insertion-metric DISTINCTION is guaranteed in
    /// one step only when the metric-space target is itself a permutation (passed directly to
    /// <see cref="MapFromGeometry"/>); under a naive centroid it degrades symmetrically across the
    /// positional embeddings — the same "naive centroid is inadequate on city-label indices" limit
    /// documented in MGS-7. See <see cref="OrderedEmbedding{TValue}"/> and
    /// <see cref="KendallTauEmbedding{TValue}"/> for the sibling metrics with the identical limit.
    /// </para>
    /// </remarks>
    public class InsertionEmbedding<TValue> : IdentityEmbedding<TValue>
    {
        private Func<IChromosome, int, int, bool> _validateInsertionFunction;

        /// <summary>Whether the target chromosome is ordered (permutation).</summary>
        public virtual bool IsOrdered { get; set; }

        /// <summary>
        /// How candidate offspring metric-space values are walked when converting them back
        /// to gene-space insertions.
        /// </summary>
        public GeneSelectionMode GeneSelectionMode { get; set; } = GeneSelectionMode.AllIndexed;

        /// <summary>
        /// The default offspring creation splits into 2 cases depending on whether the chromosome
        /// is ordered (permutation) or not.
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
        /// Walks some or all candidate offspring metric-space values; for each value that differs
        /// from the current gene, removes it from its current position and INSERTS it at the target
        /// index, shifting the intermediate genes. Each accepted move is a single insertion
        /// (one Ulam step) toward the metric-space target.
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

            foreach (var i in selectedIndices)
            {
                var current = (TValue)offspring.GetGene(i).Value;
                if (!current.Equals(values[i]))
                {
                    // Permutations have a unique position per value: find where the target currently sits.
                    var srcIndex = IndexOfValue(offspring, values[i]);
                    if (srcIndex < 0)
                    {
                        throw new ArgumentException($"value {values[i]} not found in ordered chromosome {offspring}");
                    }
                    if (ValidateInsertionFunction(offspring, i, srcIndex))
                    {
                        InsertAt(offspring, srcIndex, i);
                        // SingleFirstAllowed returns the offspring as soon as one insertion is accepted.
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
        /// Moves the gene at <paramref name="srcIndex"/> to <paramref name="destIndex"/>, shifting
        /// the genes in between by one position. This is a single insertion move (one Ulam step).
        /// </summary>
        protected static void InsertAt(IChromosome chromosome, int srcIndex, int destIndex)
        {
            if (srcIndex == destIndex)
            {
                return;
            }

            var moved = chromosome.GetGene(srcIndex);
            if (srcIndex < destIndex)
            {
                // Shift the segment (srcIndex .. destIndex-1) one step toward the source.
                for (int k = srcIndex; k < destIndex; k++)
                {
                    chromosome.ReplaceGene(k, chromosome.GetGene(k + 1));
                }
            }
            else
            {
                // Shift the segment (destIndex+1 .. srcIndex) one step away from the source.
                for (int k = srcIndex; k > destIndex; k--)
                {
                    chromosome.ReplaceGene(k, chromosome.GetGene(k - 1));
                }
            }

            chromosome.ReplaceGene(destIndex, moved);
        }

        private static int IndexOfValue(IChromosome chromosome, TValue value)
        {
            for (int i = 0; i < chromosome.Length; i++)
            {
                if (((TValue)chromosome.GetGene(i).Value).Equals(value))
                {
                    return i;
                }
            }
            return -1;
        }

        /// <summary>
        /// An insertion-validation function specific to the problem to solve.
        /// </summary>
        public Func<IChromosome, int, int, bool> ValidateInsertionFunction
        {
            get
            {
                if (_validateInsertionFunction == null)
                {
                    _validateInsertionFunction = GetDefaultInsertionValidationFunction();
                }
                return _validateInsertionFunction;
            }
            set => _validateInsertionFunction = value;
        }

        /// <summary>
        /// The default insertion validation function; can be overwritten with problem-specific embeddings.
        /// </summary>
        protected virtual Func<IChromosome, int, int, bool> GetDefaultInsertionValidationFunction()
        {
            return (chromosome, destIndex, srcIndex) => true;
        }
    }
}
