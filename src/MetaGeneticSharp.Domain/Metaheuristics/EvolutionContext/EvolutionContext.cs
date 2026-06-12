#nullable disable

using System.Collections.Concurrent;
using GeneticSharp;

namespace MetaGeneticSharp
{
    /// <summary>
    /// Population-level evolution context. Holds the scoped parameter cache
    /// (thread-safe, as operators may run under a TPL strategy) and the parameter
    /// definitions registered by the metaheuristic chain.
    /// </summary>
    public class EvolutionContext : IEvolutionContext
    {
        private readonly ConcurrentDictionary<(string key, int generation, EvolutionStage stage, IMetaHeuristic heuristic, int individual), object> _params = new();

        private readonly Dictionary<string, IMetaHeuristicParameter> _paramDefinitions = new();

        public IGeneticAlgorithm GeneticAlgorithm { get; set; }

        public IPopulation Population { get; set; }

        public int OriginalIndex { get; set; } = -1;

        public int LocalIndex { get; set; } = -1;

        public EvolutionStage CurrentStage { get; set; }

        public IList<IChromosome> SelectedParents { get; set; }

        public IList<IChromosome> GeneratedOffsprings { get; set; }

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
            return (TItemType)_params.GetOrAdd(contextKey, _ => factory());
        }

        public virtual TItemType GetParam<TItemType>(IMetaHeuristic h, string paramName)
        {
            return GetParameterDefinition(paramName).Get<TItemType>(h, this, paramName);
        }

        public void RegisterParameter(string paramName, IMetaHeuristicParameter param)
        {
            _paramDefinitions[paramName] = param;
        }

        public IMetaHeuristicParameter GetParameterDefinition(string paramName)
        {
            if (!_paramDefinitions.TryGetValue(paramName, out var definition))
            {
                throw new ArgumentException($"parameter {paramName} not found in MetaHeuristic expression chain", nameof(paramName));
            }

            return definition;
        }
    }
}
