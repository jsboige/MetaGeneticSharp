#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using GeneticSharp;

namespace MetaGeneticSharp
{
    /// <summary>
    ///   Differential Evolution, expressed as a geometric compound metaheuristic over the same
    ///   fluent grammar as WOA / EO / FBI. This is the canonical <c>DE/rand/1/bin</c> scheme of
    ///   Storn &amp; Price (1997): for each target individual <c>x_i</c> three donor individuals
    ///   <c>r1, r2, r3</c> are drawn at random and the mutant <c>v = x_r1 + F * (x_r2 - x_r3)</c>
    ///   is recombined with the target by a binomial crossover at rate <c>CR</c>. The greedy
    ///   "keep the trial only if it is no worse" selection is the inherited
    ///   <see cref="FitnessBasedElitistReinsertion"/> (best-N of parents+offspring), so this class
    ///   does NOT override <see cref="GeometricMetaHeuristicBase.GetDefaultReinsertion"/>.
    ///   Ported alongside the GeneticSharp.Domain.Metaheuristics.Compound family
    ///   (PR giacomelli/GeneticSharp#87).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Why a geometric compound.</b> DE is the textbook "non-metaphorical" continuous
    /// optimiser (Sorensen, 2015): its only operator is the linear difference <c>F * (x_r2 - x_r3)</c>,
    /// which is exactly a geometric (gene-wise, linear) operator on parent gene values. Expressing
    /// it through <see cref="GeometricCrossover{TValue}"/> keeps it on the same axis as WOA/EO/FBI
    /// — same converter, same embedding, same reinsertion contract — so the centre-bias and
    /// benchmark harnesses compare like for like.
    /// </para>
    /// <para>
    /// <b>Donor selection.</b> The four parents fed to the operator are the target plus three
    /// <see cref="MatchingKind.Random"/> matches: <c>geneValues[0]</c> is the target <c>x_i</c>,
    /// <c>[1..3]</c> the donors r1, r2, r3. GeneticSharp's <c>Randomization.GetInts</c> does NOT
    /// draw distinct indices, so r1/r2/r3 are independent random draws (each distinct from the
    /// target via the matcher's reference-skip, but not guaranteed mutually distinct). A degenerate
    /// draw where two donors coincide only shrinks the difference vector for that trial — DE keeps
    /// working — so this matches the common practical DE implementation rather than the strictest
    /// "mutually distinct" statement. Enforcing strict distinctness is left to a future reusable
    /// matcher primitive (it would also serve PSO velocity and CMA-ES mean sampling).
    /// </para>
    /// <para>
    /// <b>Binomial crossover.</b> Each gene independently takes the mutant value with probability
    /// <c>CR</c>, otherwise keeps the target value. The classic "force at least one inherited gene
    /// (j_rand)" guarantee is omitted: at CR = 0.9 and dimension &gt;= 5 the probability of an
    /// all-target trial is negligible, and the gene-wise (linear) operator form keeps the compound
    /// readable and on par with WOA/EO. Swap <see cref="TrialOperator"/> for a general operator to
    /// reintroduce j_rand if a stricter variant is needed.
    /// </para>
    /// </remarks>
    public class DifferentialEvolution : GeometricMetaHeuristicBase
    {
        /// <summary>
        /// Computes a trial gene value from the target and three donor gene values:
        /// mutant = donor1 + F * (donor2 - donor3), kept with probability CR, else the target value.
        /// </summary>
        public delegate object TrialOperator(int geneIndex, IEnumerable<object> geneValues, IGeometricConverter geometricConverter, double scale, double crossoverRate);

        /// <summary>The default scale factor F (Storn &amp; Price recommend 0.4–0.95; 0.5 is the canonical value).</summary>
        public const double DefaultScaleFactor = 0.5;

        /// <summary>The default crossover rate CR (0.9 favours the mutant for most genes, a robust general-purpose setting).</summary>
        public const double DefaultCrossoverRate = 0.9;

        /// <summary>
        /// The default DE/rand/1 trial operator: mutant then binomial crossover with the target.
        /// <c>geneValues</c> is read as <c>[target x_i, donor r1, donor r2, donor r3]</c>.
        /// </summary>
        public static object DefaultTrialOperator(int geneIndex, IEnumerable<object> geneValues, IGeometricConverter geometricConverter, double scale, double crossoverRate)
        {
            var metricValues = geneValues.Select(value => geometricConverter.GeneToDouble(geneIndex, value)).ToList();
            // mutant v = x_r1 + F * (x_r2 - x_r3), then binomial crossover with the target x_i.
            double mutant = metricValues[1] + scale * (metricValues[2] - metricValues[3]);
            double trial = RandomizationProvider.Current.GetDouble() < crossoverRate ? mutant : metricValues[0];
            return geometricConverter.DoubleToGene(geneIndex, trial);
        }

        /// <summary>The scale (mutation) factor F applied to the difference vector. Defaults to <see cref="DefaultScaleFactor"/>.</summary>
        public double ScaleFactor { get; set; } = DefaultScaleFactor;

        /// <summary>The binomial crossover rate CR. Defaults to <see cref="DefaultCrossoverRate"/>.</summary>
        public double CrossoverRate { get; set; } = DefaultCrossoverRate;

        /// <summary>The trial-vector operator (mutation + binomial crossover). Overridable to express DE/best/1, DE/rand/2, jittered F, etc.</summary>
        public TrialOperator DifferentialOperator { get; set; } = DefaultTrialOperator;

        /// <inheritdoc />
        protected override IContainerMetaHeuristic BuildMainHeuristic()
        {
            // Capture the configured constants so the crossover factory closes over stable values.
            double scale = ScaleFactor;
            double crossover = CrossoverRate;

            // The four-parent geometric crossover: geneValues = [target, r1, r2, r3].
            var trialHeuristic = new CrossoverMetaHeuristic()
                .WithName("differential mutation + binomial crossover")
                .WithCrossover(ParamScope.None,
                    (IMetaHeuristic h, IEvolutionContext ctx) => new GeometricCrossover<object>(GeometricConverter.IsOrdered, 4, false)
                        .WithLinearGeometricOperator((geneIndex, geneValues) => DifferentialOperator(geneIndex, geneValues, GeometricConverter, scale, crossover))
                        .WithGeometryEmbedding(GeometricConverter.GetEmbedding()));

            // Target x_i + three random donors, then the differential trial operator.
            return new MatchMetaHeuristic()
                .WithName("Differential Evolution", "Storn, R. & Price, K. (1997). DE/rand/1/bin: v = x_r1 + F.(x_r2 - x_r3) recombined with the target by a binomial crossover at rate CR.")
                .WithMatches(MatchingKind.Current, MatchingKind.Random, MatchingKind.Random, MatchingKind.Random)
                .WithCrossoverMetaHeuristic(trialHeuristic);
        }
    }
}
