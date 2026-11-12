using GeneticSharp;
using MetaGeneticSharp;

namespace MetaGeneticSharp.Domain.Tests.Compound;

/// <summary>
/// Instrumentation of the OFFSPRING SUPPLY offered by the Equilibrium Optimizer (#13778, wave 4).
///
/// MGS#65 reopened the fitness ties the default reinsertion controls, yet only 15-26 of 160
/// generations admit a newcomer on the Sudoku substrate: whatever still closes the population
/// sits upstream, in the children the operator offers. This test records, at the reinsertion
/// boundary — the one place that receives BOTH lists — the paired comparison of each offered
/// offspring against the parent at the same index (strictly better / equal / strictly worse),
/// plus the offspring-side diversity and how many children strictly beat the best parent.
///
/// Two reinsertions are compared under the same supply conditions:
///  * the shipped <see cref="FitnessBasedElitistReinsertion"/> (global top-N, post-#65 ties);
///  * a parent-paired greedy (mealpy's get_better_agent semantics: replace your own parent
///    only on strict improvement), which is the semantics the diagnostic pairwise arm used.
///
/// The table separates the two candidate closures: if offered children are mostly STRICTLY
/// WORSE than their own parent on Sudoku (and not on Rastrigin), the supply itself is
/// impoverished by the discrete decode — no admission rule can fix a supply that never
/// proposes an improvement. If children often tie-or-beat their parent while admission still
/// freezes, the closure is the admission semantics, not the supply.
///
/// The test asserts only what must hold for every arm; the tables are the measurement.
/// </summary>
public class EquilibriumOptimizerSupplyTests
{
    private const int PopSize = 50;
    private const int Generations = 160;

    private const string PuzzleLine =
        "902005403100063025508407060026309001057010290090670530240530600705200304080041950";

    private enum Substrate { Rastrigin, SudokuR1 }

    // ---------------------------------------------------------------- substrates (same as the diversity probe)

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

    private sealed class SupplyRecord
    {
        public int Paired;       // offspring comparable to the parent at the same index
        public int Better;       // strictly better fitness than the paired parent
        public int Equal;        // exactly equal fitness (plateau child)
        public int Worse;        // strictly worse fitness than the paired parent
        public int OffDistinctRaw;
        public int OffDistinctDecoded;
        public int BeatsBestParent; // strictly better than the BEST parent (guaranteed admit)
    }

    /// <summary>
    /// Wraps a reinsertion to record the offered offspring against the parents, index by index,
    /// before the wrapped instance decides who enters the next generation.
    /// </summary>
    private sealed class ProbingReinsertion : ReinsertionBase
    {
        private readonly ReinsertionBase _inner;
        public readonly List<SupplyRecord> Records = new();

        public ProbingReinsertion(ReinsertionBase inner) : base(true, true) => _inner = inner;

        protected override IList<IChromosome> PerformSelectChromosomes(
            IPopulation population, IList<IChromosome> offspring, IList<IChromosome> parents)
        {
            Records.Add(Record(offspring, parents));
            // MetaPopulation and GeneticSharp's Population are sibling classes (both mere
            // IPopulation implementations), so the population reference is passed through
            // untouched: the wrapped reinsertion's public entry point takes the interface.
            return _inner.SelectChromosomes(population, offspring, parents);
        }

        private static SupplyRecord Record(IList<IChromosome> offspring, IList<IChromosome> parents)
        {
            var record = new SupplyRecord();
            // Fitness is double? on IChromosome: at the reinsertion boundary every chromosome
            // has been evaluated (the shipped reinsertion orders on the same values), so the
            // null branch only guards the compiler, it is not an expected measurement state.
            double bestParent = parents.Max(p => p.Fitness ?? double.MinValue);

            var rawKeys = new HashSet<string>();
            var decodedKeys = new HashSet<string>();
            foreach (var child in offspring)
            {
                var values = ((ProbeChromosome)child).Values();
                rawKeys.Add(string.Join(",", values.Select(v => Math.Round(v, 9).ToString("R"))));
                decodedKeys.Add(string.Join(",", values.Select(v => Math.Round(v, 0).ToString("R"))));
                if (child.Fitness > bestParent + 1e-12) record.BeatsBestParent++;
            }
            record.OffDistinctRaw = rawKeys.Count;
            record.OffDistinctDecoded = decodedKeys.Count;

            int paired = Math.Min(offspring.Count, parents.Count);
            for (int i = 0; i < paired; i++)
            {
                double child = offspring[i].Fitness ?? double.MinValue;
                double parent = parents[i].Fitness ?? double.MinValue;
                if (child > parent + 1e-12) record.Better++;
                else if (child < parent - 1e-12) record.Worse++;
                else record.Equal++;
                record.Paired++;
            }
            return record;
        }
    }

