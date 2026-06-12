#nullable disable

using System.Collections.Concurrent;
using GeneticSharp;

namespace MetaGeneticSharp
{
    /// <summary>
    /// Parallel operator strategy (Task Parallel Library). Offspring are collected per
    /// parent index and re-ordered before returning, so the resulting offspring list is
    /// index-stable regardless of thread scheduling (this fixes the non-deterministic
    /// ordering of GeneticSharp's TplOperatorsStrategy).
    /// </summary>
    public class TplMetaOperatorsStrategy : IMetaOperatorsStrategy
    {
        public IList<IChromosome> Cross(IMetaHeuristic metaHeuristic, IEvolutionContext ctx, ICrossover crossover, float crossoverProbability, IList<IChromosome> parents)
        {
            var offspring = new ConcurrentDictionary<int, IList<IChromosome>>();

            Parallel.ForEach(
                Enumerable.Range(0, ctx.Population.MinSize / crossover.ParentsNumber).Select(i => i * crossover.ParentsNumber),
                i =>
                {
                    var children = metaHeuristic.MatchParentsAndCross(ctx.GetIndividual(i), crossover, crossoverProbability, parents);
                    offspring[i] = children;
                });

            return offspring
                .OrderBy(pair => pair.Key)
                .Where(pair => pair.Value != null)
                .SelectMany(pair => pair.Value)
                .ToList();
        }

        public void Mutate(IMetaHeuristic metaHeuristic, IEvolutionContext ctx, IMutation mutation, float mutationProbability, IList<IChromosome> chromosomes)
        {
            Parallel.ForEach(
                Enumerable.Range(0, chromosomes.Count),
                i => metaHeuristic.MutateChromosome(ctx.GetIndividual(i), mutation, mutationProbability, chromosomes));
        }
    }
}
