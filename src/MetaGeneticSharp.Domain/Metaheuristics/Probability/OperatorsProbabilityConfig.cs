#nullable disable

namespace MetaGeneticSharp
{
    /// <summary>
    /// Pairs the crossover and mutation probability configurations of a metaheuristic.
    /// </summary>
    public class OperatorsProbabilityConfig
    {
        public ProbabilityConfig Crossover { get; set; } = new ProbabilityConfig();

        public ProbabilityConfig Mutation { get; set; } = new ProbabilityConfig();
    }
}
