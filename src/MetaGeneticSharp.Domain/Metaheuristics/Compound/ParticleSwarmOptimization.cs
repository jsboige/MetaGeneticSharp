#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using GeneticSharp;

namespace MetaGeneticSharp
{
    /// <summary>
    ///   Canonical Particle Swarm Optimisation with the velocity/position recurrence, expressed as
    ///   a geometric compound metaheuristic over the same fluent grammar as WOA / EO / FBI / DE /
    ///   BBPSO. This is Shi &amp; Eberhart's inertia-weight PSO (Y. Shi, R. Eberhart, "A Modified
    ///   Particle Swarm Optimizer", ICEC 1998): each gene of the new particle follows
    ///   <c>v = w*v_prev + c1*r1*(pbest - x) + c2*r2*(gbest - x)</c> then
    ///   <c>x_new = x + v</c>, with <c>r1, r2 ~ U(0,1)</c> drawn per gene per generation. The
    ///   defaults are Clerc's constriction equivalents (<c>w = 0.7298, c1 = c2 = 1.49618</c>).
    ///   The greedy keep-the-best selection is the inherited
    ///   <see cref="FitnessBasedElitistReinsertion"/>, so this class does NOT override
    ///   <see cref="GeometricMetaHeuristicBase.GetDefaultReinsertion"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Per-particle memory through the evolution context.</b> Classical PSO needs two per-particle
    /// memories the four <see cref="MatchingKind"/> values do not expose: the velocity
    /// <c>v_i(t)</c> and the personal best <c>pbest_i</c>. The evolution context store
    /// (<see cref="IEvolutionContext.GetOrAdd{TItemType}"/>, a generation-keyed cache without
    /// eviction) does carry per-particle values: this compound writes the velocity and personal-best
    /// state of particle <c>i</c> under the generation-<c>g</c> key and reads it back one generation
    /// later with a seeding factory (missing history seeds <c>v_0 = 0</c> and
    /// <c>pbest_0 = x_0</c>, the canonical initialisations). This corrects the earlier recon that
    /// ruled out velocity PSO for want of a per-particle state hook.
    /// </para>
    /// <para>
    /// <b>Slot addressing.</b> A "particle" is a stable population slot index
    /// (<see cref="IEvolutionContext.OriginalIndex"/>). Under the elitist reinsertion the chromosome
    /// occupying a slot may change between generations, so <c>pbest_i</c> is read as "best solution
    /// seen at slot i" -- the framework's honest translation of the particle-identity semantics.
    /// </para>
    /// <para>
    /// <b>Self-scaling velocity clamp.</b> <see cref="IGeometricConverter"/> exposes no gene bounds,
    /// so the usual <c>|v| &lt;= Vmax = ratio * (x_max - x_min)</c> cannot be computed. This
    /// compound clamps instead against the attractor span <c>max(|pbest - x|, |gbest - x|)</c>:
    /// <c>|v| &lt;= MaxVelocityRatio * span</c>. The bound self-scales with convergence; when the
    /// particle coincides with both attractors the span is zero and the update freezes in place --
    /// the same convergence property as the bare-bones variant.
    /// </para>
    /// </remarks>
    public class ParticleSwarmOptimization : GeometricMetaHeuristicBase
    {
        private const string VelocityKeyPrefix = "pso.v.";
        private const string PbestFitnessKey = "pso.pbest.fitness";
        private const string PbestGeneKeyPrefix = "pso.pbest.gene.";

        // A stable key token scoping the per-particle store entries: the compound itself is not an
        // IMetaHeuristic (Build() produces one), so a private static sentinel plays that role.
        private static readonly IMetaHeuristic StoreScope = new DefaultMetaHeuristic();

        /// <summary>
        /// The velocity/position update. <c>geneValues</c> is read as
        /// <c>[current position x_i (Current), global best g (Best)]</c>; the velocity memory and
        /// personal best come from the evolution context store keyed by particle and generation.
        /// </summary>
        public delegate object VelocityUpdateOperator(int geneIndex, IEnumerable<object> geneValues, IGeometricConverter geometricConverter, IEvolutionContext ctx);

        /// <summary>Inertia weight <c>w</c>. Default is Clerc's constriction equivalent 0.7298.</summary>
        public double InertiaWeight { get; set; } = 0.7298;

        /// <summary>Cognitive coefficient <c>c1</c> (pull toward the personal best). Default 1.49618.</summary>
        public double CognitiveCoefficient { get; set; } = 1.49618;

        /// <summary>Social coefficient <c>c2</c> (pull toward the global best). Default 1.49618.</summary>
        public double SocialCoefficient { get; set; } = 1.49618;

        /// <summary>
        /// Velocity clamp ratio: <c>|v| &lt;= MaxVelocityRatio * max(|pbest - x|, |gbest - x|)</c>.
        /// 2.0 lets a full-attractor step (c * span) plus partial inertia through without binding.
        /// </summary>
        public double MaxVelocityRatio { get; set; } = 2.0;

        /// <summary>The velocity/position update operator. Overridable to express time-varying inertia or gbest-only variants.</summary>
        public VelocityUpdateOperator UpdateOperator { get; set; }

        /// <summary>Wires the default update operator (an instance method, so it reads the coefficients).</summary>
        public ParticleSwarmOptimization()
        {
            UpdateOperator = DefaultUpdateOperator;
        }

