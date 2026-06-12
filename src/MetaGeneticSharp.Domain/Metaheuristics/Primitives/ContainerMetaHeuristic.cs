#nullable disable

using System.ComponentModel;
using GeneticSharp;

namespace MetaGeneticSharp
{
    /// <summary>
    /// A pass-through container: every stage delegates to the sub-metaheuristic,
    /// with probability handling inherited from <see cref="CustomProbabilityMetaHeuristic"/>.
    /// Serves as the base class for metaheuristics that refine only some stages.
    /// </summary>
    [DisplayName("Container")]
    public class ContainerMetaHeuristic : CustomProbabilityMetaHeuristic, IContainerMetaHeuristic
    {
        public ContainerMetaHeuristic()
            : this(new DefaultMetaHeuristic())
        {
        }

        public ContainerMetaHeuristic(IMetaHeuristic subMetaHeuristic)
        {
            SubMetaHeuristic = subMetaHeuristic;
        }

        public IMetaHeuristic SubMetaHeuristic { get; set; }

        public override IList<IChromosome> SelectParentPopulation(IEvolutionContext ctx, ISelection selection)
        {
            return SubMetaHeuristic.SelectParentPopulation(ctx, selection);
        }

        public override IList<IChromosome> Reinsert(IEvolutionContext ctx, IReinsertion reinsertion, IList<IChromosome> offspring, IList<IChromosome> parents)
        {
            return SubMetaHeuristic.Reinsert(ctx, reinsertion, offspring, parents);
        }

        protected override IList<IChromosome> DoMatchParentsAndCross(IEvolutionContext ctx, ICrossover crossover, float crossoverProbability, IList<IChromosome> parents)
        {
            return SubMetaHeuristic.MatchParentsAndCross(ctx, crossover, crossoverProbability, parents);
        }

        protected override void DoMutateChromosome(IEvolutionContext ctx, IMutation mutation, float mutationProbability, IList<IChromosome> offSprings)
        {
            SubMetaHeuristic.MutateChromosome(ctx, mutation, mutationProbability, offSprings);
        }

        public override void RegisterParameters(IEvolutionContext ctx)
        {
            base.RegisterParameters(ctx);
            ((MetaHeuristicBase)SubMetaHeuristic).RegisterParameters(ctx);
        }
    }
}
