using GeneticSharp;
using MetaGeneticSharp;

namespace MetaGeneticSharp.Domain.Tests.Compound;

/// <summary>
/// Instrumentation of the STEP the Equilibrium Optimizer offers, raw vs decoded (#13778, wave 5).
///
/// Wave 4 measured that the Sudoku supply is value-poored but raw-diverse: children almost never
/// beat the best parent (0.1-0.2 per generation) while occupying 46-49 of 50 distinct raw
/// positions. The candidate mechanism is the discrete decode crushing continuous moves onto a
/// plateau: a child whose genes moved by less than half an integer decodes to the SAME integer
/// vector as its parent, so the move costs a fitness evaluation but produces zero decoded
/// displacement. This test quantifies that crush at the reinsertion boundary.
///
/// For each paired (parent i, offspring i) the probe records:
///  - the raw Euclidean step ||off - par|| over the floating genes;
///  - the decoded step, where the decode is the SUBSTRATE's own decode (round-and-clamp on
///    Sudoku R1, the operator DecodeR1 actually applies; identity on Rastrigin, whose objective
///    reads raw values — the control arm);
///  - child annihilation: a non-zero raw step whose decoded step is exactly zero;
///  - gene annihilation share: of the genes that moved in raw space, the fraction whose decoded
///    value did not change.
///
/// The wave-4 prediction this measures: on Sudoku the mean decoded step is a small fraction of
/// the mean raw step (crush), annihilation is common, and both worsen in the second half of the
/// run; on Rastrigin the ratio is 1 and annihilation is zero by construction. The ratio is the
/// quantity that decides the next lever: a strong crush points at exploration inside the
/// integer cell (inertia/step size), a weak crush at the representation itself.
///
/// The test asserts only what must hold for every arm; the table is the measurement.
/// </summary>
public class EquilibriumOptimizerStepTests
{
    private const int PopSize = 50;
    private const int Generations = 160;

    private const string PuzzleLine =
        "902005403100063025508407060026309001057010290090670530240530600705200304080041950";

    private enum Substrate { Rastrigin, SudokuR1 }

    // ---------------------------------------------------------------- substrates (same as the supply probe)

    private static int[,] ParsePuzzle()
    {
        var grid = new int[9, 9];
        for (int i = 0; i < 81; i++) grid[i / 9, i % 9] = PuzzleLine[i] - '0';
        return grid;
    }

