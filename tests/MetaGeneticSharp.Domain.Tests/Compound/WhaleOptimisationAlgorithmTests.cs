using GeneticSharp;
using MetaGeneticSharp;
using MetaGeneticSharp.Domain.Tests.Geometric;

namespace MetaGeneticSharp.Domain.Tests.Compound;

/// <summary>
/// Phase 4 slice 4 acceptance tests: the Whale Optimisation Algorithm compound metaheuristic.
/// The structural tests verify the assembled primitive tree (IfElse → Match → CrossoverMetaHeuristic
/// with geometric operators); the operator unit tests verify the encircling/bubble-net math; the
/// end-to-end keystone runs the built WOA against a real <see cref="MetaGeneticAlgorithm"/>,
/// proving every composed primitive (params, case generator, match, geometric crossover, embedding)
/// executes without fault.
/// </summary>
public class WhaleOptimisationAlgorithmTests
{
    private static WhaleOptimisationAlgorithm NewWoa(int maxGenerations = 20)
    {
        var woa = new WhaleOptimisationAlgorithm { MaxGenerations = maxGenerations };
        // A double↔double identity converter: DoubleArrayChromosome stores genes as bare doubles.
        woa.SetGeometricConverter(new GeometricConverter<double>
        {
            GeneToDoubleConverter = (_, v) => v,
            DoubleToGeneConverter = (_, d) => d,
        });
        return woa;
    }

    [Test]
    public void GetDefaultReinsertion_ReturnsPureReinsertion()
    {
        var woa = new WhaleOptimisationAlgorithm();

        // WOA overrides the base FitnessBasedElitistReinsertion with PureReinsertion.
        Assert.That(woa.GetDefaultReinsertion(), Is.InstanceOf<PureReinsertion>());
    }

    [Test]
    public void Build_AssemblesIfElseRootNamedAfterAlgorithm()
    {
        var woa = NewWoa();

        // Build() returns the assembled main heuristic itself (the wrapping happens via SubMetaHeuristic),
        // so the returned object is the WOA root: an IfElseMetaHeuristic carrying the algorithm name + citation.
        var built = woa.Build();

        Assert.That(built, Is.InstanceOf<IfElseMetaHeuristic>());
        Assert.That(((NamedEntity)built).Name, Is.EqualTo("Whale Optimisation Algorithm"));
    }

    [Test]
    public void Build_WrapsMainWithNoMutationAndForcedReinsertion()
    {
        var woa = NewWoa();

        // Build() (from GeometricMetaHeuristicBase) wraps the main heuristic with the No-Mutation scope
        // then a forced PureReinsertion layer, exposed through the returned object's SubMetaHeuristic chain.
        var built = woa.Build();

        var reinsertionWrapper = built.SubMetaHeuristic as ReinsertionMetaHeuristic;
        Assert.That(reinsertionWrapper, Is.Not.Null, "Build should wrap with a ReinsertionMetaHeuristic");
        Assert.That(reinsertionWrapper.StaticOperator, Is.InstanceOf<PureReinsertion>(),
            "WOA overrides the default reinsertion to PureReinsertion");

        var noMutationLayer = reinsertionWrapper.SubMetaHeuristic as ScopedMetaHeuristic;
        Assert.That(noMutationLayer, Is.Not.Null, "the reinsertion wrapper's sub should be the No-Mutation scoped layer");
        Assert.That(((NamedEntity)noMutationLayer).Name, Does.Contain("No-Mutation"));
    }

    [Test]
    public void EncirclingOperator_ComputesEncirclingFormula()
    {
        var woa = NewWoa();
        // DefaultEncirclingPreyOperator: geometricValue = geneValues[1] - A * |C * geneValues[1] - geneValues[0]|
        // With geneValues = [10 (current), 30 (target)], A = 1, C = 1:
        //   = 30 - 1 * |1 * 30 - 10| = 30 - 20 = 10
        var result = woa.EncirclingOperator(
            geneIndex: 0,
            geneValues: new List<object> { 10.0, 30.0 },
            geometricConverter: woa.GeometricConverter,
            A: 1.0,
            C: 1.0);

        Assert.That(Convert.ToDouble(result), Is.EqualTo(10.0));
    }

    [Test]
    public void BubbleOperator_ComputesSpiralFormula()
    {
        var woa = NewWoa();
        // DefaultBubbleNetOperator with l = 0 collapses the spiral to geneValues[1]:
        //   |v1 - v0| * exp(0) * cos(0) + v1 = |v1 - v0| * 1 * 1 + v1
        // With geneValues = [10 (current), 30 (best)], b (HelicoidScale) = 1:
        //   = |30 - 10| + 30 = 20 + 30 = 50
        var result = woa.BubbleOperator(
            geneIndex: 0,
            geneValues: new List<object> { 10.0, 30.0 },
            geometricConverter: woa.GeometricConverter,
            l: 0.0,
            b: 1.0);

        Assert.That(Convert.ToDouble(result), Is.EqualTo(50.0));
    }

    [Test]
    public void GetSimpleBubbleNetOperator_ReturnsConvexCombination()
    {
        // The simple bubble-net operator is a convex combination (default mix 0.5):
        //   0.5 * geneValues[0] + 0.5 * geneValues[1]
        var op = WhaleOptimisationAlgorithm.GetSimpleBubbleNetOperator();
        var woa = NewWoa();

        var result = op(
            geneIndex: 0,
            geneValues: new List<object> { 10.0, 30.0 },
            geometricConverter: woa.GeometricConverter,
            l: 0.0,
            b: 1.0);

        Assert.That(Convert.ToDouble(result), Is.EqualTo(20.0));
    }

    [Test]
    public void GetSimpleBubbleNetOperator_CustomMixShiftsTowardTarget()
    {
        // mix = 0.25 → 0.25*current + 0.75*target → closer to target.
        var op = WhaleOptimisationAlgorithm.GetSimpleBubbleNetOperator(0.25);
        var woa = NewWoa();

        var result = op(
            geneIndex: 0,
            geneValues: new List<object> { 10.0, 30.0 },
            geometricConverter: woa.GeometricConverter,
            l: 0.0,
            b: 1.0);

        Assert.That(Convert.ToDouble(result), Is.EqualTo(25.0));
    }

    /// <summary>
    /// KEYSTONE: the built WOA drives a real <see cref="MetaGeneticAlgorithm"/> end-to-end. This
    /// exercises every composed primitive (generation-scoped params a/a2, individual-scoped A/C/l,
    /// the random-vs-best case generator, the Update-Tracking vs Bubble-Net branch, Match pairing,
    /// and the geometric crossover with embedding) against a live evolution.
    /// </summary>
    [Test]
    public void Build_DrivesMetaGeneticAlgorithm_EndToEnd()
    {
        var woa = NewWoa(maxGenerations: 15);
        var metaHeuristic = woa.Build();

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
