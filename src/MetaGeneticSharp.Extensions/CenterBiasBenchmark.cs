using GeneticSharp;

namespace MetaGeneticSharp
{
    // ===========================================================================
    // mealpy-budget center-bias comparison harness.
    // ---------------------------------------------------------------------------
    // Grounds the "consolidation without performance loss" claim the way mealpy
    // grounds its own comparisons: every optimizer gets the SAME function-evaluation
    // budget (NFE = epochs x population), so a fast-converging compound and a plain
    // search are scored on equal terms instead of equal wall-clock or equal
    // generations.
    //
    // On top of the equal-budget rule it runs the center-bias protocol (Kudela,
    // Nature Machine Intelligence 4, 2022): each benchmark function is evaluated in
    // BOTH its centered form (optimum at the domain center) and a seeded-shifted
    // form (optimum relocated by ShiftVectors.Seeded), under the same budget. The
    // reported delta = shifted objective - centered objective is the bias signature:
    //   * a center-biased optimizer concentrates samples near zero and does markedly
    //     worse once the optimum moves  -> large positive delta;
    //   * an unbiased optimizer (uniform random search is the canonical control)
    //     covers the domain evenly and does about as well either way -> delta ~ 0.
    //
    // The harness is optimizer-agnostic: it drives any optimizer expressed as the
    // Optimizer delegate, so the same equal-budget centered-vs-shifted measurement
    // applies to RandomSearchOptimizer here, to the geometric compounds (WOA, EO,
    // FBI), or to an external mealpy run wrapped behind the delegate.
    // ===========================================================================

    /// <summary>
    /// An equal evaluation budget shared by every optimizer in a comparison, expressed
    /// as a number of fitness evaluations (mealpy's NFE). Converting to a generation
    /// count for a population-based engine is <c>floor(MaxEvaluations / populationSize)</c>,
    /// clamped to at least one generation.
    /// </summary>
    public sealed class EvaluationBudget
    {
        /// <summary>Total number of fitness evaluations every optimizer is allowed.</summary>
        public int MaxEvaluations { get; }

        public EvaluationBudget(int maxEvaluations)
        {
            if (maxEvaluations <= 0) throw new ArgumentOutOfRangeException(nameof(maxEvaluations));
            MaxEvaluations = maxEvaluations;
        }

        /// <summary>
        /// Generations a population-based engine may run to spend exactly this budget:
        /// <c>floor(MaxEvaluations / populationSize)</c>, at least 1. This is how an
        /// epochs-based mealpy budget maps onto GeneticSharp's per-generation loop.
        /// </summary>
        public int GenerationsFor(int populationSize)
        {
            if (populationSize <= 0) throw new ArgumentOutOfRangeException(nameof(populationSize));
            return Math.Max(1, MaxEvaluations / populationSize);
        }
    }

    /// <summary>
    /// Decorates an <see cref="IFitness"/> to count how many times it is evaluated, so a
    /// run can be checked against an <see cref="EvaluationBudget"/>. The inner value is
    /// passed through unchanged; only the call count is observed.
    /// </summary>
    public sealed class CountingFitness : IFitness
    {
        private readonly IFitness _inner;

        /// <summary>Number of <see cref="Evaluate"/> calls so far.</summary>
        public int Evaluations { get; private set; }

        public CountingFitness(IFitness inner)
        {
            ArgumentNullException.ThrowIfNull(inner);
            _inner = inner;
        }

        public double Evaluate(IChromosome chromosome)
        {
            Evaluations++;
            return _inner.Evaluate(chromosome);
        }

        /// <summary>True once the counted evaluations have reached the budget.</summary>
        public bool IsExhausted(EvaluationBudget budget)
        {
            ArgumentNullException.ThrowIfNull(budget);
            return Evaluations >= budget.MaxEvaluations;
        }
    }

