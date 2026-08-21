#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using GeneticSharp;

namespace MetaGeneticSharp
{
    /// <summary>
    ///   The Scatter Search reference-set update expressed as a reinsertion: keep the
    ///   <c>b1 = round(N * QualityFraction)</c> fittest candidates by quality, then fill the
    ///   remaining slots with the candidates that are <i>most distant</i> from the already-selected
    ///   set (max-min diversity, the classic RefSet construction of Laguna &amp; Marti). This is the
    ///   b1-quality + b2-diversity split that distinguishes Scatter Search from plain elitism: pure
    ///   quality (the <see cref="FitnessBasedElitistReinsertion"/> limit, QualityFraction = 1)
    ///   collapses the reference set onto the best point and starves the combination operator of
    ///   distant mates.
    /// </summary>
    public class ScatterSearchReinsertion : ReinsertionBase
    {
        /// <summary>
        /// Builds the reference-set reinsertion.
        /// </summary>
        /// <param name="qualityFraction">
        /// The fraction of the population kept by pure quality (0..1); the complement is kept by
        /// max-min diversity.
        /// </param>
        public ScatterSearchReinsertion(double qualityFraction)
            : base(true, true)
        {
            QualityFraction = qualityFraction;
        }

        /// <summary>The fraction of slots filled by quality; the rest by diversity.</summary>
        public double QualityFraction { get; }

        /// <inheritdoc />
        protected override IList<IChromosome> PerformSelectChromosomes(IPopulation population, IList<IChromosome> offspring, IList<IChromosome> parents)
        {
            var candidates = parents.Concat(offspring).ToList();
            if (candidates.Count == 0)
            {
                return candidates;
            }

            int target = Math.Min(population.MinSize, candidates.Count);
            int qualitySlots = Math.Max(1, Math.Min(target, (int)Math.Round(target * QualityFraction)));

            var selected = candidates
                .OrderByDescending(c => c.Fitness)
                .Take(qualitySlots)
                .ToList();
            var remaining = candidates.Except(selected).ToList();

            // Max-min diversity: repeatedly admit the candidate whose distance to the NEAREST
            // selected member is the LARGEST (the classic reference-set diversification).
            while (selected.Count < target && remaining.Count > 0)
            {
                var next = remaining
                    .OrderByDescending(c => selected.Min(s => Distance(c, s)))
                    .First();
                selected.Add(next);
                remaining.Remove(next);
            }

            return selected;
        }

        /// <summary>
        /// A representation-agnostic distance between two chromosomes: the RMS of gene-wise
        /// separations, where a gene pair contributes its absolute double-converted difference when
        /// both genes are numeric and a 0/1 equality indicator otherwise. Zero if and only if every
        /// gene is equal; used only as a relative ranking inside one population, so scale-free
        /// normalisation across problems is not required.
        /// </summary>
        public static double Distance(IChromosome a, IChromosome b)
        {
            var ga = a.GetGenes().ToArray();
            var gb = b.GetGenes().ToArray();
            int n = Math.Min(ga.Length, gb.Length);
            if (n == 0)
            {
                return 0.0;
            }

            double sumSquares = 0.0;
            for (int i = 0; i < n; i++)
            {
                double d;
                if (TryConvert(ga[i].Value, out double va) && TryConvert(gb[i].Value, out double vb))
                {
                    d = Math.Abs(va - vb);
                }
                else
                {
                    d = Equals(ga[i].Value, gb[i].Value) ? 0.0 : 1.0;
                }

                sumSquares += d * d;
            }

            return Math.Sqrt(sumSquares / n);
        }

        private static bool TryConvert(object value, out double result)
        {
            try
            {
                result = Convert.ToDouble(value);
                return true;
            }
            catch
            {
                result = 0.0;
                return false;
            }
        }
    }
}
