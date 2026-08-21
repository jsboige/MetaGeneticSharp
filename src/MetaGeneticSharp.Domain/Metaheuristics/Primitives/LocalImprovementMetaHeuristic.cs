#nullable disable
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using GeneticSharp;

namespace MetaGeneticSharp
{
    /// <summary>
    /// Applies a local-improvement (memetic) pass to the best chromosomes of the current
    /// generation, then delegates every stage to the sub-metaheuristic unchanged. This is the
    /// improvement layer of a memetic algorithm (P. Moscato, "On Evolution, Search, Optimization,
    /// Genetic Algorithms and Martial Arts", 1989): a population-based search whose individuals
    /// are refined by a problem-specific local search between generations -- the pattern the
    /// ygmh codebase expresses as its <c>+2OptBest</c>/<c>+2OptFirst</c> metaheuristic variants
    /// (A. Hernandez &amp; Y. Gonzalez, "Metaheuristics in C#", 2012, chapter 1).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Why the selection hook.</b> The improvement runs in <see cref="SelectParentPopulation"/>:
    /// by the time the engine selects the parents of generation N+1, generation N is complete AND
    /// evaluated, which makes it the first point of the evolution loop where "the N best
    /// individuals" is a well-defined, fitness-ranked set. The reinsertion hook runs before the
    /// offspring are evaluated, and the mutation hook fires mid-generation.
    /// </para>
    /// <para>
    /// <b>Acceptance and in-place update.</b> The operator returns a fresh CANDIDATE chromosome;
    /// this heuristic evaluates it through the running algorithm's fitness and accepts it only
    /// when strictly better (<c>candidate.CompareTo(target) &gt; 0</c> -- the engine's own
    /// ordering, the same convention BestChromosome tracking uses). On acceptance the target's
    /// GENES are replaced in place and its fitness re-assigned, so every existing reference
    /// (Population.BestChromosome, the generation list, elite selections) stays valid: no
    /// chromosome object is ever swapped out from under the engine. The generation ranking is
    /// refreshed at the next <c>EndCurrentGeneration</c>, one generation of latency at most --
    /// and none when the improved chromosome already IS the tracked best, the common case since
    /// improvement targets the top of the ranking.
    /// </para>
    /// </remarks>
    [DisplayName("Local Improvement")]
    public class LocalImprovementMetaHeuristic : ContainerMetaHeuristic
    {
        private static readonly IComparer<IChromosome> BetterFirst =
            Comparer<IChromosome>.Create((a, b) => a.CompareTo(b));

        /// <summary>
        /// Produces an improved CANDIDATE for the given chromosome. The candidate must be a
        /// distinct chromosome (typically <c>current.Clone()</c> with modified genes) of the same
        /// length; its carried fitness is ignored (the layer re-evaluates). Returning null skips
        /// the improvement for that chromosome this generation.
        /// </summary>
        public delegate IChromosome ImprovementOperator(IChromosome current, IEvolutionContext ctx);

        /// <summary>The number of best chromosomes improved each generation (the "N best particles").</summary>
        public int ImprovementCount { get; set; } = 1;

        /// <summary>The local-improvement operator; null disables the layer (pure pass-through).</summary>
        public ImprovementOperator ImproveOperator { get; set; }

        public LocalImprovementMetaHeuristic()
        {
        }

        public LocalImprovementMetaHeuristic(IMetaHeuristic subMetaHeuristic)
        {
            SubMetaHeuristic = subMetaHeuristic;
        }

        /// <inheritdoc />
        public override IList<IChromosome> SelectParentPopulation(IEvolutionContext ctx, ISelection selection)
        {
            ImproveBest(ctx);
            return SubMetaHeuristic.SelectParentPopulation(ctx, selection);
        }

        /// <summary>
        /// Improves the <see cref="ImprovementCount"/> best evaluated chromosomes of the current
        /// generation. Chromosomes without a fitness are skipped: they cannot be ranked.
        /// </summary>
        protected virtual void ImproveBest(IEvolutionContext ctx)
        {
            var chromosomes = ctx?.Population?.CurrentGeneration?.Chromosomes;
            if (ImproveOperator == null || ImprovementCount <= 0 || chromosomes == null || chromosomes.Count == 0)
            {
                return;
            }

            var targets = chromosomes
                .Where(c => c.Fitness.HasValue)
                .OrderByDescending(c => c, BetterFirst)
                .Take(ImprovementCount)
                .ToList();

            foreach (var target in targets)
            {
                var candidate = ImproveOperator(target, ctx);
                if (candidate == null || ReferenceEquals(candidate, target))
                {
                    continue;
                }

                // A candidate is unevaluated until proven otherwise: Clone() carries the source
                // fitness over, and an operator returning a stale-fitness chromosome must not
                // have that fitness honored.
                candidate.Fitness = null;
                EvaluateCandidate(candidate, ctx);
                if (candidate.Fitness.HasValue && candidate.CompareTo(target) > 0)
                {
                    target.ReplaceGenes(0, candidate.GetGenes());
                    target.Fitness = candidate.Fitness;
                }
            }
        }

        /// <summary>
        /// Evaluates a candidate through the running algorithm's fitness, assigning its
        /// <see cref="IChromosome.Fitness"/>. <see cref="IGeneticAlgorithm"/> does not expose the
        /// fitness, so the concrete algorithm is resolved (MGS first, then the vanilla GeneticSharp
        /// algorithm); without a resolvable fitness the candidate simply stays unevaluated -- never
        /// accepted, never corrupting the target. Virtual so tests can drive the evaluation without
        /// a live algorithm.
        /// </summary>
        protected virtual void EvaluateCandidate(IChromosome candidate, IEvolutionContext ctx)
        {
            var algorithm = ctx?.GeneticAlgorithm;
            var fitness = (algorithm as MetaGeneticAlgorithm)?.Fitness
                ?? (algorithm as GeneticSharp.GeneticAlgorithm)?.Fitness;
            if (fitness == null)
            {
                return;
            }

            candidate.Fitness = fitness.Evaluate(candidate);
        }
    }
}