    private static List<(int Row, int Col)> EmptyCells()
    {
        var cells = new List<(int, int)>();
        var grid = ParsePuzzle();
        for (int r = 0; r < 9; r++)
            for (int c = 0; c < 9; c++)
                if (grid[r, c] == 0) cells.Add((r, c));
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

    private static int[,] DecodeR1(double[] genes)
    {
        var grid = ParsePuzzle();
        var empties = EmptyCells();
        for (int k = 0; k < empties.Count && k < genes.Length; k++)
            grid[empties[k].Row, empties[k].Col] = Math.Max(1, Math.Min(9, (int)Math.Round(genes[k])));
        return grid;
    }

    private static int GeneCount = 36;
    private static double Lo = -5.12;
    private static double Hi = 5.12;

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

    /// <summary>The substrate's own decode: round-and-clamp on Sudoku R1, identity on Rastrigin.</summary>
    private static double[] Decode(Substrate substrate, double[] genes)
    {
        var decoded = new double[genes.Length];
        for (int i = 0; i < genes.Length; i++)
            decoded[i] = substrate == Substrate.SudokuR1
                ? Math.Max(1, Math.Min(9, (int)Math.Round(genes[i])))
                : genes[i];
        return decoded;
    }

    private static double Rastrigin(double[] x)
    {
        double sum = 10.0 * x.Length;
        foreach (var xi in x) sum += xi * xi - 10.0 * Math.Cos(2.0 * Math.PI * xi);
        return sum;
    }

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

    // ---------------------------------------------------------------- the record

    private sealed class StepRecord
    {
        public int Paired;
        public double RawStepSum;      // sum over children of ||off - par|| in raw space
        public double DecStepSum;      // sum over children of ||dec(off) - dec(par)||
        public int ChildAnnihilated;   // children whose raw step > 0 but decoded step == 0
        public int GenesMovedRaw;      // genes moved by more than epsilon in raw space
        public int GenesMovedDec;      // genes whose decoded value changed
    }

    /// <summary>
    /// Wraps a reinsertion to record the raw and decoded displacement of each offered offspring
    /// against the parent at the same index, before the wrapped instance selects the survivors.
    /// </summary>
    private sealed class StepProbingReinsertion : ReinsertionBase
    {
        private readonly ReinsertionBase _inner;
        public readonly List<StepRecord> Records = new();

        public StepProbingReinsertion(ReinsertionBase inner) : base(true, true) => _inner = inner;

        protected override IList<IChromosome> PerformSelectChromosomes(
            IPopulation population, IList<IChromosome> offspring, IList<IChromosome> parents)
        {
            Records.Add(Record(offspring, parents));
            return _inner.SelectChromosomes(population, offspring, parents);
        }

        private static StepRecord Record(IList<IChromosome> offspring, IList<IChromosome> parents)
        {
            var record = new StepRecord();
            int paired = Math.Min(offspring.Count, parents.Count);
            for (int i = 0; i < paired; i++)
            {
                var offRaw = ((ProbeChromosome)offspring[i]).Values();
                var parRaw = ((ProbeChromosome)parents[i]).Values();
                var offDec = Decode(CurrentSubstrate, offRaw);
                var parDec = Decode(CurrentSubstrate, parRaw);

                double rawSq = 0, decSq = 0;
                for (int g = 0; g < offRaw.Length; g++)
                {
                    double dr = offRaw[g] - parRaw[g];
                    if (Math.Abs(dr) > 1e-12) record.GenesMovedRaw++;
                    rawSq += dr * dr;
                    double dd = offDec[g] - parDec[g];
                    if (Math.Abs(dd) > 1e-12) record.GenesMovedDec++;
                    decSq += dd * dd;
                }
                double rawStep = Math.Sqrt(rawSq), decStep = Math.Sqrt(decSq);
                record.RawStepSum += rawStep;
                record.DecStepSum += decStep;
                if (rawStep > 1e-12 && decStep == 0) record.ChildAnnihilated++;
                record.Paired++;
            }
            return record;
        }
    }

    // The probe needs the substrate inside a static callback context; the run loop sets it
    // before starting the algorithm (single-threaded test execution, no cross-arm interleaving).
    private static Substrate CurrentSubstrate;

    // ---------------------------------------------------------------- run

    private static (List<StepRecord> Records, List<int> Admissions, List<double> Best) RunArm(
        Substrate substrate, int seed)
    {
        Configure(substrate);
        CurrentSubstrate = substrate;
        FastRandomRandomization.ResetSeed(seed);

        var eo = new EquilibriumOptimizer { MaxGenerations = Generations, NoMutation = true };
        var probing = new StepProbingReinsertion(new FitnessBasedElitistReinsertion());
        // CustomReinsertion is the actual injection point of the compound (see the supply probe).
        eo.CustomReinsertion = probing;
        eo.SetGeometricConverter(new GeometricConverter<double>
        {
            GeneToDoubleConverter = (_, v) => v,
            DoubleToGeneConverter = (_, d) => d,
        });

        Func<IChromosome, double> objective = substrate == Substrate.SudokuR1
            ? c => -CountConflicts(DecodeR1(((ProbeChromosome)c).Values()))
            : c => -Rastrigin(((ProbeChromosome)c).Values());

        var ga = new MetaGeneticAlgorithm(
            new MetaPopulation(PopSize, PopSize, new ProbeChromosome()),
            new FuncFitness(objective),
            new EliteSelection(),
            new UniformCrossover(0.5f),
            new UniformMutation(true),
            eo.Build())
        {
            Termination = new GenerationNumberTermination(Generations),
        };

        var admissions = new List<int>();
        var best = new List<double>();
        HashSet<IChromosome> previous = null;
        ga.GenerationRan += (_, _) =>
        {
            var current = ga.Population.CurrentGeneration.Chromosomes;
            admissions.Add(previous == null ? 0 : current.Count(c => !previous.Contains(c)));
            previous = new HashSet<IChromosome>(current);
            best.Add(objective(ga.BestChromosome));
        };

        ga.Start();

        Assert.Multiple(() =>
        {
            Assert.That(ga.GenerationsNumber, Is.EqualTo(Generations), "the run must reach its budget");
            Assert.That(probing.Records, Has.Count.EqualTo(Generations - 1), "one step record per generation transition");
            Assert.That(probing.Records.All(r => r.Paired > 0), Is.True, "reinsertion must see paired lists");
            Assert.That(best.All(double.IsFinite), Is.True, "non-finite best fitness");
        });

        return (probing.Records, admissions, best);
    }

    private static string Half(IReadOnlyList<StepRecord> records, int from, int to)
    {
        var slice = records.Skip(from).Take(to - from).ToList();
        double meanRaw = slice.Average(r => r.RawStepSum / r.Paired);
        double meanDec = slice.Average(r => r.DecStepSum / r.Paired);
        double ratio = meanRaw > 1e-12 ? meanDec / meanRaw : 0.0;
        double childAnn = 100.0 * slice.Average(r => (double)r.ChildAnnihilated / r.Paired);
        double geneAnn = slice.Sum(r => (double)r.GenesMovedRaw) > 0
            ? 100.0 * (1.0 - slice.Sum(r => (double)r.GenesMovedDec) / slice.Sum(r => (double)r.GenesMovedRaw))
            : 0.0;
        return $"raw {meanRaw,7:F3} dec {meanDec,7:F3} ratio {ratio,5:F3} | childAnn {childAnn,5:F1}% geneAnn {geneAnn,5:F1}%";
    }

    /// <summary>
    /// The measurement table: two substrates x the shipped reinsertion x four seeds. For each
    /// arm the mean child step in raw and decoded space, the decoded/raw ratio (the crush), the
    /// share of children whose non-zero raw step decodes to zero displacement, and the share of
    /// moved raw genes whose decoded value did not change — each for the first and second half
    /// of the run.
    /// </summary>
    [Test]
    public void Step_RawVsDecoded_DefaultReinsertion()
    {
        foreach (var substrate in new[] { Substrate.SudokuR1, Substrate.Rastrigin })
        {
            foreach (var seed in new[] { 0, 1, 7, 42 })
            {
                var (records, admissions, best) = RunArm(substrate, seed);
                int transitions = records.Count;
                int mid = transitions / 2;
                int admitPositive = admissions.Count(a => a > 0);

                TestContext.Progress.WriteLine(
                    $"{substrate,-9} seed={seed,2} | H1 [{Half(records, 0, mid)}] | H2 [{Half(records, mid, transitions)}] " +
                    $"| admit>0 {admitPositive,3}/{Generations} | best {-best[^1],7:F2}");
            }
        }
    }
}
