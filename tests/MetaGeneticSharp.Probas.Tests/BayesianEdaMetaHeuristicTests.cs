using GeneticSharp;

namespace MetaGeneticSharp.Probas.Tests;

/// <summary>
///   Acceptance tests of the <see cref="BayesianEdaMetaHeuristic"/> plugin (#24013): the structural
///   tests verify the assembled root, its comma-strategy reinsertion and the propagation of the
///   configuration; the model tests pin the declared cost bounds (d &lt;= 5, K &lt;= 3, elites &lt;= 200 --
///   each enforced by an explicit exception, never silently clamped), the recovery of a bimodal
///   structure by the Infer.NET posteriors, and the reproducibility of the posterior-predictive
///   sampling under a fixed pair (Infer.NET seed, harness seed).
/// </summary>
public class BayesianEdaMetaHeuristicTests
{
    private IRandomization _previousRandomization = null!;

    [SetUp]
    public void UseSeedableFastRandom()
    {
        _previousRandomization = RandomizationProvider.Current;
        RandomizationProvider.Current = new FastRandomRandomization();
    }

    [TearDown]
    public void RestoreRandomization()
    {
        RandomizationProvider.Current = _previousRandomization;
        FastRandomRandomization.ResetSeed(null);
    }

    private static BayesianEdaMetaHeuristic NewEda()
    {
        var eda = new BayesianEdaMetaHeuristic();
        // A double<->double identity converter: the test chromosome stores genes as bare doubles.
        eda.SetGeometricConverter(new GeometricConverter<double>
        {
            GeneToDoubleConverter = (_, v) => v,
            DoubleToGeneConverter = (_, d) => d,
        });
        return eda;
    }

    // -----------------------------------------------------------------------
    // Structure of the assembled compound
    // -----------------------------------------------------------------------

    [Test]
    public void GetDefaultReinsertion_ReturnsPureReinsertion()
    {
        // The EDA is a comma strategy: the model is estimated from the current generation's elites
        // and the whole offspring is sampled from it.
        Assert.That(new BayesianEdaMetaHeuristic().GetDefaultReinsertion(), Is.InstanceOf<PureReinsertion>());
    }

    [Test]
    public void Build_AssemblesEdaRootWithConfigAndConverter()
    {
        var eda = NewEda();
        eda.Components = 3;
        eda.EliteCount = 15;
        eda.Algorithm = EdaInferenceAlgorithm.VariationalMessagePassing;
        eda.InferenceIterations = 12;
        eda.Seed = 42;

        var built = eda.Build();

        Assert.That(built, Is.InstanceOf<BayesianEdaMetaHeuristicMetaHeuristic>());
        var root = (BayesianEdaMetaHeuristicMetaHeuristic)built;
        Assert.Multiple(() =>
        {
            Assert.That(((NamedEntity)root).Name, Is.EqualTo("Bayesian Estimation-of-Distribution"));
            Assert.That(root.Components, Is.EqualTo(3));
            Assert.That(root.EliteCount, Is.EqualTo(15));
            Assert.That(root.Algorithm, Is.EqualTo(EdaInferenceAlgorithm.VariationalMessagePassing));
            Assert.That(root.InferenceIterations, Is.EqualTo(12));
            Assert.That(root.Seed, Is.EqualTo(42));
            Assert.That(root.GeometricConverter, Is.SameAs(eda.GeometricConverter));
        });
    }

    // -----------------------------------------------------------------------
    // Declared cost bounds (d <= 5, K <= 3, elites <= 200, iterations <= 100)
    // -----------------------------------------------------------------------

    [Test]
    public void Fit_RejectsDimensionBeyondTheDeclaredBound()
    {
        var points = Enumerable.Range(0, 20).Select(_ => new double[BayesianGaussianMixture.MaxDimension + 1]).ToList();

        var ex = Assert.Throws<InvalidOperationException>(() => BayesianGaussianMixture.Fit(points, components: 2));
        Assert.That(ex!.Message, Does.Contain($"d <= {BayesianGaussianMixture.MaxDimension}"), "the bound must be declared in the message, not silently clamped");
    }

    [Test]
    public void Fit_RejectsComponentsBeyondTheDeclaredBound()
    {
        var points = Enumerable.Range(0, 20).Select(_ => new double[2]).ToList();

        var ex = Assert.Throws<InvalidOperationException>(() => BayesianGaussianMixture.Fit(points, components: BayesianGaussianMixture.MaxComponents + 1));
        Assert.That(ex!.Message, Does.Contain($"K <= {BayesianGaussianMixture.MaxComponents}"));
    }

