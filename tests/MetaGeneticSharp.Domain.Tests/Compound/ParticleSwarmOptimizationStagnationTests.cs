using GeneticSharp;
using MetaGeneticSharp;

namespace MetaGeneticSharp.Domain.Tests.Compound;

/// <summary>
/// Instrumentation of late stagnation in the Particle Swarm Optimizer (#13778, wave 1 item P1).
///
/// The MGS-vs-mealpy face-off of MGS-22 and the convergence checkpoints of MGS-22/#13767 measured
/// a profile on the MGS arms that the final conflict count cannot explain. For the Equilibrium
/// Optimizer the probe of <see cref="EquilibriumOptimizerDiversityTests"/> located the closure at
/// the reinsertion tie. For PSO the hypothesis is different and must be tested on its own
/// mechanism: the velocity/position recurrence of <see cref="ParticleSwarmOptimization"/> clamps
/// against the attractor span, <c>|v| &lt;= MaxVelocityRatio * max(|pbest - x|, |gbest - x|)</c>,
/// which shrinks as the particles approach their attractors and freezes exactly when a particle
/// coincides with both.
///
/// The final best value cannot separate the two ways a particle can stop moving:
///
///  * <b>healthy convergence</b> — the particle sits on the global best, so nothing is left to
///    travel;
///  * <b>stagnation</b> — the particle stopped while still away from the global best, which is the
///    defect the corrective loop of #13778 is looking for.
///
/// This test separates them without touching the library: it wraps the public
/// <see cref="ParticleSwarmOptimization.UpdateOperator"/> and records, per gene update, the step
/// actually taken and the distance that was left to the global best. Everything below is a
/// <b>proxy</b> measured from the outside — the clamp ratio is inferred from
/// <c>step / gap == MaxVelocityRatio</c>, it is not read from an instrumented clamp.
///
/// Two substrates keep the reading honest: Rastrigin 36-D has one global optimum, so a collapse
/// there is convergence; the Sudoku R1 substrate of MGS-26 decodes by rounding, so many positions
/// give the same grid.
///
/// The causal pre-test is the cheapest lever the public surface offers: raising
/// <see cref="ParticleSwarmOptimization.MaxVelocityRatio"/> from its shipped 2.0 to a value the
/// clamp cannot bind (10.0), on the same seeds and budget. If the stuck share is unchanged, the
/// clamp is not the cause and the hypothesis is refuted by measurement rather than by argument.
///
/// The test asserts only what must hold for every arm (budget reached, one sample per generation,
/// non-empty population, finite values). The tables are the measurement.
/// </summary>
public class ParticleSwarmOptimizationStagnationTests
{
    private const int PopSize = 50;
    private const int Generations = 160;

    /// <summary>Below this, a step is considered zero (genes are doubles, so equality is not used).</summary>
    private const double StepEpsilon = 1e-12;

    /// <summary>A particle this close to the global best has nowhere left to go.</summary>
    private const double GapEpsilon = 1e-6;

    private const string PuzzleLine =
        "902005403100063025508407060026309001057010290090670530240530600705200304080041950";

    private enum Substrate { Rastrigin, SudokuR1 }

    // ---------------------------------------------------------------- substrates

    private static int[,] ParsePuzzle()
    {
        var grid = new int[9, 9];
        for (int i = 0; i < 81; i++) grid[i / 9, i % 9] = PuzzleLine[i] - '0';
        return grid;
    }

    private static List<(int Row, int Col)> EmptyCells()
    {
        var cells = new List<(int, int)>();
        var puzzle = ParsePuzzle();
        for (int r = 0; r < 9; r++)
            for (int c = 0; c < 9; c++)
                if (puzzle[r, c] == 0) cells.Add((r, c));
        return cells;
    }

    private static int CountConflicts(int[,] grid)
    {
        int conflicts = 0;
        for (int i = 0; i < 9; i++)
        {
            var row = new HashSet<int>();
            var col = new HashSet<int>();
            var block = new HashSet<int>();
            for (int j = 0; j < 9; j++)
            {
                if (!row.Add(grid[i, j])) conflicts++;
                if (!col.Add(grid[j, i])) conflicts++;
                int br = 3 * (i / 3) + j / 3, bc = 3 * (i % 3) + j % 3;
                if (!block.Add(grid[br, bc])) conflicts++;
            }
        }
        return conflicts;
    }

