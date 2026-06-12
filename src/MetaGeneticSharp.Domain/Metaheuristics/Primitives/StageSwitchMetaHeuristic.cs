#nullable disable

using System.ComponentModel;

namespace MetaGeneticSharp
{
    /// <summary>
    /// Dispatches each evolution stage to a distinct metaheuristic.
    /// </summary>
    [DisplayName("StageSwitch")]
    public class StageSwitchMetaHeuristic : SwitchMetaHeuristic<EvolutionStage>
    {
        public StageSwitchMetaHeuristic()
        {
            // The PR caches this parameter with ParamScope.Generation, which masks the
            // stage out of the cache key: the stage computed first would be reused for
            // every stage of the generation, freezing the switch. The generator is a
            // trivial read, so no caching at all is the correct scope.
            DynamicParameter = new MetaHeuristicParameter<EvolutionStage>
            {
                Scope = ParamScope.None,
                Generator = (h, ctx) => ctx.CurrentStage
            };
        }
    }
}
