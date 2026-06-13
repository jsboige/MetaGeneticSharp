#nullable disable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;

namespace MetaGeneticSharp
{
    /// <summary>
    /// Expression-tree composition helpers. Ported from the PR's
    /// GeneticSharp.Infrastructure.Framework.Commons.LambdaExpressionHelper, itself imported
    /// from https://github.com/jarekczek/LambdaExpressionHelper. Lives here (not in upstream)
    /// because upstream 3.1.4 has no such helpers and the Phase 3 parameter system needs to
    /// fuse inline lambdas into a single expression tree.
    /// </summary>
    public static class LambdaExpressionHelper
    {
        private class ReplaceParameterVisitor : ExpressionVisitor
        {
            protected Expression searchedExpr;
            protected Expression replaceExpr;

            public void PrepareReplace(ParameterExpression src, Expression dst)
            {
                searchedExpr = src;
                replaceExpr = dst;
            }

            protected override Expression VisitParameter(ParameterExpression node)
            {
                if (node == searchedExpr)
                    return replaceExpr;
                return base.VisitParameter(node);
            }
        }

        private class ParameterNameUnifyingVisitor : ExpressionVisitor
        {
            private Dictionary<string, ParameterExpression> dict;

            public Expression Process(LambdaExpression node)
            {
                dict = new Dictionary<string, ParameterExpression>();
                Expression rv = base.Visit(node);
                dict = null;
                return rv;
            }

            protected override Expression VisitParameter(ParameterExpression node)
            {
                if (dict.TryGetValue(node.Name, out var replPar))
                    return replPar;
                dict[node.Name] = node;
                return base.VisitParameter(node);
            }

            public override Expression Visit(Expression node)
            {
                if (dict == null)
                    throw new InvalidOperationException("Use Process method instead.");
                return base.Visit(node);
            }
        }

        /// <summary>
        /// Processes the expression to make parameters identical if they have the same name,
        /// even if they come from different scopes. The scope of the first found parameter is used.
        /// </summary>
        public static LambdaExpression UnifyParametersByName(this LambdaExpression expr)
        {
            var vis = new ParameterNameUnifyingVisitor();
            return (LambdaExpression)vis.Process(expr);
        }

        /// <summary>
        /// Replaces a parameter with name <paramref name="parName"/> with the given expression.
        /// Used to simplify syntax of complex expressions, and compose expressions from reusable
        /// inline lambdas.
        /// </summary>
        public static LambdaExpression ReplaceParameter(this LambdaExpression expr,
            string parName, Expression replacementExpr)
        {
            try
            {
                var parToRepl = expr.Parameters.First(p => p.Name.Equals(parName));
                var newPars = expr.Parameters.Where(p => !p.Name.Equals(parName)).ToArray();
                var vis = new ReplaceParameterVisitor();
                vis.PrepareReplace(parToRepl, replacementExpr);
                var newExprBody = vis.Visit(expr.Body);
                return Expression.Lambda(newExprBody, newPars).UnifyParametersByName();
            }
            catch (Exception e)
            {
                throw new ApplicationException($"Expression {expr} couldn't have parameter {parName} replaced with {replacementExpr}", e);
            }
        }
    }
}
