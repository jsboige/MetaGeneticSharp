#nullable disable

using GeneticSharp;

namespace MetaGeneticSharp
{
    /// <summary>
    /// A population wrapping a subset (or projection) of a parent population's individuals,
    /// used to apply genetic operators to islands or Eukaryote sub-chromosomes.
    /// Derives from <see cref="MetaPopulation"/> (the PR derives from its patched
    /// Population) so generation order stays stable and the parameter store is available.
    /// </summary>
    public class SubPopulation : MetaPopulation
    {
        public IPopulation ParentPopulation { get; set; }

        public SubPopulation(IPopulation parentPopulation, IList<IChromosome> subPopulation)
            : base(parentPopulation.MinSize, parentPopulation.MaxSize, subPopulation[0])
        {
            ParentPopulation = parentPopulation;
            CreateNewGeneration(subPopulation);
            GenerationsNumber = parentPopulation.GenerationsNumber;
            MinSize = subPopulation.Count;
            MaxSize = (parentPopulation.MaxSize / parentPopulation.MinSize) * MinSize;
            EndCurrentGeneration();
        }

        private IEvolutionContext _subContext;

        /// <summary>
        /// Returns the cached sub-population context, aligned on the parent context's
        /// individual indices.
        /// </summary>
        public IEvolutionContext GetContext(IEvolutionContext parentContext)
        {
            if (_subContext == null)
            {
                lock (this)
                {
                    if (_subContext == null)
                    {
                        _subContext = new SubPopulationContext(parentContext, this);
                    }
                }
            }

            var toReturn = _subContext;
            if (parentContext.OriginalIndex != toReturn.OriginalIndex)
            {
                toReturn = toReturn.GetIndividual(parentContext.OriginalIndex);
            }
            if (parentContext.LocalIndex != toReturn.LocalIndex)
            {
                toReturn = toReturn.GetLocal(parentContext.LocalIndex);
            }

            return toReturn;
        }
    }
}