    /// <summary>R1 decoding: round then clamp to 1..9 on the empty cells, reading order.</summary>
    private static int[,] DecodeR1(double[] genes)
    {
        var grid = ParsePuzzle();
        var empties = EmptyCells();
        for (int k = 0; k < empties.Count && k < genes.Length; k++)
            grid[empties[k].Row, empties[k].Col] = Math.Max(1, Math.Min(9, (int)Math.Round(genes[k])));
        return grid;
    }

    private static string GridKey(int[,] grid)
    {
        var chars = new char[81];
        for (int i = 0; i < 81; i++) chars[i] = (char)('0' + grid[i / 9, i % 9]);
        return new string(chars);
    }

    private static double Rastrigin(double[] x)
    {
        double sum = 10.0 * x.Length;
        foreach (var xi in x) sum += xi * xi - 10.0 * Math.Cos(2.0 * Math.PI * xi);
        return sum;
    }

    // ---------------------------------------------------------------- chromosome

    private static int GeneCount = 36;
    private static double Lo = -5.12;
    private static double Hi = 5.12;

    private sealed class ProbeChromosome : ChromosomeBase
    {
        public ProbeChromosome() : base(GeneCount) => CreateGenes();

        public override Gene GenerateGene(int geneIndex)
            => new Gene(RandomizationProvider.Current.GetDouble(Lo, Hi));

        public override IChromosome CreateNew() => new ProbeChromosome();

        public double[] Values()
        {
            var values = new double[Length];
            for (int i = 0; i < Length; i++) values[i] = (double)GetGene(i).Value;
            return values;
        }
    }

    private static void Configure(Substrate substrate)
    {
        if (substrate == Substrate.SudokuR1)
        {
            GeneCount = EmptyCells().Count;
            Lo = 1.0;
            Hi = 10.0;
        }
        else
        {
            GeneCount = 36;
            Lo = -5.12;
            Hi = 5.12;
        }
    }

    // ---------------------------------------------------------------- measurement

    /// <summary>Per-gene tally of what the velocity update actually did, flushed once per generation.</summary>
    private sealed class StepTally
    {
        public int Calls;
        public int ZeroSteps;

        /// <summary>Zero step while the global best is still away: the stagnation signature.</summary>
        public int StuckAwayFromBest;

        /// <summary>step / gap equals the clamp ratio: the clamp was the binding constraint (proxy).</summary>
        public int ClampBound;

        /// <summary>step &gt; gap: the particle travelled past the global best.</summary>
        public int Overshoot;

        public double SumSteps;
        public double SumGaps;

        public void Reset()
        {
            Calls = 0;
            ZeroSteps = 0;
            StuckAwayFromBest = 0;
            ClampBound = 0;
            Overshoot = 0;
            SumSteps = 0.0;
            SumGaps = 0.0;
        }
    }

    private sealed class Sample
    {
        public int Generation;
        public int DistinctRaw;
        public int DistinctDecoded;
        public double MaxStd;
        public double Best;
        public int Admitted;

        /// <summary>Gene updates attempted during this generation, and how many took no step at all.</summary>
        public int Updates;
        public int ZeroSteps;

        /// <summary>Total travelled distance over the generation, in gene units.</summary>
        public double SumSteps;
    }

    /// <summary>
    /// Records the reinsertion boundary. <see cref="MetaPopulation.CreateNewGeneration"/> is virtual,
    /// so comparing the incoming list by reference to the previous generation says how many slots
    /// actually changed without touching the library.
    /// </summary>
    private sealed class ProbePopulation : MetaPopulation
    {
        private HashSet<IChromosome>? _previous;

        public int AdmittedLast;

        public ProbePopulation(int minSize, int maxSize, IChromosome adam)
            : base(minSize, maxSize, adam)
        {
        }

        public override void CreateNewGeneration(IList<IChromosome> chromosomes)
        {
            AdmittedLast = _previous == null
                ? 0
                : chromosomes.Count(c => !_previous.Contains(c));
            base.CreateNewGeneration(chromosomes);
            _previous = new HashSet<IChromosome>(chromosomes);
        }
    }

    private sealed record Arm(string Name, double MaxVelocityRatio, double InertiaWeight);

    private static readonly Arm[] Arms =
    {
        new("shipped", 2.0, 0.7298),
        new("clamp-off", 10.0, 0.7298),
        new("inertia-0", 2.0, 0.0),
    };