    [Test]
    public void Fit_RejectsElitesBeyondTheDeclaredBound()
    {
        var points = Enumerable.Range(0, BayesianGaussianMixture.MaxPoints + 1).Select(_ => new double[2]).ToList();

        var ex = Assert.Throws<InvalidOperationException>(() => BayesianGaussianMixture.Fit(points, components: 2));
        Assert.That(ex!.Message, Does.Contain($"Truncate the elites"));
    }

    [Test]
    public void Fit_RejectsIterationCountBeyondTheDeclaredBound()
    {
        var points = Enumerable.Range(0, 20).Select(_ => new double[2]).ToList();

        Assert.Throws<ArgumentOutOfRangeException>(() => BayesianGaussianMixture.Fit(points, components: 2, iterations: BayesianGaussianMixture.MaxIterations + 1));
    }

    [Test]
    public void Fit_DegradesComponentsRatherThanFailingOnTinyElites()
    {
        // 5 points cannot carry a proper 3-component posterior: the fit degrades K and says so,
        // instead of throwing on a legitimately small early population.
        var points = new List<double[]> { new[] { 0.0, 0.0 }, new[] { 0.1, 0.0 }, new[] { 0.0, 0.1 }, new[] { 5.0, 5.0 }, new[] { 5.1, 5.0 } };

        var model = BayesianGaussianMixture.Fit(points, components: 3);

        Assert.Multiple(() =>
        {
            Assert.That(model.Components, Is.LessThan(3));
            Assert.That(model.DegradedToFewerComponents, Is.True);
        });
    }

    // -----------------------------------------------------------------------
    // The inference itself: bimodal recovery by the posteriors
    // -----------------------------------------------------------------------

    [Test]
    public void Fit_TwoSeparatedClusters_RecoversBothMeans()
    {
        var rnd = new Random(7);
        var c1 = new[] { -6.0, -4.0 };
        var c2 = new[] { 6.0, 4.0 };
        var points = new List<double[]>();
        for (int i = 0; i < 60; i++) points.Add(Noisy(rnd, c1, 0.5));
        for (int i = 0; i < 60; i++) points.Add(Noisy(rnd, c2, 0.5));

        var model = BayesianGaussianMixture.Fit(points, components: 2, seed: 42);

        // Each estimated mean matches its nearest true centre (0.3 = 60 % of the cluster sd), and
        // the two estimates split between the two centres: both modes are represented.
        foreach (var mean in model.Means)
        {
            double d1 = Distance(mean, c1);
            double d2 = Distance(mean, c2);
            Assert.That(Math.Min(d1, d2), Is.LessThan(0.3),
                $"estimated mean ({mean[0]:F2}, {mean[1]:F2}) matches neither cluster centre");
        }
        int matchedCentres = model.Means
            .GroupBy(m => Distance(m, c1) <= Distance(m, c2) ? 1 : 2)
            .Count();
        Assert.That(matchedCentres, Is.EqualTo(2), "both modes must be represented by distinct components");

        double weightSum = model.Weights.Sum();
        Assert.That(weightSum, Is.EqualTo(1.0).Within(1e-9));
        Assert.That(model.InferenceTimeMs, Is.GreaterThan(0.0), "the inference cost is measured, not assumed");
        TestContext.Out.WriteLine(
            $"MoG fit: means ({model.Means[0][0]:F2}, {model.Means[0][1]:F2}) / ({model.Means[1][0]:F2}, {model.Means[1][1]:F2}), " +
            $"weights [{model.Weights[0]:F2}, {model.Weights[1]:F2}], inference {model.InferenceTimeMs:F0} ms");
    }

