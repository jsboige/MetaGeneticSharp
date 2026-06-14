using GeneticSharp;
using MetaGeneticSharp;
using MetaGeneticSharp.Domain.Tests.Geometric;

namespace MetaGeneticSharp.Domain.Tests.Compound;

/// <summary>
/// Phase 4 slice 6 acceptance tests: the Forensic-Based Investigation compound metaheuristic.
/// The structural tests verify the assembled primitive tree (a GenerationMetaHeuristic root
/// cycling the A1/A2/B1/B2 investigation steps, each a Match/IfElse over a GeometricCrossover)
/// and the pairwise reinsertion override; the end-to-end keystone runs the built FBI against a
/// real <see cref="MetaGeneticAlgorithm"/>, proving every composed primitive — the
/// Evolution-scoped pbSize, the Generation-scoped pBest (derived from Chromosomes via MaxBy, the
/// adaptation for the order-preserving MetaPopulation) and pWorst (GetWorstChromosomes), the
/// Individual-scoped nChange/randomBetter, the A2 fitness-scale switch, and the four step
/// geometric operators — executes against a live evolution.
/// </summary>
public class ForensicBasedInvestigationTests
{
    private static ForensicBasedInvestigation NewFbi(int maxGenerations = 20)
    {
        var fbi = new ForensicBasedInvestigation { MaxGenerations = maxGenerations };
        // A double<->double identity converter: DoubleArrayChromosome stores genes as bare doubles.
        fbi.SetGeometricConverter(new GeometricConverter<double>
        {
            GeneToDoubleConverter = (_, v) => v,
            DoubleToGeneConverter = (_, d) => d,
        });
        return fbi;
    }

    [Test]
    public void GetDefaultReinsertion_ReturnsFitnessBasedPairwise()
    {
        var fbi = new ForensicBasedInvestigation();

        // The original FBI publication describes a pairwise reinsertion scheme; the compound overrides
        // GetDefaultReinsertion to return the FitnessBasedPairwiseReinsertion ported alongside it.
        Assert.That(fbi.GetDefaultReinsertion(), Is.InstanceOf<FitnessBasedPairwiseReinsertion>());
    }

    [Test]
    public void Build_AssemblesGenerationMetaHeuristicRootNamedAfterAlgorithm()
    {
        var fbi = NewFbi();

        // Build() returns the assembled main heuristic itself. FBI's root is a GenerationMetaHeuristic
        // (it cycles the A1/A2/B1/B2 investigation steps once per generation), carrying the algorithm name + citation.
        var built = fbi.Build();

        Assert.That(built, Is.InstanceOf<GenerationMetaHeuristic>());
        Assert.That(((NamedEntity)built).Name, Is.EqualTo("Forensic Based Investigation"));
    }

    [Test]
    public void Build_WrapsMainWithNoMutationAndForcedPairwiseReinsertion()
    {
        var fbi = NewFbi();

        // Build() (from GeometricMetaHeuristicBase) wraps the main heuristic with the No-Mutation scope
        // then a forced reinsertion layer, exposed through the returned object's SubMetaHeuristic chain.
        var built = fbi.Build();

        var reinsertionWrapper = built.SubMetaHeuristic as ReinsertionMetaHeuristic;
        Assert.That(reinsertionWrapper, Is.Not.Null, "Build should wrap with a ReinsertionMetaHeuristic");
        Assert.That(reinsertionWrapper.StaticOperator, Is.InstanceOf<FitnessBasedPairwiseReinsertion>(),
            "FBI overrides GetDefaultReinsertion to the pairwise scheme");

        var noMutationLayer = reinsertionWrapper.SubMetaHeuristic as ScopedMetaHeuristic;
        Assert.That(noMutationLayer, Is.Not.Null, "the reinsertion wrapper's sub should be the No-Mutation scoped layer");
        Assert.That(((NamedEntity)noMutationLayer).Name, Does.Contain("No-Mutation"));
    }

    /// <summary>
    /// KEYSTONE: the built FBI drives a real <see cref="MetaGeneticAlgorithm"/> end-to-end. This
    /// exercises every composed primitive — the Evolution-scoped pbSize, the Generation-scoped pBest
    /// (current-generation best via Chromosomes.MaxBy, the order-preserving adaptation) and pWorst
    /// (GetWorstChromosomes), the Individual-scoped nChange/randomBetter, the A2 fitness-scale
    /// IfElse switch, and the A1/A2/B1/B2 geometric operators over GeometricCrossover<object> —
    /// against a live evolution.
    /// </summary>
    [Test]
    public void Build_DrivesMetaGeneticAlgorithm_EndToEnd()
    {
        var fbi = NewFbi(maxGenerations: 15);
        var metaHeuristic = fbi.Build();

        // Fitness: minimize the sum of squares (target is the origin), so higher fitness = closer to (0,0).
        var chromosome = new DoubleArrayChromosome(new double[] { 50.0, 50.0 });
        var fitness = new FuncFitness(c =>
        {
            var values = ((DoubleArrayChromosome)c).GetDoubleValues();
            return -(values[0] * values[0] + values[1] * values[1]);
        });

        var population = new MetaPopulation(20, 20, chromosome);
        var ga = new MetaGeneticAlgorithm(
            population,
            fitness,
            new EliteSelection(),
            new UniformCrossover(0.5f),
            new UniformMutation(true),
            metaHeuristic)
        {
            Termination = new GenerationNumberTermination(15)
        };

        ga.Start();

        Assert.Multiple(() =>
        {
            Assert.That(ga.GenerationsNumber, Is.EqualTo(15));
            Assert.That(ga.State, Is.EqualTo(GeneticAlgorithmState.TerminationReached));
            Assert.That(ga.BestChromosome, Is.Not.Null);
            Assert.That(ga.BestChromosome.Fitness, Is.Not.Null);
        });
    }
}
