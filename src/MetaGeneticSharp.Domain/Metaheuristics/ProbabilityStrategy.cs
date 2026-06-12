#nullable disable

namespace MetaGeneticSharp
{
    /// <summary>
    /// Defines how a metaheuristic deals with the crossover/mutation probabilities
    /// it receives from the running genetic algorithm.
    /// </summary>
    [Flags]
    public enum ProbabilityStrategy
    {
        /// <summary>
        /// The probability is passed unchanged to descendant operators, which apply it themselves.
        /// </summary>
        PassToDescendents = 0,

        /// <summary>
        /// The probability is tested by the metaheuristic itself; descendants run with probability 1.
        /// </summary>
        TestProbability = 1,

        /// <summary>
        /// The probability is overwritten by the metaheuristic's own configuration.
        /// </summary>
        OverwriteProbability = 2
    }
}