    [Test]
    public void Fit_ReusedModelKeepsBothModesOnUnbalancedRefit()
    {
        // Regression of the cached-model path: the second fit goes through the SAME compiled model
        // (same point count, dimension, K, algorithm, iterations) but with UNBALANCED data -- the
        // elite dynamics of the EDA, where one mode dominates the ranking. If the initial messages
        // of the first fit were stale, the message passing falls back into the degenerate
        // one-component mode and the minor mode is lost (measured on the bimodal bench).
        var rnd = new Random(11);
        var c1 = new[] { -8.0, -6.0 };
        var c2 = new[] { 8.0, 6.0 };

        // First fit of this cache key: balanced data (compiles the model).
        var balanced = new List<double[]>();
        for (int i = 0; i < 25; i++) balanced.Add(Noisy(rnd, c1, 0.6));
        for (int i = 0; i < 25; i++) balanced.Add(Noisy(rnd, c2, 0.6));
        var first = BayesianGaussianMixture.Fit(balanced, components: 2, seed: 42);
        Assert.That(first.Means.Length, Is.EqualTo(2));

        // Second fit through the cache: 35 points on the dominant mode, 15 on the minor one.
        var unbalanced = new List<double[]>();
        for (int i = 0; i < 35; i++) unbalanced.Add(Noisy(rnd, c2, 0.6));
        for (int i = 0; i < 15; i++) unbalanced.Add(Noisy(rnd, c1, 0.6));
        var second = BayesianGaussianMixture.Fit(unbalanced, components: 2, seed: 42);

        foreach (var mean in second.Means)
        {
            double d1 = Distance(mean, c1);
            double d2 = Distance(mean, c2);
            Assert.That(Math.Min(d1, d2), Is.LessThan(0.4),
                $"estimated mean ({mean[0]:F2}, {mean[1]:F2}) matches neither cluster centre on the refit");
        }
        int matchedCentres = second.Means
            .GroupBy(m => Distance(m, c1) <= Distance(m, c2) ? 1 : 2)
            .Count();
        Assert.That(matchedCentres, Is.EqualTo(2), "the minor mode must survive the unbalanced refit through the cached model");
        Assert.That(second.Weights.Min(), Is.GreaterThan(0.05), "the minor component keeps measurable weight");
        TestContext.Out.WriteLine(
            $"Unbalanced refit: means ({second.Means[0][0]:F2}, {second.Means[0][1]:F2}) / ({second.Means[1][0]:F2}, {second.Means[1][1]:F2}), " +
            $"weights [{second.Weights[0]:F2}, {second.Weights[1]:F2}]");
    }

    // -----------------------------------------------------------------------
    // Reproducibility of the posterior-predictive sampling
    // -----------------------------------------------------------------------

    [Test]
    public void Sample_IsReproducibleUnderTheSameSeedPair()
    {
        var points = BimodalPoints(seed: 7);
        FastRandomRandomization.ResetSeed(99);
        var first = BayesianGaussianMixture.Fit(points, components: 2, seed: 42);
        var draws1 = Draw(first, count: 10);

        FastRandomRandomization.ResetSeed(99);
        var second = BayesianGaussianMixture.Fit(points, components: 2, seed: 42);
        var draws2 = Draw(second, count: 10);

        Assert.That(draws2.Length, Is.EqualTo(draws1.Length));
        for (int i = 0; i < draws1.Length; i++)
        {
            Assert.That(draws2[i], Is.EqualTo(draws1[i]).Within(1e-12), $"draw {i} diverged between the two seeded runs");
        }
    }

    [Test]
    public void Sample_ProducesPointsInTheDataDimension()
    {
        var model = BayesianGaussianMixture.Fit(BimodalPoints(seed: 3), components: 2, seed: 42);
        FastRandomRandomization.ResetSeed(1);

        for (int i = 0; i < 50; i++)
        {
            var point = model.Sample(RandomizationProvider.Current);
            Assert.That(point.Length, Is.EqualTo(2));
            Assert.That(point.All(double.IsFinite), Is.True);
        }
    }

    private static double[][] Draw(BayesianGaussianMixture model, int count)
    {
        var draws = new double[count][];
        for (int i = 0; i < count; i++) draws[i] = model.Sample(RandomizationProvider.Current);
        return draws;
    }

    private static List<double[]> BimodalPoints(int seed)
    {
        var rnd = new Random(seed);
        var points = new List<double[]>();
        for (int i = 0; i < 40; i++) points.Add(Noisy(rnd, new[] { -6.0, -4.0 }, 0.8));
        for (int i = 0; i < 40; i++) points.Add(Noisy(rnd, new[] { 6.0, 4.0 }, 0.8));
        return points;
    }

    private static double[] Noisy(Random rnd, double[] centre, double sd)
    {
        var p = new double[centre.Length];
        for (int i = 0; i < centre.Length; i++)
        {
            double u1 = 1.0 - rnd.NextDouble();
            double u2 = rnd.NextDouble();
            p[i] = centre[i] + sd * Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
        }
        return p;
    }

    private static double Distance(double[] a, double[] b)
    {
        double s = 0.0;
        for (int i = 0; i < a.Length; i++) s += (a[i] - b[i]) * (a[i] - b[i]);
        return Math.Sqrt(s);
    }
}
