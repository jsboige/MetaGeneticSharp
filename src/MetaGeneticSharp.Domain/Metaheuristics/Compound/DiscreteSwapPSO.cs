#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using GeneticSharp;

namespace MetaGeneticSharp
{
    /// <summary>
    ///   Discrete Particle Swarm Optimisation over permutations, expressed as a compound
    ///   metaheuristic on the same fluent grammar as the canonical PSO. This is the swap-PSO of
    ///   the ygmh DiscretePSO (A. Hernandez &amp; Y. Gonzalez, "Metaheuristics in C#", Univ. de La
    ///   Habana, 2012 -- the velocity is a SEQUENCE OF TRANSPOSITIONS): the position is a
    ///   permutation of arbitrary discrete genes, the velocity is a list of swaps, the difference
    ///   between two permutations is the greedy transposition sequence transforming one into the
    ///   other, and the update samples that sequence:
    ///   <c>v = w·v_prev ⊕ c1·r1·(pbest ⊖ x) ⊕ c2·r2·(gbest ⊖ x)</c>, <c>x_new = x ⊕ v</c>.
    ///   Applied swaps preserve the permutation property by construction -- the search never
    ///   leaves the discrete space, no repair step is ever needed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Why not the linear per-gene operator.</b> A swap touches TWO gene positions at once:
    /// the whole-chromosome update cannot be expressed gene-by-gene through
    /// <see cref="GeometricCrossover{T}"/>'s linear operator. This compound therefore supplies
    /// its own <see cref="ICrossover"/> (<see cref="SwapUpdateCrossover"/>) to the
    /// <see cref="CrossoverMetaHeuristic"/>, the same extension point any custom geometry uses.
    /// The geometric view still holds (Moraglio): the swap sequence IS the geodesic between two
    /// permutations under the Cayley/Kendall-Tau metric, so sampling it is sampling a point on
    /// the segment -- the discrete reading of the convex combination.
    /// </para>
    /// <para>
    /// <b>Per-particle memory.</b> As in the canonical velocity PSO, the two memories the match
    /// kinds do not expose -- the personal best and the velocity -- live in the evolution
    /// context store, keyed by particle slot and generation. The velocity of particle
    /// <c>i</c> is the list of swaps it applied last generation; the personal best is the best
    /// chromosome seen at slot <c>i</c> (elitist reinsertion may change the occupant, the same
    /// honest slot semantics as the canonical compound).
    /// </para>
    /// <para>
    /// <b>Coefficients as sampling rates.</b> The continuous <c>w, c1, c2</c> become the
    /// per-swap retention probabilities of the inertial, cognitive and social components:
    /// <c>keep each swap of v_prev with probability w, each swap of (pbest ⊖ x) with
    /// probability c1·r1</c> (one <c>r ~ U(0,1)</c> draw per component per particle per
    /// generation), <c>each swap of (gbest ⊖ x) with probability c2·r2</c>. Defaults are the
    /// canonical Clerc-equivalent pair.
    /// </para>
    /// </remarks>
    public class DiscreteSwapPSO : GeometricMetaHeuristicBase
    {
        private const string VelocityKey = "dpsp.v";
        private const string PbestFitnessKey = "dpsp.pbest.fitness";
        private const string PbestChromosomeKey = "dpsp.pbest.chrom";

        // A stable key token scoping the per-particle store entries (same sentinel pattern as
        // the canonical PSO: the compound itself is not an IMetaHeuristic).
        private static readonly IMetaHeuristic StoreScope = new DefaultMetaHeuristic();

        /// <summary>A transposition of gene positions <c>I</c> and <c>J</c>.</summary>
        public readonly record struct Swap(int I, int J);

        /// <summary>
        /// The whole-chromosome update. <c>parents</c> is read as
        /// <c>[current position x (Current), global best (Best)]</c>; the velocity memory and
        /// personal best come from the evolution context store.
        /// </summary>
        public delegate IList<IChromosome> SwapUpdateOperator(IList<IChromosome> parents, IEvolutionContext ctx);

        /// <summary>Inertia weight <c>w</c>: retention probability of each previous-velocity swap. Default 0.7298.</summary>
        public double InertiaWeight { get; set; } = 0.7298;

        /// <summary>Cognitive coefficient <c>c1</c>: sampling rate of the swaps toward the personal best. Default 1.49618.</summary>
        public double CognitiveCoefficient { get; set; } = 1.49618;

        /// <summary>Social coefficient <c>c2</c>: sampling rate of the swaps toward the global best. Default 1.49618.</summary>
        public double SocialCoefficient { get; set; } = 1.49618;

        /// <summary>The whole-chromosome update operator. Overridable to express gbest-only or time-varying variants.</summary>
        public SwapUpdateOperator UpdateOperator { get; set; }

        /// <summary>Wires the default update operator (an instance method, so it reads the coefficients and the store).</summary>
        public DiscreteSwapPSO()
        {
            UpdateOperator = DefaultUpdateOperator;
        }

        /// <summary>
        /// The greedy transposition sequence transforming <paramref name="from"/> into
        /// <paramref name="to"/> (the ygmh "PermutationSubstruction"): for each position where
        /// the genes differ, swap the occupant into the position where the target gene currently
        /// sits. At most <c>n-1</c> swaps, each moving one more gene to its target position --
        /// a geodesic under the Cayley metric.
        /// </summary>
        public static List<Swap> Minus(object[] from, object[] to)
        {
            var current = (object[])from.Clone();
            var swaps = new List<Swap>();
            for (int i = 0; i < current.Length && i < to.Length; i++)
            {
                if (Equals(current[i], to[i]))
                {
                    continue;
                }

                int j = Array.IndexOf(current, to[i], i + 1);
                if (j < 0)
                {
                    // The multisets differ: no transposition sequence exists. Stop at the
                    // longest common prefix -- the caller's sampling simply gets fewer swaps.
                    break;
                }

                (current[i], current[j]) = (current[j], current[i]);
                swaps.Add(new Swap(i, j));
            }

            return swaps;
        }

