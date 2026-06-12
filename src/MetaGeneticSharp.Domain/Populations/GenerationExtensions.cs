#nullable disable

using GeneticSharp;

namespace MetaGeneticSharp
{
    /// <summary>
    /// Best/worst lookups over a generation's chromosomes. In PR #87 these were added
    /// to the Generation class itself (trunk change); the autonomous engine keeps
    /// upstream Generation untouched and provides them as extensions instead.
    /// The PR used a lazy partial sort (LazyOrderBy); a plain ordering is ported first,
    /// the optimization can be reintroduced with the benchmarks of Phase 5.
    /// </summary>
    public static class GenerationExtensions
    {
        public static IEnumerable<IChromosome> GetBestChromosomes(this Generation generation, int nbChromosomes)
        {
            return generation.Chromosomes.OrderByDescending(c => c.Fitness ?? 0).Take(nbChromosomes);
        }

        public static IEnumerable<IChromosome> GetWorstChromosomes(this Generation generation, int nbChromosomes)
        {
            return generation.Chromosomes.OrderBy(c => c.Fitness ?? 0).Take(nbChromosomes);
        }
    }
}
