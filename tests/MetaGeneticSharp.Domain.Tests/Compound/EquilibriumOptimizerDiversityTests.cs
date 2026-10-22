using GeneticSharp;
using MetaGeneticSharp;

namespace MetaGeneticSharp.Domain.Tests.Compound;

/// <summary>
/// Instrumentation of the Equilibrium Optimizer's population diversity (#13778, wave 3).
///
/// The MGS-vs-mealpy benchmark (MGS-26) measured a collapse signature on the MGS arms of the
/// Sudoku substrate but the final conflict count cannot say WHY, nor even whether the collapse
/// is harmful. This test instruments the population itself — distinct raw positions, distinct
/// DECODED positions, dispersion, improvement rate — on the two substrates that matter:
///
///  * Rastrigin 36-D (continuous, one global optimum): converging to a point is the correct
///    outcome, so a collapse there is convergence, not a defect;
///  * the Sudoku R1 substrate of MGS-26 (36 genes, rounding decode): many vectors decode to the
///    same grid, so raw diversity and search diversity are not the same quantity.
///
/// It also runs the cheapest causal pre-test available, <see cref="GeometricMetaHeuristicBase.NoMutation"/>,
/// to see whether that single button explains the profile.
///
/// The test asserts only what must hold for every arm (full population, finite values, valid
/// grid). The tables are the measurement: they are reported in the PR body and decide whether
/// the next tranche patches a mechanism or refutes the hypothesis.
/// </summary>
public class EquilibriumOptimizerDiversityTests
{
    private const int PopSize = 50;
    private const int Generations = 160;

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
        for (int r = 0; r < 9; r++)
            for (int c = 0; c < 9; c++)
                if (ParsePuzzle()[r, c] == 0) cells.Add((r, c));
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

    private static double Rastrigin(double[] x)
    {
        double sum = 10.0 * x.Length;
        foreach (var xi in x) sum += xi * xi - 10.0 * Math.Cos(2.0 * Math.PI * xi);
        return sum;
    }

    // ---------------------------------------------------------------- measurement

    private sealed class Sample
    {
        public int Generation;
        public int DistinctRaw;
        public int DistinctDecoded;
        public double MaxStd;
        public double Best;
    }

    private static List<Sample> RunArm(Substrate substrate, bool noMutation, int seed, out double elapsedMs)
    {
        Configure(substrate);
        FastRandomRandomization.ResetSeed(seed);

        var eo = new EquilibriumOptimizer { MaxGenerations = Generations, NoMutation = noMutation };
        eo.SetGeometricConverter(new GeometricConverter<double>
        {
            GeneToDoubleConverter = (_, v) => v,
            DoubleToGeneConverter = (_, d) => d,
        });

        var population = new MetaPopulation(PopSize, PopSize, new ProbeChromosome());
        Func<IChromosome, double> objective = substrate == Substrate.SudokuR1
            ? c => -CountConflicts(DecodeR1(((ProbeChromosome)c).Values()))
            : c => -Rastrigin(((ProbeChromosome)c).Values());

        var ga = new MetaGeneticAlgorithm(
            population,
            new FuncFitness(objective),
            new EliteSelection(),
            new UniformCrossover(0.5f),
            new UniformMutation(true),
            eo.Build())
        {
            Termination = new GenerationNumberTermination(Generations)
        };

        var samples = new List<Sample>();
        ga.GenerationRan += (_, _) => samples.Add(Measure(ga, substrate));

        var watch = System.Diagnostics.Stopwatch.StartNew();
        ga.Start();
        watch.Stop();
        elapsedMs = watch.Elapsed.TotalMilliseconds;

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

    private static Sample Measure(MetaGeneticAlgorithm ga, Substrate substrate)
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
            Best = substrate == Substrate.SudokuR1
                ? CountConflicts(DecodeR1(best.Values()))
                : Rastrigin(best.Values()),
        };
    }

    /// <summary>
    /// The measurement table: two substrates × the No-Mutation button × four seeds, with distinct
    /// raw positions, distinct decoded positions, dispersion and best objective at the
    /// 25/50/75/100 % checkpoints.
    /// </summary>
    [Test]
    public void Diversity_SubstrateByNoMutation_SameSeedsSameBudget()
    {
        var checkpoints = new[] { Generations / 4, Generations / 2, 3 * Generations / 4, Generations };
        foreach (var substrate in new[] { Substrate.Rastrigin, Substrate.SudokuR1 })
        {
            foreach (var noMutation in new[] { true, false })
            {
                foreach (var seed in new[] { 0, 1, 7, 42 })
                {
                    var samples = RunArm(substrate, noMutation, seed, out var elapsedMs);
                    int improvements = samples.Zip(samples.Skip(1), (a, b) => b.Best < a.Best - 1e-12).Count(x => x);
                    var atCheckpoints = checkpoints
                        .Select(cp => samples[cp - 1])
                        .Select(s =>
                            $"g{s.Generation}: raw {s.DistinctRaw,2}/50 dec {s.DistinctDecoded,2}/50 " +
                            $"std {s.MaxStd:F4} best {s.Best:F2}");
                    TestContext.Progress.WriteLine(
                        $"{substrate,-9} NoMutation={noMutation,-5} seed={seed,2} {elapsedMs,6:F0}ms " +
                        $"| improvements {improvements,3}/{Generations} | " +
                        string.Join(" | ", atCheckpoints));
                }
            }
        }
    }
}
