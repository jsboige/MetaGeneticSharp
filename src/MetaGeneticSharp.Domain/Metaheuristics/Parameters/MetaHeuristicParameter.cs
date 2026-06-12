#nullable disable

namespace MetaGeneticSharp
{
    public delegate TParamType ParameterGenerator<out TParamType>(IMetaHeuristic h, IEvolutionContext ctx);
    public delegate TParamType ParameterGenerator<out TParamType, in TArg1>(IMetaHeuristic h, IEvolutionContext ctx, TArg1 arg1);
    public delegate TParamType ParameterGenerator<out TParamType, in TArg1, in TArg2>(IMetaHeuristic h, IEvolutionContext ctx, TArg1 arg1, TArg2 arg2);
    public delegate TParamType ParameterGenerator<out TParamType, in TArg1, in TArg2, in TArg3>(IMetaHeuristic h, IEvolutionContext ctx, TArg1 arg1, TArg2 arg2, TArg3 arg3);

    /// <summary>
    /// A typed, delegate-generated parameter cached in the evolution context under a key
    /// masked according to its <see cref="ParamScope"/>: scope flags that are not set are
    /// zeroed out of the cache key, widening the reuse of the computed value.
    /// The expression-based variants (auto-wired dependencies between parameters) are
    /// ported in Phase 3; this class is the runtime core they build upon.
    /// </summary>
    /// <typeparam name="TParamType">The type of the parameter value.</typeparam>
    public class MetaHeuristicParameter<TParamType> : NamedEntity, IMetaHeuristicParameterGenerator<TParamType>
    {
        public ParamScope Scope { get; set; }

        /// <summary>
        /// The function that generates the parameter value from the context.
        /// </summary>
        public ParameterGenerator<TParamType> Generator { get; set; }

        /// <summary>
        /// Typed convenience overload of <see cref="Get{TItemType}"/>.
        /// </summary>
        public TParamType Get(IMetaHeuristic h, IEvolutionContext ctx, string paramName)
        {
            return Get<TParamType>(h, ctx, paramName);
        }

        public TItemType Get<TItemType>(IMetaHeuristic h, IEvolutionContext ctx, string paramName)
        {
            if (Scope == ParamScope.None)
            {
                return (TItemType)ComputeParameter(h, ctx);
            }

            var maskedTuple = (paramName, ctx.Population?.GenerationsNumber ?? 0, ctx.CurrentStage, h, ctx.OriginalIndex);
            GetScopeMask(ref maskedTuple);

            return (TItemType)ctx.GetOrAdd(maskedTuple, () => ComputeParameter(h, ctx));
        }

        private object ComputeParameter(IMetaHeuristic h, IEvolutionContext ctx)
        {
            return GetGenerator(ctx)(h, ctx);
        }

        public virtual ParameterGenerator<TParamType> GetGenerator(IEvolutionContext ctx)
        {
            return Generator;
        }

        private void GetScopeMask(ref (string key, int generation, EvolutionStage stage, IMetaHeuristic heuristic, int individual) input)
        {
            if ((Scope & ParamScope.Generation) != ParamScope.Generation)
            {
                input.generation = 0;
            }
            if ((Scope & ParamScope.Stage) != ParamScope.Stage)
            {
                input.stage = EvolutionStage.All;
            }
            if ((Scope & ParamScope.MetaHeuristic) != ParamScope.MetaHeuristic)
            {
                input.heuristic = null;
            }
            if ((Scope & ParamScope.Individual) != ParamScope.Individual)
            {
                input.individual = 0;
            }
        }
    }
}
