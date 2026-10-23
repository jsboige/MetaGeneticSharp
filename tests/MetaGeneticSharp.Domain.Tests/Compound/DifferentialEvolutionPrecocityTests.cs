using GeneticSharp;
using MetaGeneticSharp;

namespace MetaGeneticSharp.Domain.Tests.Compound;

/// <summary>
/// Instrumentation of early precipitation in Differential Evolution (#13778, wave 1 item P2).
///
/// The MGS-23 face-off and its causal experiments (#13774) classified the MGS DE arm as
/// <b>précipitation précoce</b>: behind at the 25 % checkpoint and never catching up. The
/// confound was neutralized first, as #13778 requires — mealpy aligned to <c>wf = 0,5</c>
/// (verdict CONFOND_REJECTED, the gap grew) and MGS reduced to <c>F = 0,1</c> (verdict
/// NO IMPROVEMENT, median unchanged) — and the bound clamp was shown reproductively inert
/// (identity converter == factory path, 4/4 seeds). The gap survived every parameter
/// alignment, so the remaining owed step is the sub-module instrument: what the compound
/// does to its population, measured from the inside.
///
/// DE's exploration fuel is self-referential: the trial step is <c>F * (x_r2 - x_r3)</c>,
/// a difference drawn from the population itself. Unlike PSO (whose probe of
/// <see cref="ParticleSwarmOptimizationStagnationTests"/> found the lever in the inertia
/// term) DE has no external memory: once the population concentrates, every difference
/// vector shrinks with it and the search freezes on whatever basin it precipitated into.
/// The greedy best-N-of-parents-and-offspring reinsertion can only amplify that, because
/// a trial outside the collapsing cloud is usually worse than the cloud's best and never
/// re-enters the population to re-open the spread.
///
/// This test separates the three quantities the operator actually moves, per gene trial,
/// without touching the library — it wraps the public
/// <see cref="DifferentialEvolution.DifferentialOperator"/> hook:
///
///  * <b>diff</b> <c>|x_r2 - x_r3|</c> — the differential fuel, F-independent. This is the
///    direct read of population spread at the operator's input: it collapses when the
///    donors coincide, whatever F is doing;
///  * <b>offered</b> <c>|mutant - x_i|</c> — what the mutant proposes to the target;
///  * <b>retained</b> <c>|trial - x_i|</c> — what survives the binomial crossover and
///    actually enters the child.
///
/// The final best cannot separate healthy convergence from precipitation; the timing of
/// the diff collapse can. Two substrates keep the reading honest (Rastrigin 36-D: one
/// global optimum, a collapse there may be convergence; Sudoku R1: decodes by rounding,
/// so many positions give the same grid and only the raw collapse is informative).
///
/// Arms: `shipped` (F = 0.5, CR = 0.9), `mealpy-F` (F = 0.1 — the parameter alignment
/// already measured at the notebook level, kept here to read its effect on the dynamics
/// rather than on the finals), and `ramp` (<see cref="DifferentialEvolution.InitialScaleFactor"/>
/// = 0.9 tapering to 0.5 — the early-exploration lever the public surface exposes: big
/// difference steps while the population is still spread, standard F once it has
/// concentrated). The test asserts only what must hold for every arm; the tables are the
/// measurement.
/// </summary>
public class DifferentialEvolutionPrecocityTests
{
    private const int PopSize = 50;
    private const int Generations = 160;

    /// <summary>Below this, a distance is considered zero (genes are doubles).</summary>
    private const double StepEpsilon = 1e-12;

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

    /// <summary>Per-gene-trial tally of what the differential operator actually moved, flushed once per generation.</summary>
    private sealed class TrialTally
    {
        public int Calls;

        /// <summary>|x_r2 - x_r3| below epsilon: the donor pair coincided — the direct population-collapse read.</summary>
        public int ZeroDiff;

        /// <summary>|mutant - x_i| below epsilon: the trial proposed no displacement at all.</summary>
        public int ZeroOffered;