    /// <summary>
    /// mealpy's per-agent greedy: the child at index i replaces the parent at index i only on
    /// STRICT improvement; ties and losses keep the incumbent. This is the admission semantics
    /// the diagnostic pairwise arm used (32.0 median, pre-#65 code path).
    /// </summary>
    private sealed class ParentPairedGreedyReinsertion : ReinsertionBase
    {
        public ParentPairedGreedyReinsertion() : base(true, true)
        {
        }

        protected override IList<IChromosome> PerformSelectChromosomes(
            IPopulation population, IList<IChromosome> offspring, IList<IChromosome> parents)
        {
            var next = new List<IChromosome>(parents.Count);
            int paired = Math.Min(offspring.Count, parents.Count);
            for (int i = 0; i < parents.Count; i++)
            {
                if (i < paired && offspring[i].Fitness > parents[i].Fitness + 1e-12)
                    next.Add(offspring[i]);
                else
                    next.Add(parents[i]);
            }
            return next;
        }
    }

    // ---------------------------------------------------------------- run

    private static (List<SupplyRecord> Records, List<int> Admissions, List<double> Best) RunArm(
        Substrate substrate, ReinsertionBase inner, int seed)
    {
        Configure(substrate);
        FastRandomRandomization.ResetSeed(seed);

        var eo = new EquilibriumOptimizer { MaxGenerations = Generations, NoMutation = true };
        var probing = new ProbingReinsertion(inner);
        // The algorithm-level Reinsertion property is NOT the operator the compound invokes:
        // GeometricMetaHeuristicBase.Build wraps its own ReinsertionMetaHeuristic whose GetOperator
        // ignores the passed reinsertion. CustomReinsertion is the actual injection point.
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
            // The initial generation is created, not reinserted: the probe sees one boundary
            // per generation transition, hence Generations - 1 records.
            Assert.That(probing.Records, Has.Count.EqualTo(Generations - 1), "one supply record per generation transition");
            Assert.That(probing.Records.All(r => r.Paired > 0), Is.True, "reinsertion must see paired lists");
            Assert.That(best.All(double.IsFinite), Is.True, "non-finite best fitness");
        });

        return (probing.Records, admissions, best);
    }

    /// <summary>
    /// The measurement table: two substrates x two reinsertions x the shipped NoMutation default
    /// x four seeds. Columns: generations whose supply contains at least one strict improvement
    /// over the paired parent (supply>0), mean per-generation better/equal/worse against the
    /// paired parent, children beating the best parent, offspring distinct raw/decoded positions,
    /// and the final best objective.
    /// </summary>
    [Test]
    public void Supply_DefaultVsParentPaired_SameSeedsSameBudget()
    {
        foreach (var substrate in new[] { Substrate.SudokuR1, Substrate.Rastrigin })
        {
            foreach (var (label, inner) in new (string, ReinsertionBase)[]
            {
                ("default", new FitnessBasedElitistReinsertion()),
                ("paired ", new ParentPairedGreedyReinsertion()),
            })
            {
                foreach (var seed in new[] { 0, 1, 7, 42 })
                {
                    var (records, admissions, best) = RunArm(substrate, inner, seed);
                    int supplyPositive = records.Count(r => r.Better > 0);
                    int transitions = records.Count;
                    double meanBetter = records.Average(r => r.Better);
                    double meanEqual = records.Average(r => r.Equal);
                    double meanWorse = records.Average(r => r.Worse);
                    double meanBeatsBest = records.Average(r => r.BeatsBestParent);
                    double meanRaw = records.Average(r => r.OffDistinctRaw);
                    double meanDec = records.Average(r => r.OffDistinctDecoded);
                    int admitPositive = admissions.Count(a => a > 0);

                    TestContext.Progress.WriteLine(
                        $"{substrate,-9} {label} seed={seed,2} | supply>0 {supplyPositive,3}/{transitions} " +
                        $"| better/equal/worse {meanBetter,5:F1}/{meanEqual,5:F1}/{meanWorse,5:F1} " +
                        $"| beatsBest {meanBeatsBest,4:F1} | off raw {meanRaw,5:F1} dec {meanDec,5:F1} " +
                        $"| admit>0 {admitPositive,3}/{Generations} | best {-best[^1],7:F2}");
                }
            }
        }
    }
}
