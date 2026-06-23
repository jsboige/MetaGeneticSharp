#nullable disable

using System;
using System.Collections.Generic;
using System.Linq;
using GeneticSharp;

namespace MetaGeneticSharp
{
    /// <summary>
    /// Metropolis acceptance reinsertion for the Simulated-Annealing compound. For each index of the
    /// parent/offspring populations (same length), the offspring -- a perturbed neighbour of the parent
    /// -- is accepted if it is at least as fit as the parent; otherwise it is accepted with the Metropolis
    /// probability <c>exp((f_off - f_par) / T_k)</c>, so uphill moves survive early in the run and are
    /// frozen out as the temperature cools. The temperature follows a geometric cooling schedule
    /// <c>T_k = T_0 * alpha^k</c> read from the population's current generation number, so the schedule is
    /// stateless (no per-individual memory) -- this is what makes simulated annealing expressible in a
    /// framework whose only matching kinds are Current / Random / Best / Worst (no per-particle state).
    /// Leftover offspring beyond the paired range fill any remaining slots up to MinSize, mirroring the
    /// pairwise reinsertion. Ported pattern from GeneticSharp.Domain.Reinsertions (PR giacomelli/GeneticSharp#87).
    /// </summary>
    public class MetropolisReinsertion : ReinsertionBase
    {
        /// <summary>
        /// Creates a Metropolis reinsertion with the given geometric cooling schedule.
        /// </summary>
        /// <param name="initialTemperature">T_0, the temperature at generation 0.</param>
        /// <param name="coolingRate">alpha in T_k = T_0 * alpha^k (should lie in (0, 1)).</param>
        public MetropolisReinsertion(double initialTemperature = 1.0, double coolingRate = 0.95)
            : base(true, true)
        {
            InitialTemperature = initialTemperature;
            CoolingRate = coolingRate;
        }

        /// <summary>T_0, the temperature at generation 0.</summary>
        public double InitialTemperature { get; set; }

        /// <summary>alpha in T_k = T_0 * alpha^k. Should lie in (0, 1).</summary>
        public double CoolingRate { get; set; }

        /// <summary>The current temperature T_k = T_0 * alpha^generationsNumber.</summary>
        public double CurrentTemperature(int generationsNumber)
        {
            double t = InitialTemperature * Math.Pow(CoolingRate, generationsNumber);
            // Guard against a degenerate (non-positive) schedule: a frozen temperature means greedy
            // acceptance (keep a neighbour only if it is fitter), which is the T -> 0 limit.
            return t > 0.0 ? t : 0.0;
        }

        /// <inheritdoc />
        protected override IList<IChromosome> PerformSelectChromosomes(IPopulation population, IList<IChromosome> offspring, IList<IChromosome> parents)
        {
            int maxCrossIndex = Math.Min(parents.Count, offspring.Count);
            var toReturn = new List<IChromosome>(parents);
            var rnd = RandomizationProvider.Current;

            double temperature = CurrentTemperature(population.GenerationsNumber);

            for (int i = 0; i < maxCrossIndex; i++)
            {
                var parent = toReturn[i];
                var child = offspring[i];

                // Un-evaluated fitness is treated as the worst possible value, so an evaluated neighbour is
                // always preferred over an un-evaluated incumbent (and conversely an un-evaluated neighbour
                // never displaces an evaluated incumbent unless the incumbent is itself un-evaluated).
                double parentFitness = parent.Fitness ?? double.NegativeInfinity;
                double childFitness = child.Fitness ?? double.NegativeInfinity;

                bool accept;
                if (childFitness >= parentFitness)
                {
                    // Downhill (or level) move: always accept.
                    accept = true;
                }
                else if (temperature <= 0.0)
                {
                    // Frozen schedule: greedy -- a strictly worse neighbour is rejected.
                    accept = false;
                }
                else
                {
                    // Uphill move: accept with the Metropolis probability exp(delta / T), delta < 0.
                    double delta = childFitness - parentFitness;
                    accept = rnd.GetDouble() < Math.Exp(delta / temperature);
                }

                if (accept)
                {
                    toReturn[i] = child;
                }
            }

            int missingNb = population.MinSize - toReturn.Count;
            if (missingNb > 0 && offspring.Count > maxCrossIndex)
            {
                var leftOver = offspring.Skip(maxCrossIndex).ToArray();
                foreach (var chromosome in leftOver.Take(missingNb))
                {
                    toReturn.Add(chromosome);
                }
            }

            return toReturn;
        }
    }
}
