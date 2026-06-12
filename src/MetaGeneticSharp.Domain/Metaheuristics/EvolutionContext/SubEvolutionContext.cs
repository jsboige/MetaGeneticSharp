#nullable disable

using GeneticSharp;

namespace MetaGeneticSharp
{
    /// <summary>
    /// Base class for contexts deriving from a population-level context. All members
    /// delegate to the parent context; subclasses override the few members they refine
    /// (typically the individual indices).
    /// Note: the parameter store and definitions always live in the population context,
    /// so values cached from a sub-context are shared population-wide (individual
    /// scoping refinements are revisited in Phase 3, see ROADMAP.md).
    /// </summary>
    public abstract class SubEvolutionContext : IEvolutionContext
    {
        protected SubEvolutionContext(IEvolutionContext populationContext)
        {
            PopulationContext = populationContext ?? throw new ArgumentNullException(nameof(populationContext));
        }

        public IEvolutionContext PopulationContext { get; }

        public IGeneticAlgorithm GeneticAlgorithm
        {
            get => PopulationContext.GeneticAlgorithm;
            set => PopulationContext.GeneticAlgorithm = value;
        }

        public virtual IPopulation Population
        {
            get => PopulationContext.Population;
            set => PopulationContext.Population = value;
        }

        public virtual int OriginalIndex => PopulationContext.OriginalIndex;

        public virtual int LocalIndex => PopulationContext.LocalIndex;

        public EvolutionStage CurrentStage
        {
            get => PopulationContext.CurrentStage;
            set => PopulationContext.CurrentStage = value;
        }

        public virtual IList<IChromosome> SelectedParents
        {
            get => PopulationContext.SelectedParents;
            set => PopulationContext.SelectedParents = value;
        }

        public virtual IList<IChromosome> GeneratedOffsprings
        {
            get => PopulationContext.GeneratedOffsprings;
            set => PopulationContext.GeneratedOffsprings = value;
        }

        public virtual IEvolutionContext GetIndividual(int index)
        {
            return new IndividualContext(this, index, index);
        }

        public virtual IEvolutionContext GetLocal(int index)
        {
            if (OriginalIndex < 0)
            {
                throw new InvalidOperationException("Cannot create a local context from a population-level context: no original individual index.");
            }

            return new IndividualContext(this, OriginalIndex, index);
        }

        public virtual TItemType GetOrAdd<TItemType>((string key, int generation, EvolutionStage stage, IMetaHeuristic heuristic, int individual) contextKey, Func<TItemType> factory)
        {
            return PopulationContext.GetOrAdd(contextKey, factory);
        }

        public virtual TItemType GetParam<TItemType>(IMetaHeuristic h, string paramName)
        {
            return PopulationContext.GetParam<TItemType>(h, paramName);
        }

        public void RegisterParameter(string paramName, IMetaHeuristicParameter param)
        {
            PopulationContext.RegisterParameter(paramName, param);
        }

        public IMetaHeuristicParameter GetParameterDefinition(string paramName)
        {
            return PopulationContext.GetParameterDefinition(paramName);
        }
    }
}
