#nullable disable

using System.ComponentModel;

namespace MetaGeneticSharp
{
    /// <summary>
    /// Applies distinct metaheuristics depending on the generation number: cycles
    /// through the phases, each lasting its phase size in generations.
    /// </summary>
    [DisplayName("Generation")]
    public class GenerationMetaHeuristic : SizeBasedMetaHeuristic
    {
        public GenerationMetaHeuristic()
        {
            Init();
        }

        public GenerationMetaHeuristic(int phaseDuration, params IMetaHeuristic[] phaseHeuristics) : base(phaseDuration, phaseHeuristics)
        {
            Init();
        }

        private void Init()
        {
            // The PR wires an ExpressionMetaHeuristicParameter here; the expression
            // variants are Phase 3 material and the plain delegate parameter has the
            // same runtime semantics.
            DynamicParameter = new MetaHeuristicParameter<int>
            {
                Scope = ParamScope.Generation | ParamScope.MetaHeuristic,
                Generator = (h, ctx) => GetGenerationPhase(ctx)
            };
        }

        private int GetGenerationPhase(IEvolutionContext ctx)
        {
            // Generation index is 1-based
            return (ctx.Population.GenerationsNumber - 1) % PhaseSizes.TotalPhaseSize;
        }
    }
}
