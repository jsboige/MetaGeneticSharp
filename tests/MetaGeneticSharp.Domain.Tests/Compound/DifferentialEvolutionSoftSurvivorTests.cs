using GeneticSharp;
using MetaGeneticSharp;

namespace MetaGeneticSharp.Domain.Tests.Compound;

/// <summary>
/// Probe: selection pressure versus basin structure on the plateau substrate
/// (#13778, follow-up of the stagnation-reseed finding).
///
/// The stagnation reseed (PR #68) measured that reopening the differential fuel on the
/// plateau (Sudoku R1, rounded-decode fitness) is necessary but not sufficient: fresh
/// chromosomes enter, donate, and are evicted by the elitist reinsertion within one
/// generation — a fresh random decodes badly on a rounded-decode plateau, so it can only
/// survive as a donor, never as a survivor. Finals did not move (median 25.50 both arms).
///
/// That finding leaves two competing explanations for the flat finals:
/// <list type="bullet">
/// <item><b>H1 — selection pressure:</b> pure best-N-of-parents-and-offspring elitism
/// evicts mid-grade material that mealpy-style survivor policies conserve; conserving it
/// would let difference vectors keep exploring.</item>
/// <item><b>H2 — basin structure:</b> the rounded-decode plateau is flat around the
/// incumbent — mid-grade material exists but no combination of it improves the decoded
/// objective; nothing selection-side can help.</item>
/// </list>
///
/// Discriminating arm: <see cref="SoftSurvivorReinsertion"/> keeps the top E of
/// parents-and-offspring as a guaranteed elite (incumbent never discarded), then fills the
/// remaining MinSize - E slots with the best offspring regardless of parent competition —
/// a generational (mu,lambda)-with-elite shape, the selection-side approximation of what
/// mealpy's survivor step does (canonical DE one-to-one greedy replacement also lets a
/// trial displace its target without competing against the whole parent pool). If H1
/// holds, finals must improve materially at equal budget; if finals stay flat while raw
/// diversity stays high and the fuel stays alive, H2 holds and the next corrective target
/// is decoder-side (basin-aware moves), not selection-side.
///
/// Assertions are mechanism-level: the soft arm must demonstrably conserve raw diversity
/// on the plateau (final distinct raw positions well above the shipped collapse to 1/50)
/// and keep the donor pairs alive. Whether finals improve is measured and reported — the
/// RNG streams diverge between arms, per-seed dominance is not a promise of the arm.
/// </summary>
public class DifferentialEvolutionSoftSurvivorTests
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
        var cells = new List<(int Row, int Col)>();
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

    // ---------------------------------------------------------------- soft survivor arm

    /// <summary>
    /// Generational-with-elite reinsertion: the top <c>eliteCount</c> of parents and
    /// offspring combined form a guaranteed elite (incumbent never discarded), and the
    /// remaining <c>MinSize - eliteCount</c> slots go to the best offspring regardless of
    /// parent competition. Parents survive only through the elite. In-test probe class: it
    /// reorders and filters already-evaluated chromosomes, so the engine contract (every
    /// returned chromosome has a fitness) holds by construction.
    /// </summary>
    private sealed class SoftSurvivorReinsertion : ReinsertionBase
    {
        private readonly int _eliteCount;

        public SoftSurvivorReinsertion(int eliteCount)
            : base(true, true)
        {
            if (eliteCount < 1) throw new ArgumentOutOfRangeException(nameof(eliteCount));
            _eliteCount = eliteCount;
        }

        protected override IList<IChromosome> PerformSelectChromosomes(
            IPopulation population, IList<IChromosome> offspring, IList<IChromosome> parents)
        {
            var elite = parents
                .Concat(offspring)
                .OrderByDescending(p => p.Fitness)
                .Take(_eliteCount)
                .ToList();

            // Offspring already guaranteed an elite slot never take a second one.
            var eliteSet = new HashSet<IChromosome>(elite, ReferenceEqualityComparer.Instance);
            var rest = offspring
                .Where(o => !eliteSet.Contains(o))
                .OrderByDescending(o => o.Fitness)
                .Take(population.MinSize - _eliteCount)
                .ToList();

            // Defensive: if the offspring cohort cannot fill the non-elite slots (crossover
            // produced fewer children than the population size), complete with the best
            // remaining candidates so the returned count always equals MinSize.
            if (rest.Count < population.MinSize - _eliteCount)
            {
                var restSet = new HashSet<IChromosome>(rest, ReferenceEqualityComparer.Instance);
                rest.AddRange(parents
                    .Concat(offspring)
                    .Where(p => !eliteSet.Contains(p) && !restSet.Contains(p))
                    .OrderByDescending(p => p.Fitness)
                    .Take(population.MinSize - _eliteCount - rest.Count));
            }

            return elite.Concat(rest).ToList();
        }

        /// <summary>Test-only window on the protected selection, for the unit contract below.</summary>
        public IList<IChromosome> SelectForTest(IPopulation population, IList<IChromosome> offspring, IList<IChromosome> parents)
            => PerformSelectChromosomes(population, offspring, parents);
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

        public void Reset()
        {
            Calls = 0;
            ZeroDiff = 0;
            ZeroOffered = 0;
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
    /// A probe arm: the elite count the soft-survivor reinsertion guarantees (-1 = shipped
    /// behaviour, the default best-N elitist reinsertion). Everything else (F, CR, selection
    /// shell, budget) is held fixed by <see cref="RunArm"/> so the pair isolates the
    /// survivor policy.
    /// </summary>
    private sealed record Arm(string Name, int EliteCount)
    {
        public override string ToString() => Name;
    }

    private static readonly Arm[] Arms =
    {
        new("shipped", -1),
        new("soft-elite10", 10),
    };

    private static List<Sample> RunArm(Substrate substrate, Arm arm, int seed,
        out double elapsedMs, out int zeroDiffShare, out int zeroOfferedShare,
        out SoftSurvivorReinsertion? reinsertionUsed)
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

        // The soft arm injects its reinsertion explicitly; the shipped arm keeps the default
        // (FitnessBasedElitistReinsertion) so the pair differs by the survivor policy alone.
        if (arm.EliteCount > 0)
        {
            reinsertionUsed = new SoftSurvivorReinsertion(arm.EliteCount);
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
            double r2 = metric[2], r3 = metric[3];
            object result = inner(geneIndex, geneValues, converter, scale, crossoverRate);

            double diff = Math.Abs(r2 - r3);
            if (diff < StepEpsilon) tally.ZeroDiff++;
            if (result is double offered && Math.Abs(offered - metric[0]) < StepEpsilon) tally.ZeroOffered++;
            tally.Calls++;
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
        long calls = 0, zeroDiff = 0, zeroOfferedTotal = 0;
        ga.GenerationRan += (_, _) =>
        {
            samples.Add(Measure(ga, substrate, population));
            calls += tally.Calls;
            zeroDiff += tally.ZeroDiff;
            zeroOfferedTotal += tally.ZeroOffered;
            tally.Reset();
        };

        var watch = System.Diagnostics.Stopwatch.StartNew();
        ga.Start();
        watch.Stop();
        elapsedMs = watch.Elapsed.TotalMilliseconds;
        zeroDiffShare = Share(zeroDiff, calls);
        zeroOfferedShare = Share(zeroOfferedTotal, calls);

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

    private static Sample Measure(MetaGeneticAlgorithm ga, Substrate substrate, ProbePopulation population)
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
            Best = substrate == Substrate.SudokuR1
                ? CountConflicts(DecodeR1(best.Values()))
                : Rastrigin(best.Values()),
        };
    }

    // ---------------------------------------------------------------- tests

    /// <summary>
    /// The paired probe table: two substrates x two arms (shipped elitist vs soft survivor,
    /// elite 10 of 50) x four seeds, same budget and fixed F/CR as the precocity and
    /// reseed probes. On the plateau substrate the mechanism assertions are hard: the soft
    /// arm must demonstrably conserve raw diversity (the shipped arm collapses to 1/50 raw
    /// at generation 160) and keep the donor pairs alive — otherwise the arm does not test
    /// what it claims. Finals are measured and reported: they are the H1/H2 read-out.
    /// </summary>
    [Test]
    public void SoftSurvivor_VsShipped_SameSeedsSameBudget()
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
                        out var zeroDiffShare, out _, out var reinsertionUsed);
                    finals.Add(samples[^1].Best);
                    int improvements = samples.Zip(samples.Skip(1), (a, b) => b.Best < a.Best - 1e-12).Count(x => x);
                    var atCheckpoints = checkpoints
                        .Select(cp => samples[cp - 1])
                        .Select(s =>
                            $"g{s.Generation}: raw {s.DistinctRaw,2}/50 dec {s.DistinctDecoded,2}/50 " +
                            $"std {s.MaxStd:F4} best {s.Best:F2}");
                    TestContext.Progress.WriteLine(
                        $"{substrate,-9} {arm.Name,-13} seed={seed,2} {elapsedMs,6:F0}ms | " +
                        $"improvements {improvements,3}/{Generations} | adm {samples.Count(s => s.Admitted > 0),3}/{samples.Count} | " +
                        $"donor-pair zero {zeroDiffShare,3}% | " +
                        string.Join(" | ", atCheckpoints));

                    if (substrate == Substrate.SudokuR1 && arm.EliteCount > 0)
                    {
                        // The arm's mechanism, hard-asserted on the plateau substrate where the
                        // shipped reinsertion collapses the population to 1/50 raw: the soft
                        // survivor policy must conserve raw diversity through the final
                        // generation, and the conserved spread must show as live donor pairs.
                        var final = samples[^1];
                        Assert.Multiple(() =>
                        {
                            Assert.That(reinsertionUsed, Is.Not.Null, "soft arm must inject the reinsertion");
                            Assert.That(final.DistinctRaw, Is.GreaterThanOrEqualTo(5),
                                $"seed={seed}: soft survivor collapsed to {final.DistinctRaw}/50 raw positions at g{Generations} — the arm no longer conserves diversity");
                            Assert.That(zeroDiffShare, Is.LessThan(100),
                                $"seed={seed}: donor-pair zero still 100% over the run — the conserved spread is not feeding the operator");
                        });
                    }
                }

                finals.Sort();
                double median = (finals[1] + finals[2]) / 2.0;
                TestContext.Progress.WriteLine(
                    $"{substrate,-9} {arm.Name,-13} MEDIAN best over four seeds = {median:F4} " +
                    $"[{finals[0]:F4} - {finals[^1]:F4}]");
            }
        }
    }

    /// <summary>
    /// The in-test reinsertion's contract, on a synthetic cohort: the returned cohort always
    /// counts exactly MinSize chromosomes with no duplicate instance, the incumbent best is
    /// selected first, and a short offspring cohort is completed from the best remaining
    /// candidates (the defensive fill). Setup: 12 parents of fitness 0..11, 7 offspring of
    /// fitness 100..106 (all better than every parent), elite 3 — so the elite is the top
    /// three offspring, the four remaining offspring fill the next slots, and the last five
    /// slots must come from the five best parents (11..7).
    /// </summary>
    [Test]
    public void SoftSurvivorReinsertion_ReturnsFullCohortWithIncumbent()
    {
        Configure(Substrate.Rastrigin);
        var population = new MetaPopulation(12, 12, new ProbeChromosome());

        var parents = Enumerable.Range(0, 12)
            .Select(i => (IChromosome)new ProbeChromosome { Fitness = (double)i })
            .ToList();
        var offspring = Enumerable.Range(0, 7)
            .Select(i => (IChromosome)new ProbeChromosome { Fitness = 100.0 + i })
            .ToList();

        var reinsertion = new SoftSurvivorReinsertion(3);
        var selected = reinsertion.SelectForTest(population, offspring, parents);

        Assert.Multiple(() =>
        {
            Assert.That(selected, Has.Count.EqualTo(12),
                "the cohort must be exactly MinSize: elite 3 + offspring 4 + defensive fill 5");
            Assert.That(selected[0], Is.SameAs(offspring[6]),
                "the incumbent best (offspring fitness 106) must be selected first");
            Assert.That(new HashSet<IChromosome>(selected, ReferenceEqualityComparer.Instance), Has.Count.EqualTo(12),
                "no chromosome instance may take two slots");
            Assert.That(selected, Does.Contain(parents[11]),
                "the defensive fill must pull the best remaining parent (fitness 11)");
            Assert.That(selected, Does.Not.Contain(parents[0]),
                "the worst parent (fitness 0) must not survive: five better parents exist");
            Assert.That(selected, Does.Not.Contain(parents[6]),
                "the sixth-best parent (fitness 6) must not survive: only five fill slots exist");
        });
    }
}
