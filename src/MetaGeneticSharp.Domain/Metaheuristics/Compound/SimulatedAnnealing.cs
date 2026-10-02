#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using GeneticSharp;

namespace MetaGeneticSharp
{
    /// <summary>
    ///   Simulated Annealing, expressed as a stateless geometric compound metaheuristic over the same
    ///   fluent grammar as WOA / EO / FBI / DE / BBPSO. This is the Metropolis acceptance rule
    ///   (Metropolis et al., 1953; Kirkpatrick, Gelatt &amp; Vecchi, 1983) recast onto a population: each
    ///   individual's perturbed neighbour is accepted if it is fitter, otherwise with probability
    ///   <c>exp((f' - f) / T_k)</c>, where the temperature cools on a geometric schedule
    ///   <c>T_k = T_0 * alpha^k</c> read from the population's generation number.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Why a geometric compound, and why stateless.</b> Classical SA keeps the current solution and a
    /// temperature; the per-individual analogue would need per-particle state, which the geometric-compound
    /// framework does not expose (no PersonalBest match, no per-individual hook -- cf. the recon that also
    /// rules out classical-PSO velocity and Tabu's tabu-list). The temperature, however, is a <i>global</i>
    /// schedule (the same T_k for every individual at generation k), so it is fully derivable from
    /// <see cref="IPopulation.GenerationsNumber"/> and needs no per-individual memory. This is exactly the
    /// stateless variant the framework can express -- the same insight that admits the bare-bones PSO.
    /// </para>
    /// <para>
    /// <b>Perturbation neighbourhood.</b> The neighbour is a Gaussian step from the current position with a
    /// population-relative scale: <c>sigma = StepFraction * |random - current|</c>, where the matched random
    /// individual supplies the <i>scale</i> (the population's spread) but not the <i>direction</i> -- the
    /// step is isotropic. As the swarm converges the spread -- and thus the step -- shrinks naturally, so the
    /// proposal self-adapts without a second cooling knob; the <i>acceptance</i> temperature alone carries
    /// the annealing. When a random individual coincides with the current one the scale is zero and the draw
    /// collapses to the current position (the SA analogue of BBPSO's freeze-at-the-best).
    /// </para>
    /// <para>
    /// <b>Acceptance.</b> The defining SA behaviour lives in <see cref="MetropolisReinsertion"/>: uphill
    /// moves survive while T is high and are frozen out as T -&gt; 0 (the greedy limit is the pairwise
    /// reinsertion of FBI). This compound therefore overrides
    /// <see cref="GeometricMetaHeuristicBase.GetDefaultReinsertion"/> to install the Metropolis layer,
    /// forwarding <see cref="InitialTemperature"/> and <see cref="CoolingRate"/>.
    /// </para>
    /// </remarks>
    public class SimulatedAnnealing : GeometricMetaHeuristicBase
    {
        /// <summary>The default fraction of the population-relative spread used as the Gaussian step scale.</summary>
        public const double DefaultStepFraction = 0.5;

        /// <summary>
        /// Draws a perturbed gene value: <c>current + StepFraction * |random - current| * N(0, 1)</c>.
        /// <c>geneValues</c> is read as <c>[current x_i (Current), random x_r (Random)]</c>; the random
        /// individual supplies the step scale only, the step direction is isotropic.
        /// </summary>
        public delegate object PerturbationOperator(int geneIndex, IEnumerable<object> geneValues, IGeometricConverter geometricConverter);

        /// <summary>
        /// The default Gaussian perturbation. Uses a Box-Muller transform because
        /// <see cref="IRandomization"/> exposes no Gaussian sampler (only uniform draws).
        /// </summary>
        public static object DefaultPerturbationOperator(int geneIndex, IEnumerable<object> geneValues, IGeometricConverter geometricConverter)
        {
            var metricValues = geneValues.Select(value => geometricConverter.GeneToDouble(geneIndex, value)).ToList();
            double current = metricValues[0]; // Current position x_i.
            double random = metricValues[1];  // A random individual x_r -- supplies the step scale only.
            double scale = DefaultStepFraction * Math.Abs(random - current);

            // Box-Muller standard normal. 1 - GetDouble() keeps u1 in (0, 1] so Math.Log never sees 0.
            var rnd = RandomizationProvider.Current;
            double u1 = 1.0 - rnd.GetDouble();
            double u2 = rnd.GetDouble();
            double z = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);

            double sample = current + scale * z;
            return geometricConverter.DoubleToGene(geneIndex, sample);
        }

        /// <summary>The perturbation operator. Overridable to express fixed-step, best-directed-scale, or cooled-sigma variants.</summary>
        public PerturbationOperator SamplingOperator { get; set; } = DefaultPerturbationOperator;

        /// <summary>T_0, the temperature at generation 0 (forwarded to the Metropolis reinsertion).</summary>
        public double InitialTemperature { get; set; } = 1.0;

