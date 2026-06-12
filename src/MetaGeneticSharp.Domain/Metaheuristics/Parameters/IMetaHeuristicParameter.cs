#nullable disable

namespace MetaGeneticSharp
{
    /// <summary>
    /// A named parameter usable by metaheuristics, resolved against an evolution
    /// context with a caching scope. The full expression-based parameter system
    /// is ported in Phase 3 (see ROADMAP.md); this interface is the stable contract.
    /// </summary>
    public interface IMetaHeuristicParameter
    {
        ParamScope Scope { get; set; }

        TItemType Get<TItemType>(IMetaHeuristic h, IEvolutionContext ctx, string paramName);
    }
}
