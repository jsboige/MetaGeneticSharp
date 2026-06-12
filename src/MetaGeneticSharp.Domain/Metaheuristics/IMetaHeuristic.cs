#nullable disable

using GeneticSharp;

namespace MetaGeneticSharp
{
    /// <summary>
    /// A metaheuristic intercepts each stage of the GA evolution loop (selection,
    /// crossover, mutation, reinsertion) and can alter, replace or compose the
    /// corresponding operator's behavior. Metaheuristics are composable: most
    /// implementations wrap a sub-metaheuristic and scope or parameterize it.
    /// </summary>
    public interface IMetaHeuristic
    {
        Guid Guid { get; set; }

        /// <summary>
        /// Gets the evolution context for the given algorithm and population
        /// (cached in the population's parameter store when available).
        /// </summary>
        IEvolutionContext GetContext(IGeneticAlgorithm geneticAlgorithm, IPopulation population);

        /// <summary>
        /// Selects the parent population from the current generation.
        /// </summary>
        IList<IChromosome> SelectParentPopulation(IEvolutionContext ctx, ISelection selection);

        /// <summary>
        /// Matches parents and performs the crossover for the individual targeted by the context.
        /// Returns null if no crossover was performed.
        /// </summary>
        IList<IChromosome> MatchParentsAndCross(IEvolutionContext ctx, ICrossover crossover, float crossoverProbability, IList<IChromosome> parents);

        /// <summary>
        /// Mutates the offspring targeted by the context.
        /// </summary>
        void MutateChromosome(IEvolutionContext ctx, IMutation mutation, float mutationProbability, IList<IChromosome> offSprings);

        /// <summary>
        /// Reinserts chromosomes to build the next generation from offspring and parents.
        /// </summary>
        IList<IChromosome> Reinsert(IEvolutionContext ctx, IReinsertion reinsertion, IList<IChromosome> offspring, IList<IChromosome> parents);

        /// <summary>
        /// Registers this metaheuristic's parameters into the context's definitions.
        /// </summary>
        void RegisterParameters(IEvolutionContext ctx);
    }
}