        /// <summary>alpha in T_k = T_0 * alpha^k (forwarded to the Metropolis reinsertion).</summary>
        public double CoolingRate { get; set; } = 0.95;

        /// <summary>
        ///   Optional exponent coupling the Gaussian step scale to the annealing progress: when set,
        ///   each proposed step is contracted around the current position by
        ///   <c>coolingRate^(k * gamma)</c> at generation k -- the same geometric schedule as the
        ///   acceptance temperature, raised to <c>gamma</c>. Leave null (the default) to preserve the
        ///   historical behaviour where the step scale tracks the population spread alone.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>Why this knob exists.</b> Motivated by the MGS-vs-mealpy SA benchmark loop (CoursIA
        /// #13778): once T_k has cooled to the greedy limit, lateral moves accepted on fitness
        /// plateaus inflate the population spread, and the spread-relative step scale feeds that
        /// inflation back -- the population drifts in a near-unbounded random walk instead of
        /// refining a basin (measured on the Sudoku R1 benchmark: inter-individual spread growing
        /// past the gene bounds [1, 10) while the best-so-far plateaus for the last quarter of the
        /// budget). Coupling the step to the same geometric decay as the temperature keeps the
        /// proposal fine-grained exactly when uphill acceptance has frozen out, without touching the
        /// shared geometric converter (and therefore the other compounds).
        /// </para>
        /// </remarks>
        public double? StepCoolingExponent { get; set; }

        /// <summary>
        ///   The step-contraction factor at generation <paramref name="generation"/>:
        ///   <c>coolingRate^(generation * exponent)</c>, i.e. (T_k / T_0)^exponent. Always in (0, 1]
        ///   for a valid schedule and a non-negative exponent.
        /// </summary>
        public static double CooledStepScale(double coolingRate, int generation, double exponent)
        {
            return Math.Pow(coolingRate, generation * exponent);
        }

        /// <summary>
        ///   Contracts a proposed gene value towards the current one by the given factor -- an
        ///   homothety of the step around the current position:
        ///   <c>x' = current + factor * (proposed - current)</c>. A factor of 1 returns the proposal
        ///   unchanged; a factor of 0 collapses to the current position. Because it post-processes
        ///   the proposed value, it composes with any <see cref="SamplingOperator"/>.
        /// </summary>
        public static object ApplyStepCooling(int geneIndex, object proposed, object current, IGeometricConverter geometricConverter, double factor)
        {
            if (factor >= 1.0)
                return proposed;
            double x = geometricConverter.GeneToDouble(geneIndex, proposed);
            double c = geometricConverter.GeneToDouble(geneIndex, current);
            return geometricConverter.DoubleToGene(geneIndex, c + factor * (x - c));
        }

        /// <inheritdoc />
        public override IReinsertion GetDefaultReinsertion()
        {
            return new MetropolisReinsertion(InitialTemperature, CoolingRate);
        }

        /// <inheritdoc />
        protected override IContainerMetaHeuristic BuildMainHeuristic()
        {
            // Capture the configured schedule so the crossover factory closes over stable values.
            double? stepCoolingExponent = StepCoolingExponent;
            double coolingRate = CoolingRate;

            // The two-parent geometric crossover: geneValues = [current (Current), random (Random)].
            var perturbHeuristic = new CrossoverMetaHeuristic()
                .WithName("simulated-annealing gaussian perturbation")
                .WithCrossover(ParamScope.None,
                    (IMetaHeuristic h, IEvolutionContext ctx) =>
                    {
                        // Null exponent = the historical fixed-coupling path (factor 1, no post-processing).
                        double factor = stepCoolingExponent.HasValue
                            ? CooledStepScale(coolingRate, ctx.Population?.GenerationsNumber ?? 0, stepCoolingExponent.Value)
                            : 1.0;
                        return new GeometricCrossover<object>(GeometricConverter.IsOrdered, 2, false)
                            .WithLinearGeometricOperator((geneIndex, geneValues) =>
                            {
                                var proposed = SamplingOperator(geneIndex, geneValues, GeometricConverter);
                                return factor >= 1.0
                                    ? proposed
                                    : ApplyStepCooling(geneIndex, proposed, geneValues.First(), GeometricConverter, factor);
                            })
                            .WithGeometryEmbedding(GeometricConverter.GetEmbedding());
                    });

            // Current position + a random individual (for the step scale), then the isotropic Gaussian step.
            return new MatchMetaHeuristic()
                .WithName("Simulated Annealing", "Metropolis, N. et al. (1953); Kirkpatrick, S., Gelatt, C.D., & Vecchi, M.P. (1983). Each individual's Gaussian neighbour is accepted if fitter, else with probability exp((f'-f)/T_k); T cools geometrically with the generation.")
                .WithMatches(MatchingKind.Current, MatchingKind.Random)
                .WithCrossoverMetaHeuristic(perturbHeuristic);
        }
    }
}
