#nullable disable

using GeneticSharp;

namespace MetaGeneticSharp
{
    /// <summary>
    /// Base class for metaheuristics that split the population into sub-populations
    /// (Eukaryote sub-chromosomes, islands), apply one phase heuristic per sub-population,
    /// and recombine the results.
    /// </summary>
    public abstract class SubPopulationMetaHeuristicBase<T> : SizeBasedMetaHeuristic where T : SubPopulation
    {
        protected SubPopulationMetaHeuristicBase()
        {
        }

        protected SubPopulationMetaHeuristicBase(int phaseSize, params IMetaHeuristic[] phaseHeuristics) : base(phaseSize, phaseHeuristics)
        {
        }

        protected SubPopulationMetaHeuristicBase(int phaseSize, int phaseNb, params IMetaHeuristic[] phaseHeuristics) : base(phaseSize, phaseNb, phaseHeuristics)
        {
        }

        protected SubPopulationMetaHeuristicBase(params (int phaseSize, IMetaHeuristic phaseMetaHeuristic)[] phases) : base(phases)
        {
        }

        public ParamScope SubPopulationCachingScope { get; set; } = ParamScope.Generation | ParamScope.MetaHeuristic;

        protected abstract IList<T> GenerateSubPopulations(IMetaHeuristic h, IEvolutionContext c);

        private MetaHeuristicParameter<IList<T>> _dynamicSubPopulationParameter;

        public MetaHeuristicParameter<IList<T>> DynamicSubPopulationParameter
        {
            get
            {
                if (_dynamicSubPopulationParameter == null)
                {
                    _dynamicSubPopulationParameter = new MetaHeuristicParameter<IList<T>>
                    {
                        Scope = SubPopulationCachingScope,
                        Generator = GenerateSubPopulations
                    };
                }
                return _dynamicSubPopulationParameter;
            }
        }

        /// <summary>
        /// Applies the per-phase operator to each sub-population and recombines the
        /// per-position results into complete individuals.
        /// </summary>
        protected IList<IChromosome> PerformSubOperator(IList<T> subPopulations, Func<IMetaHeuristic, T, IList<IChromosome>> subPopulationOperator)
        {
            var resultSubPopulations = new List<IList<IChromosome>>();
            for (var subChromosomeIndex = 0; subChromosomeIndex < subPopulations.Count; subChromosomeIndex++)
            {
                var subPopulation = subPopulations[subChromosomeIndex];
                var subHeuristic = PhaseHeuristics[subChromosomeIndex];
                var subResults = subPopulationOperator(subHeuristic, subPopulation);
                resultSubPopulations.Add(subResults);
            }

            var resultPopulation = EukaryoteChromosome.GetNewIndividuals(resultSubPopulations);
            return resultPopulation;
        }
    }
}
