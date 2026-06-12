#nullable disable

using System.ComponentModel;
using GeneticSharp;

namespace MetaGeneticSharp
{
    /// <summary>
    /// A metaheuristic that performs no genetic operation: selection passes the current
    /// generation through, crossover returns the targeted parents unchanged, mutation does
    /// nothing, reinsertion returns the parents. Useful as a neutral leaf in compositions.
    /// </summary>
    [DisplayName("NoOp")]
    public class NoOpMetaHeuristic : MetaHeuristicBase
    {
        public override IList<IChromosome> SelectParentPopulation(IEvolutionContext ctx, ISelection selection)
        {
            return ctx.Population.CurrentGeneration.Chromosomes.Take(ctx.Population.MinSize).ToList();
        }

        public override IList<IChromosome> MatchParentsAndCross(IEvolutionContext ctx, ICrossover crossover, float crossoverProbability, IList<IChromosome> parents)
        {
            return parents.Skip(ctx.LocalIndex).Take(crossover.ChildrenNumber).ToList();
        }

        public override void MutateChromosome(IEvolutionContext ctx, IMutation mutation, float mutationProbability, IList<IChromosome> offSprings)
        {
        }

        public override IList<IChromosome> Reinsert(IEvolutionContext ctx, IReinsertion reinsertion, IList<IChromosome> offspring, IList<IChromosome> parents)
        {
            return parents;
        }
    }
}
