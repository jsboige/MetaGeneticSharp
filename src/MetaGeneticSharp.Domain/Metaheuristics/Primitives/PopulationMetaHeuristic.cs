#nullable disable

using System.ComponentModel;

namespace MetaGeneticSharp
{
    /// <summary>
    /// Applies distinct metaheuristics depending on the individual index: divides the
    /// individuals into contiguous phase sets proportional to the phase sizes.
    /// </summary>
    [DisplayName("Population")]
    public class PopulationMetaHeuristic : SizeBasedMetaHeuristic
    {
        public PopulationMetaHeuristic()
        {
            Init();
        }

        public PopulationMetaHeuristic(int groupSize, params IMetaHeuristic[] phaseHeuristics) : base(groupSize, phaseHeuristics)
        {
            Init();
        }

        private void Init()
        {
            // The PR caches this parameter with ParamScope.Generation, which masks the
            // individual out of the cache key: every individual of a generation would
            // reuse the first-computed index, pinning the whole population to one phase.
            // The generator is a trivial read, so no caching at all is the correct scope.
            DynamicParameter = new MetaHeuristicParameter<int>
            {
                Scope = ParamScope.None,
                Generator = (h, ctx) => ctx.LocalIndex
            };
        }
    }
}
