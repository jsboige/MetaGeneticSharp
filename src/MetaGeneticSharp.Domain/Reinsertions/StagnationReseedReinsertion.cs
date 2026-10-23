#nullable disable

using System;
using System.Collections.Generic;
using System.Linq;
using GeneticSharp;

namespace MetaGeneticSharp
{
    /// <summary>
    ///   <see cref="FitnessBasedElitistReinsertion"/> with a stagnation-triggered diversity
    ///   reseed. While the best fitness keeps improving, the inherited best-N-of-parents-and-
    ///   offspring semantics apply unchanged. After <see cref="StagnationGenerations"/>
    ///   consecutive generations without any improvement, the worst <c>MinSize *
    ///   ReseedFraction</c> slots of the selected set are replaced with fresh random
    ///   chromosomes (<c>IChromosome.CreateNew()</c>), which the engine evaluates right after
    ///   reinsertion. This reopens the population spread that differential operators draw
    ///   their steps from.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Motivation.</b> The DE precocity probe (MetaGeneticSharp#67) measured that on plateau
    /// substrates (rounded-decode fitness) the elitist frontier stops admitting anything new,
    /// the population collapses to a single raw position out of 50, every difference vector
    /// dies (donor-pair zero 100% at generation 160) and all remaining trials are no-ops while
    /// half the budget spins. No parameter lever (F, F-ramp) reopens it: the fuel of a
    /// self-referential operator is the population spread itself, so the corrective must
    /// re-supply spread at the reinsertion frontier, not resize the steps.
    /// </para>
    /// <para>
    /// <b>What is preserved.</b> The best <c>(1 - ReseedFraction)</c> of the population keeps
    /// the pure elitist contract: the incumbent best is never discarded by a reseed, only the
    /// worst slots — which the frontier would otherwise carry unchanged — are recycled into
    /// exploration material. Fresh chromosomes are donors first, survivors only if they earn it.
    /// </para>
    /// </remarks>
    public class StagnationReseedReinsertion : FitnessBasedElitistReinsertion
    {
        private double? _lastBestFitness;
        private int _stagnationCount;

        /// <summary>
        /// Initializes the reinsertion with a stagnation threshold and a reseed fraction.
        /// </summary>
        /// <param name="stagnationGenerations">Consecutive generations without best-fitness improvement that trigger a reseed (positive).</param>
        /// <param name="reseedFraction">Fraction of the worst selected slots replaced at each reseed, in (0, 1).</param>
        public StagnationReseedReinsertion(int stagnationGenerations, double reseedFraction)
        {
            if (stagnationGenerations <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(stagnationGenerations), "Stagnation threshold must be positive.");
            }

            if (reseedFraction <= 0.0 || reseedFraction >= 1.0)
            {
                throw new ArgumentOutOfRangeException(nameof(reseedFraction), "Reseed fraction must be in (0, 1).");
            }

            StagnationGenerations = stagnationGenerations;
            ReseedFraction = reseedFraction;
        }

        /// <summary>Consecutive generations without best-fitness improvement that trigger a reseed.</summary>
        public int StagnationGenerations { get; }

        /// <summary>Fraction of the worst selected slots replaced by fresh random chromosomes at each reseed.</summary>
        public double ReseedFraction { get; }

        /// <summary>Number of reseeds performed so far (observable by probes and tests).</summary>
        public int ReseedCount { get; private set; }

        /// <inheritdoc />
        protected override IList<IChromosome> PerformSelectChromosomes(IPopulation population, IList<IChromosome> offspring, IList<IChromosome> parents)
        {
            var selected = base.PerformSelectChromosomes(population, offspring, parents);

            // base returns the best MinSize of parents+offspring sorted descending: index 0 is the incumbent best.
            double best = selected.Count > 0 ? selected[0].Fitness ?? double.MinValue : double.MinValue;

            if (!_lastBestFitness.HasValue || best > _lastBestFitness.Value)
            {
                _lastBestFitness = best;
                _stagnationCount = 0;
            }
            else
            {
                _stagnationCount++;
            }

            if (_stagnationCount >= StagnationGenerations)
            {
                _stagnationCount = 0;
                Reseed(selected, offspring, parents);
            }

            return selected;
        }

        private void Reseed(IList<IChromosome> selected, IList<IChromosome> offspring, IList<IChromosome> parents)
        {
            int slots = (int)Math.Floor(selected.Count * ReseedFraction);
            if (slots <= 0 || slots >= selected.Count)
            {
                return;
            }

            // Any existing chromosome can mint a fresh random sibling via CreateNew().
            var factory = offspring.FirstOrDefault() ?? parents.FirstOrDefault();
            if (factory == null)
            {
                return;
            }

            for (int i = 0; i < slots; i++)
            {
                selected[selected.Count - 1 - i] = factory.CreateNew();
            }

            ReseedCount++;
        }
    }
}
