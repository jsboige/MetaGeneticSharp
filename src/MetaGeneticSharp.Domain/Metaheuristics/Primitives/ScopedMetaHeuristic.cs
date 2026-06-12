#nullable disable

using GeneticSharp;

namespace MetaGeneticSharp
{
    /// <summary>
    /// A container metaheuristic whose own behavior only applies to the evolution
    /// stages in <see cref="Scope"/>; other stages fall through to the sub-metaheuristic.
    /// </summary>
    public abstract class ScopedMetaHeuristic : ContainerMetaHeuristic
    {
        protected ScopedMetaHeuristic()
        {
        }

        protected ScopedMetaHeuristic(IMetaHeuristic subMetaHeuristic)
            : base(subMetaHeuristic)
        {
        }

        public EvolutionStage Scope { get; set; } = EvolutionStage.All;

        public sealed override IList<IChromosome> SelectParentPopulation(IEvolutionContext ctx, ISelection selection)
        {
            return (Scope & EvolutionStage.Selection) == EvolutionStage.Selection
                ? ScopedSelectParentPopulation(ctx, selection)
                : base.SelectParentPopulation(ctx, selection);
        }

        protected sealed override IList<IChromosome> DoMatchParentsAndCross(IEvolutionContext ctx, ICrossover crossover, float crossoverProbability, IList<IChromosome> parents)
        {
            return (Scope & EvolutionStage.Crossover) == EvolutionStage.Crossover
                ? ScopedMatchParentsAndCross(ctx, crossover, crossoverProbability, parents)
                : base.DoMatchParentsAndCross(ctx, crossover, crossoverProbability, parents);
        }

        protected sealed override void DoMutateChromosome(IEvolutionContext ctx, IMutation mutation, float mutationProbability, IList<IChromosome> offSprings)
        {
            if ((Scope & EvolutionStage.Mutation) == EvolutionStage.Mutation)
            {
                ScopedMutateChromosome(ctx, mutation, mutationProbability, offSprings);
            }
            else
            {
                base.DoMutateChromosome(ctx, mutation, mutationProbability, offSprings);
            }
        }

        public sealed override IList<IChromosome> Reinsert(IEvolutionContext ctx, IReinsertion reinsertion, IList<IChromosome> offspring, IList<IChromosome> parents)
        {
            return (Scope & EvolutionStage.Reinsertion) == EvolutionStage.Reinsertion
                ? ScopedReinsert(ctx, reinsertion, offspring, parents)
                : base.Reinsert(ctx, reinsertion, offspring, parents);
        }

        protected abstract IList<IChromosome> ScopedSelectParentPopulation(IEvolutionContext ctx, ISelection selection);

        protected abstract IList<IChromosome> ScopedMatchParentsAndCross(IEvolutionContext ctx, ICrossover crossover, float crossoverProbability, IList<IChromosome> parents);

        protected abstract void ScopedMutateChromosome(IEvolutionContext ctx, IMutation mutation, float mutationProbability, IList<IChromosome> offSprings);

        protected abstract IList<IChromosome> ScopedReinsert(IEvolutionContext ctx, IReinsertion reinsertion, IList<IChromosome> offspring, IList<IChromosome> parents);
    }
}
