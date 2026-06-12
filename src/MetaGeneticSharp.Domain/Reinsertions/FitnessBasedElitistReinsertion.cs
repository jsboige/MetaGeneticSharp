#nullable disable

using GeneticSharp;

namespace MetaGeneticSharp
{
    /// <summary>
    /// Keeps the best MinSize chromosomes among parents and offspring combined.
    /// This is MetaGeneticSharp's default reinsertion: since the engine never sorts
    /// generations implicitly, elitism must be an explicit reinsertion decision.
    /// </summary>
    public class FitnessBasedElitistReinsertion : ReinsertionBase
    {
        public FitnessBasedElitistReinsertion()
            : base(true, true)
        {
        }

        protected override IList<IChromosome> PerformSelectChromosomes(IPopulation population, IList<IChromosome> offspring, IList<IChromosome> parents)
        {
            return parents
                .Concat(offspring)
                .OrderByDescending(p => p.Fitness)
                .Take(population.MinSize)
                .ToList();
        }
    }
}
