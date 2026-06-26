#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using GeneticSharp;

namespace MetaGeneticSharp
{
    /// <summary>
    /// An adjacent-transposition geometric embedding for ordered chromosomes (permutations).
    /// Where <see cref="OrderedEmbedding{TValue}"/> walks the offspring toward the metric-space
    /// target through gene SWAPS of two arbitrary positions (the Cayley/swap metric) and
    /// <see cref="InsertionEmbedding{TValue}"/> walks it through gene INSERTIONS (the Ulam metric),
    /// this embedding walks it through ADJACENT TRANSPOSITIONS — swapping only neighbouring genes
    /// (the Kendall-Tau / bubble-sort metric). All three are valid geometric crossovers in the
    /// sense of Moraglio (a descendant lies on a geodesic of a permutation metric toward the
    /// parent(s)), but they explore neighbourhoods of different diameter: a single arbitrary swap
    /// (Cayley) or insertion (Ulam) can move an element across the whole chromosome in one move,
    /// whereas a single adjacent transposition (Kendall-Tau) shifts it by exactly one slot.
    /// The Kendall-Tau distance between two permutations equals the number of adjacent swaps a
    /// bubble sort needs to turn one into the other — hence the back-walk here IS a bubble sort
    /// toward the metric-space target. Different metrics => different geodesic segments =>
    /// different offspring reachable in one crossover step. See the keystone test
    /// <c>KendallTauVsCayleyAndUlam_DistinctGeodesicFromSameParentAndTarget</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Honest caveat (G.9). As with <see cref="InsertionEmbedding{TValue}"/>, the 2-parent centroid
    /// operator of <see cref="GeometricCrossover{TValue}"/> maps permutations to a metric-space
    /// target that is NOT itself a permutation (e.g. the gene-wise mean of [0,1,2,3,4] and
    /// [2,0,1,3,4] is [1,0.5,1.5,3,4]). The back-walk interprets that target as a desired ranking
    /// and bubble-sorts toward it, so the swap-vs-insertion-vs-adjacent-swap DISTINCTION is
    /// guaranteed in one step only when the metric-space target is itself a permutation (passed
    /// directly to <see cref="MapFromGeometry"/>); under a naive centroid it degrades symmetrically
    /// across all three embeddings — the same "naive centroid is inadequate on city-label indices"
    /// limit documented in MGS-7.
    /// </para>
    /// </remarks>
    public class KendallTauEmbedding<TValue> : IdentityEmbedding<TValue>
    {
        /// <summary>Whether the target chromosome is ordered (permutation).</summary>
        public virtual bool IsOrdered { get; set; }

        /// <summary>
        /// How candidate offspring metric-space values are walked when converting them back
        /// to gene-space adjacent transpositions.
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
        /// Bubble-sorts the offspring toward the metric-space target one adjacent transposition at
        /// a time. Each accepted adjacent swap is a single Kendall-Tau step toward the target.
        /// Under <see cref="GeneSelectionMode.SingleFirstAllowed"/> the walk returns after the FIRST
        /// accepted swap; under <see cref="GeneSelectionMode.AllIndexed"/> it keeps sweeping passes
        /// until no swap is accepted (full convergence to the target permutation).
        /// </summary>
        public virtual IChromosome MapFromGeometryOrdered(IList<IChromosome> parents, IList<TValue> values)
        {
            var offspring = parents.First().Clone();

            // Rank of each target value: targetRank[v] = position of v in the target permutation.
            // A pair of adjacent genes (i, i+1) is an "inversion" when targetRank[gene[i]] >
            // targetRank[gene[i+1]]: swapping them is one Kendall-Tau step toward the target.
            var targetRank = new Dictionary<TValue, int>(values.Count);
            for (int i = 0; i < values.Count; i++)
            {
                targetRank[values[i]] = i;
            }

            bool singleFirst = (GeneSelectionMode & GeneSelectionMode.SingleFirstAllowed) == GeneSelectionMode.SingleFirstAllowed;

            // Each pass scans adjacent pairs once. A bubble sort converges in at most n passes
            // (n = length); the loop stops early as soon as a pass accepts no swap.
            for (int pass = 0; pass < offspring.Length; pass++)
            {
                bool swappedThisPass = false;
                for (int i = 0; i < offspring.Length - 1; i++)
                {
                    var a = (TValue)offspring.GetGene(i).Value;
                    var b = (TValue)offspring.GetGene(i + 1).Value;
                    if (targetRank.TryGetValue(a, out int ra) && targetRank.TryGetValue(b, out int rb) && ra > rb)
                    {
                        offspring.ReplaceGene(i, new Gene(b));
                        offspring.ReplaceGene(i + 1, new Gene(a));
                        swappedThisPass = true;
                        if (singleFirst)
                        {
                            return offspring;
                        }
                    }
                }

                if (!swappedThisPass)
                {
                    break;
                }
            }

            return offspring;
        }
    }
}
