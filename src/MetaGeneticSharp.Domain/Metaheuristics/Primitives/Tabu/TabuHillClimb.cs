#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using GeneticSharp;

namespace MetaGeneticSharp
{
    /// <summary>
    ///   The tabu-driven local search, packaged as an improvement operator for the memetic layer
    ///   (#12049 axe 4). This is where the three decoupled dimensions compose: a
    ///   <see cref="ITabuProjection"/> names what a move is, an <see cref="ITabuMemory"/> stores
    ///   it across generations, an <see cref="ITabuFilter"/> arbitrates conflicts -- and the walk
    ///   itself (best-strict / first-lateral hill climb over an injectable neighborhood) stays
    ///   independent of all three. The ygmh DiscreteTS hard-wires all of this into one
    ///   mono-solution class; here every axis is caller-chosen.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Cross-generation memory.</b> The memory instance is carried by the evolution-context
    /// store (<see cref="IEvolutionContext.GetOrAdd{TItemType}"/>) under a POPULATION-WIDE key
    /// (constant generation component, individual -1), so it survives generations and is shared
    /// by every improved individual of the population. The improvement pass runs inside the
    /// selection hook, which the engine executes sequentially (only fitness evaluation is
    /// task-parallel), so the memory needs no internal locking.
    /// </para>
    /// <para>
    /// <b>Lateral acceptance.</b> A strict-only hill climb cannot cross a plateau; accepting
    /// equal-fitness moves (<c>acceptLateral: true</c>, first admissible) can -- and without the
    /// tabu memory it CYCLES on the plateau forever (the deterministic keystone of the test
    /// suite). With the interdict, the walk cannot revisit, so laterals make progress.
    /// </para>
    /// <para>
    /// Candidates are evaluated THROUGH THE CALLER'S evaluator before filtering, so
    /// aspiration-on-best sees their fitness; an unevaluable candidate (null) is skipped.
    /// </para>
    /// </remarks>
    public static class TabuHillClimb
    {
        private const string MemoryKey = "tabu.memory";
        private static readonly IMetaHeuristic StoreScope = new DefaultMetaHeuristic();

        /// <summary>
        /// The metric-space neighborhood of a gene: the replacement values (in metric space)
        /// the walk may move that gene to, in enumeration order.
        /// </summary>
        public delegate IEnumerable<double> Neighborhood(int geneIndex, double metricValue);

        /// <summary>Sign-flip neighborhood: v → -v. For ±-encoded genes (the keystone plateau).</summary>
        public static Neighborhood Flip => (_, v) => new[] { -v };

        /// <summary>Stepped neighborhood: v → v ± step. The classic gradient-descent-style stencil.</summary>
        public static Neighborhood Stepped(double step) => (_, v) => new[] { v - step, v + step };

        /// <summary>
        /// Builds an improvement operator for <see cref="LocalImprovementMetaHeuristic"/> /
        /// <see cref="MemeticAlgorithm"/> performing a tabu hill climb from the given chromosome.
        /// </summary>
        /// <param name="converter">Gene ↔ metric conversion used to enumerate the neighborhood.</param>
        /// <param name="evaluate">Candidate evaluator (typically wrapping the algorithm's fitness); null result = skip.</param>
        /// <param name="neighborhood">Which metric replacements each gene may move to.</param>
        /// <param name="projection">The tabu attribute projection.</param>
        /// <param name="memoryFactory">
        /// Builds the memory ONCE per population (the instance is cached in the context store and
        /// shared across generations); a factory rather than an instance so the store owns it.
        /// </param>
        /// <param name="filter">The admissibility arbitration (aspiration policy).</param>
        /// <param name="maxMoves">Budget of the walk (moves per improvement pass).</param>
        /// <param name="acceptLateral">Whether equal-fitness moves are accepted (plateau traversal).</param>
        public static LocalImprovementMetaHeuristic.ImprovementOperator Improvement(
            IGeometricConverter converter,
            Func<IChromosome, double?> evaluate,
            Neighborhood neighborhood,
            ITabuProjection projection,
            Func<ITabuMemory> memoryFactory,
            ITabuFilter filter,
            int maxMoves,
            bool acceptLateral = true)
        {
            return (current, ctx) => Walk(current, ctx, converter, evaluate, neighborhood, projection, memoryFactory, filter, maxMoves, acceptLateral);
        }

        /// <summary>The walk itself, directly testable without a live evolution context.</summary>
        public static IChromosome Walk(
            IChromosome current,
            IEvolutionContext ctx,
            IGeometricConverter converter,
            Func<IChromosome, double?> evaluate,
            Neighborhood neighborhood,
            ITabuProjection projection,
            Func<ITabuMemory> memoryFactory,
            ITabuFilter filter,
            int maxMoves,
            bool acceptLateral)
        {
            var memory = ctx.GetOrAdd((MemoryKey, 0, EvolutionStage.Selection, StoreScope, -1), memoryFactory);

            var walk = current.Clone();
            var startFitness = evaluate(walk);
            if (!startFitness.HasValue)
            {
                return current;
            }

            walk.Fitness = startFitness;
            double bestEver = startFitness.Value;
            int moves = 0;

            while (moves < maxMoves)
            {
                var pick = PickNeighbor(walk, converter, evaluate, neighborhood, projection, memory, filter, bestEver, acceptLateral, out double pickFitness);
                if (pick == null)
                {
                    break;
                }

                memory.Remember(projection.Committed(walk, pick));
                walk = pick;
                walk.Fitness = pickFitness;
                if (pickFitness > bestEver)
                {
                    bestEver = pickFitness;
                }

                moves++;
            }

            memory.Tick();
            return walk;
        }

        private static IChromosome PickNeighbor(
            IChromosome walk,
            IGeometricConverter converter,
            Func<IChromosome, double?> evaluate,
            Neighborhood neighborhood,
            ITabuProjection projection,
            ITabuMemory memory,
            ITabuFilter filter,
            double bestEver,
            bool acceptLateral,
            out double pickFitness)
        {
            IChromosome pick = null;
            pickFitness = double.NaN;
            var genes = walk.GetGenes();

            for (int i = 0; i < genes.Length; i++)
            {
                double metric = converter.GeneToDouble(i, genes[i].Value);
                foreach (var replacement in neighborhood(i, metric))
                {
                    if (replacement == metric)
                    {
                        continue;
                    }

                    var candidate = walk.Clone();
                    candidate.ReplaceGene(i, new Gene(converter.DoubleToGene(i, replacement)));
                    var fitness = evaluate(candidate);
                    if (!fitness.HasValue)
                    {
                        continue;
                    }

                    candidate.Fitness = fitness;
                    if (!filter.IsAdmissible(walk, candidate, projection, memory, bestEver))
                    {
                        continue;
                    }

                    // Steepest on strict improvement; FIRST on lateral (equal) -- a plateau is
                    // crossed by the first admissible equal move, and the tabu memory is what
                    // stops that from cycling.
                    if (fitness > walk.Fitness.Value)
                    {
                        if (pick == null || fitness > pickFitness)
                        {
                            pick = candidate;
                            pickFitness = fitness.Value;
                        }
                    }
                    else if (acceptLateral && fitness == walk.Fitness.Value && pick == null)
                    {
                        pick = candidate;
                        pickFitness = fitness.Value;
                    }
                }
            }

            return pick;
        }
    }
}
