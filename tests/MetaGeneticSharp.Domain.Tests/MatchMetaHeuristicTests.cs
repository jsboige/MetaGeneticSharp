using GeneticSharp;
using MetaGeneticSharp;

namespace MetaGeneticSharp.Domain.Tests;

public class MatchMetaHeuristicTests
{
    private static FloatingPointChromosome CreateChromosome()
    {
        return new FloatingPointChromosome(
            new double[] { 0, 0 },
            new double[] { 100, 100 },
            new int[] { 16, 16 },
            new int[] { 2, 2 });
    }

    private static MetaGeneticAlgorithm CreateAlgorithm(int generations, IMetaHeuristic metaHeuristic)
    {
        var fitness = new FuncFitness(c =>
        {
            var values = ((FloatingPointChromosome)c).ToFloatingPoints();
            return -(Math.Abs(values[0] - 42) + Math.Abs(values[1] - 13));
        });

        var population = new MetaPopulation(40, 40, CreateChromosome());

        var ga = new MetaGeneticAlgorithm(population, fitness, new EliteSelection(), new UniformCrossover(0.5f), new FlipBitMutation(), metaHeuristic)
        {
            Termination = new GenerationNumberTermination(generations)
        };
        return ga;
    }

    [Test]
    public void WithMatches_AssignsDefaultCachingScopes()
    {
        var heuristic = new MatchMetaHeuristic().WithMatches(MatchingKind.Current, MatchingKind.Random, MatchingKind.Best);

        Assert.Multiple(() =>
        {
            Assert.That(heuristic.Picker.MatchPicks, Has.Count.EqualTo(3));
            Assert.That(heuristic.Picker.MatchPicks[0].CachingScope, Is.EqualTo(ParamScope.None));
            Assert.That(heuristic.Picker.MatchPicks[1].CachingScope, Is.EqualTo(ParamScope.None));
            Assert.That(heuristic.Picker.MatchPicks[2].CachingScope, Is.EqualTo(ParamScope.MetaHeuristic | ParamScope.Generation));
        });
    }

    [Test]
    public void SelectMatches_CurrentAndNeighbor_PicksReferenceThenNext()
    {
        var picker = new MatchPicker((1, MatchingKind.Current, ParamScope.None), (1, MatchingKind.Neighbor, ParamScope.None));
        var parents = new List<IChromosome> { CreateChromosome(), CreateChromosome(), CreateChromosome() };
        var ctx = new EvolutionContext();

        var matches = picker.SelectMatches(new NoOpMetaHeuristic(), ctx, 1, new UniformCrossover(), parents);

        Assert.Multiple(() =>
        {
            Assert.That(matches, Has.Count.EqualTo(2));
            Assert.That(matches[0], Is.SameAs(parents[1]), "Current pick should return the reference parent.");
            Assert.That(matches[1], Is.SameAs(parents[2]), "Neighbor pick should return the parent after the reference.");
        });
    }

    [Test]
    public void GenerationExtensions_BestAndWorst_OrderByFitness()
    {
        var chromosomes = new List<IChromosome>();
        for (int i = 0; i < 5; i++)
        {
            var chromosome = CreateChromosome();
            chromosome.Fitness = i;
            chromosomes.Add(chromosome);
        }
        var generation = new Generation(1, chromosomes);

        var best = generation.GetBestChromosomes(2).ToList();
        var worst = generation.GetWorstChromosomes(2).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(best.Select(c => c.Fitness), Is.EqualTo(new double?[] { 4, 3 }));
            Assert.That(worst.Select(c => c.Fitness), Is.EqualTo(new double?[] { 0, 1 }));
        });
    }

    [Test]
    public void Start_MatchMetaHeuristic_CurrentRandom_RunsToTermination()
    {
        var ga = CreateAlgorithm(30, new MatchMetaHeuristic().WithMatches(MatchingKind.Current, MatchingKind.Random));

        ga.Start();

        Assert.Multiple(() =>
        {
            Assert.That(ga.State, Is.EqualTo(GeneticAlgorithmState.TerminationReached));
            Assert.That(ga.GenerationsNumber, Is.EqualTo(30));
            Assert.That(ga.BestChromosome.Fitness, Is.Not.Null);
        });
    }

    [Test]
    public void Start_MatchMetaHeuristic_BestAndWorstKinds_RunsToTermination()
    {
        // Covers the population-best path (first generations fall back to random while
        // BestChromosome is not yet computed) and the sorted best/worst lookups.
        var ga = CreateAlgorithm(15, new MatchMetaHeuristic().WithMatches(MatchingKind.Current, MatchingKind.Best, MatchingKind.Worst));

        ga.Start();

        Assert.That(ga.State, Is.EqualTo(GeneticAlgorithmState.TerminationReached));
    }

    [Test]
    public void Start_MatchMetaHeuristic_RouletteWheel_RunsToTermination()
    {
        var ga = CreateAlgorithm(15, new MatchMetaHeuristic().WithMatches(MatchingKind.Current, MatchingKind.RouletteWheel));

        ga.Start();

        Assert.That(ga.State, Is.EqualTo(GeneticAlgorithmState.TerminationReached));
    }

    [Test]
    public void Start_DefaultMetaHeuristic_MatchPathRestored_RunsToTermination()
    {
        var heuristic = new DefaultMetaHeuristic();
        // Touching the property at configuration time switches the crossover stage
        // to the match metaheuristic (PR #87 semantics).
        heuristic.MatchMetaHeuristic.EnableHyperSpeed = false;

        var ga = CreateAlgorithm(30, heuristic);

        ga.Start();

        Assert.Multiple(() =>
        {
            Assert.That(ga.State, Is.EqualTo(GeneticAlgorithmState.TerminationReached));
            Assert.That(ga.BestChromosome.Fitness, Is.Not.Null);
        });
    }
}