    private static List<Sample> RunArm(Substrate substrate, Arm arm, int seed,
        out double elapsedMs, out int zeroShare, out int stuckShare, out int clampShare, out int overshootShare)
    {
        var maxVelocityRatio = arm.MaxVelocityRatio;
        Configure(substrate);
        FastRandomRandomization.ResetSeed(seed);

        var tally = new StepTally();
        var pso = new ParticleSwarmOptimization
        {
            MaxGenerations = Generations,
            MaxVelocityRatio = arm.MaxVelocityRatio,
            InertiaWeight = arm.InertiaWeight,
        };
        pso.SetGeometricConverter(new GeometricConverter<double>
        {
            GeneToDoubleConverter = (_, v) => v,
            DoubleToGeneConverter = (_, d) => d,
        });

        // The public operator hook is the only place the library exposes the recurrence inputs
        // ([current position, global best]) before the update is applied.
        var inner = pso.DefaultUpdateOperator;
        pso.UpdateOperator = (geneIndex, geneValues, converter, ctx) =>
        {
            var values = geneValues.ToList();
            double x = converter.GeneToDouble(geneIndex, values[0]);
            double gbest = converter.GeneToDouble(geneIndex, values[^1]);
            object updated = inner(geneIndex, values, converter, ctx);
            double xNew = converter.GeneToDouble(geneIndex, updated);

            double step = Math.Abs(xNew - x);
            double gap = Math.Abs(gbest - x);
            tally.Calls++;
            tally.SumSteps += step;
            tally.SumGaps += gap;
            if (step < StepEpsilon)
            {
                tally.ZeroSteps++;
                if (gap > GapEpsilon) tally.StuckAwayFromBest++;
            }
            else
            {
                if (gap > GapEpsilon && Math.Abs(step / gap - maxVelocityRatio) < 1e-6) tally.ClampBound++;
                if (step > gap + StepEpsilon) tally.Overshoot++;
            }
            return updated;
        };

        var population = new ProbePopulation(PopSize, PopSize, new ProbeChromosome());
        Func<IChromosome, double> objective = substrate == Substrate.SudokuR1
            ? c => -CountConflicts(DecodeR1(((ProbeChromosome)c).Values()))
            : c => -Rastrigin(((ProbeChromosome)c).Values());

        var ga = new MetaGeneticAlgorithm(
            population,
            new FuncFitness(objective),
            new EliteSelection(),
            new UniformCrossover(0.5f),
            new UniformMutation(true),
            pso.Build())
        {
            Termination = new GenerationNumberTermination(Generations)
        };

        var samples = new List<Sample>();
        long calls = 0, zeros = 0, stuck = 0, clamped = 0, overshoot = 0;
        ga.GenerationRan += (_, _) =>
        {
            samples.Add(Measure(ga, substrate, population, tally));
            calls += tally.Calls;
            zeros += tally.ZeroSteps;
            stuck += tally.StuckAwayFromBest;
            clamped += tally.ClampBound;
            overshoot += tally.Overshoot;
            tally.Reset();
        };

        var watch = System.Diagnostics.Stopwatch.StartNew();
        ga.Start();
        watch.Stop();
        elapsedMs = watch.Elapsed.TotalMilliseconds;
        zeroShare = Share(zeros, calls);
        stuckShare = Share(stuck, calls);
        clampShare = Share(clamped, calls);
        overshootShare = Share(overshoot, calls);

        Assert.Multiple(() =>
        {
            Assert.That(ga.GenerationsNumber, Is.EqualTo(Generations), "the run must reach its budget");
            Assert.That(samples, Has.Count.EqualTo(Generations), "one sample per generation");
            foreach (var s in samples)
            {
                Assert.That(s.DistinctRaw, Is.GreaterThanOrEqualTo(1), $"gen {s.Generation}: empty population");
                Assert.That(s.DistinctDecoded, Is.GreaterThanOrEqualTo(1), $"gen {s.Generation}: empty decode");
                Assert.That(double.IsFinite(s.Best), Is.True, $"gen {s.Generation}: non-finite best fitness");
                Assert.That(double.IsFinite(s.MaxStd), Is.True, $"gen {s.Generation}: non-finite dispersion");
            }
        });

        return samples;
    }

    private static int Share(long part, long total) => total == 0 ? 0 : (int)Math.Round(100.0 * part / total);

