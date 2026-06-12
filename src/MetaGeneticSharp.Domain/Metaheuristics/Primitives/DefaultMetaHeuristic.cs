#nullable disable

using System.ComponentModel;
using GeneticSharp;

namespace MetaGeneticSharp
{
    /// <summary>
    /// Reproduces the standard GA behavior as a metaheuristic: regular selection,
    /// adjacent-parents crossover with probability test, per-index mutation, regular
    /// reinsertion. This is the default leaf of every metaheuristic composition.
    /// </summary>
    [DisplayName("Default")]
    public class DefaultMetaHeuristic : ScopedMetaHeuristic
    {
        public DefaultMetaHeuristic()
            : base(new NoOpMetaHeuristic())
        {
        }

        protected override IList<IChromosome> ScopedSelectParentPopulation(IEvolutionContext ctx, ISelection selection)
        {
            return selection.SelectChromosomes(ctx.Population.MinSize, ctx.Population.CurrentGeneration);
        }

        protected override IList<IChromosome> ScopedMatchParentsAndCross(IEvolutionContext ctx, ICrossover crossover, float crossoverProbability, IList<IChromosome> parents)
        {
            // Adjacent matching only; the configurable MatchingTechnique system
            // (randomized/best/child-generation pairings) is ported in Phase 2.
            if (parents.Count - ctx.LocalIndex >= crossover.ParentsNumber
                && RandomizationProvider.Current.GetDouble() <= crossoverProbability)
            {
                var selectedParents = new List<IChromosome>(crossover.ParentsNumber);

                for (int i = 0; i < crossover.ParentsNumber; i++)
                {
                    selectedParents.Add(parents[ctx.LocalIndex + i]);
                }

                return crossover.Cross(selectedParents);
            }

            return new List<IChromosome>();
        }

        protected override void ScopedMutateChromosome(IEvolutionContext ctx, IMutation mutation, float mutationProbability, IList<IChromosome> offSprings)
        {
            mutation.Mutate(offSprings[ctx.LocalIndex], mutationProbability);
        }

        protected override IList<IChromosome> ScopedReinsert(IEvolutionContext ctx, IReinsertion reinsertion, IList<IChromosome> offspring, IList<IChromosome> parents)
        {
            return reinsertion.SelectChromosomes(ctx.Population, offspring, parents);
        }
    }
}
