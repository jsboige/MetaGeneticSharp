using System.Globalization;
using System.Text;
using GeneticSharp;

namespace MetaGeneticSharp.Extensions.Tests;

/// <summary>
///   Rotation robustness of the <see cref="NaturalEvolutionStrategy"/> compound on the axis-alignment
///   bench (<see cref="RotatedFitness"/>, composed with <see cref="ShiftedFitness"/> for the CEC
///   shifted-then-rotated variant). Two parts:
///   <list type="number">
///     <item><b>Exact equivariance.</b> xNES started from a rotated population with the rotation as its
///     initial shape follows, evaluation for evaluation, the rotated image of the run on the rotated
///     landscape. SNES, the same algorithm with a diagonal covariance, does not: it is the negative
///     control that proves the test can fail.</item>
///     <item><b>The bench.</b> Rosenbrock unrotated vs rotated, seeds {0, 1, 7, 42}, the MGS-12 harness
///     (population 50, 2000 evaluations, EliteSelection / UniformCrossover / UniformMutation), best-so-far
///     objective at the 25/50/75/100 % evaluation checkpoints, median and min-max per arm. The verdict on
///     xNES vs SNES is computed with the rule pre-registered on CoursIA issue #13778 and printed as is;
///     the test asserts harness properties only (determinism, equal budgets, monotone checkpoints),
///     never the verdict.</item>
///   </list>
/// </summary>
[TestFixture]
public class NaturalEvolutionStrategyRotationTests
{
    private const int PopulationSize = 50;
    private const int EvaluationBudget = 2000;
    private const double Min = -30.0;
    private const double Max = 30.0;
    private const int RotationSeed = 7;
    private const double ObjectiveFloor = 1e-12;
    private static readonly int[] Seeds = { 0, 1, 7, 42 };
    private static readonly double[] CheckpointFractions = { 0.25, 0.50, 0.75, 1.00 };
    private static readonly string[] Arms = { "xNES", "SNES", "BBPSO", "DE", "GA", "WOA", "Random" };

    private IRandomization _previousRandomization = null!;

    [SetUp]
    public void UseSeedableFastRandom()
    {
        // The MGS-12 harness seeds FastRandomRandomization; other fixtures may switch the provider.
        _previousRandomization = RandomizationProvider.Current;
        RandomizationProvider.Current = new FastRandomRandomization();
    }

    [TearDown]
    public void RestoreRandomization()
    {
        RandomizationProvider.Current = _previousRandomization;
        FastRandomRandomization.ResetSeed(null);
    }

    // -----------------------------------------------------------------------
    // Part 1 -- exact equivariance
    // -----------------------------------------------------------------------

    /// <summary>
    /// Run A optimises <c>f(M x)</c> from a population <c>u</c> with an identity initial shape; run B
    /// optimises <c>f(x)</c> from the population <c>M u</c> with initial shape <c>M</c>. The first
    /// distribution only depends on the population through its centroid (mapped by <c>M</c>) and its
    /// total variance (invariant under an orthogonal <c>M</c>), so the xNES recursion gives
    /// <c>x_B = M x_A</c> for every sample and the two runs see the same fitness at every evaluation.
    /// Both runs consume the random stream identically (same draws, same order): run B evaluates through
    /// the identity rotation so that the chromosome clone inside <see cref="RotatedFitness"/> happens in both.
    /// </summary>
    [Test]
    public void FullCovariance_IsExactlyRotationEquivariant()
    {
        var (traceA, traceB) = RunEquivariancePair(NesCovarianceModel.Full, dimension: 5, seed: 42);

        Assert.That(traceA.Count, Is.EqualTo(EvaluationBudget));
        Assert.That(traceB.Count, Is.EqualTo(traceA.Count));
        double worst = MaxRelativeDifference(traceA, traceB);
        TestContext.Out.WriteLine($"xNES equivariance: {traceA.Count} evaluations, max relative fitness difference {worst:E2}");
        Assert.That(worst, Is.LessThan(1e-6), "xNES on f(Mx) from u must mirror xNES on f(x) from Mu, evaluation for evaluation");
    }

