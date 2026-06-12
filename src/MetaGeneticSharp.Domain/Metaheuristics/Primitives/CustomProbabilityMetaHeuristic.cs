#nullable disable

using GeneticSharp;

namespace MetaGeneticSharp
{
    /// <summary>
    /// A metaheuristic that controls how the crossover/mutation probabilities are applied,
    /// according to its <see cref="ProbabilityConfig"/>. A base probability greater than 1
    /// yields multiple operator applications (one full run per unit, then a probabilistic
    /// run for the remainder).
    /// </summary>
    public abstract class CustomProbabilityMetaHeuristic : MetaHeuristicBase
    {
        public OperatorsProbabilityConfig ProbabilityConfig { get; set; } = new();

        public sealed override IList<IChromosome> MatchParentsAndCross(IEvolutionContext ctx, ICrossover crossover, float crossoverProbability, IList<IChromosome> parents)
        {
            IList<IChromosome> toReturn = null;
            var baseProbability = ProbabilityConfig.Crossover.GetProbability(ctx, crossoverProbability);

            while (ShouldRun(baseProbability, ProbabilityConfig.Crossover.Strategy, out var subProbability))
            {
                var newChildren = DoMatchParentsAndCross(ctx, crossover, subProbability, parents);

                if (toReturn == null)
                {
                    toReturn = newChildren;
                }
                else if (newChildren != null)
                {
                    foreach (var child in newChildren)
                    {
                        toReturn.Add(child);
                    }
                }

                baseProbability--;
            }

            return toReturn;
        }

        public sealed override void MutateChromosome(IEvolutionContext ctx, IMutation mutation, float mutationProbability, IList<IChromosome> offSprings)
        {
            var baseProbability = ProbabilityConfig.Mutation.GetProbability(ctx, mutationProbability);

            while (ShouldRun(baseProbability, ProbabilityConfig.Mutation.Strategy, out var subProbability))
            {
                DoMutateChromosome(ctx, mutation, subProbability, offSprings);
                baseProbability--;
            }
        }

        /// <summary>
        /// Decides whether the operator should run for the given residual probability.
        /// When the strategy tests the probability itself, descendants run with probability 1.
        /// </summary>
        protected bool ShouldRun(float baseProbability, ProbabilityStrategy strategy, out float subProbability)
        {
            subProbability = baseProbability;

            if (baseProbability <= float.Epsilon)
            {
                return false;
            }

            if ((strategy & ProbabilityStrategy.TestProbability) != ProbabilityStrategy.TestProbability)
            {
                return true;
            }

            if (1 - subProbability < float.Epsilon || RandomizationProvider.Current.GetDouble() < subProbability)
            {
                subProbability = 1;
                return true;
            }

            return false;
        }

        protected abstract IList<IChromosome> DoMatchParentsAndCross(IEvolutionContext ctx, ICrossover crossover, float crossoverProbability, IList<IChromosome> parents);

        protected abstract void DoMutateChromosome(IEvolutionContext ctx, IMutation mutation, float mutationProbability, IList<IChromosome> offSprings);
    }
}
