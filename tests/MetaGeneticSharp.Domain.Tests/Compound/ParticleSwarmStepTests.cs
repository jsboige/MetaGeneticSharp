using GeneticSharp;
using MetaGeneticSharp;

namespace MetaGeneticSharp.Domain.Tests.Compound;

/// <summary>
/// The wave-5 step instrument, pointed at the Particle Swarm Optimizer (#13778, wave 6).
///
/// Wave 5 measured the Equilibrium Optimizer's offered step: the round decode annihilates
/// 86-87% of moved genes in the second half of the run, and the raw step itself collapses
/// 3-4x. The falsifiable prediction posted on the issue: run the SAME table on the arm
/// where the inertia lever lives — PSO with InertiaWeight 0 (the arm where the #66 probe
/// measured admissions x15 and the freeze pushed from g10 to g80 on Sudoku) — and see
/// whether the raw step holds above the rounding threshold and the annihilation drops.
///
/// If w=0 holds the raw step up on Sudoku while the shipped w=0.7298 collapses like the EO
/// arm did, the lever class is validated (exploration magnitude) and the EO fix belongs in
/// the operator's step schedule; if both PSO arms annihilate equally, the round decode
/// dominates whatever the operator does and the lever is the representation.
///
/// The probe is the same boundary wrapper as wave 5 (CustomReinsertion is the actual
/// injection point; neither compound overrides GetDefaultReinsertion, so wrapping
/// FitnessBasedElitistReinsertion IS the shipped behavior). The shell matches the #66
/// probe (EliteSelection, UniformCrossover, UniformMutation, no NoMutation flag).
///
/// The test asserts only what must hold for every arm; the table is the measurement.
/// </summary>
public class ParticleSwarmStepTests
{
    private const int PopSize = 50;
    private const int Generations = 160;

    private const string PuzzleLine =
        "902005403100063025508407060026309001057010290090670530240530600705200304080041950";

    private enum Substrate { Rastrigin, SudokuR1 }

    // ---------------------------------------------------------------- substrates (same as the probes)

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

    // ---------------------------------------------------------------- the record (same as wave 5)

    private sealed class StepRecord
    {
        public int Paired;
        public double RawStepSum;
        public double DecStepSum;
        public int ChildAnnihilated;
        public int GenesMovedRaw;
        public int GenesMovedDec;
    }

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

    private static Substrate CurrentSubstrate;

    // ---------------------------------------------------------------- run

    private static (List<StepRecord> Records, List<int> Admissions, List<double> Best) RunArm(
        Substrate substrate, double inertiaWeight, int seed)
    {
        Configure(substrate);
        CurrentSubstrate = substrate;
        FastRandomRandomization.ResetSeed(seed);

        var pso = new ParticleSwarmOptimization
        {
            MaxGenerations = Generations,
            MaxVelocityRatio = 2.0,
            InertiaWeight = inertiaWeight,
        };
        var probing = new StepProbingReinsertion(new FitnessBasedElitistReinsertion());
        pso.CustomReinsertion = probing;
        pso.SetGeometricConverter(new GeometricConverter<double>
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
            pso.Build())
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
    /// The measurement table: two substrates x two inertia weights x four seeds, same columns
    /// as wave 5 (raw and decoded mean child step, decoded/raw ratio, child and gene
    /// annihilation, first and second half of the run).
    /// </summary>
    [Test]
    public void Step_InertiaShippedVsZero_SameSeedsSameBudget()
    {
        foreach (var substrate in new[] { Substrate.SudokuR1, Substrate.Rastrigin })
        {
            foreach (var (label, w) in new (string, double)[] { ("w=0.73", 0.7298), ("w=0   ", 0.0) })
            {
                foreach (var seed in new[] { 0, 1, 7, 42 })
                {
                    var (records, admissions, best) = RunArm(substrate, w, seed);
                    int transitions = records.Count;
                    int mid = transitions / 2;
                    int admitPositive = admissions.Count(a => a > 0);

                    TestContext.Progress.WriteLine(
                        $"{substrate,-9} {label} seed={seed,2} | H1 [{Half(records, 0, mid)}] | H2 [{Half(records, mid, transitions)}] " +
                        $"| admit>0 {admitPositive,3}/{Generations} | best {-best[^1],7:F2}");
                }
            }
        }
    }
}