    /// <summary>
    /// A single optimization request: minimize <paramref name="Fitness"/> over the
    /// box <c>[Min, Max]^Dimension</c> using at most <paramref name="Evaluations"/>
    /// fitness calls. An <see cref="Optimizer"/> returns the best fitness it found
    /// (the GeneticSharp maximization convention: closer to 0 is better, since the
    /// benchmark functions negate their objective).
    /// </summary>
    public sealed record OptimizerRequest(IFitness Fitness, (double Min, double Max) Bounds, int Dimension, int Evaluations);

    /// <summary>
    /// Any optimizer reduced to its measurable contract for the harness: consume the
    /// requested evaluation budget over the requested box and return the best fitness.
    /// </summary>
    public delegate double Optimizer(OptimizerRequest request);

    /// <summary>
    /// Uniform random search over the box bounds: the unbiased control baseline. It
    /// samples points independently and uniformly, so it has no center preference and
    /// its centered-vs-shifted delta should sit near zero — the contrast that makes a
    /// biased optimizer's positive delta meaningful. Seeded for reproducibility; each
    /// <see cref="Run"/> call starts a fresh RNG from the seed, so the same request
    /// always draws the same points.
    /// </summary>
    public sealed class RandomSearchOptimizer
    {
        private readonly int _seed;

        public RandomSearchOptimizer(int seed = 0)
        {
            _seed = seed;
        }

        /// <summary>
        /// Draw <see cref="OptimizerRequest.Evaluations"/> uniform points in the box and
        /// return the best (maximum) fitness. Spends exactly the requested budget.
        /// </summary>
        public double Run(OptimizerRequest request)
        {
            ArgumentNullException.ThrowIfNull(request);
            if (request.Dimension < 2) throw new ArgumentOutOfRangeException(nameof(request), "Dimension must be >= 2 (ChromosomeBase requires at least two genes).");
            if (request.Evaluations <= 0) throw new ArgumentOutOfRangeException(nameof(request), "Evaluations must be positive.");

            var rng = new Random(_seed);
            (double min, double max) = request.Bounds;
            double span = max - min;
            double best = double.NegativeInfinity;
            double[] point = new double[request.Dimension];

            for (int e = 0; e < request.Evaluations; e++)
            {
                for (int d = 0; d < request.Dimension; d++)
                    point[d] = min + rng.NextDouble() * span;

                double fitness = request.Fitness.Evaluate(new PointChromosome(point));
                if (fitness > best) best = fitness;
            }

            return best;
        }
    }

    /// <summary>
    /// One centered-vs-shifted measurement for a single benchmark function under a
    /// fixed evaluation budget. Objectives are the true (positive, minimized) values:
    /// the harness negates the engine's maximized fitness back to the objective scale,
    /// so 0 is optimal and larger is worse. <see cref="Delta"/> is the bias signature.
    /// </summary>
    public sealed record CenterBiasResult(
        string Function,
        int Dimension,
        double CenteredObjective,
        double ShiftedObjective,
        int CenteredEvaluations,
        int ShiftedEvaluations,
        IReadOnlyList<double> Shift)
    {
        /// <summary>Shifted minus centered objective: a large positive value is the center-bias signature.</summary>
        public double Delta => ShiftedObjective - CenteredObjective;

        /// <summary>Compact one-line row for a results table (notebook / console rendering).</summary>
        public string ToRow() =>
            $"{Function,-16} dim={Dimension,2}  centered={CenteredObjective,12:0.000000}  shifted={ShiftedObjective,12:0.000000}  delta={Delta,12:0.000000}";
    }

