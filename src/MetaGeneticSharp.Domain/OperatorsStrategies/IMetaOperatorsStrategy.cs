#nullable disable

using GeneticSharp;

namespace MetaGeneticSharp
{
    /// <summary>
    /// Drives how the per-individual crossover and mutation calls of a metaheuristic
    /// are iterated (sequentially or in parallel). The metaheuristic decides *what*
    /// happens for each individual; the strategy decides *how* the individuals are walked.
    /// </summary>
    public interface IMetaOperatorsStrategy
    {
        IList<IChromosome> Cross(IMetaHeuristic metaHeuristic, IEvolutionContext ctx, ICrossover crossover, float crossoverProbability, IList<IChromosome> parents);

        void Mutate(IMetaHeuristic metaHeuristic, IEvolutionContext ctx, IMutation mutation, float mutationProbability, IList<IChromosome> chromosomes);
    }
}
