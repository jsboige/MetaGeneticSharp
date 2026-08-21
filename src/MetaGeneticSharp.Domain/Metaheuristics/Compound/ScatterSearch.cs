#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using GeneticSharp;

namespace MetaGeneticSharp
{
    /// <summary>
    ///   Scatter Search, expressed as a geometric compound metaheuristic over the same fluent
    ///   grammar as WOA / EO / FBI / DE / BBPSO / PSO / SA. This is Glover's five-method template
    ///   (F. Glover, "A Template for Scatter Search and Path Relinking", 1998; M. Laguna &amp;
    ///   R. Marti, "Scatter Search: Methodology and Implementations in C", 2003) recast onto the
    ///   engine's generation loop: the population plays the reference set, the combination method
    ///   is the geometric crossover itself -- the thesis of the geometric view (Moraglio): a
    ///   crossover whose offspring lie on the segment between the parents IS the path-relinking
    ///   combination of Scatter Search -- and the reference-set update (b1 quality + b2 diversity)
    ///   lives in <see cref="ScatterSearchReinsertion"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Mapping of the five methods.</b> (1) Diversification generation: the engine's random
    /// initial population. (2) Improvement: deferred -- the local-search layer is the generic
    /// memetic axis (#12049 axe 2), not this compound; the template explicitly makes improvement
    /// optional. (3) Reference-set update: <see cref="ScatterSearchReinsertion"/> keeps
    /// <c>QualityFraction</c> of the slots by fitness, the rest by max-min distance. (4) Subset
    /// generation: every individual is paired with a <see cref="MatchingKind.Random"/> reference --
    /// unlike PSO's elite-only (Current, Best) pairing, the random mate spans the whole reference
    /// set, diverse members included. (5) Combination: a uniform point on the geometric segment
    /// between the two references, drawn once per individual per generation.
    /// </para>
    /// <para>
    /// <b>Per-child combination weight.</b> The child lies ON the path between its parents:
    /// <c>x_child = lambda * x_a + (1 - lambda) * x_b</c> with a single
    /// <c>lambda ~ U(0, 1)</c> drawn per individual per generation, carried through the
    /// evolution-context store (<see cref="IEvolutionContext.GetOrAdd{TItemType}"/>) so every gene
    /// of the child shares the same weight -- the point stays on the segment (a per-gene draw
    /// would fill the box spanned by the parents instead, the uniform-geometric-crossover limit).
    /// </para>
    /// <para>
    /// <b>Reference design.</b> The structure follows the DiscreteSS of A. Hernandez &amp;
    /// Y. Gonzalez ("Metaheuristics in C#", Univ. de La Habana, 2012, MIT): diversified pool,
    /// reference set split quality/diversity, combination, and the improvement hook their
    /// <c>Improve</c> method provides (here the memetic axis). Their <c>Combine</c> is a one-point
    /// crossover followed by repair; the geometric operator here replaces it with the
    /// representation-agnostic convex combination.
    /// </para>
    /// </remarks>
    public class ScatterSearch : GeometricMetaHeuristicBase
    {
        private const string LambdaKeyPrefix = "ss.lambda.";

        // A stable key token scoping the per-individual store entries (same sentinel pattern as
        // ParticleSwarmOptimization: the compound itself is not an IMetaHeuristic).
        private static readonly IMetaHeuristic StoreScope = new DefaultMetaHeuristic();

        /// <summary>
        /// The combination operator. <c>geneValues</c> is read as
        /// <c>[reference x_a (Current), reference x_b (Random)]</c>; the combination weight is
        /// shared by every gene of the child (drawn once, carried by the evolution-context store).
        /// </summary>
        public delegate object CombinationOperator(int geneIndex, IEnumerable<object> geneValues, IGeometricConverter geometricConverter, IEvolutionContext ctx);

        /// <summary>
        /// The default fraction of the reference set kept by quality; the complement is kept by
        /// max-min diversity. 0.7 mirrors the b1 &gt; b2 skew of the classic reference sets.
        /// </summary>
        public const double DefaultQualityFraction = 0.7;

        /// <summary>The fraction of reference-set slots kept by quality (forwarded to the reinsertion).</summary>
        public double QualityFraction { get; set; } = DefaultQualityFraction;

        /// <summary>The combination operator. Overridable to express extrapolation or path-step variants.</summary>
        public CombinationOperator CombineOperator { get; set; }

        /// <summary>Wires the default combination operator (an instance method, so it reads the store).</summary>
        public ScatterSearch()
        {
            CombineOperator = DefaultCombineOperator;
        }

        /// <summary>
        /// The convex combination, pure and deterministic (no store access):
        /// <c>lambda * a + (1 - lambda) * b</c>.
        /// </summary>
        public static double CombineGene(double a, double b, double lambda)
        {
            return lambda * a + (1.0 - lambda) * b;
        }

        /// <summary>The default combination: draws the per-child lambda, blends the two references gene-wise.</summary>
        public object DefaultCombineOperator(int geneIndex, IEnumerable<object> geneValues, IGeometricConverter geometricConverter, IEvolutionContext ctx)
        {
            var metricValues = geneValues.Select(value => geometricConverter.GeneToDouble(geneIndex, value)).ToList();
            double xa = metricValues[0]; // The individual's own position (Current).
            double xb = metricValues[1]; // A random reference-set member (Random).

            // One draw per individual per generation: the first gene to run seeds the lambda and
            // every other gene of the same child reads it back.
            int individual = ctx.OriginalIndex;
            int generation = GetCurrentGeneration(ctx);
            double lambda = ctx.GetOrAdd((LambdaKeyPrefix, generation, EvolutionStage.Crossover, StoreScope, individual),
                () => RandomizationProvider.Current.GetDouble());

            return geometricConverter.DoubleToGene(geneIndex, CombineGene(xa, xb, lambda));
        }

        /// <summary>
        /// The generation index keying the per-individual lambda store. Virtual so tests can
        /// control the clock without a live population.
        /// </summary>
        protected virtual int GetCurrentGeneration(IEvolutionContext ctx)
        {
            return ctx.Population?.GenerationsNumber ?? 0;
        }

        /// <inheritdoc />
        public override IReinsertion GetDefaultReinsertion()
        {
            return new ScatterSearchReinsertion(QualityFraction);
        }

        /// <inheritdoc />
        protected override IContainerMetaHeuristic BuildMainHeuristic()
        {
            // The two-parent geometric crossover: geneValues = [x_a (Current), x_b (Random)].
            var combineHeuristic = new CrossoverMetaHeuristic()
                .WithName("reference-set convex combination")
                .WithCrossover(ParamScope.None,
                    (IMetaHeuristic h, IEvolutionContext ctx) => new GeometricCrossover<object>(GeometricConverter.IsOrdered, 2, false)
                        .WithLinearGeometricOperator((geneIndex, geneValues) => CombineOperator(geneIndex, geneValues, GeometricConverter, ctx))
                        .WithGeometryEmbedding(GeometricConverter.GetEmbedding()));

            // The individual's own position plus a random reference-set member, then the
            // convex combination (a point on the segment between the two references).
            return new MatchMetaHeuristic()
                .WithName("Scatter Search", "Glover, F. (1998). A Template for Scatter Search and Path Relinking. Each individual combines with a random reference-set member by a convex combination; the reference set keeps b1 members by quality and the rest by max-min diversity.")
                .WithMatches(MatchingKind.Current, MatchingKind.Random)
                .WithCrossoverMetaHeuristic(combineHeuristic);
        }
    }
}