    /// <summary>
    /// Negative control: the same construction with the diagonal (SNES) covariance. Its samples are
    /// <c>mu + s * z</c> coordinate by coordinate, which the rotation does not commute with, so the two
    /// runs part ways from the second generation on. Without this control the equivariance test could
    /// pass for a reason unrelated to the covariance structure (a harness that ignores the rotation).
    /// </summary>
    [Test]
    public void SeparableCovariance_IsNotRotationEquivariant()
    {
        var (traceA, traceB) = RunEquivariancePair(NesCovarianceModel.Separable, dimension: 5, seed: 42);

        Assert.That(traceB.Count, Is.EqualTo(traceA.Count));
        // The first generation is identical by construction (same fitted mean and scale, x_B = M x_A only for the initial population).
        Assert.That(MaxRelativeDifference(traceA.Take(PopulationSize).ToList(), traceB.Take(PopulationSize).ToList()), Is.LessThan(1e-9));
        double worst = MaxRelativeDifference(traceA, traceB);
        TestContext.Out.WriteLine($"SNES control: max relative fitness difference {worst:E2}");
        Assert.That(worst, Is.GreaterThan(1e-3), "SNES samples coordinate-wise: its trajectories must not be rotation images of each other");
    }

    private static (List<double> TraceA, List<double> TraceB) RunEquivariancePair(NesCovarianceModel model, int dimension, int seed)
    {
        var rotation = RotationMatrices.Seeded(dimension, RotationSeed);
        int generations = EvaluationBudget / PopulationSize;

        var fitnessA = new CheckpointFitness(new RotatedFitness(new RosenbrockFitness(), rotation), Checkpoints(), keepTrace: true);
        FastRandomRandomization.ResetSeed(seed);
        RunGa(new BoxChromosome(Min, Max, dimension), fitnessA, BuildNes(model, generations, initialShape: null), generations);

        // RotatedFitness clones the chromosome, and a clone draws a fresh chromosome first: the identity
        // rotation keeps f(I x) == f(x) bit for bit while consuming the random stream exactly as run A does.
        var fitnessB = new CheckpointFitness(new RotatedFitness(new RosenbrockFitness(), RotationMatrices.Identity(dimension)), Checkpoints(), keepTrace: true);
        FastRandomRandomization.ResetSeed(seed);
        RunGa(new BoxChromosome(Min, Max, dimension, rotation), fitnessB, BuildNes(model, generations, initialShape: rotation), generations);

        return (fitnessA.Trace!, fitnessB.Trace!);
    }

    private static IMetaHeuristic BuildNes(NesCovarianceModel model, int generations, double[,]? initialShape)
    {
        var nes = new NaturalEvolutionStrategy
        {
            CovarianceModel = model,
            MaxGenerations = generations,
            InitialShape = initialShape,
        };
        nes.SetGeometricConverter(new GeometricConverter<double>
        {
            IsOrdered = false,
            GeneToDoubleConverter = (_, v) => v,
            DoubleToGeneConverter = (_, d) => d,
        });
        return nes.Build();
    }

    private static double MaxRelativeDifference(IReadOnlyList<double> a, IReadOnlyList<double> b)
    {
        double worst = 0.0;
        for (int i = 0; i < Math.Min(a.Count, b.Count); i++)
        {
            double scale = Math.Max(1.0, Math.Max(Math.Abs(a[i]), Math.Abs(b[i])));
            worst = Math.Max(worst, Math.Abs(a[i] - b[i]) / scale);
        }
        return worst;
    }

    // -----------------------------------------------------------------------
    // Part 2 -- the bench
    // -----------------------------------------------------------------------

