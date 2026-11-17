using System;
using System.Collections.Generic;

namespace MetaGeneticSharp
{
    /// <summary>
    /// Coordinates independent CMA-ES runs with a doubled population after each restart.
    /// The caller owns the genetic algorithm and creates a fresh run for each invocation.
    /// </summary>
    public static class IpopRestartController
    {
        /// <summary>
        /// Runs the supplied optimizer at most <paramref name="maxRestarts"/> plus one times.
        /// Fitness is maximized, as in GeneticSharp. The callback decides whether the target or
        /// another global stopping condition has been met; no further run starts after it does.
        /// </summary>
        public static IpopResult<T> Run<T>(int initialPopulationSize, int maxRestarts,
            Func<int, int, IpopRunResult<T>> run)
        {
            ArgumentNullException.ThrowIfNull(run);
            if (initialPopulationSize < 2)
                throw new ArgumentOutOfRangeException(nameof(initialPopulationSize));
            if (maxRestarts < 0)
                throw new ArgumentOutOfRangeException(nameof(maxRestarts));

            var sizes = new List<int>();
            IpopRunResult<T> best = default;
            bool hasBest = false;
            int populationSize = initialPopulationSize;
            for (int restart = 0; ; restart++)
            {
                var result = run(populationSize, restart);
                if (double.IsNaN(result.Fitness))
                    throw new InvalidOperationException("A run returned NaN fitness.");
                sizes.Add(populationSize);
                if (!hasBest || result.Fitness > best.Fitness)
                {
                    best = result;
                    hasBest = true;
                }
                if (result.Stop || restart == maxRestarts)
                    return new IpopResult<T>(best.Value, best.Fitness, sizes.AsReadOnly(), result.Stop);
                populationSize = checked(populationSize * 2);
            }
        }
    }

    /// <summary>The best value and termination decision of one independent optimizer run.</summary>
    public readonly record struct IpopRunResult<T>(T Value, double Fitness, bool Stop);

    /// <summary>The best result across runs, the actual population sizes and whether a callback stopped the search.</summary>
    public sealed record IpopResult<T>(T Value, double Fitness, IReadOnlyList<int> PopulationSizes, bool Stopped);
}