        /// <summary>Differential fuel |x_r2 - x_r3|, F-independent.</summary>
        public double SumDiff;

        /// <summary>What the mutant proposed: |r1 + F.(r2 - r3) - x_i|.</summary>
        public double SumOffered;

        /// <summary>What entered the child after the binomial crossover: |trial - x_i|.</summary>
        public double SumRetained;

        public void Reset()
        {
            Calls = 0;
            ZeroDiff = 0;
            ZeroOffered = 0;
            SumDiff = 0.0;
            SumOffered = 0.0;
            SumRetained = 0.0;
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

        /// <summary>Distinct chromosome instances in the generation (reference equality).</summary>
        public int DistinctRefs;

        /// <summary>Gene trials attempted during this generation, and their differential profile.</summary>
        public int Trials;
        public int ZeroDiff;
        public double SumDiff;
        public double SumRetained;
    }

    /// <summary>
    /// Records the reinsertion boundary: how many slots actually changed versus the previous
    /// generation, by reference, without touching the library.
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

    private sealed record Arm(string Name, double ScaleFactor, double? InitialScaleFactor)
    {
        public override string ToString() => Name;
    }

    private static readonly Arm[] Arms =
    {
        new("shipped", 0.5, null),
        new("mealpy-F", 0.1, null),
        new("ramp", 0.5, 0.9),
    };

    /// <summary>
    /// Which selector drives the <see cref="MetaGeneticAlgorithm"/> shell — the same attribution
    /// control as the PSO probe: the compound only ever sees the population the shell hands it.
    /// </summary>
    private enum ShellSelector { Elite, Tournament }

    private static ISelection BuildSelection(ShellSelector shell) => shell switch
    {
        ShellSelector.Tournament => new TournamentSelection(),
        _ => new EliteSelection(),
    };

    private static List<Sample> RunArm(Substrate substrate, Arm arm, int seed, ShellSelector shell,
        out double elapsedMs, out int zeroDiffShare, out int zeroOfferedShare)
    {
        Configure(substrate);
        FastRandomRandomization.ResetSeed(seed);

        var tally = new TrialTally();
        var de = new DifferentialEvolution
        {
            MaxGenerations = Generations,
            ScaleFactor = arm.ScaleFactor,
            InitialScaleFactor = arm.InitialScaleFactor,
            CrossoverRate = 0.9,
        };
        de.SetGeometricConverter(new GeometricConverter<double>
        {
            GeneToDoubleConverter = (_, v) => v,
            DoubleToGeneConverter = (_, d) => d,
        });

        // The public operator hook is the only place the library exposes the four parents
        // [target, r1, r2, r3] and the schedule-resolved F before the trial is built.
        DifferentialEvolution.TrialOperator inner = de.DifferentialOperator;
        de.DifferentialOperator = (geneIndex, geneValues, converter, scale, crossoverRate) =>
        {
            var metric = geneValues.Select(v => converter.GeneToDouble(geneIndex, v)).ToList();
            double xi = metric[0], r1 = metric[1], r2 = metric[2], r3 = metric[3];
            object result = inner(geneIndex, geneValues, converter, scale, crossoverRate);
            double trial = converter.GeneToDouble(geneIndex, result);

            double diff = Math.Abs(r2 - r3);
            double offered = Math.Abs(r1 + scale * (r2 - r3) - xi);
            double retained = Math.Abs(trial - xi);
            tally.Calls++;
            tally.SumDiff += diff;
            tally.SumOffered += offered;
            tally.SumRetained += retained;
            if (diff < StepEpsilon) tally.ZeroDiff++;
            if (offered < StepEpsilon) tally.ZeroOffered++;
            return result;
        };

        var population = new ProbePopulation(PopSize, PopSize, new ProbeChromosome());
        Func<IChromosome, double> objective = substrate == Substrate.SudokuR1
            ? c => -CountConflicts(DecodeR1(((ProbeChromosome)c).Values()))
            : c => -Rastrigin(((ProbeChromosome)c).Values());

        var ga = new MetaGeneticAlgorithm(
            population,
            new FuncFitness(objective),
            BuildSelection(shell),
            new UniformCrossover(0.5f),
            new UniformMutation(true),
            de.Build())
        {
            Termination = new GenerationNumberTermination(Generations),
            MutationProbability = 0.1f,
        };

        var samples = new List<Sample>();
        long calls = 0, zeroDiff = 0, zeroOffered = 0;
        ga.GenerationRan += (_, _) =>
        {
            samples.Add(Measure(ga, substrate, population, tally));
            calls += tally.Calls;
            zeroDiff += tally.ZeroDiff;
            zeroOffered += tally.ZeroOffered;
            tally.Reset();
        };

        var watch = System.Diagnostics.Stopwatch.StartNew();
        ga.Start();
        watch.Stop();
        elapsedMs = watch.Elapsed.TotalMilliseconds;
        zeroDiffShare = Share(zeroDiff, calls);
        zeroOfferedShare = Share(zeroOffered, calls);

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

    private static Sample Measure(MetaGeneticAlgorithm ga, Substrate substrate, ProbePopulation population, TrialTally tally)
    {
        var members = ga.Population.CurrentGeneration.Chromosomes;
        var rawKeys = new HashSet<string>();
        var decodedKeys = new HashSet<string>();
        var refSet = new HashSet<IChromosome>(members, ReferenceEqualityComparer.Instance);
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
            DistinctRefs = refSet.Count,
            MaxStd = maxStd,
            Admitted = population.AdmittedLast,
            Trials = tally.Calls,
            ZeroDiff = tally.ZeroDiff,
            SumDiff = tally.SumDiff,
            SumRetained = tally.SumRetained,
            Best = substrate == Substrate.SudokuR1
                ? CountConflicts(DecodeR1(best.Values()))
                : Rastrigin(best.Values()),
        };
    }

