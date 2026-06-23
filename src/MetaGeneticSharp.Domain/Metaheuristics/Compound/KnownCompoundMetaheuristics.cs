#nullable disable

namespace MetaGeneticSharp
{
    /// <summary>
    ///   The catalogue of compound metaheuristics the <see cref="MetaHeuristicsService"/>
    ///   knows how to build by name. Covers the Default GA, the reconstructed geometric
    ///   compounds (WOA / EO / FBI) and the heterogeneous-island archipelago variants.
    ///   Ported from GeneticSharp.Domain.Metaheuristics (PR giacomelli/GeneticSharp#87).
    /// </summary>
    public enum KnownCompoundMetaheuristics
    {
        None = 0,
        Default,
        DefaultRandomHyperspeed,
        WhaleOptimisation,
        WhaleOptimisationNaive,
        EquilibriumOptimizer,
        ForensicBasedInvestigation,
        DifferentialEvolution,
        Islands5Default,
        Islands5DefaultNoMigration,
        Islands5BestMixture,
        Islands5BestMixtureNoMigration
    }
}