        /// <summary>Applies the swaps to a copy of the genes, in order.</summary>
        public static object[] Move(object[] genes, IEnumerable<Swap> swaps)
        {
            var result = (object[])genes.Clone();
            foreach (var (i, j) in swaps)
            {
                if (i >= 0 && i < result.Length && j >= 0 && j < result.Length)
                {
                    (result[i], result[j]) = (result[j], result[i]);
                }
            }

            return result;
        }

        /// <summary>Keeps each swap of the sequence independently with the given probability.</summary>
        public static List<Swap> Times(IEnumerable<Swap> swaps, double probability)
        {
            var rnd = RandomizationProvider.Current;
            var p = Math.Clamp(probability, 0.0, 1.0);
            return swaps.Where(_ => rnd.GetDouble() < p).ToList();
        }

        /// <summary>The default whole-chromosome update: reads the store, samples the three components, applies.</summary>
        public IList<IChromosome> DefaultUpdateOperator(IList<IChromosome> parents, IEvolutionContext ctx)
        {
            var x = parents[0];
            var gbest = parents[1];
            var xGenes = x.GetGenes().Select(g => g.Value).ToArray();

            int particle = ctx.OriginalIndex;
            int generation = GetCurrentGeneration(ctx);
            double currentFitness = SelectedCurrentFitness(ctx);

            // Previous state; seeding factories give v_0 = [] and pbest_0 = x_0.
            var vPrev = ctx.GetOrAdd((VelocityKey, generation - 1, EvolutionStage.Crossover, StoreScope, particle),
                () => new List<Swap>());
            double prevPbestFitness = ctx.GetOrAdd((PbestFitnessKey, generation - 1, EvolutionStage.Crossover, StoreScope, particle),
                () => currentFitness);
            var prevPbestGenes = ctx.GetOrAdd((PbestChromosomeKey, generation - 1, EvolutionStage.Crossover, StoreScope, particle),
                () => xGenes);

            // Running personal best: the particle's own position when it improved on its record.
            bool improved = currentFitness >= prevPbestFitness;
            var pbestGenes = improved ? xGenes : prevPbestGenes;

            var rnd = RandomizationProvider.Current;
            var velocity = new List<Swap>();
            velocity.AddRange(Times(vPrev, InertiaWeight));
            velocity.AddRange(Times(Minus(xGenes, pbestGenes), CognitiveCoefficient * rnd.GetDouble()));
            velocity.AddRange(Times(Minus(xGenes, gbest.GetGenes().Select(g => g.Value).ToArray()), SocialCoefficient * rnd.GetDouble()));

            // Publish this generation's state (first writer wins; one crossover per particle).
            ctx.GetOrAdd((PbestFitnessKey, generation, EvolutionStage.Crossover, StoreScope, particle),
                () => Math.Max(currentFitness, prevPbestFitness));
            ctx.GetOrAdd((PbestChromosomeKey, generation, EvolutionStage.Crossover, StoreScope, particle), () => pbestGenes);
            ctx.GetOrAdd((VelocityKey, generation, EvolutionStage.Crossover, StoreScope, particle), () => velocity);

            var child = x.Clone();
            var moved = Move(xGenes, velocity);
            child.ReplaceGenes(0, moved.Select((value, i) => new Gene(value)).ToArray());
            return new List<IChromosome> { child };
        }

        /// <summary>
        /// The generation index keying the per-particle store. Virtual so tests can control the
        /// clock without a live population.
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
            // The two-parent whole-chromosome crossover: parents = [x (Current), gbest (Best)].
            var updateHeuristic = new CrossoverMetaHeuristic()
                .WithName("swap-sequence velocity/position update")
                .WithCrossover(ParamScope.None,
                    (IMetaHeuristic h, IEvolutionContext ctx) => new SwapUpdateCrossover(this, ctx));

            // Current position + global best, then the swap-sequence recurrence (pbest and
            // velocity come from the per-particle store, not from a match).
            return new MatchMetaHeuristic()
                .WithName("Discrete Swap PSO",
                    "Hernandez, A. & Gonzalez, Y. (2012). Metaheuristics in C#: v = w*v + c1*r1*(pbest - x) + c2*r2*(gbest - x) over permutations, the velocity being a sequence of transpositions sampled component-wise; per-particle memory carried by the evolution context store.")
                .WithMatches(MatchingKind.Current, MatchingKind.Best)
                .WithCrossoverMetaHeuristic(updateHeuristic);
        }

        /// <summary>
        /// The whole-chromosome ICrossover delegating to the compound's update operator. Two
        /// parents (current + global best), one child -- the swap-sequence update needs the full
        /// chromosome because a transposition touches two gene positions at once.
        /// </summary>
        public sealed class SwapUpdateCrossover : CrossoverBase
        {
            private readonly DiscreteSwapPSO _compound;
            private readonly IEvolutionContext _ctx;

            /// <summary>Builds the crossover bound to its compound and evolution context.</summary>
            public SwapUpdateCrossover(DiscreteSwapPSO compound, IEvolutionContext ctx)
                : base(2, 1)
            {
                _compound = compound;
                _ctx = ctx;
            }

            /// <inheritdoc />
            protected override IList<IChromosome> PerformCross(IList<IChromosome> parents)
            {
                return _compound.UpdateOperator(parents, _ctx);
            }
        }
    }
}
