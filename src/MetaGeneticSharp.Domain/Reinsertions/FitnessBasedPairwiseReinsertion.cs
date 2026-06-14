#nullable disable

using System;
using System.Collections.Generic;
using System.Linq;
using GeneticSharp;

namespace MetaGeneticSharp
{
    /// <summary>
    /// Fitness-based pairwise reinsertion. For each index of the parent/offspring
    /// populations (same length), compares the fitness of the parent and offspring
    /// individuals at that index and keeps the one with the better fitness. Leftover
    /// offspring beyond the paired range fill any remaining slots up to MinSize.
    /// This is the canonical reinsertion of the Forensic-Based Investigation compound.
    /// Ported from GeneticSharp.Domain.Reinsertions (PR giacomelli/GeneticSharp#87).
    /// </summary>
    public class FitnessBasedPairwiseReinsertion : ReinsertionBase
    {
        public FitnessBasedPairwiseReinsertion()
            : base(true, true)
        {
        }

        protected override IList<IChromosome> PerformSelectChromosomes(IPopulation population, IList<IChromosome> offspring, IList<IChromosome> parents)
        {
            int maxCrossIndex = Math.Min(parents.Count, offspring.Count);
            var toReturn = new List<IChromosome>(parents);

            for (int i = 0; i < maxCrossIndex; i++)
            {
                if (toReturn[i].Fitness < offspring[i].Fitness)
                {
                    toReturn[i] = offspring[i];
                }
            }

            int missingNb = population.MinSize - toReturn.Count;
            if (missingNb > 0)
            {
                if (offspring.Count > maxCrossIndex)
                {
                    var leftOver = offspring.Skip(maxCrossIndex).ToArray();
                    if (leftOver.Length >= missingNb)
                    {
                        foreach (var chromosome in leftOver.Take(missingNb))
                        {
                            toReturn.Add(chromosome);
                        }
                    }
                }
            }

            return toReturn;
        }
    }
}
