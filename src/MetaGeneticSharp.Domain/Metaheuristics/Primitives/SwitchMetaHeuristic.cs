#nullable disable

using System.ComponentModel;
using GeneticSharp;

namespace MetaGeneticSharp
{
    /// <summary>
    /// A two-branch switch keyed on a boolean dynamic parameter.
    /// </summary>
    [DisplayName("IfElse")]
    public class IfElseMetaHeuristic : SwitchMetaHeuristic<bool>
    {
    }

    /// <summary>
    /// Dispatches each stage to the phase heuristic whose key matches the value of
    /// <see cref="DynamicParameter"/>, evaluated against the current context.
    /// </summary>
    [DisplayName("Switch")]
    public class SwitchMetaHeuristic<TIndex> : PhaseMetaHeuristicBase<TIndex>
    {
        private const string ParamName = "phaseIndexGenerator";

        public IMetaHeuristicParameterGenerator<TIndex> DynamicParameter { get; set; }

        public SwitchMetaHeuristic()
        {
        }

        public SwitchMetaHeuristic(IMetaHeuristic subMetaHeuristic) : base(subMetaHeuristic)
        {
        }

        protected override IList<IChromosome> ScopedSelectParentPopulation(IEvolutionContext ctx, ISelection selection)
        {
            var phaseItemIdx = DynamicParameter.Get<TIndex>(this, ctx, ParamName);
            var currentHeuristic = GetCurrentHeuristic(phaseItemIdx);
            if (currentHeuristic != null)
            {
                return currentHeuristic.SelectParentPopulation(ctx, selection);
            }

            throw new InvalidOperationException($"No phase heuristic for MetaHeuristic {Guid} and phase index {phaseItemIdx}");
        }

        protected override IList<IChromosome> ScopedMatchParentsAndCross(IEvolutionContext ctx, ICrossover crossover, float crossoverProbability, IList<IChromosome> parents)
        {
            var phaseItemIdx = DynamicParameter.Get<TIndex>(this, ctx, ParamName);
            var currentHeuristic = GetCurrentHeuristic(phaseItemIdx);
            if (currentHeuristic != null)
            {
                return currentHeuristic.MatchParentsAndCross(ctx, crossover, crossoverProbability, parents);
            }

            throw new InvalidOperationException($"No phase heuristic for MetaHeuristic {Guid} and phase index {phaseItemIdx}");
        }

        protected override void ScopedMutateChromosome(IEvolutionContext ctx, IMutation mutation, float mutationProbability, IList<IChromosome> offSprings)
        {
            var phaseItemIdx = DynamicParameter.Get<TIndex>(this, ctx, ParamName);
            var currentHeuristic = GetCurrentHeuristic(phaseItemIdx);
            if (currentHeuristic != null)
            {
                currentHeuristic.MutateChromosome(ctx, mutation, mutationProbability, offSprings);
            }
            else
            {
                throw new InvalidOperationException($"No phase heuristic for MetaHeuristic {Guid} and phase index {phaseItemIdx}");
            }
        }

        protected override IList<IChromosome> ScopedReinsert(IEvolutionContext ctx, IReinsertion reinsertion, IList<IChromosome> offspring, IList<IChromosome> parents)
        {
            var phaseItemIdx = DynamicParameter.Get<TIndex>(this, ctx, ParamName);
            var currentHeuristic = GetCurrentHeuristic(phaseItemIdx);
            if (currentHeuristic != null)
            {
                return currentHeuristic.Reinsert(ctx, reinsertion, offspring, parents);
            }

            throw new InvalidOperationException($"No phase heuristic for MetaHeuristic {Guid} and phase index {phaseItemIdx}");
        }

        protected virtual IMetaHeuristic GetCurrentHeuristic(TIndex phaseItemIndex)
        {
            return PhaseHeuristics.TryGetValue(phaseItemIndex, out var currentHeuristic) ? currentHeuristic : null;
        }
    }
}
