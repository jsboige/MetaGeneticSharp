#nullable disable

using GeneticSharp;

namespace MetaGeneticSharp
{
    /// <summary>
    /// A sub-population holding a contiguous slice of the parent population's individuals
    /// (full chromosomes, unlike Eukaryote slices), evolved independently by
    /// <see cref="IslandMetaHeuristic"/> with periodic migrations.
    /// </summary>
    public class IslandPopulation : SubPopulation
    {
        public IslandPopulation(IPopulation parentPopulation, IList<IChromosome> subPopulation) : base(parentPopulation, subPopulation)
        {
        }

        /// <summary>
        /// Outbound migration rate toward each island (index-aligned with the island list);
        /// set by the migration mode at each migration generation.
        /// </summary>
        public List<double> MigrationRates { get; set; }
    }
}