    /// <summary>
    /// The measurement table: two substrates x the scale-factor arm x four seeds, with distinct
    /// raw positions, distinct decoded positions, dispersion, admission rate and the differential
    /// profile (zero-difference donor pairs, mean fuel, mean retained step) at the 25/50/75/100 %
    /// checkpoints.
    /// </summary>
    [Test]
    public void Precocity_SubstrateByScaleFactorArm_SameSeedsSameBudget()
    {
        var checkpoints = new[] { Generations / 4, Generations / 2, 3 * Generations / 4, Generations };
        foreach (var substrate in new[] { Substrate.Rastrigin, Substrate.SudokuR1 })
        {
            foreach (var arm in Arms)
            {
                var finals = new List<double>();
                foreach (var seed in new[] { 0, 1, 7, 42 })
                {
                    var samples = RunArm(substrate, arm, seed, ShellSelector.Elite, out var elapsedMs,
                        out var zeroDiffShare, out var zeroOfferedShare);
                    finals.Add(samples[^1].Best);
                    int improvements = samples.Zip(samples.Skip(1), (a, b) => b.Best < a.Best - 1e-12).Count(x => x);
                    var atCheckpoints = checkpoints
                        .Select(cp => samples[cp - 1])
                        .Select(s =>
                            $"g{s.Generation}: raw {s.DistinctRaw,2}/50 refs {s.DistinctRefs,2}/50 dec {s.DistinctDecoded,2}/50 " +
                            $"std {s.MaxStd:F4} best {s.Best:F2}");
                    TestContext.Progress.WriteLine(
                        $"{substrate,-9} {arm.Name,-9} seed={seed,2} {elapsedMs,6:F0}ms | " +
                        $"improvements {improvements,3}/{Generations} | adm {samples.Count(s => s.Admitted > 0),3}/{samples.Count} | " +
                        $"donor-pair zero {zeroDiffShare,3}% offered zero {zeroOfferedShare,3}% | " +
                        string.Join(" | ", atCheckpoints));

                    if (seed == 0)
                    {
                        // Abrupt or gradual? Fuel collapsing early while the best is still far from
                        // its final value is the precipitation signature; a fuel that decays with the
                        // best is convergence.
                        var early = new[] { 1, 2, 3, 4, 5, 6, 8, 10, 15, 20, 30, 40, 80, 160 };
                        TestContext.Progress.WriteLine(
                            $"  {substrate,-9} {arm.Name,-9} seed= 0 trajectory: " +
                            string.Join(" ", early.Select(g =>
                            {
                                var s = samples[g - 1];
                                // The first generation only seeds the population: no trial ran yet.
                                if (s.Trials == 0) return $"g{g}:seed";
                                double meanFuel = s.SumDiff / s.Trials;
                                double meanRetained = s.SumRetained / s.Trials;
                                int zeroDiffPct = Share(s.ZeroDiff, s.Trials);
                                return $"g{g}:{meanFuel:E1}/{meanRetained:E1}/{zeroDiffPct}%";
                            })));
                    }
                }

                finals.Sort();
                double median = (finals[1] + finals[2]) / 2.0;
                TestContext.Progress.WriteLine(
                    $"{substrate,-9} {arm.Name,-9} MEDIAN best over four seeds = {median:F4} " +
                    $"[{finals[0]:F4} - {finals[^1]:F4}]");
            }
        }
    }

