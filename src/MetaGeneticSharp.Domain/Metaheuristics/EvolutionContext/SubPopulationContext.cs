#nullable disable

using System.Collections.Concurrent;
using GeneticSharp;

namespace MetaGeneticSharp
{
    /// <summary>
    /// A context scoped to a <see cref="SubPopulation"/>: population, selected parents,
    /// generated offspring and the parameter cache are all local to the sub-population
    /// instead of delegating to the parent context.
    /// </summary>
    public class SubPopulationContext : SubEvolutionContext
    {
        public override IPopulation Population { get; set; }

        public override IList<IChromosome> SelectedParents { get; set; }

        public override IList<IChromosome> GeneratedOffsprings { get; set; }

        /// <summary>
        /// Sub-population-local parameter cache (the parent population's store is not shared).
        /// </summary>
        public ConcurrentDictionary<(string key, int generation, EvolutionStage stage, IMetaHeuristic heuristic, int individual), object> Params { get; set; }
            = new ConcurrentDictionary<(string, int, EvolutionStage, IMetaHeuristic, int), object>();

        public SubPopulationContext(IEvolutionContext populationContext, IPopulation subPopulation) : base(populationContext)
        {
            Population = subPopulation;
            GeneratedOffsprings = new List<IChromosome>();
        }

        public override TItemType GetOrAdd<TItemType>(
            (string key, int generation, EvolutionStage stage, IMetaHeuristic heuristic, int individual) contextKey,
            Func<TItemType> factory)
        {
            var toReturn = (TItemType)Params.GetOrAdd(contextKey, s => (object)factory());
            return toReturn;
        }
    }
}
