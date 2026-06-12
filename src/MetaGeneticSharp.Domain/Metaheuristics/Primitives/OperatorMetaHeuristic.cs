#nullable disable

namespace MetaGeneticSharp
{
    /// <summary>
    /// Base class for metaheuristics substituting a specific genetic operator for the
    /// one the engine was configured with: either a static instance, or a dynamically
    /// generated one (cached back as static when the parameter scope is Constant).
    /// </summary>
    public abstract class OperatorMetaHeuristic<TOperator> : ContainerMetaHeuristic
    {
        public IMetaHeuristicParameterGenerator<TOperator> DynamicParameter { get; set; }

        public TOperator StaticOperator { get; set; }

        protected TOperator GetOperator(IEvolutionContext ctx)
        {
            if (StaticOperator != null)
            {
                return StaticOperator;
            }

            var toReturn = DynamicParameter.GetGenerator(ctx)(this, ctx);
            if (DynamicParameter.Scope == ParamScope.Constant)
            {
                StaticOperator = toReturn;
            }

            return toReturn;
        }
    }
}
