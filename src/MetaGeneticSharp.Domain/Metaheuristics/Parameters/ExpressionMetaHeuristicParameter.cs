#nullable disable

using System;
using System.Linq.Expressions;

namespace MetaGeneticSharp
{
    /// <summary>
    /// The Expression Metaheuristic parameter extends the default delegate-based parameter class
    /// with supporting Lambda Expression fusion: instead of each parameter generating a value
    /// independently, several parameters can be composed into a single expression tree that is
    /// compiled once per scope. Ported from the PR's
    /// GeneticSharp.Domain.Metaheuristics.Parameters.ExpressionMetaHeuristicParameter.
    /// </summary>
    /// <typeparam name="TParamType"></typeparam>
    public class ExpressionMetaHeuristicParameter<TParamType> : MetaHeuristicParameter<TParamType>,
        IExpressionGeneratorParameter
    {
        public override ParameterGenerator<TParamType> GetGenerator(IEvolutionContext ctx)
        {
            if (Generator == null)
            {
                Expression<ParameterGenerator<TParamType>> expr = GetDynamicGenerator(ctx);
                try
                {
                    Generator = expr.Compile();
                }
                catch (Exception e)
                {
                    throw new InvalidOperationException($"could not compile expression {expr}", e);
                }
            }

            return base.GetGenerator(ctx);
        }

        public virtual Expression<ParameterGenerator<TParamType>> GetDynamicGenerator(IEvolutionContext ctx)
        {
            return DynamicGenerator;
        }

        /// <summary>
        /// The inline lambda generating this parameter's value, possibly referencing other named
        /// parameters (reduced away by <see cref="ParameterReplacer"/> in the WithArgs variants).
        /// </summary>
        public Expression<ParameterGenerator<TParamType>> DynamicGenerator { get; set; }

        public LambdaExpression GetExpression(IEvolutionContext evolutionContext, string paramName)
        {
            if (Scope == ParamScope.None)
            {
                return GetDynamicGenerator(evolutionContext);
            }

            // When cached, wrap the Get call itself in an expression so the cache lookup happens
            // at evaluation time rather than being baked into the compiled tree.
            Expression<ParameterGenerator<TParamType>> cachedExpression = (h, ctx) => Get(h, ctx, paramName);
            return cachedExpression;
        }
    }

    /// <summary>
    /// Base for the multi-arg variants: reduces the WithArgs lambda (which references extra named
    /// parameters) into a closed expression tree via <see cref="ParameterReplacer"/>.
    /// </summary>
    public abstract class ExpressionMetaHeuristicParameterWithArgs<TParamType> : ExpressionMetaHeuristicParameter<TParamType>
    {
        public override Expression<ParameterGenerator<TParamType>> GetDynamicGenerator(IEvolutionContext ctx)
        {
            if (DynamicGenerator == null)
            {
                var expWithArgs = GetExpressionWithArgs();
                DynamicGenerator = ParameterReplacer.ReduceLambdaParameterGenerator<TParamType>(expWithArgs, ctx);
            }
            return base.GetDynamicGenerator(ctx);
        }

        protected abstract LambdaExpression GetExpressionWithArgs();
    }

    public class ExpressionMetaHeuristicParameter<TParamType, TArg1> : ExpressionMetaHeuristicParameterWithArgs<TParamType>
    {
        protected override LambdaExpression GetExpressionWithArgs()
        {
            return DynamicGeneratorWithArg;
        }

        public Expression<ParameterGenerator<TParamType, TArg1>> DynamicGeneratorWithArg { get; set; }
    }

    public class ExpressionMetaHeuristicParameter<TParamType, TArg1, TArg2> : ExpressionMetaHeuristicParameterWithArgs<TParamType>
    {
        protected override LambdaExpression GetExpressionWithArgs()
        {
            return DynamicGeneratorWithArgs;
        }

        public Expression<ParameterGenerator<TParamType, TArg1, TArg2>> DynamicGeneratorWithArgs { get; set; }
    }

    public class ExpressionMetaHeuristicParameter<TParamType, TArg1, TArg2, TArg3> : ExpressionMetaHeuristicParameterWithArgs<TParamType>
    {
        protected override LambdaExpression GetExpressionWithArgs()
        {
            return DynamicGeneratorWithArgs;
        }

        public Expression<ParameterGenerator<TParamType, TArg1, TArg2, TArg3>> DynamicGeneratorWithArgs { get; set; }
    }
}