    private static Sample Measure(MetaGeneticAlgorithm ga, Substrate substrate, ProbePopulation population, StepTally tally)
    {
        var members = ga.Population.CurrentGeneration.Chromosomes;
        var rawKeys = new HashSet<string>();
        var decodedKeys = new HashSet<string>();
        var columns = new List<double>[GeneCount];
        for (int g = 0; g < GeneCount; g++) columns[g] = new List<double>(members.Count);

        foreach (var chromosome in members)
        {
            var values = ((ProbeChromosome)chromosome).Values();
            rawKeys.Add(string.Join(",", values.Select(v => Math.Round(v, 9).ToString("R"))));
            decodedKeys.Add(substrate == Substrate.SudokuR1
                ? GridKey(DecodeR1(values))
                : string.Join(",", values.Select(v => Math.Round(v, 0).ToString("R"))));
            for (int g = 0; g < GeneCount; g++) columns[g].Add(values[g]);
        }

        double maxStd = 0.0;
        foreach (var column in columns)
        {
            double mean = column.Average();
            double variance = column.Sum(v => (v - mean) * (v - mean)) / column.Count;
            maxStd = Math.Max(maxStd, Math.Sqrt(variance));
        }

        var best = (ProbeChromosome)ga.BestChromosome;
        return new Sample
        {
            Generation = ga.GenerationsNumber,
            DistinctRaw = rawKeys.Count,
            DistinctDecoded = decodedKeys.Count,
            MaxStd = maxStd,
            Admitted = population.AdmittedLast,
            Updates = tally.Calls,
            ZeroSteps = tally.ZeroSteps,
            SumSteps = tally.SumSteps,
            Best = substrate == Substrate.SudokuR1
                ? CountConflicts(DecodeR1(best.Values()))
                : Rastrigin(best.Values()),
        };
    }

    /// <summary>
    /// The measurement table: two substrates x the velocity-clamp lever x four seeds, with distinct
    /// raw positions, distinct decoded positions, dispersion, admission rate and the step profile
    /// (zero steps, steps frozen away from the global best, clamp-bound steps) at the 25/50/75/100 %
    /// checkpoints.
    /// </summary>
    [Test]
    public void Stagnation_SubstrateByVelocityClamp_SameSeedsSameBudget()
    {
        var checkpoints = new[] { Generations / 4, Generations / 2, 3 * Generations / 4, Generations };
        foreach (var substrate in new[] { Substrate.Rastrigin, Substrate.SudokuR1 })
        {
            foreach (var arm in Arms)
            {
                var finals = new List<double>();
                foreach (var seed in new[] { 0, 1, 7, 42 })
                {
                    var samples = RunArm(substrate, arm, seed, out var elapsedMs,
                        out var zeroShare, out var stuckShare, out var clampShare, out var overshootShare);
                    finals.Add(samples[^1].Best);
                    int improvements = samples.Zip(samples.Skip(1), (a, b) => b.Best < a.Best - 1e-12).Count(x => x);
                    var atCheckpoints = checkpoints
                        .Select(cp => samples[cp - 1])
                        .Select(s =>
                            $"g{s.Generation}: raw {s.DistinctRaw,2}/50 dec {s.DistinctDecoded,2}/50 " +
                            $"std {s.MaxStd:F4} best {s.Best:F2}");
                    TestContext.Progress.WriteLine(
                        $"{substrate,-9} {arm.Name,-10} seed={seed,2} {elapsedMs,6:F0}ms | " +
                        $"improvements {improvements,3}/{Generations} | adm {samples.Count(s => s.Admitted > 0),3}/{samples.Count} | " +
                        $"steps zero {zeroShare,3}% stuck-away {stuckShare,3}% clamp {clampShare,3}% past-gbest {overshootShare,3}% | " +
                        string.Join(" | ", atCheckpoints));

                    if (seed == 0)
                    {
                        // Abrupt or gradual? A live inertia term decays geometrically; a lost velocity
                        // memory stops the swarm in one generation, once the particle sits on its attractors.
                        var early = new[] { 1, 2, 3, 4, 5, 6, 8, 10, 15, 20, 30, 40, 80, 160 };
                        TestContext.Progress.WriteLine(
                            $"  {substrate,-9} {arm.Name,-10} seed= 0 trajectory: " +
                            string.Join(" ", early.Select(g =>
                            {
                                var s = samples[g - 1];
                                // The first generation only seeds the population: no update ran yet.
                                if (s.Updates == 0) return $"g{g}:seed";
                                double meanStep = s.SumSteps / s.Updates;
                                int zeroPct = Share(s.ZeroSteps, s.Updates);
                                return $"g{g}:{meanStep:E1}/{zeroPct}%";
                            })));
                    }
                }

                finals.Sort();
                double median = (finals[1] + finals[2]) / 2.0;
                TestContext.Progress.WriteLine(
                    $"{substrate,-9} {arm.Name,-10} MEDIAN best over four seeds = {median:F4} " +
                    $"[{finals[0]:F4} - {finals[^1]:F4}]");
            }
        }
    }
}
