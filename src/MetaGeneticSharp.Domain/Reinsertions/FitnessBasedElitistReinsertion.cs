#nullable disable

using GeneticSharp;

namespace MetaGeneticSharp
{
    /// <summary>
    /// Keeps the best MinSize chromosomes among parents and offspring combined.
    /// This is MetaGeneticSharp's default reinsertion: since the engine never sorts
    /// generations implicitly, elitism must be an explicit reinsertion decision.
    ///
    /// Fitness decides first; at EQUAL fitness the newcomer wins. Arrival order must not
    /// decide, because the sort is stable and the inherited candidate order lists parents
    /// first: an offspring that merely ties its parent would then always lose its slot, and
    /// on an integer objective with many ties (a conflict count) the population closes —
    /// measured on the Sudoku substrate of #13778 (12-18 admitting generations out of 160,
    /// then none for the remaining 140, with the best frozen at 35-42 conflicts).
    /// Preferring the newcomer is neutral drift on the fitness landscape: the best value
    /// never decreases (a tie replaces an equal), it only stops the search from freezing.
    /// </summary>
    public class FitnessBasedElitistReinsertion : ReinsertionBase
    {
        public FitnessBasedElitistReinsertion()
            : base(true, true)
        {
        }

        protected override IList<IChromosome> PerformSelectChromosomes(IPopulation population, IList<IChromosome> offspring, IList<IChromosome> parents)
        {
            var candidates = new List<IChromosome>(parents.Count + offspring.Count);
            candidates.AddRange(parents);
            candidates.AddRange(offspring);

            // Total order on the candidate positions (no reliance on sort stability):
            // fitness descending, then the later position first so that a tie is resolved
            // for the offspring, which are appended after the parents.
            var ranked = candidates
                .Select((chromosome, position) => (Chromosome: chromosome, Position: position))
                .OrderByDescending(entry => entry.Chromosome.Fitness)
                .ThenByDescending(entry => entry.Position)
                .ToList();

            return ranked
                .Take(population.MinSize)
                .Select(entry => entry.Chromosome)
                .ToList();
        }
    }
}
