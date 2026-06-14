#nullable disable
namespace MetaGeneticSharp
{
    /// <summary>
    /// Interface for metaheuristics built from compounding available metaheuristic primitives.
    /// A compound metaheuristic assembles primitives (operators, scope switches, phases, match
    /// machinery) into a single <see cref="IContainerMetaHeuristic"/> ready to drive an evolution.
    /// Ported from GeneticSharp.Domain.Metaheuristics.Compound (PR giacomelli/GeneticSharp#87).
    /// </summary>
    public interface ICompoundMetaheuristic
    {
        /// <summary>
        /// Creates a compound metaheuristic from local state.
        /// </summary>
        /// <returns>A compound metaheuristic ready to drive an evolution.</returns>
        IContainerMetaHeuristic Build();
    }
}
