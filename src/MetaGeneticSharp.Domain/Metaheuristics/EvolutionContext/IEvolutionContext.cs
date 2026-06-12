#nullable disable

using GeneticSharp;

namespace MetaGeneticSharp
{
    /// <summary>
    /// The ambient state shared by metaheuristics during an evolution: current algorithm,
    /// population, stage, intermediate stage products, and a typed parameter store keyed
    /// by caching scope. Individual-level contexts are obtained via <see cref="GetIndividual"/>.
    /// </summary>
    public interface IEvolutionContext
    {
        IGeneticAlgorithm GeneticAlgorithm { get; set; }

        IPopulation Population { get; set; }

        /// <summary>
        /// Index of the targeted individual in the original (population-wide) addressing,
        /// or -1 for a population-level context.
        /// </summary>
        int OriginalIndex { get; }

        /// <summary>
        /// Index of the targeted individual in the local collection being processed
        /// (e.g. parents or offspring), or -1 for a population-level context.
        /// </summary>
        int LocalIndex { get; }

        EvolutionStage CurrentStage { get; set; }

        IList<IChromosome> SelectedParents { get; set; }

        IList<IChromosome> GeneratedOffsprings { get; set; }

        /// <summary>
        /// Gets an individual-level context targeting the given index (original == local).
        /// </summary>
        IEvolutionContext GetIndividual(int index);

        /// <summary>
        /// Gets an individual-level context keeping the current original index but
        /// targeting a different local index.
        /// </summary>
        IEvolutionContext GetLocal(int index);

        /// <summary>
        /// Gets or computes a value cached under the given composite scope key.
        /// </summary>
        TItemType GetOrAdd<TItemType>((string key, int generation, EvolutionStage stage, IMetaHeuristic heuristic, int individual) contextKey, Func<TItemType> factory);

        /// <summary>
        /// Resolves a registered parameter for the given metaheuristic.
        /// </summary>
        TItemType GetParam<TItemType>(IMetaHeuristic h, string paramName);

        void RegisterParameter(string paramName, IMetaHeuristicParameter param);

        IMetaHeuristicParameter GetParameterDefinition(string paramName);
    }
}
