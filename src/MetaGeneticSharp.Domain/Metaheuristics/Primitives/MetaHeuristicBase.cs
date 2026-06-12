#nullable disable

using GeneticSharp;

namespace MetaGeneticSharp
{
    /// <summary>
    /// Base class for metaheuristics: named entity + parameter registration + evolution
    /// context retrieval (cached in the population's parameter store when the population
    /// is an <see cref="IMetaPopulation"/>).
    /// </summary>
    public abstract class MetaHeuristicBase : NamedEntity, IMetaHeuristic
    {
        public Dictionary<string, IMetaHeuristicParameter> Parameters { get; set; } = new();

        public abstract IList<IChromosome> SelectParentPopulation(IEvolutionContext ctx, ISelection selection);

        public abstract IList<IChromosome> MatchParentsAndCross(IEvolutionContext ctx, ICrossover crossover, float crossoverProbability, IList<IChromosome> parents);

        public abstract void MutateChromosome(IEvolutionContext ctx, IMutation mutation, float mutationProbability, IList<IChromosome> offSprings);

        public abstract IList<IChromosome> Reinsert(IEvolutionContext ctx, IReinsertion reinsertion, IList<IChromosome> offspring, IList<IChromosome> parents);

        public virtual IEvolutionContext GetContext(IGeneticAlgorithm geneticAlgorithm, IPopulation population)
        {
            if (population is not IMetaPopulation metaPopulation)
            {
                // Vanilla IPopulation: no parameter store available, fresh context per call.
                return GetNewContext(geneticAlgorithm, population);
            }

            if (!metaPopulation.Parameters.TryGetValue(nameof(IEvolutionContext), out var cached))
            {
                lock (population)
                {
                    if (!metaPopulation.Parameters.TryGetValue(nameof(IEvolutionContext), out cached))
                    {
                        cached = GetNewContext(geneticAlgorithm, population);
                        metaPopulation.Parameters[nameof(IEvolutionContext)] = cached;
                    }
                }
            }

            return (IEvolutionContext)cached;
        }

        protected virtual IEvolutionContext GetNewContext(IGeneticAlgorithm geneticAlgorithm, IPopulation population)
        {
            var toReturn = new EvolutionContext
            {
                GeneticAlgorithm = geneticAlgorithm,
                Population = population
            };

            RegisterParameters(toReturn);
            return toReturn;
        }

        public virtual void RegisterParameters(IEvolutionContext ctx)
        {
            foreach (var parameter in Parameters)
            {
                ctx.RegisterParameter(parameter.Key, parameter.Value);
            }
        }
    }
}
