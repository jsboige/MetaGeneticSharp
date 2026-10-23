using GeneticSharp;
using MetaGeneticSharp;

namespace MetaGeneticSharp.Domain.Tests.Compound;

/// <summary>
/// Paired re-benchmark of the stagnation reseed corrective (#13778, corrective step of wave 1 item P2).
///
/// The DE precocity probe (<see cref="DifferentialEvolutionPrecocityTests"/>) measured the failure
/// mode this corrective targets: on the plateau substrate (Sudoku R1, rounded-decode fitness) the
/// elitist reinsertion frontier stops admitting anything new, the population collapses to a single
/// raw position out of 50, every difference vector dies (donor-pair zero 100 % at generation 160,
/// admissions 16-33/160 versus 159/160 on Rastrigin) and the second half of the budget spins on
/// no-op trials. No parameter lever reopens it — F = 0.1 (mealpy alignment) and the F-ramp left
/// the collapse untouched — because the fuel of a self-referential operator <b>is</b> the population
/// spread: steps sized by <see cref="DifferentialEvolution.ScaleFactor"/> cannot reopen what the
/// spread closed.
///
/// The corrective therefore acts at the reinsertion frontier, not on the steps:
/// <see cref="StagnationReseedReinsertion"/> keeps the inherited best-N-of-parents-and-offspring
/// contract while the best improves, and after K stagnant generations replaces the worst
/// <c>MinSize * fraction</c> slots with fresh random chromosomes — donors first, survivors only
/// if they earn it, incumbent best never discarded. It is exposed publicly as
/// <see cref="DifferentialEvolution.StagnationReseedGenerations"/> (0 = shipped behaviour).
///
/// This file runs the paired re-benchmark the issue's loop requires: same substrates, same seeds
/// {0, 1, 7, 42}, same budget (160 generations, 50 individuals) as the probe, with the reseed arm
/// differing from the shipped arm by the corrective alone (F, CR, selection shell all held fixed).
/// Arms are wired through <see cref="GeometricMetaHeuristicBase.CustomReinsertion"/> with an
/// explicit instance so <see cref="StagnationReseedReinsertion.ReseedCount"/> stays observable;
/// the knob path that produces the same instance is covered by the wiring test below.
///
/// Assertions are mechanism-level, not performance-level: the run must stay inside its budget and
/// finite for every arm, and on the plateau substrate the corrective must demonstrably fire
/// (ReseedCount &gt; 0) and reopen the differential fuel (donor-pair zero share &lt; 100 %).
/// Whether the final best improves is a measured, reported quantity — not asserted — because the
/// RNG stream diverges between arms and per-seed dominance is not a property the corrective
/// promises.
/// </summary>
public class DifferentialEvolutionStagnationReseedTests
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

    /// <summary>
    /// A corrective arm: the stagnation threshold K (0 = shipped behaviour, no reinsertion change)
    /// and the reseed fraction. Everything else (F, CR, selection shell, budget) is held fixed by
    /// <see cref="RunArm"/> so the pair isolates the corrective.
    /// </summary>
    private sealed record Arm(string Name, int StagnationGenerations, double ReseedFraction)
    {
        public override string ToString() => Name;
    }

    private static readonly Arm[] Arms =
    {
        new("shipped", 0, 0.3),
        new("reseed-20/0.3", 20, 0.3),
    };

    private static List<Sample> RunArm(Substrate substrate, Arm arm, int seed,
        out double elapsedMs, out int zeroDiffShare, out int zeroOfferedShare,
        out StagnationReseedReinsertion reinsertionUsed)
    {
        Configure(substrate);
        FastRandomRandomization.ResetSeed(seed);

        var tally = new TrialTally();
        var de = new DifferentialEvolution
        {
            MaxGenerations = Generations,
            ScaleFactor = 0.5,
            CrossoverRate = 0.9,
        };
        de.SetGeometricConverter(new GeometricConverter<double>
        {
            GeneToDoubleConverter = (_, v) => v,
            DoubleToGeneConverter = (_, d) => d,
        });

        // The corrective arm injects the reinsertion explicitly so ReseedCount stays observable;
        // this is the same instance the public knob produces (see the wiring test below).
        if (arm.StagnationGenerations > 0)
        {
            reinsertionUsed = new StagnationReseedReinsertion(arm.StagnationGenerations, arm.ReseedFraction);
            de.CustomReinsertion = reinsertionUsed;
        }
        else
        {
            reinsertionUsed = null;
        }

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
            new EliteSelection(),
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

    // ---------------------------------------------------------------- tests

    /// <summary>
    /// The paired re-benchmark table: two substrates x two arms (shipped vs corrective) x four
    /// seeds, same budget as the precocity probe, with distinct raw/decoded positions, dispersion,
    /// admission rate, the differential fuel profile (donor-pair zero share over the run) and the
    /// reseed count at the 25/50/75/100 % checkpoints. On the plateau substrate the mechanism
    /// assertions are hard: the corrective must fire and reopen the fuel.
    /// </summary>
    [Test]
    public void StagnationReseed_CorrectiveVsShipped_SameSeedsSameBudget()
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
                        out var zeroDiffShare, out var zeroOfferedShare, out var reinsertionUsed);
                    finals.Add(samples[^1].Best);
                    int improvements = samples.Zip(samples.Skip(1), (a, b) => b.Best < a.Best - 1e-12).Count(x => x);
                    var atCheckpoints = checkpoints
                        .Select(cp => samples[cp - 1])
                        .Select(s =>
                            $"g{s.Generation}: raw {s.DistinctRaw,2}/50 refs {s.DistinctRefs,2}/50 dec {s.DistinctDecoded,2}/50 " +
                            $"std {s.MaxStd:F4} best {s.Best:F2}");
                    TestContext.Progress.WriteLine(
                        $"{substrate,-9} {arm.Name,-14} seed={seed,2} {elapsedMs,6:F0}ms | " +
                        $"improvements {improvements,3}/{Generations} | adm {samples.Count(s => s.Admitted > 0),3}/{samples.Count} | " +
                        $"donor-pair zero {zeroDiffShare,3}% offered zero {zeroOfferedShare,3}% | " +
                        $"reseeds {(reinsertionUsed == null ? "-" : reinsertionUsed.ReseedCount.ToString())} | " +
                        string.Join(" | ", atCheckpoints));

                    if (substrate == Substrate.SudokuR1 && arm.StagnationGenerations > 0)
                    {
                        // The corrective's mechanism, hard-asserted on the plateau substrate where the
                        // probe measured the collapse: the reseed must actually fire, and the fuel it
                        // reopens must show up as non-dead donor pairs.
                        Assert.Multiple(() =>
                        {
                            Assert.That(reinsertionUsed, Is.Not.Null, "corrective arm must inject the reinsertion");
                            Assert.That(reinsertionUsed.ReseedCount, Is.GreaterThan(0),
                                $"seed={seed}: the corrective never fired — stagnation was never detected");
                            Assert.That(zeroDiffShare, Is.LessThan(100),
                                $"seed={seed}: donor-pair zero still 100% over the run — the reseed reopened no fuel");
                        });
                    }
                }

                finals.Sort();
                double median = (finals[1] + finals[2]) / 2.0;
                TestContext.Progress.WriteLine(
                    $"{substrate,-9} {arm.Name,-14} MEDIAN best over four seeds = {median:F4} " +
                    $"[{finals[0]:F4} - {finals[^1]:F4}]");
            }
        }
    }

    /// <summary>
    /// Wiring: the public knob must produce the corrective reinsertion, and its absence must keep
    /// the shipped behaviour byte-for-byte — the default path is the historical
    /// <see cref="FitnessBasedElitistReinsertion"/>, never the corrective.
    /// </summary>
    [Test]
    public void GetDefaultReinsertion_FollowsStagnationKnob()
    {
        var de = new DifferentialEvolution();

        var shipped = de.GetDefaultReinsertion();
        Assert.That(shipped, Is.InstanceOf<FitnessBasedElitistReinsertion>(), "knob 0 must keep the historical reinsertion");
        Assert.That(shipped, Is.Not.InstanceOf<StagnationReseedReinsertion>(), "knob 0 must not install the corrective");

        de.StagnationReseedGenerations = 20;
        de.StagnationReseedFraction = 0.25;
        var corrective = de.GetDefaultReinsertion() as StagnationReseedReinsertion;
        Assert.That(corrective, Is.Not.Null, "knob > 0 must install the corrective");
        Assert.That(corrective.StagnationGenerations, Is.EqualTo(20), "threshold must be threaded through");
        Assert.That(corrective.ReseedFraction, Is.EqualTo(0.25), "fraction must be threaded through");
    }

    /// <summary>Constructor guards: the threshold must be positive and the fraction in (0, 1).</summary>
    [Test]
    public void StagnationReseedReinsertion_RejectsDegenerateConfiguration()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new StagnationReseedReinsertion(0, 0.3));
        Assert.Throws<ArgumentOutOfRangeException>(() => new StagnationReseedReinsertion(-5, 0.3));
        Assert.Throws<ArgumentOutOfRangeException>(() => new StagnationReseedReinsertion(20, 0.0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new StagnationReseedReinsertion(20, 1.0));
    }
}
