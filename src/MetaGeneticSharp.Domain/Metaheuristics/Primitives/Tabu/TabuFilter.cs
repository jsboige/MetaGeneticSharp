#nullable disable
using System.Collections.Generic;
using GeneticSharp;

namespace MetaGeneticSharp
{
    /// <summary>
    /// Decides whether a candidate transition is admissible given the memory and the search
    /// state (#12049 axe 4) -- the ASPIRATION layer of the tabu template. A hard interdict with
    /// no escape would forbid the only move that leads out of a region; the classic escape is
    /// aspiration: a tabu transition is admitted anyway when it improves on the best solution
    /// ever seen. Decoupled from the memory (WHO is interdicted) and the projection (WHAT is
    /// tracked), so the admissibility policy varies independently of them.
    /// </summary>
    public interface ITabuFilter
    {
        /// <summary>
        /// Whether the transition current → candidate may be performed. When no interdicted
        /// attribute is involved the answer must be true (the filter only arbitrates CONFLICTS
        /// with the memory).
        /// </summary>
        /// <param name="current">The chromosome the transition starts from.</param>
        /// <param name="candidate">The candidate transition target.</param>
        /// <param name="projection">The projection giving the transition's attributes.</param>
        /// <param name="memory">The tabu memory consulted for the interdict.</param>
        /// <param name="bestFitness">The best fitness seen so far in the search (the aspiration reference).</param>
        bool IsAdmissible(IChromosome current, IChromosome candidate, ITabuProjection projection, ITabuMemory memory, double bestFitness);
    }

    /// <summary>
    /// The strict filter: a transition whose <see cref="ITabuProjection.Forbidden"/> attributes
    /// intersect the memory is rejected, no exception. The baseline policy.
    /// </summary>
    public class StrictTabuFilter : ITabuFilter
    {
        /// <inheritdoc />
        public bool IsAdmissible(IChromosome current, IChromosome candidate, ITabuProjection projection, ITabuMemory memory, double bestFitness)
        {
            foreach (var attribute in projection.Forbidden(current, candidate))
            {
                if (memory.IsTabu(attribute))
                {
                    return false;
                }
            }

            return true;
        }
    }

    /// <summary>
    /// Aspiration-on-best (Glover's aspiration criterion): the interdict is LIFTED when the
    /// candidate is strictly better than the best fitness ever seen -- the tabu move that would
    /// find a new global best is always admissible, preventing the interdict from trapping the
    /// search away from an improvement it can see.
    /// </summary>
    public class AspirationOnBestFilter : ITabuFilter
    {
        private readonly ITabuFilter _inner;

        /// <param name="inner">The arbitration used when the candidate does NOT aspire (default <see cref="StrictTabuFilter"/>).</param>
        public AspirationOnBestFilter(ITabuFilter inner = null)
        {
            _inner = inner ?? new StrictTabuFilter();
        }

        /// <inheritdoc />
        public bool IsAdmissible(IChromosome current, IChromosome candidate, ITabuProjection projection, ITabuMemory memory, double bestFitness)
        {
            if (_inner.IsAdmissible(current, candidate, projection, memory, bestFitness))
            {
                return true;
            }

            // Aspiration: only a strictly-better candidate lifts the interdict, and only when a
            // reference best is actually known (NaN sentinel: no reference yet, nothing aspires).
            if (!double.IsNaN(bestFitness) && candidate.Fitness.HasValue && candidate.Fitness.Value.CompareTo(bestFitness) > 0)
            {
                return true;
            }

            return false;
        }
    }
}