        /// <summary>
        /// The canonical recurrence, pure and deterministic (no store access): computes the clamped
        /// velocity <c>v = w*v_prev + c1*r1*(pbest - x) + c2*r2*(gbest - x)</c>.
        /// </summary>
        public static double ComputeVelocity(double position, double personalBest, double globalBest,
            double previousVelocity, double r1, double r2,
            double inertiaWeight, double cognitiveCoefficient, double socialCoefficient, double maxVelocityRatio)
        {
            double cognitive = cognitiveCoefficient * r1 * (personalBest - position);
            double social = socialCoefficient * r2 * (globalBest - position);
            double v = inertiaWeight * previousVelocity + cognitive + social;

            double span = Math.Max(Math.Abs(personalBest - position), Math.Abs(globalBest - position));
            double vMax = maxVelocityRatio * span;
            return Math.Max(-vMax, Math.Min(vMax, v));
        }

        /// <summary>The default update: reads/writes the per-particle store, draws r1/r2, applies the recurrence.</summary>
        public object DefaultUpdateOperator(int geneIndex, IEnumerable<object> geneValues, IGeometricConverter geometricConverter, IEvolutionContext ctx)
        {
            var metricValues = geneValues.Select(value => geometricConverter.GeneToDouble(geneIndex, value)).ToList();
            double x = metricValues[0];     // Current position x_i.
            double gbest = metricValues[1]; // Global best g.

            int particle = ctx.OriginalIndex;
            int generation = GetCurrentGeneration(ctx);
            // Fitness of the particle's current chromosome (higher is better); parents are evaluated
            // before crossover. Unknown fitness degrades to never-improving (keeps the stored pbest).
            double currentFitness = SelectedCurrentFitness(ctx);

            // Read the previous generation's state; the seeding factories give the canonical
            // initialisations v_0 = 0 and pbest_0 = x_0 when no history exists.
            double vPrev = ctx.GetOrAdd((VelocityKeyPrefix + geneIndex, generation - 1, EvolutionStage.Crossover, StoreScope, particle), () => 0.0);
            double prevPbestFitness = ctx.GetOrAdd((PbestFitnessKey, generation - 1, EvolutionStage.Crossover, StoreScope, particle), () => currentFitness);
            double prevPbestGene = ctx.GetOrAdd((PbestGeneKeyPrefix + geneIndex, generation - 1, EvolutionStage.Crossover, StoreScope, particle), () => x);

            // Running personal best: the particle's own position when it improved on its record,
            // the stored record otherwise.
            double pbest = currentFitness >= prevPbestFitness ? x : prevPbestGene;

            // Publish this generation's state (first writer wins; recomputation from the same
            // previous state is idempotent under the single-crossover-per-particle pipeline).
            ctx.GetOrAdd((PbestFitnessKey, generation, EvolutionStage.Crossover, StoreScope, particle), () => Math.Max(currentFitness, prevPbestFitness));
            ctx.GetOrAdd((PbestGeneKeyPrefix + geneIndex, generation, EvolutionStage.Crossover, StoreScope, particle), () => pbest);

            var rnd = RandomizationProvider.Current;
            double v = ComputeVelocity(x, pbest, gbest, vPrev, rnd.GetDouble(), rnd.GetDouble(),
                InertiaWeight, CognitiveCoefficient, SocialCoefficient, MaxVelocityRatio);
            ctx.GetOrAdd((VelocityKeyPrefix + geneIndex, generation, EvolutionStage.Crossover, StoreScope, particle), () => v);

            return geometricConverter.DoubleToGene(geneIndex, x + v);
        }

        /// <summary>
        /// The generation index keying the per-particle store. Virtual so alternative drivers (and
        /// tests exercising the memory without a live population) can control the clock.
        /// </summary>
        protected virtual int GetCurrentGeneration(IEvolutionContext ctx)
        {
            return ctx.Population?.GenerationsNumber ?? 0;
        }

        private static double SelectedCurrentFitness(IEvolutionContext ctx)
        {
            var current = ctx?.SelectedParents?.FirstOrDefault();
            return current?.Fitness ?? double.NegativeInfinity;
        }

        /// <inheritdoc />
        protected override IContainerMetaHeuristic BuildMainHeuristic()
        {
            // The two-parent geometric crossover: geneValues = [current position (Current), gbest (Best)].
            var updateHeuristic = new CrossoverMetaHeuristic()
                .WithName("canonical velocity/position update")
                .WithCrossover(ParamScope.None,
                    (IMetaHeuristic h, IEvolutionContext ctx) => new GeometricCrossover<object>(GeometricConverter.IsOrdered, 2, false)
                        .WithLinearGeometricOperator((geneIndex, geneValues) => UpdateOperator(geneIndex, geneValues, GeometricConverter, ctx))
                        .WithGeometryEmbedding(GeometricConverter.GetEmbedding()));

            // Current position + global best, then the velocity/position recurrence (pbest and
            // velocity come from the per-particle store, not from a match).
            return new MatchMetaHeuristic()
                .WithName("Particle Swarm Optimisation", "Shi, Y. & Eberhart, R. (1998). A Modified Particle Swarm Optimizer: v = w*v + c1*r1*(pbest - x) + c2*r2*(gbest - x), x += v, with per-particle velocity and personal-best memory carried by the evolution context store.")
                .WithMatches(MatchingKind.Current, MatchingKind.Best)
                .WithCrossoverMetaHeuristic(updateHeuristic);
        }
    }
}
