#nullable disable

namespace MetaGeneticSharp
{
    /// <summary>
    /// The stages of a GA generation evolution. Used to scope metaheuristic behaviors
    /// to specific parts of the evolution loop.
    /// </summary>
    [Flags]
    public enum EvolutionStage
    {
        None = 0,
        Selection = 1,
        Crossover = 2,
        Mutation = 4,
        Reinsertion = 8,
        All = Selection | Crossover | Mutation | Reinsertion
    }
}
