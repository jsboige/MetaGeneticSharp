#nullable disable

using GeneticSharp;

namespace MetaGeneticSharp
{
    /// <summary>
    /// A context targeting one individual: <see cref="OriginalIndex"/> addresses it in the
    /// population-wide collection, <see cref="LocalIndex"/> in the collection currently
    /// being processed (parents, offspring). Both usually coincide.
    /// </summary>
    public class IndividualContext : SubEvolutionContext
    {
        private IList<IChromosome> _selectedParents;

        public IndividualContext(IEvolutionContext populationContext, int originalIndex, int localIndex)
            : base(populationContext)
        {
            OriginalIndex = originalIndex;
            LocalIndex = localIndex;
        }

        public override int OriginalIndex { get; }

        public override int LocalIndex { get; }

        /// <summary>
        /// Selected parents can be shadowed at the individual level (e.g. when a
        /// metaheuristic re-matches parents for a specific crossover) without
        /// affecting the population-level selection.
        /// </summary>
        public override IList<IChromosome> SelectedParents
        {
            get => _selectedParents ?? base.SelectedParents;
            set => _selectedParents = value;
        }

        public override IEvolutionContext GetIndividual(int index)
        {
            return index == OriginalIndex ? this : PopulationContext.GetIndividual(index);
        }
    }
}
