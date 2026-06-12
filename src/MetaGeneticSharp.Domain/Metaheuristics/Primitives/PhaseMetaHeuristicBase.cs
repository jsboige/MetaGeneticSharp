#nullable disable

namespace MetaGeneticSharp
{
    /// <summary>
    /// Base class for metaheuristics dispatching to distinct sub-heuristics depending
    /// on a phase state. The phase state can depend on the population (e.g. generation
    /// number), the individual index (distinct sets), or genes (see EukaryoteMetaHeuristic).
    /// </summary>
    public abstract class PhaseMetaHeuristicBase<TIndex> : ScopedMetaHeuristic
    {
        protected PhaseMetaHeuristicBase()
        {
            PhaseHeuristics = new Dictionary<TIndex, IMetaHeuristic>();
        }

        protected PhaseMetaHeuristicBase(IMetaHeuristic subMetaHeuristic) : base(subMetaHeuristic)
        {
            PhaseHeuristics = new Dictionary<TIndex, IMetaHeuristic>();
        }

        public Dictionary<TIndex, IMetaHeuristic> PhaseHeuristics { get; }

        public override void RegisterParameters(IEvolutionContext ctx)
        {
            base.RegisterParameters(ctx);
            foreach (var keyValuePair in PhaseHeuristics)
            {
                ((MetaHeuristicBase)keyValuePair.Value).RegisterParameters(ctx);
            }
        }
    }
}