    [Test]
    [Category("Benchmark")]
    public void RotationBench_Rosenbrock_PublishesCheckpointsAndPreRegisteredVerdict()
    {
        var report = new StringBuilder();
        report.AppendLine(Invariant($"NES rotation bench (#13778): Rosenbrock, bounds [{Min}, {Max}], population {PopulationSize}, budget {EvaluationBudget} evaluations, rotation Seeded(d, {RotationSeed}), seeds {{{string.Join(", ", Seeds)}}}"));
        report.AppendLine("Objective = best-so-far Rosenbrock value (0 is optimal), reported as median [min, max] over the seeds.");
        report.AppendLine();

        var primary = new Dictionary<string, ArmResult>();
        foreach (int dimension in new[] { 5, 10 })
        {
            foreach (bool shifted in new[] { false, true })
            {
                string condition = shifted ? "shifted" : "plain";
                var results = new Dictionary<string, ArmResult>();
                foreach (var arm in Arms)
                {
                    results[arm] = RunArm(arm, dimension, shifted);
                }

                AppendConditionTable(report, dimension, condition, results);
                if (dimension == 5 && !shifted)
                {
                    foreach (var pair in results) primary[pair.Key] = pair.Value;
                }
                else
                {
                    report.AppendLine("  secondary: " + DescribeVerdict(results["xNES"], results["SNES"]));
                    report.AppendLine();
                }
            }
        }

        report.AppendLine("PRE-REGISTERED VERDICT (primary: dimension 5, plain Rosenbrock, 100 % budget)");
        report.AppendLine("  " + DescribeVerdict(primary["xNES"], primary["SNES"]));
        TestContext.Out.Write(report.ToString());

        // Harness properties only -- the verdict is published, never asserted.
        Assert.Multiple(() =>
        {
            foreach (var pair in primary)
            {
                foreach (var run in pair.Value.Unrotated.Concat(pair.Value.Rotated))
                {
                    Assert.That(run.Evaluations, Is.LessThanOrEqualTo(EvaluationBudget), $"{pair.Key} overspent the budget");
                    for (int k = 1; k < run.Checkpoints.Length; k++)
                    {
                        Assert.That(run.Checkpoints[k], Is.LessThanOrEqualTo(run.Checkpoints[k - 1]), $"{pair.Key} best-so-far went up");
                    }
                }
            }
            foreach (var arm in new[] { "xNES", "SNES", "Random" })
            {
                Assert.That(primary[arm].Unrotated.All(r => r.Evaluations == EvaluationBudget), Is.True, $"{arm} must spend exactly the budget");
            }
        });
    }

    [Test]
    public void RotationBench_ArmsAreDeterministicPerSeed()
    {
        foreach (var arm in new[] { "xNES", "SNES", "DE" })
        {
            var first = RunSingle(arm, dimension: 5, shifted: false, rotated: true, seed: 7);
            var second = RunSingle(arm, dimension: 5, shifted: false, rotated: true, seed: 7);
            Assert.That(second.Checkpoints, Is.EqualTo(first.Checkpoints), $"{arm} is not reproducible under a fixed seed");
        }
    }

    private static ArmResult RunArm(string arm, int dimension, bool shifted)
    {
        var unrotated = Seeds.Select(seed => RunSingle(arm, dimension, shifted, rotated: false, seed)).ToList();
        var rotated = Seeds.Select(seed => RunSingle(arm, dimension, shifted, rotated: true, seed)).ToList();
        return new ArmResult(unrotated, rotated);
    }

    private static RunResult RunSingle(string arm, int dimension, bool shifted, bool rotated, int seed)
    {
        IFitness landscape = new RosenbrockFitness();
        if (shifted) landscape = new ShiftedFitness(landscape, ShiftVectors.Seeded(dimension, 10.0, RotationSeed));
        // The unrotated condition goes through the identity rotation: same decorator, same random-stream
        // consumption (RotatedFitness clones the chromosome), f(I x) == f(x) exactly. Only M differs.
        landscape = new RotatedFitness(landscape, rotated ? RotationMatrices.Seeded(dimension, RotationSeed) : RotationMatrices.Identity(dimension));
        var fitness = new CheckpointFitness(landscape, Checkpoints(), keepTrace: false);

        if (arm == "Random")
        {
            // A per-seed random search: the unbiased control, blind to both shift and rotation.
            new RandomSearchOptimizer(seed).Run(new OptimizerRequest(fitness, (Min, Max), dimension, EvaluationBudget));
            return new RunResult(fitness.CheckpointObjectives(), fitness.Evaluations);
        }

        int generations = Math.Max(1, EvaluationBudget / PopulationSize);
        FastRandomRandomization.ResetSeed(seed);
        IMetaHeuristic metaHeuristic = arm switch
        {
            "xNES" => MetaHeuristicsService.CreateMetaHeuristicByName(nameof(KnownCompoundMetaheuristics.ExponentialNaturalEvolutionStrategy), maxGenerations: generations, populationSize: PopulationSize),
            "SNES" => MetaHeuristicsService.CreateMetaHeuristicByName(nameof(KnownCompoundMetaheuristics.SeparableNaturalEvolutionStrategy), maxGenerations: generations, populationSize: PopulationSize),
            "BBPSO" => MetaHeuristicsService.CreateMetaHeuristicByName(nameof(KnownCompoundMetaheuristics.BareBonesParticleSwarm), maxGenerations: generations, populationSize: PopulationSize),
            "DE" => MetaHeuristicsService.CreateMetaHeuristicByName(nameof(KnownCompoundMetaheuristics.DifferentialEvolution), maxGenerations: generations, populationSize: PopulationSize),
            "WOA" => MetaHeuristicsService.CreateMetaHeuristicByName(nameof(KnownCompoundMetaheuristics.WhaleOptimisation), maxGenerations: generations, populationSize: PopulationSize),
            "GA" => new DefaultMetaHeuristic(),
            _ => throw new ArgumentOutOfRangeException(nameof(arm)),
        };
        RunGa(new BoxChromosome(Min, Max, dimension), fitness, metaHeuristic, generations);
        return new RunResult(fitness.CheckpointObjectives(), fitness.Evaluations);
    }

