#nullable disable
namespace MetaGeneticSharp
{
    /// <summary>
    /// A simple compound metaheuristic holding a primitive (or an already-built compound
    /// metaheuristic) returned unchanged upon building. Useful as a trivial adapter from a plain
    /// <see cref="IContainerMetaHeuristic"/> to the <see cref="ICompoundMetaheuristic"/> contract.
    /// Ported from GeneticSharp.Domain.Metaheuristics.Compound (PR giacomelli/GeneticSharp#87).
    /// </summary>
    public class SimpleCompoundMetaheuristic : ICompoundMetaheuristic
    {
        /// <summary>Builds a simple compound wrapping <paramref name="simpleMetaHeuristic"/>.</summary>
        public SimpleCompoundMetaheuristic(IContainerMetaHeuristic simpleMetaHeuristic)
        {
            SimpleMetaHeuristic = simpleMetaHeuristic;
        }

        /// <summary>The heuristic returned by <see cref="Build"/>.</summary>
        public IContainerMetaHeuristic SimpleMetaHeuristic { get; set; }

        /// <inheritdoc />
        public IContainerMetaHeuristic Build()
        {
            return SimpleMetaHeuristic;
        }
    }
}
