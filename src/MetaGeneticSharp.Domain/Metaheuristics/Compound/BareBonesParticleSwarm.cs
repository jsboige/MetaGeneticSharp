#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using GeneticSharp;

namespace MetaGeneticSharp
{
    /// <summary>
    ///   Bare Bones Particle Swarm Optimisation, expressed as a geometric compound metaheuristic
    ///   over the same fluent grammar as WOA / EO / FBI / DE. This is Kennedy's 2003 "Bare Bones"
    ///   PSO update (J. Kennedy, "Bare Bones Particle Swarms", SIS 2003), which replaces the
    ///   velocity/position recurrence of classical PSO with a single Gaussian draw: each gene of
    ///   the new particle is sampled from <c>N(mean, std)</c> with
    ///   <c>mean = (anchor + gbest) / 2</c> and <c>std = |gbest - anchor|</c>. The greedy
    ///   keep-the-best selection is the inherited <see cref="FitnessBasedElitistReinsertion"/>
    ///   (best-N of parents+offspring), so this class does NOT override
    ///   <see cref="GeometricMetaHeuristicBase.GetDefaultReinsertion"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Why a geometric compound.</b> The bare-bones draw is a gene-wise, position-only operator
    /// (no velocity memory), so it fits <see cref="GeometricCrossover{TValue}"/> exactly: it reads
    /// the matched parents' gene values and returns a new gene value. Expressing it through the same
    /// converter/embedding/reinsertion contract as WOA/EO/FBI/DE keeps the centre-bias and benchmark
    /// harnesses comparing like for like.
    /// </para>
    /// <para>
    /// <b>Personal anchor vs. personal best.</b> Kennedy's original samples around
    /// <c>(pbest_i + gbest) / 2</c>, the midpoint of the particle's <i>own best</i> and the global
    /// best. The geometric-compound framework exposes only the four <see cref="MatchingKind"/> values
    /// Current / Random / Best / Worst: there is no per-individual "personal best" memory (no
    /// PersonalBest match, no per-particle state hook on <see cref="GeometricMetaHeuristicBase"/> --
    /// cf. the recon that also rules out classical-PSO velocity and Tabu's tabu-list). This compound
    /// therefore uses the particle's <see cref="MatchingKind.Current"/> position as the personal
    /// anchor in place of pbest_i, giving the gbest-anchored bare-bones variant. The drop is the
    /// honest consequence of the framework's stateless contract, documented rather than hidden: the
    /// algorithm is still a recognised bare-bones PSO (the gbest-only / current-anchored family),
    /// and retains BBPSO's defining property -- sampling, not stepping, toward the best.
    /// </para>
    /// <para>
    /// <b>Freeze at the global best.</b> When a particle IS the global best, anchor == gbest, so
    /// <c>std = 0</c> and its draw collapses to a copy of itself: the best particle does not move
    /// that generation. This is the literal Kennedy 2003 behaviour (the elite sits still while the
    /// swarm samples around it); the rest of the population keeps drawing and the greedy reinsertion
    /// preserves the incumbent. No jitter floor is added -- the freeze is a property, not a bug.
    /// </para>
    /// </remarks>
    public class BareBonesParticleSwarm : GeometricMetaHeuristicBase
    {
        /// <summary>
        /// Draws a new gene value from the bare-bones Gaussian: <c>N((anchor+gbest)/2, |gbest-anchor|)</c>.
        /// <c>geneValues</c> is read as <c>[personal anchor x_i (Current), global best g (Best)]</c>.
        /// </summary>
        public delegate object SampleOperator(int geneIndex, IEnumerable<object> geneValues, IGeometricConverter geometricConverter);

        /// <summary>
        /// The default bare-bones update. Uses a Box-Muller transform because
        /// <see cref="IRandomization"/> exposes no Gaussian sampler (only uniform draws).
        /// </summary>
        public static object DefaultSampleOperator(int geneIndex, IEnumerable<object> geneValues, IGeometricConverter geometricConverter)
        {
            var metricValues = geneValues.Select(value => geometricConverter.GeneToDouble(geneIndex, value)).ToList();
            double anchor = metricValues[0]; // Current position (personal anchor, proxy for pbest_i).
            double best = metricValues[1];   // Global best gbest.
            double mean = 0.5 * (anchor + best);
            double std = Math.Abs(best - anchor);

            // Box-Muller standard normal. 1 - GetDouble() keeps u1 in (0, 1] so Math.Log never sees 0.
            var rnd = RandomizationProvider.Current;
            double u1 = 1.0 - rnd.GetDouble();
            double u2 = rnd.GetDouble();
            double z = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);

            double sample = mean + std * z;
            return geometricConverter.DoubleToGene(geneIndex, sample);
        }

        /// <summary>The bare-bones sampling operator. Overridable to express jittered-std, gbest-only, or BBPSO-jrand variants.</summary>
        public SampleOperator SamplingOperator { get; set; } = DefaultSampleOperator;

        /// <inheritdoc />
        protected override IContainerMetaHeuristic BuildMainHeuristic()
        {
            // The two-parent geometric crossover: geneValues = [anchor (Current), gbest (Best)].
            var sampleHeuristic = new CrossoverMetaHeuristic()
                .WithName("bare-bones gaussian update")
                .WithCrossover(ParamScope.None,
                    (IMetaHeuristic h, IEvolutionContext ctx) => new GeometricCrossover<object>(GeometricConverter.IsOrdered, 2, false)
                        .WithLinearGeometricOperator((geneIndex, geneValues) => SamplingOperator(geneIndex, geneValues, GeometricConverter))
                        .WithGeometryEmbedding(GeometricConverter.GetEmbedding()));

            // Personal anchor (Current) + global best (Best), then the bare-bones Gaussian draw.
            return new MatchMetaHeuristic()
                .WithName("Bare Bones Particle Swarm", "Kennedy, J. (2003). Bare Bones Particle Swarms: each gene is sampled from N((x_i + gbest)/2, |gbest - x_i|), replacing PSO's velocity recurrence with a single Gaussian draw toward the global best.")
                .WithMatches(MatchingKind.Current, MatchingKind.Best)
                .WithCrossoverMetaHeuristic(sampleHeuristic);
        }
    }
}
