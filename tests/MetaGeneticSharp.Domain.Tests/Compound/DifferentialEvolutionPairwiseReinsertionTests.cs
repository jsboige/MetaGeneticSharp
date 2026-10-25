using GeneticSharp;
using MetaGeneticSharp;

namespace MetaGeneticSharp.Domain.Tests.Compound;

/// <summary>
/// Probe: is the shipped DE reinsertion the canonical one-to-one greedy replacement?
///
/// <see cref="DifferentialEvolution"/> documents its selection as
/// <i>"the greedy 'keep the trial only if it is no worse' selection is the inherited
/// <see cref="FitnessBasedElitistReinsertion"/> (best-N of parents+offspring)"</i>. Those two
/// descriptions are not the same operator. Canonical DE (Storn &amp; Price, 1997) compares each
/// trial with <b>its own target</b>: a trial that improves replaces that target and nothing else;
/// a target that is not improved cannot be displaced by a better trial belonging to another pair.
/// Best-N-of-parents-and-offspring allows exactly that displacement — call it cross-pair
/// cannibalisation.
///
/// The distinction matters for the plateau finding of the soft-survivor probe (#69): the soft arm
/// conserved diversity but finals stayed flat, read as H2 (basin structure). H2 was measured
/// against a <i>generational-with-elite</i> arm, not against the one-to-one rule DE prescribes, so
/// the selection side had not been tested in its own canonical form.
///
/// The probe answers three questions, each the precondition of the next:
/// <list type="number">
/// <item><b>Do the two operators differ in kind?</b> A synthetic cohort where per-index greedy and
/// best-N provably select different sets, with the evicted index-winners named.</item>
/// <item><b>Can the one-to-one rule be plugged in at all?</b> <see cref="FitnessBasedPairwiseReinsertion"/>
/// pairs <c>parents[i]</c> with <c>offspring[i]</c>, which is the canonical rule only if
/// <c>parents[i]</c> is the target of <c>offspring[i]</c>. That is measured, not read: the target
/// is drawn from the parents list itself, so the pairing holds even though the shipped selection
/// orders that list by fitness. A second selection (population order) runs the same reinsertion so
/// the ordering effect on <i>performance</i> is separated from the pairing question.</item>
/// <item><b>What does the canonical rule do to the plateau?</b> Three arms, same substrates, seeds,
/// budget and F/CR as the precocity, reseed and soft-survivor probes; diversity and finals are
/// reported rather than promised.</item>
/// </list>
///
/// Substrate helpers are duplicated from <see cref="DifferentialEvolutionSoftSurvivorTests"/> so
/// each probe stays self-contained and independently deletable.
/// </summary>
public class DifferentialEvolutionPairwiseReinsertionTests
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

    // ---------------------------------------------------------------- operators under test

    /// <summary>Test-only window on the shipped pairwise (one-to-one) reinsertion.</summary>
    private sealed class PairwiseProbe : FitnessBasedPairwiseReinsertion
    {
        public IList<IChromosome> SelectForTest(IPopulation population, IList<IChromosome> offspring, IList<IChromosome> parents)
            => PerformSelectChromosomes(population, offspring, parents);
    }

    /// <summary>Test-only window on the shipped default (best-N) reinsertion.</summary>
    private sealed class ElitistProbe : FitnessBasedElitistReinsertion
    {
        public IList<IChromosome> SelectForTest(IPopulation population, IList<IChromosome> offspring, IList<IChromosome> parents)
            => PerformSelectChromosomes(population, offspring, parents);
    }

    /// <summary>
    /// The same selection contract as the shipped <see cref="EliteSelection"/> (all parents, no
    /// randomness) with one property changed: it returns the generation in <b>population order</b>
    /// instead of fitness order. It exists so the probe can isolate whether the one-to-one pairing
    /// works, because that question is about ordering, not about elitism.
    /// </summary>
    private sealed class PopulationOrderSelection : SelectionBase
    {
        public PopulationOrderSelection() : base(2)
        {
        }

        protected override IList<IChromosome> PerformSelectChromosomes(int number, Generation generation)
            => generation.Chromosomes.Take(number).ToList();
    }

    /// <summary>
    /// Delegates to the real shipped reinsertion (so the arm <i>is</i> the operator under study,
    /// not a copy) and records, per generation, who won each index pair and who survived.
    /// </summary>
    private sealed class RecordingReinsertion : ReinsertionBase
    {
        private readonly Func<IPopulation, IList<IChromosome>, IList<IChromosome>, IList<IChromosome>> _inner;

        public RecordingReinsertion(Func<IPopulation, IList<IChromosome>, IList<IChromosome>, IList<IChromosome>> inner)
            : base(true, true) => _inner = inner;

        /// <summary>Index-winners (better of parents[i]/offspring[i]) that did not survive the cohort.</summary>
        public int EvictedIndexWinners;

        /// <summary>Survivors that are not the index-winner of any pair — the cannibalisation count.</summary>
        public int ForeignSurvivors;

        /// <summary>Survivors that are offspring instances (by reference).</summary>
        public int OffspringAdmitted;

        /// <summary>Generations where the parent list was not sorted by descending fitness.</summary>
        public int UnsortedParentGenerations;

        /// <summary>
        /// Positions lost to a repeated instance in the parent list: <see cref="EliteSelection"/>
        /// builds its pool as (previous elite ∪ whole generation) and takes MinSize, so one
        /// chromosome can be handed over twice, leaving the cohort with a duplicate slot.
        /// </summary>
        public int DuplicateParentInstances;

        protected override IList<IChromosome> PerformSelectChromosomes(
            IPopulation population, IList<IChromosome> offspring, IList<IChromosome> parents)
        {
            var result = _inner(population, offspring, parents);

            var survivors = new HashSet<IChromosome>(result, ReferenceEqualityComparer.Instance);
            var offspringSet = new HashSet<IChromosome>(offspring, ReferenceEqualityComparer.Instance);
            var winners = new HashSet<IChromosome>(ReferenceEqualityComparer.Instance);

            int paired = Math.Min(parents.Count, offspring.Count);
            int evicted = 0;
            int foreign = 0;

            for (int i = 0; i < paired; i++)
            {
                // Tie keeps the parent: both shipped operators prefer the parent on equal fitness
                // (the elitist's stable sort lists parents first, the pairwise keeps the parent
                // unless the trial is strictly better).
                var winner = parents[i].Fitness < offspring[i].Fitness ? offspring[i] : parents[i];
                winners.Add(winner);
                if (!survivors.Contains(winner)) evicted++;
            }

            foreach (var survivor in survivors)
                if (!winners.Contains(survivor)) foreign++;

            bool sorted = true;
            for (int i = 1; i < parents.Count; i++)
                if (parents[i - 1].Fitness < parents[i].Fitness) { sorted = false; break; }
            if (!sorted) UnsortedParentGenerations++;

            var distinctParents = new HashSet<IChromosome>(parents, ReferenceEqualityComparer.Instance);
            DuplicateParentInstances += parents.Count - distinctParents.Count;

            EvictedIndexWinners += evicted;
            ForeignSurvivors += foreign;
            OffspringAdmitted += result.Count(offspringSet.Contains);
            return result;
        }
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

    private enum ArmSelection { Elite, PopulationOrder }

    private sealed record Arm(string Name, bool Pairwise, ArmSelection Selection)
    {
        public override string ToString() => Name;
    }

    private static readonly Arm[] Arms =
    {
        new("shipped(best-N)", false, ArmSelection.Elite),
        new("pairwise(1:1)", true, ArmSelection.Elite),
        new("pairwise+order", true, ArmSelection.PopulationOrder),
    };

    private sealed record RunStats(int EvictedWinners, int ForeignSurvivors, int OffspringAdmitted,
        int UnsortedParentGenerations, int TrialsChecked, int AlignedTrials, int DuplicateParentInstances);

    private static List<Sample> RunArm(Substrate substrate, Arm arm, int seed, out RunStats stats,
        out double elapsedMs)
    {
        Configure(substrate);
        FastRandomRandomization.ResetSeed(seed);

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

        // Target capture: the operator hook is the only place the library exposes the four
        // parents [target, r1, r2, r3]. One entry per (trial, gene).
        var currentTrial = new Dictionary<int, double>();
        var trialsOfThisGeneration = new List<Dictionary<int, double>>();
        int callCount = 0;
        DifferentialEvolution.TrialOperator inner = de.DifferentialOperator;
        de.DifferentialOperator = (geneIndex, geneValues, converter, scale, crossoverRate) =>
        {
            var metric = geneValues.Select(v => converter.GeneToDouble(geneIndex, v)).ToList();
            if (callCount % GeneCount == 0) currentTrial = new Dictionary<int, double>();
            currentTrial[geneIndex] = metric[0];
            if (callCount % GeneCount == GeneCount - 1) trialsOfThisGeneration.Add(currentTrial);
            callCount++;
            return inner(geneIndex, geneValues, converter, scale, crossoverRate);
        };

        int aligned = 0, checkedTrials = 0;
        var pairwise = new PairwiseProbe();
        var elitist = new ElitistProbe();
        var recording = new RecordingReinsertion((population, offspring, parents) =>
        {
            var trials = trialsOfThisGeneration;
            trialsOfThisGeneration = new List<Dictionary<int, double>>();

            // A trial is aligned when its target's gene values are those of parents[j], its own
            // position in the offspring list: that, and only that, makes parents[j] its target.
            if (trials.Count == parents.Count && offspring.Count == parents.Count)
            {
                for (int j = 0; j < trials.Count; j++)
                {
                    var values = ((ProbeChromosome)parents[j]).Values();
                    checkedTrials++;
                    if (trials[j].All(kv => Math.Abs(values[kv.Key] - kv.Value) < 1e-12)) aligned++;
                }
            }

            return arm.Pairwise
                ? pairwise.SelectForTest(population, offspring, parents)
                : elitist.SelectForTest(population, offspring, parents);
        });
        de.CustomReinsertion = recording;

        var population = new MetaPopulation(PopSize, PopSize, new ProbeChromosome());
        Func<IChromosome, double> objective = substrate == Substrate.SudokuR1
            ? c => -CountConflicts(DecodeR1(((ProbeChromosome)c).Values()))
            : c => -Rastrigin(((ProbeChromosome)c).Values());

        ISelection selection = arm.Selection == ArmSelection.PopulationOrder
            ? new PopulationOrderSelection()
            : new EliteSelection();

        var ga = new MetaGeneticAlgorithm(
            population,
            new FuncFitness(objective),
            selection,
            new UniformCrossover(0.5f),
            new UniformMutation(true),
            de.Build())
        {
            Termination = new GenerationNumberTermination(Generations),
            MutationProbability = 0.1f,
        };

        var samples = new List<Sample>();
        ga.GenerationRan += (_, _) => samples.Add(Measure(ga, substrate));

        var watch = System.Diagnostics.Stopwatch.StartNew();
        ga.Start();
        watch.Stop();
        elapsedMs = watch.Elapsed.TotalMilliseconds;

        stats = new RunStats(recording.EvictedIndexWinners, recording.ForeignSurvivors,
            recording.OffspringAdmitted, recording.UnsortedParentGenerations, checkedTrials, aligned,
            recording.DuplicateParentInstances);

        Assert.Multiple(() =>
        {
            Assert.That(ga.GenerationsNumber, Is.EqualTo(Generations), "the run must reach its budget");
            Assert.That(samples, Has.Count.EqualTo(Generations), "one sample per generation");
            foreach (var s in samples)
            {
                Assert.That(s.DistinctRaw, Is.GreaterThanOrEqualTo(1), $"gen {s.Generation}: empty population");
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

    // ---------------------------------------------------------------- tests

    /// <summary>
    /// The two operators are different in kind, on a cohort whose answer is hand-computable.
    /// Twelve parents of fitness 0..11; six offspring (even indices) of fitness 200, six (odd
    /// indices) of fitness -1. Per-index greedy keeps the six strong trials and the six parents
    /// they belong to; best-N keeps the six strong trials plus the six <i>globally</i> best
    /// parents (11..6), evicting p1, p3 and p5 although each of them beat its own trial.
    /// </summary>
    [Test]
    public void PairwiseAndBestN_SelectDifferentSets_OnTheSameCohort()
    {
        Configure(Substrate.Rastrigin);
        var population = new MetaPopulation(12, 12, new ProbeChromosome());

        var parents = Enumerable.Range(0, 12)
            .Select(i => (IChromosome)new ProbeChromosome { Fitness = (double)i })
            .ToList();
        var offspring = Enumerable.Range(0, 12)
            .Select(i => (IChromosome)new ProbeChromosome { Fitness = i % 2 == 0 ? 200.0 : -1.0 })
            .ToList();

        var pairwise = new PairwiseProbe().SelectForTest(population, offspring, parents);
        var elitist = new ElitistProbe().SelectForTest(population, offspring, parents);

        var pairwiseSet = new HashSet<IChromosome>(pairwise, ReferenceEqualityComparer.Instance);
        var elitistSet = new HashSet<IChromosome>(elitist, ReferenceEqualityComparer.Instance);

        Assert.Multiple(() =>
        {
            Assert.That(pairwise, Has.Count.EqualTo(12), "pairwise must return exactly MinSize");
            Assert.That(pairwiseSet, Has.Count.EqualTo(12), "pairwise must not repeat an instance");

            // One-to-one is a per-index rule: every kept chromosome is the better of its own pair.
            for (int i = 0; i < 12; i++)
            {
                var expected = i % 2 == 0 ? offspring[i] : parents[i];
                Assert.That(pairwise[i], Is.SameAs(expected),
                    $"index {i}: pairwise must keep the better of (parent, its own trial)");
            }

            Assert.That(elitist, Has.Count.EqualTo(12), "best-N must return exactly MinSize");
            Assert.That(elitistSet.SetEquals(pairwiseSet), Is.False,
                "best-N and one-to-one must not coincide on a mixed cohort");
            Assert.That(elitist.Except(pairwiseSet, ReferenceEqualityComparer.Instance), Is.Not.Empty,
                "best-N must keep chromosomes the one-to-one rule discards");
            Assert.That(pairwise.Except(elitistSet, ReferenceEqualityComparer.Instance),
                Does.Contain(parents[1]).And.Contain(parents[3]).And.Contain(parents[5]),
                "p1/p3/p5 beat their own trials and must survive only under one-to-one");
            Assert.That(elitistSet, Does.Contain(parents[11]).And.Contain(parents[6]),
                "best-N fills its remaining slots with the globally best parents");
        });
    }

    /// <summary>
    /// The precondition of the whole question, measured on the real engine under the shipped
    /// selection: <see cref="FitnessBasedPairwiseReinsertion"/> pairs by position, so it is the
    /// canonical rule only if <c>parents[i]</c> is the target of <c>offspring[i]</c>.
    ///
    /// It is — and the reason is worth writing down, because the source reads the other way.
    /// <see cref="EliteSelection"/> does hand the reinsertion a fitness-ordered list (asserted
    /// below); but a trial's target is drawn with <c>MatchingKind.Current</c>, that is
    /// <c>parents[referenceIndex]</c> — from the same ordered list, not from the population. The two
    /// orders cannot diverge, so index pairing and target pairing coincide.
    ///
    /// Measured, not read: every trial's target gene vector is captured from the operator hook and
    /// compared with the values of the parent sitting at its own position.
    /// </summary>
    [Test]
    public void UnderEliteSelection_EveryTrialSitsAtItsTargetsIndex()
    {
        var arm = new Arm("pairwise(1:1)", true, ArmSelection.Elite);
        RunArm(Substrate.Rastrigin, arm, seed: 0, out var stats, out _);

        TestContext.Progress.WriteLine(
            $"elite selection: {stats.AlignedTrials}/{stats.TrialsChecked} trials aligned with their own " +
            $"index ({Share(stats.AlignedTrials, stats.TrialsChecked)}%) | " +
            $"generations with an unsorted parent list: {stats.UnsortedParentGenerations} | " +
            $"duplicate parent instances: {stats.DuplicateParentInstances} | " +
            $"offspring admitted: {stats.OffspringAdmitted} | " +
            $"evicted index-winners: {stats.EvictedWinners}");

        Assert.Multiple(() =>
        {
            Assert.That(stats.TrialsChecked, Is.GreaterThan(0), "the operator hook must observe trials");
            Assert.That(stats.UnsortedParentGenerations, Is.Zero,
                "EliteSelection must hand the reinsertion a fitness-sorted parent list; sorting alone is harmless because the target is drawn from that same list");
            Assert.That(stats.AlignedTrials, Is.EqualTo(stats.TrialsChecked),
                "every trial must sit at its target's index, otherwise the pairwise class pairs strangers");
            Assert.That(stats.EvictedWinners, Is.Zero,
                "the pairwise operator must never evict an index-winner, whatever the index means");
        });
    }

    /// <summary>
    /// The controlled counterpart: the same DE, the same pairwise class, the same budget, with the
    /// selection operator changed in one respect only — it returns the generation in population
    /// order instead of fitness order. If the alignment failure above is caused by ordering, this
    /// arm must align on every trial; if it is caused by something else in the engine, it will not,
    /// and the probe says so instead of blaming the selection.
    /// </summary>
    [Test]
    public void UnderPopulationOrderSelection_EveryTrialSitsAtItsTargetsIndex()
    {
        var arm = new Arm("pairwise+order", true, ArmSelection.PopulationOrder);
        RunArm(Substrate.Rastrigin, arm, seed: 0, out var stats, out _);

        TestContext.Progress.WriteLine(
            $"population-order selection: {stats.AlignedTrials}/{stats.TrialsChecked} trials aligned " +
            $"({Share(stats.AlignedTrials, stats.TrialsChecked)}%) | " +
            $"evicted index-winners: {stats.EvictedWinners}");

        Assert.Multiple(() =>
        {
            Assert.That(stats.TrialsChecked, Is.GreaterThan(0), "the operator hook must observe trials");
            Assert.That(stats.AlignedTrials, Is.EqualTo(stats.TrialsChecked),
                "with a population-ordered parent list every trial must sit at its target's index — otherwise the alignment failure has another cause");
            Assert.That(stats.EvictedWinners, Is.Zero,
                "the pairwise operator must never evict an index-winner");
        });
    }

    /// <summary>
    /// The paired sweep: two substrates x three arms x four seeds, same budget and F/CR as the
    /// precocity, reseed and soft-survivor probes.
    /// <para>
    /// <c>shipped(best-N)</c> is the operator DE ships with; <c>pairwise(1:1)</c> is the one-to-one
    /// class as a user can plug it in today (under the shipped selection); <c>pairwise+order</c> is
    /// the canonical rule, with the parent list left in population order so that index pairing and
    /// target pairing coincide. The first two differ in the reinsertion operator, the last two in
    /// the selection order only.
    /// </para>
    /// Mechanism assertions are hard and one-sided: a pairwise arm never evicts an index-winner.
    /// Finals and diversity are the read-out and are reported, not promised.
    /// </summary>
    [Test]
    public void ThreeArms_SameSeedsSameBudget()
    {
        var checkpoints = new[] { Generations / 4, Generations / 2, 3 * Generations / 4, Generations };
        foreach (var substrate in new[] { Substrate.Rastrigin, Substrate.SudokuR1 })
        {
            foreach (var arm in Arms)
            {
                var finals = new List<double>();
                foreach (var seed in new[] { 0, 1, 7, 42 })
                {
                    var samples = RunArm(substrate, arm, seed, out var stats, out var elapsedMs);
                    finals.Add(samples[^1].Best);
                    int improvements = samples.Zip(samples.Skip(1), (a, b) => b.Best < a.Best - 1e-12).Count(x => x);
                    var atCheckpoints = checkpoints
                        .Select(cp => samples[cp - 1])
                        .Select(s =>
                            $"g{s.Generation}: raw {s.DistinctRaw,2}/50 dec {s.DistinctDecoded,2}/50 " +
                            $"std {s.MaxStd:F4} best {s.Best:F2}");
                    TestContext.Progress.WriteLine(
                        $"{substrate,-9} {arm.Name,-16} seed={seed,2} {elapsedMs,6:F0}ms | " +
                        $"improvements {improvements,3}/{Generations} | " +
                        $"aligned {Share(stats.AlignedTrials, stats.TrialsChecked),3}% | " +
                        $"evicted-winners {stats.EvictedWinners,5} | foreign-survivors {stats.ForeignSurvivors,5} | " +
                        string.Join(" | ", atCheckpoints));

                    if (arm.Pairwise)
                    {
                        // One-to-one means exactly this: a chromosome that beat its own trial is
                        // never displaced, by anybody, in any generation.
                        Assert.That(stats.EvictedWinners, Is.Zero,
                            $"{substrate} {arm.Name} seed={seed}: the one-to-one rule evicted {stats.EvictedWinners} index-winner(s) — it is no longer one-to-one");
                    }
                }

                finals.Sort();
                double median = (finals[1] + finals[2]) / 2.0;
                TestContext.Progress.WriteLine(
                    $"{substrate,-9} {arm.Name,-16} MEDIAN best over four seeds = {median:F4} " +
                    $"[{finals[0]:F4} - {finals[^1]:F4}]");
            }
        }
    }

    private static int Share(int part, int total) => total == 0 ? 0 : (int)Math.Round(100.0 * part / total);
}