    /// <summary>
    /// Shell attribution control: the probe reads a population the shell has already filtered,
    /// and the PSO square (jsboige/MetaGeneticSharp#66) measured two shell homogenisation
    /// mechanisms that the raw-positions column conflates — EliteSelection carries its top-1
    /// across calls (references decay with no variation at all) while TournamentSelection clones
    /// every winner (positions concentrate, references stay distinct). One arm, both shells,
    /// same seeds and budget: if the fuel collapse follows the shell it is a shell property; if
    /// it stays under both, it travels with the operator.
    /// </summary>
    [Test]
    public void ShellAttribution_SelectionVariant_ShippedArm_SameSeeds()
    {
        var shipped = Array.Find(Arms, a => a.Name == "shipped")!;
        var checkpoints = new[] { Generations / 4, Generations / 2, Generations };
        foreach (var substrate in new[] { Substrate.Rastrigin, Substrate.SudokuR1 })
        {
            foreach (var shell in new[] { ShellSelector.Elite, ShellSelector.Tournament })
            {
                var finals = new List<double>();
                foreach (var seed in new[] { 0, 1, 7, 42 })
                {
                    var samples = RunArm(substrate, shipped, seed, shell, out var elapsedMs,
                        out var zeroDiffShare, out var zeroOfferedShare);
                    finals.Add(samples[^1].Best);
                    int improvements = samples.Zip(samples.Skip(1), (a, b) => b.Best < a.Best - 1e-12).Count(x => x);
                    var atCheckpoints = checkpoints
                        .Select(cp => samples[cp - 1])
                        .Select(s =>
                            $"g{s.Generation}: raw {s.DistinctRaw,2}/50 refs {s.DistinctRefs,2}/50 dec {s.DistinctDecoded,2}/50 " +
                            $"std {s.MaxStd:F4} best {s.Best:F2}");
                    TestContext.Progress.WriteLine(
                        $"{substrate,-9} {shell,-10} seed={seed,2} {elapsedMs,6:F0}ms | " +
                        $"improvements {improvements,3}/{Generations} | adm {samples.Count(s => s.Admitted > 0),3}/{samples.Count} | " +
                        $"donor-pair zero {zeroDiffShare,3}% offered zero {zeroOfferedShare,3}% | " +
                        string.Join(" | ", atCheckpoints));
                }

                finals.Sort();
                double median = (finals[1] + finals[2]) / 2.0;
                TestContext.Progress.WriteLine(
                    $"{substrate,-9} {shell,-10} MEDIAN best over four seeds = {median:F4} " +
                    $"[{finals[0]:F4} - {finals[^1]:F4}]");
            }
        }
    }
}