    /// <summary>
    /// The center-bias comparison harness: drive an <see cref="Optimizer"/> over a
    /// benchmark function in its centered and seeded-shifted forms under the same
    /// <see cref="EvaluationBudget"/>, and report the objective delta.
    /// </summary>
    public static class CenterBiasBenchmark
    {
        /// <summary>
        /// Run one benchmark function centered and shifted under the same budget.
        /// </summary>
        /// <param name="inner">The benchmark function (its bounds come from <see cref="KnownFunctionsBounds"/>).</param>
        /// <param name="dimension">Problem dimension (>= 2).</param>
        /// <param name="budget">Shared evaluation budget for both runs.</param>
        /// <param name="optimizer">The optimizer under test, as an <see cref="Optimizer"/> delegate.</param>
        /// <param name="shiftMagnitude">Per-dimension shift amplitude for the shifted run (kept small enough that the relocated optimum stays in bounds).</param>
        /// <param name="seed">Seed for the shift vector (reproducible relocation).</param>
        /// <param name="name">Optional label; defaults to the inner function's type name.</param>
        public static CenterBiasResult Run(
            IFitness inner,
            int dimension,
            EvaluationBudget budget,
            Optimizer optimizer,
            double shiftMagnitude,
            int seed,
            string? name = null)
        {
            ArgumentNullException.ThrowIfNull(inner);
            ArgumentNullException.ThrowIfNull(budget);
            ArgumentNullException.ThrowIfNull(optimizer);
            if (dimension < 2) throw new ArgumentOutOfRangeException(nameof(dimension), "Dimension must be >= 2.");

            (double, double) bounds = KnownFunctionsBounds.For(inner.GetType());

            // Centered run: the function as published, optimum at the domain center.
            var centeredFitness = new CountingFitness(inner);
            double centeredBest = optimizer(new OptimizerRequest(centeredFitness, bounds, dimension, budget.MaxEvaluations));

            // Shifted run: same function, optimum relocated off-center by a seeded vector.
            double[] shift = ShiftVectors.Seeded(dimension, shiftMagnitude, seed);
            var shiftedFitness = new CountingFitness(new ShiftedFitness(inner, shift));
            double shiftedBest = optimizer(new OptimizerRequest(shiftedFitness, bounds, dimension, budget.MaxEvaluations));

            // The functions negate their objective (engine maximizes); negate the best
            // fitness back to the objective scale so 0 is optimal and larger is worse.
            return new CenterBiasResult(
                name ?? inner.GetType().Name,
                dimension,
                CenteredObjective: -centeredBest,
                ShiftedObjective: -shiftedBest,
                centeredFitness.Evaluations,
                shiftedFitness.Evaluations,
                shift);
        }

        /// <summary>
        /// Run a whole suite of (function, dimension) pairs under the same optimizer,
        /// budget, shift magnitude and seed — the "runs every function in both centered
        /// and shifted form by default" entry point. Each function is seeded distinctly
        /// (seed + index) so the relocations are not identical across functions.
        /// </summary>
        public static IReadOnlyList<CenterBiasResult> RunSuite(
            IReadOnlyList<(IFitness fitness, int dimension)> problems,
            EvaluationBudget budget,
            Optimizer optimizer,
            double shiftMagnitude,
            int seed)
        {
            ArgumentNullException.ThrowIfNull(problems);
            var results = new List<CenterBiasResult>(problems.Count);
            for (int i = 0; i < problems.Count; i++)
            {
                (IFitness fitness, int dimension) = problems[i];
                results.Add(Run(fitness, dimension, budget, optimizer, shiftMagnitude, seed + i));
            }
            return results;
        }
    }

    /// <summary>
    /// Minimal concrete chromosome wrapping a double[] point, used internally by
    /// <see cref="RandomSearchOptimizer"/> to feed candidate coordinates to an
    /// <see cref="IFitness"/>. Geometry-agnostic: it stores gene values as doubles, the
    /// representation contract <see cref="KnownFunctionGenes"/> reads. Not a GA operand
    /// (random search uses no crossover/mutation), so <see cref="GenerateGene"/> and
    /// <see cref="CreateNew"/> only need to preserve length for <c>Clone()</c>.
    /// </summary>
    internal sealed class PointChromosome : ChromosomeBase
    {
        public PointChromosome(double[] values) : base(values.Length)
        {
            for (int i = 0; i < values.Length; i++)
                ReplaceGene(i, new Gene(values[i]));
        }

        public override IChromosome CreateNew() => new PointChromosome(new double[Length]);

        public override Gene GenerateGene(int geneIndex) => new Gene(0.0);
    }
}
