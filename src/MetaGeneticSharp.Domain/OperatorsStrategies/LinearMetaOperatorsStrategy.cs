#nullable disable

using GeneticSharp;

namespace MetaGeneticSharp
{
    /// <summary>
    /// Sequential operator strategy: individuals are processed in index order on the
    /// calling thread. Deterministic given a deterministic randomization provider.
    /// </summary>
    public class LinearMetaOperatorsStrategy : IMetaOperatorsStrategy
    {
        public IList<IChromosome> Cross(IMetaHeuristic metaHeuristic, IEvolutionContext ctx, ICrossover crossover, float crossoverProbability, IList<IChromosome> parents)
        {
            var offspring = new List<IChromosome>();

            for (int i = 0; i < ctx.Population.MinSize; i += crossover.ParentsNumber)
            {
                var children = metaHeuristic.MatchParentsAndCross(ctx.GetIndividual(i), crossover, crossoverProbability, parents);

                if (children != null)
                {
                    offspring.AddRange(children);
                }
            }

            return offspring;
        }

        public void Mutate(IMetaHeuristic metaHeuristic, IEvolutionContext ctx, IMutation mutation, float mutationProbability, IList<IChromosome> chromosomes)
        {
            for (int i = 0; i < chromosomes.Count; i++)
            {
                metaHeuristic.MutateChromosome(ctx.GetIndividual(i), mutation, mutationProbability, chromosomes);
            }
        }
    }
}