    private static void RunGa(IChromosome adam, IFitness fitness, IMetaHeuristic metaHeuristic, int generations)
    {
        var population = new MetaPopulation(PopulationSize, PopulationSize, adam);
        var ga = new MetaGeneticAlgorithm(population, fitness, new EliteSelection(), new UniformCrossover(0.5f), new UniformMutation(true), metaHeuristic)
        {
            Termination = new GenerationNumberTermination(generations),
        };
        ga.Start();
    }

    private static int[] Checkpoints() => CheckpointFractions.Select(f => (int)Math.Round(f * EvaluationBudget)).ToArray();

    // -----------------------------------------------------------------------
    // Reporting and the pre-registered verdict
    // -----------------------------------------------------------------------

    private static void AppendConditionTable(StringBuilder report, int dimension, string condition, IReadOnlyDictionary<string, ArmResult> results)
    {
        report.AppendLine(Invariant($"dimension {dimension}, {condition} Rosenbrock"));
        report.AppendLine("  arm     landscape  " + string.Join("  ", CheckpointFractions.Select(f => Invariant($"{f * 100,4:0}%").PadRight(27))) + "evals   rho = log10(rot/unrot) at 100%");
        foreach (var arm in Arms)
        {
            var r = results[arm];
            report.AppendLine("  " + arm.PadRight(7) + " unrotated  " + CheckpointCells(r.Unrotated) + Invariant($"{r.Unrotated.Max(x => x.Evaluations),5}"));
            var rho = r.Rho();
            report.AppendLine("  " + "".PadRight(7) + " rotated    " + CheckpointCells(r.Rotated) + Invariant($"{r.Rotated.Max(x => x.Evaluations),5}   ") + Summary(rho));
        }
    }

    private static string CheckpointCells(IReadOnlyList<RunResult> runs)
    {
        var cells = new StringBuilder();
        for (int k = 0; k < CheckpointFractions.Length; k++)
        {
            cells.Append(Summary(runs.Select(r => r.Checkpoints[k]).ToArray()).PadRight(29));
        }
        return cells.ToString();
    }

    private static string Summary(IReadOnlyList<double> values) =>
        Invariant($"{Median(values):G4} [{values.Min():G3}, {values.Max():G3}]");

    /// <summary>
    /// The rule pre-registered in the #13778 claim, on rho_s = log10(obj_rot / obj_unrot) per seed at
    /// 100 % of the budget (objectives floored at 1e-12): IMPROVES if median |rho|(xNES) is at most half
    /// of median |rho|(SNES) and |rho_s|(xNES) &lt; |rho_s|(SNES) on at least 3 of the 4 seeds;
    /// NO IMPROVEMENT if median |rho|(xNES) &gt;= median |rho|(SNES); INCONCLUSIVE otherwise.
    /// TRADE-OFF flags xNES being worse than SNES in absolute rotated quality (median objective).
    /// </summary>
    private static string DescribeVerdict(ArmResult xnes, ArmResult snes)
    {
        var absX = xnes.Rho().Select(Math.Abs).ToArray();
        var absS = snes.Rho().Select(Math.Abs).ToArray();
        double medX = Median(absX);
        double medS = Median(absS);
        int seedWins = absX.Zip(absS, (x, s) => x < s).Count(w => w);

        string verdict;
        if (medX >= medS) verdict = "NO IMPROVEMENT";
        else if (medX <= 0.5 * medS && seedWins >= 3) verdict = "IMPROVES";
        else verdict = "INCONCLUSIVE";

        double rotX = Median(xnes.Rotated.Select(r => r.Checkpoints[^1]).ToArray());
        double rotS = Median(snes.Rotated.Select(r => r.Checkpoints[^1]).ToArray());
        string flag = rotX > rotS ? " + TRADE-OFF (xNES worse than SNES on the rotated landscape)" : "";

        return Invariant($"{verdict}{flag} -- median |rho| xNES {medX:G3} vs SNES {medS:G3}; xNES less rotation-sensitive on {seedWins}/{absX.Length} seeds; rotated median objective xNES {rotX:G4} vs SNES {rotS:G4}");
    }

