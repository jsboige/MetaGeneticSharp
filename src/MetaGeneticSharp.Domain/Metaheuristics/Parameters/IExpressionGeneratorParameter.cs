#nullable disable

using System.Linq.Expressions;

namespace MetaGeneticSharp
{
    /// <summary>
    /// A parameter that can contribute its value as an inline lambda expression, so several
    /// parameters can be fused into one expression tree (compiled once) rather than each
    /// generating a value independently at runtime. Used by <see cref="ParameterReplacer"/>
    /// to wire parameters that depend on other parameters.
    /// </summary>
    public interface IExpressionGeneratorParameter : IMetaHeuristicParameter
    {
        LambdaExpression GetExpression(IEvolutionContext ctx, string paramName);
    }
}
