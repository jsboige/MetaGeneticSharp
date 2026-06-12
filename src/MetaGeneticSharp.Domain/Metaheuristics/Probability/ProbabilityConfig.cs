#nullable disable

namespace MetaGeneticSharp
{
    /// <summary>
    /// Configuration of how a metaheuristic computes the probability for a genetic
    /// operator (crossover or mutation), optionally overriding the algorithm-level value.
    /// </summary>
    public class ProbabilityConfig
    {
        public ProbabilityStrategy Strategy { get; set; }

        /// <summary>
        /// Static probability used when <see cref="ProbabilityStrategy.OverwriteProbability"/>
        /// is set and no dynamic probability is defined.
        /// </summary>
        public float StaticProbability { private get; set; } = 1;

        /// <summary>
        /// Dynamic probability computed from the evolution context and the initial probability.
        /// </summary>
        public Func<IEvolutionContext, float, float> DynamicProbability { private get; set; }

        public float GetProbability(IEvolutionContext ctx, float initialProbability)
        {
            if ((Strategy & ProbabilityStrategy.OverwriteProbability) == ProbabilityStrategy.OverwriteProbability)
            {
                return DynamicProbability?.Invoke(ctx, initialProbability) ?? StaticProbability;
            }

            return initialProbability;
        }
    }
}
