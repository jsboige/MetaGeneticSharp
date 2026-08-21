#nullable disable
using System;
using GeneticSharp;

namespace MetaGeneticSharp
{
    /// <summary>
    ///   The generic memetic layer: wraps ANY compound metaheuristic with a post-generation
    ///   local-improvement pass over its N best individuals. A memetic algorithm is a
    ///   population-based search whose individuals are refined by a problem-specific local
    ///   search between generations (P. Moscato, "On Evolution, Search, Optimization, Genetic
    ///   Algorithms and Martial Arts", 1989) -- Glover's scatter-search template likewise makes
    ///   the improvement method one of its five methods, deliberately left to the caller. This
    ///   wrapper is that method, applicable uniformly: <c>new MemeticAlgorithm(pso)</c>,
    ///   <c>new MemeticAlgorithm(scatterSearch)</c>, <c>new MemeticAlgorithm(de)</c>... all gain
    ///   the same improvement layer over the compound's own assembly.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Assembly.</b> The inner compound's <see cref="ICompoundMetaheuristic.Build"/> output
    /// (its full pipeline: No-Mutation scope, forced reinsertion, match machinery) becomes the
    /// sub-metaheuristic of a <see cref="LocalImprovementMetaHeuristic"/> that runs the
    /// improvement pass in the selection hook -- the first point of the loop where the previous
    /// generation is complete and evaluated -- then delegates every stage to the inner pipeline
    /// unchanged.
    /// </para>
    /// <para>
    /// The improvement operator is necessarily caller-provided (a local search is
    /// problem-specific: 2-Opt on a tour, gradient step on a surface, tabu move on a
    /// schedule), so this compound has no name-based factory entry: it is composed directly.
    /// </para>
    /// </remarks>
    public class MemeticAlgorithm : ICompoundMetaheuristic
    {
        /// <summary>The default number of best chromosomes improved each generation.</summary>
        public const int DefaultImprovementCount = 1;

        /// <summary>The compound being given the memetic layer (PSO, ScatterSearch, DE, ...).</summary>
        public ICompoundMetaheuristic Inner { get; set; }

        /// <summary>Wraps the given compound with the default improvement configuration.</summary>
        public MemeticAlgorithm(ICompoundMetaheuristic inner)
        {
            Inner = inner;
        }

        /// <summary>Parameterless construction, Inner set afterwards (object-initializer style).</summary>
        public MemeticAlgorithm()
        {
        }

        /// <summary>The number of best chromosomes improved each generation (the "N best particles").</summary>
        public int ImprovementCount { get; set; } = DefaultImprovementCount;

        /// <summary>
        /// The problem-specific local-improvement operator forwarded to the layer; null leaves
        /// the wrapper a pure pass-through (useful for A/B-measuring the layer's contribution).
        /// </summary>
        public LocalImprovementMetaHeuristic.ImprovementOperator ImproveOperator { get; set; }

        /// <inheritdoc />
        public IContainerMetaHeuristic Build()
        {
            if (Inner == null)
            {
                throw new InvalidOperationException(
                    "MemeticAlgorithm.Inner must be set to the compound receiving the improvement layer.");
            }

            var innerPipeline = Inner.Build();
            return new LocalImprovementMetaHeuristic(innerPipeline)
            {
                ImprovementCount = ImprovementCount,
                ImproveOperator = ImproveOperator,
            }.WithName("Memetic improvement layer",
                "Moscato, P. (1989). On Evolution, Search, Optimization, Genetic Algorithms and Martial Arts. " +
                "Improves the N best individuals of the evaluated generation through the caller's local-search " +
                "operator, then delegates to the inner compound unchanged.");
        }
    }
}
