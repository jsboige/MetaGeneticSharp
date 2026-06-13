#nullable disable
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using GeneticSharp;

namespace MetaGeneticSharp
{
    /// <summary>
    /// The geometric crossover yields new genes by applying geometric operators on the parent
    /// gene values. The default operator converts genes to doubles, computes the middle between
    /// gene values, and converts back to the target type.
    /// Geometric operators can be gene-based (Linear, same for all genes) or multidimensional (General).
    /// Ported from GeneticSharp.Domain.Crossovers.Geometric (PR giacomelli/GeneticSharp#87).
    /// </summary>
    /// <typeparam name="TValue">The gene-value type (typically a .NET value type).</typeparam>
    [DisplayName("Geometric")]
    public class GeometricCrossover<TValue> : CrossoverBase
    {
        private static TValue GetCentroid(int geneIndex, IList<TValue> geneValues)
        {
            return (geneValues.Sum(val => val.To<double>()) / geneValues.Count).To<TValue>();
        }

        private static readonly Func<int, IList<TValue>, TValue> _defaultGeometricOperator = GetCentroid;

        /// <summary>Builds an unordered geometric crossover with 2 parents.</summary>
        public GeometricCrossover() : this(false) { }

        /// <summary>Builds a geometric crossover with the given ordering flag, 2 parents, no twin.</summary>
        public GeometricCrossover(bool ordered) : this(ordered, 2, false) { }

        /// <summary>Builds a geometric crossover with the given ordering flag, parent count and twin flag.</summary>
        public GeometricCrossover(bool ordered, int parentNb, bool generateTwin) : base(parentNb, generateTwin ? 2 : 1)
        {
            IsOrdered = ordered;
            LinearGeometricOperator = _defaultGeometricOperator;
        }

        /// <summary>Builds a geometric crossover with a custom linear geometric operator.</summary>
        public GeometricCrossover(bool ordered, int parentNb, Func<int, IList<TValue>, TValue> linearGeometricOperator, bool generateTwin = false) : this(ordered, parentNb, generateTwin)
        {
            LinearGeometricOperator = linearGeometricOperator;
        }

        /// <summary>A function to compute a child gene value from same-index parent gene values.</summary>
        public Func<int, IList<TValue>, TValue> LinearGeometricOperator { get; set; }

        /// <summary>A function to compute child gene values from all parent gene values.</summary>
        public Func<IList<IList<TValue>>, IList<TValue>> GeneralGeometricOperator { get; set; }

        /// <summary>
        /// A geometry embedding can introduce a transformation between chromosome values and
        /// metric space before and after the geometric operator is computed.
        /// </summary>
        public IGeometryEmbedding<TValue> GeometryEmbedding { get; set; }

        /// <summary>
        /// Applies the geometric operator to parents for a single offspring, and optionally
        /// generates the symmetrical twin when the children count is set to 2.
        /// </summary>
        protected override IList<IChromosome> PerformCross(IList<IChromosome> parents)
        {
            if (GeometryEmbedding == null)
            {
                GeometryEmbedding = new OrderedEmbedding<TValue> { IsOrdered = IsOrdered };
            }

            var toReturn = new List<IChromosome>(ChildrenNumber);
            var firstChild = CreateOffspring(parents);
            toReturn.Add(firstChild);
            if (ChildrenNumber == 2)
            {
                parents = parents.Reverse().ToList();
                var twinChild = CreateOffspring(parents);
                toReturn.Add(twinChild);
            }
            return toReturn;
        }

        /// <summary>
        /// Creates a single offspring by applying a geometric operator to parent individuals.
        /// It optionally applies an embedding interface between metric space and genome, and
        /// applies either a general operator or a linear gene-wise operator.
        /// </summary>
        public IChromosome CreateOffspring(IList<IChromosome> parents)
        {
            var geometricParents = GeometryEmbedding.MapToGeometry(parents);
            IList<TValue> geometricChild;

            if (GeneralGeometricOperator != null)
            {
                geometricChild = GeneralGeometricOperator(geometricParents);
                return GeometryEmbedding.MapFromGeometry(parents, geometricChild);
            }

            if (LinearGeometricOperator == null)
            {
                throw new InvalidOperationException("GeometricCrossover has not geometric operator defined");
            }

            var nbValues = geometricParents[0].Count;
            geometricChild = new List<TValue>(nbValues);
            for (int i = 0; i < nbValues; i++)
            {
                var inputs = geometricParents.Select(p => p[i]).ToArray();
                var newGeneValue = LinearGeometricOperator(i, inputs);
                geometricChild.Add(newGeneValue);
            }

            return GeometryEmbedding.MapFromGeometry(parents, geometricChild);
        }
    }
}