    private static double Median(IReadOnlyList<double> values)
    {
        var sorted = values.OrderBy(v => v).ToArray();
        int n = sorted.Length;
        return n % 2 == 1 ? sorted[n / 2] : 0.5 * (sorted[n / 2 - 1] + sorted[n / 2]);
    }

    private static string Invariant(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);

    private sealed record RunResult(double[] Checkpoints, int Evaluations);

    private sealed record ArmResult(IReadOnlyList<RunResult> Unrotated, IReadOnlyList<RunResult> Rotated)
    {
        public double[] Rho() => Unrotated.Zip(Rotated, (u, r) =>
            Math.Log10(Math.Max(r.Checkpoints[^1], ObjectiveFloor) / Math.Max(u.Checkpoints[^1], ObjectiveFloor))).ToArray();
    }

    // -----------------------------------------------------------------------
    // Harness pieces
    // -----------------------------------------------------------------------

    /// <summary>
    /// Records the best-so-far objective (the negated fitness) at fixed evaluation counts, and
    /// optionally the full fitness trace. Pass-through for the fitness value itself.
    /// </summary>
    private sealed class CheckpointFitness : IFitness
    {
        private readonly IFitness _inner;
        private readonly int[] _checkpoints;
        private readonly double[] _values;

        public CheckpointFitness(IFitness inner, int[] checkpoints, bool keepTrace)
        {
            _inner = inner;
            _checkpoints = checkpoints;
            _values = Enumerable.Repeat(double.NaN, checkpoints.Length).ToArray();
            Trace = keepTrace ? new List<double>() : null;
        }

        public int Evaluations { get; private set; }

        public double BestObjective { get; private set; } = double.PositiveInfinity;

        public List<double>? Trace { get; }

        public double Evaluate(IChromosome chromosome)
        {
            double fitness = _inner.Evaluate(chromosome);
            Evaluations++;
            BestObjective = Math.Min(BestObjective, -fitness);
            Trace?.Add(fitness);
            for (int k = 0; k < _checkpoints.Length; k++)
            {
                if (Evaluations == _checkpoints[k]) _values[k] = BestObjective;
            }
            return fitness;
        }

        /// <summary>The checkpoint objectives; a checkpoint the run never reached takes the final best.</summary>
        public double[] CheckpointObjectives() => _values.Select(v => double.IsNaN(v) ? BestObjective : v).ToArray();
    }

    /// <summary>
    /// A chromosome drawing each gene uniformly in [min, max] (the MGS-12 initial population), or,
    /// given a rotation <c>M</c>, the rotated draw <c>M u</c>. Both variants consume exactly the same
    /// random numbers, which is what makes the equivariance pair comparable draw for draw.
    /// </summary>
    private sealed class BoxChromosome : ChromosomeBase
    {
        private readonly double _min;
        private readonly double _max;
        private readonly double[,]? _rotation;

        public BoxChromosome(double min, double max, int length, double[,]? rotation = null) : base(length)
        {
            _min = min;
            _max = max;
            _rotation = rotation;
            var u = new double[length];
            for (int i = 0; i < length; i++) u[i] = Draw();
            var x = rotation == null ? u : NaturalEvolutionStrategyMath.Multiply(rotation, u);
            for (int i = 0; i < length; i++) ReplaceGene(i, new Gene(x[i]));
        }

        public override IChromosome CreateNew() => new BoxChromosome(_min, _max, Length, _rotation);

        public override Gene GenerateGene(int geneIndex) => new Gene(Draw());

        private double Draw() => _min + RandomizationProvider.Current.GetDouble() * (_max - _min);
    }
}
