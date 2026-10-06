using GeneticSharp;
using MetaGeneticSharp;

namespace MetaGeneticSharp.Domain.Tests.Compound;

/// <summary>
///   Acceptance tests for the <see cref="NaturalEvolutionStrategy"/> compound (Wierstra et al.,
///   JMLR 2014). The structural tests verify the assembled root and its comma-strategy reinsertion;
///   the math tests pin the rank utilities, the paper learning rates and the small dense linear
///   algebra (matrix exponential, inverse) against closed forms; the distribution tests check the
///   fit, the natural-coordinate round trip, the direction of one update step and the invariants
///   the xNES shape update must keep (unit determinant, maintained inverse); the keystones run both
///   covariance models inside a real <see cref="MetaGeneticAlgorithm"/> on Sphere.
/// </summary>
public class NaturalEvolutionStrategyTests
{
    private static NaturalEvolutionStrategy NewNes(NesCovarianceModel model, int maxGenerations = 20)
    {
        var nes = new NaturalEvolutionStrategy { CovarianceModel = model, MaxGenerations = maxGenerations };
        // A double<->double identity converter: the test chromosome stores genes as bare doubles.
        nes.SetGeometricConverter(new GeometricConverter<double>
        {
            GeneToDoubleConverter = (_, v) => v,
            DoubleToGeneConverter = (_, d) => d,
        });
        return nes;
    }

    [Test]
    public void GetDefaultReinsertion_ReturnsPureReinsertion()
    {
        // NES is a comma strategy: the gradient is estimated from samples of the current distribution only.
        Assert.That(new NaturalEvolutionStrategy().GetDefaultReinsertion(), Is.InstanceOf<PureReinsertion>());
    }

    [TestCase(NesCovarianceModel.Full, "Exponential Natural Evolution Strategy")]
    [TestCase(NesCovarianceModel.Separable, "Separable Natural Evolution Strategy")]
    public void Build_AssemblesNesRootNamedAfterCovarianceModel(NesCovarianceModel model, string expectedName)
    {
        var built = NewNes(model).Build();

        Assert.That(built, Is.InstanceOf<NaturalEvolutionStrategyMetaHeuristic>());
        Assert.That(((NamedEntity)built).Name, Is.EqualTo(expectedName));
    }

    [Test]
    public void Build_PropagatesConfigurationToRoot()
    {
        var shape = NaturalEvolutionStrategyMath.Identity(3);
        var nes = NewNes(NesCovarianceModel.Separable);
        nes.MeanLearningRate = 0.5;
        nes.ScaleLearningRate = 0.2;
        nes.ShapeLearningRate = 0.1;
        nes.InitialShape = shape;

        var root = (NaturalEvolutionStrategyMetaHeuristic)nes.Build();

        Assert.Multiple(() =>
        {
            Assert.That(root.CovarianceModel, Is.EqualTo(NesCovarianceModel.Separable));
            Assert.That(root.MeanLearningRate, Is.EqualTo(0.5));
            Assert.That(root.ScaleLearningRate, Is.EqualTo(0.2));
            Assert.That(root.ShapeLearningRate, Is.EqualTo(0.1));
            Assert.That(root.InitialShape, Is.SameAs(shape));
            Assert.That(root.GeometricConverter, Is.SameAs(nes.GeometricConverter));
        });
    }

    [TestCase(2)]
    [TestCase(7)]
    [TestCase(10)]
    [TestCase(50)]
    public void RankUtilities_SumToZero_AreNonIncreasing_AndWeighOnlyTheBetterHalfPositively(int lambda)
    {
        var u = NaturalEvolutionStrategyMath.RankUtilities(lambda);

        Assert.That(u.Length, Is.EqualTo(lambda));
        Assert.That(u.Sum(), Is.EqualTo(0.0).Within(1e-12));
        for (int k = 1; k < lambda; k++)
        {
            Assert.That(u[k], Is.LessThanOrEqualTo(u[k - 1] + 1e-15), $"utility increases at rank {k}");
        }
        // max(0, ln(lambda/2 + 1) - ln k) is positive exactly for k < lambda/2 + 1.
        int positive = u.Count(x => x > 0);
        Assert.That(positive, Is.LessThanOrEqualTo(lambda / 2 + 1));
        Assert.That(positive, Is.GreaterThanOrEqualTo(1));
    }

    [Test]
    public void DefaultLearningRates_MatchThePaper()
    {
        const int d = 5;
        double xnes = 3.0 * (3.0 + Math.Log(d)) / (5.0 * d * Math.Sqrt(d));
        double snes = (3.0 + Math.Log(d)) / (5.0 * Math.Sqrt(d));

        Assert.Multiple(() =>
        {
            Assert.That(NaturalEvolutionStrategyMath.DefaultScaleLearningRate(NesCovarianceModel.Full, d), Is.EqualTo(xnes).Within(1e-15));
            Assert.That(NaturalEvolutionStrategyMath.DefaultShapeLearningRate(d), Is.EqualTo(xnes).Within(1e-15));
            Assert.That(NaturalEvolutionStrategyMath.DefaultScaleLearningRate(NesCovarianceModel.Separable, d), Is.EqualTo(snes).Within(1e-15));
        });
    }

    [Test]
    public void MatrixExponential_OfDiagonal_IsElementwiseExp()
    {
        var a = new double[,] { { 1.5, 0.0, 0.0 }, { 0.0, -2.0, 0.0 }, { 0.0, 0.0, 0.0 } };

        var e = NaturalEvolutionStrategyMath.MatrixExponential(a);

        AssertMatrixEqual(e, new double[,] { { Math.Exp(1.5), 0.0, 0.0 }, { 0.0, Math.Exp(-2.0), 0.0 }, { 0.0, 0.0, 1.0 } }, 1e-12);
    }

    [Test]
    public void MatrixExponential_OfSkewGenerator_IsARotation()
    {
        // expm([[0, -t], [t, 0]]) is the plane rotation by t: the closed form the xNES shape update relies on.
        const double t = 2.3;
        var a = new double[,] { { 0.0, -t }, { t, 0.0 } };

        var e = NaturalEvolutionStrategyMath.MatrixExponential(a);

        AssertMatrixEqual(e, new double[,] { { Math.Cos(t), -Math.Sin(t) }, { Math.Sin(t), Math.Cos(t) } }, 1e-12);
    }

    [Test]
    public void MatrixExponential_OfOppositeMatrices_AreInverses()
    {
        var a = new double[,] { { 0.3, -1.2, 0.7 }, { 2.1, -0.4, 0.05 }, { -0.9, 0.6, 1.1 } };
        var minus = new double[3, 3];
        for (int i = 0; i < 3; i++) for (int j = 0; j < 3; j++) minus[i, j] = -a[i, j];

        var product = NaturalEvolutionStrategyMath.Multiply(
            NaturalEvolutionStrategyMath.MatrixExponential(a),
            NaturalEvolutionStrategyMath.MatrixExponential(minus));

        AssertMatrixEqual(product, NaturalEvolutionStrategyMath.Identity(3), 1e-10);
    }

    [Test]
    public void Invert_TimesOriginal_IsIdentity_AndSingularThrows()
    {
        // A zero leading entry forces a row swap: the partial pivoting path is exercised.
        var a = new double[,] { { 0.0, 2.0, 1.0 }, { 1.0, 1.0, 0.0 }, { 3.0, 0.0, 4.0 } };

        var inverse = NaturalEvolutionStrategyMath.Invert(a);

        AssertMatrixEqual(NaturalEvolutionStrategyMath.Multiply(a, inverse), NaturalEvolutionStrategyMath.Identity(3), 1e-12);
        Assert.Throws<ArgumentException>(() => NaturalEvolutionStrategyMath.Invert(new double[,] { { 1.0, 2.0 }, { 2.0, 4.0 } }));
    }

    [Test]
    public void Fit_UsesCentroidMean_AndRootMeanPerCoordinateVariance()
    {
        var points = new List<double[]> { new[] { 0.0, 0.0 }, new[] { 2.0, 0.0 }, new[] { 0.0, 4.0 }, new[] { 2.0, 4.0 } };
        // Per-coordinate population variances are 1 and 4: sigma = sqrt((1 + 4) / 2).
        double expectedSigma = Math.Sqrt(2.5);

        var full = NesDistribution.Fit(NesCovarianceModel.Full, points);
        var separable = NesDistribution.Fit(NesCovarianceModel.Separable, points);

        Assert.Multiple(() =>
        {
            Assert.That(full.Mean, Is.EqualTo(new[] { 1.0, 2.0 }).Within(1e-12));
            Assert.That(full.Sigma, Is.EqualTo(expectedSigma).Within(1e-12));
            AssertMatrixEqual(full.Shape, NaturalEvolutionStrategyMath.Identity(2), 0.0);
            Assert.That(separable.Mean, Is.EqualTo(new[] { 1.0, 2.0 }).Within(1e-12));
            Assert.That(separable.Scales, Is.EqualTo(new[] { expectedSigma, expectedSigma }).Within(1e-12));
            Assert.That(double.IsNaN(separable.Sigma), Is.True);
        });
        Assert.Throws<ArgumentException>(() => NesDistribution.Fit(NesCovarianceModel.Full, points, NaturalEvolutionStrategyMath.Identity(3)));
    }

    [TestCase(NesCovarianceModel.Full)]
    [TestCase(NesCovarianceModel.Separable)]
    public void ToNatural_InvertsFromNatural(NesCovarianceModel model)
    {
        // A rotated initial shape makes the xNES round trip go through a non-trivial B and B^-1.
        var shape = NaturalEvolutionStrategyMath.MatrixExponential(new double[,] { { 0.0, -0.8, 0.1 }, { 0.8, 0.0, -0.4 }, { -0.1, 0.4, 0.0 } });
        var points = new List<double[]> { new[] { 1.0, -2.0, 0.5 }, new[] { 3.0, 0.0, -1.5 }, new[] { -1.0, 4.0, 2.0 } };
        var distribution = NesDistribution.Fit(model, points, shape);
        var z = new[] { 0.3, -1.7, 2.2 };

        var back = distribution.ToNatural(distribution.FromNatural(z));

        Assert.That(back, Is.EqualTo(z).Within(1e-12));
    }

    [TestCase(NesCovarianceModel.Full)]
    [TestCase(NesCovarianceModel.Separable)]
    public void Update_MovesTheMeanTowardTheBetterSamples_AndShrinksWhenTheyAreCloser(NesCovarianceModel model)
    {
        // Mean 0, unit scale, identity shape. The better half sits near +0.5 on axis 0, the worse half
        // far out at -3 and +/-3 on axis 1: the step must move the mean toward +x and shrink the scale.
        var start = NesDistribution.Fit(model, new List<double[]> { new[] { 1.0, 1.0 }, new[] { -1.0, -1.0 }, new[] { 1.0, -1.0 }, new[] { -1.0, 1.0 } });
        var ranked = new List<double[]>
        {
            new[] { 0.5, 0.1 }, new[] { 0.4, -0.1 }, new[] { 0.6, 0.0 }, new[] { 0.5, 0.05 },
            new[] { -3.0, 3.0 }, new[] { -3.0, -3.0 }, new[] { -2.5, 3.0 }, new[] { -3.0, 2.5 },
        };

        var next = start.Update(ranked, meanRate: 1.0, scaleRate: 0.5, shapeRate: 0.5);

        Assert.That(next.Mean[0], Is.GreaterThan(start.Mean[0]));
        if (model == NesCovarianceModel.Full)
        {
            Assert.That(next.Sigma, Is.LessThan(start.Sigma));
        }
        else
        {
            Assert.That(next.Scales[0], Is.LessThan(start.Scales[0]));
            Assert.That(next.Scales[1], Is.LessThan(start.Scales[1]));
        }
    }

    [Test]
    public void Update_Full_KeepsUnitShapeDeterminant_AndMaintainsTheInverse()
    {
        // tr(G_B) = 0 by construction, so det(expm(G_B)) = 1: the shape carries no scale, sigma carries it all.
        // The ranked samples are re-drawn from each current distribution through fixed natural coordinates,
        // so every step sees genuine samples of the distribution it updates (an anisotropic, correlated set).
        var start = NesDistribution.Fit(NesCovarianceModel.Full, new List<double[]> { new[] { 1.0, 0.0 }, new[] { -1.0, 0.0 }, new[] { 0.0, 1.0 }, new[] { 0.0, -1.0 } });
        var zs = new List<double[]>
        {
            new[] { 0.9, 0.8 }, new[] { 0.7, 0.9 }, new[] { -0.2, 0.1 }, new[] { 1.5, -1.4 },
            new[] { -2.0, 0.3 }, new[] { 0.2, -2.2 },
        };

        var next = start;
        for (int step = 0; step < 5; step++)
        {
            var current = next;
            next = current.Update(zs.Select(current.FromNatural).ToList(), 1.0, 0.3, 0.3);
        }

        Assert.That(next.Shape[0, 1], Is.Not.EqualTo(0.0), "the correlated samples should have rotated the shape off the axes");

        double det = next.Shape[0, 0] * next.Shape[1, 1] - next.Shape[0, 1] * next.Shape[1, 0];
        Assert.That(det, Is.EqualTo(1.0).Within(1e-10));
        AssertMatrixEqual(NaturalEvolutionStrategyMath.Multiply(next.Shape, next.InverseShape), NaturalEvolutionStrategyMath.Identity(2), 1e-10);
    }

    [Test]
    public void Sample_Separable_HasTheDistributionMeanAndScales()
    {
        BasicRandomization.ResetSeed(2014);
        // Centroid (2, -3); per-coordinate variances 2 and 2, so both scales are sqrt(2).
        var points = new List<double[]> { new[] { 0.0, -3.0 }, new[] { 4.0, -3.0 }, new[] { 2.0, -1.0 }, new[] { 2.0, -5.0 } };
        var distribution = NesDistribution.Fit(NesCovarianceModel.Separable, points);
        double scale = distribution.Scales[0];
        var rnd = new BasicRandomization();
        const int n = 20000;
        var samples = Enumerable.Range(0, n).Select(_ => distribution.Sample(rnd)).ToList();

        for (int i = 0; i < 2; i++)
        {
            double mean = samples.Average(s => s[i]);
            double sd = Math.Sqrt(samples.Average(s => (s[i] - mean) * (s[i] - mean)));
            // Standard errors at n = 20000: mean ~ scale / 141, sd ~ scale / 200; both bounds exceed 5 of them.
            Assert.That(mean, Is.EqualTo(distribution.Mean[i]).Within(6.0 * scale / Math.Sqrt(n)));
            Assert.That(sd, Is.EqualTo(scale).Within(0.03 * scale));
        }
    }

    /// <summary>
    /// KEYSTONES: the built compound drives a real <see cref="MetaGeneticAlgorithm"/> end-to-end on
    /// Sphere(5) (fitness = -sum of squares). The population must be diverse at start, since the first
    /// distribution is fitted to it. The run completes the planned generations and ends deep in the
    /// origin well, far below the best of a random start in [-10, 10]^5.
    /// </summary>
    [TestCase(NesCovarianceModel.Full)]
    [TestCase(NesCovarianceModel.Separable)]
    public void Build_DrivesMetaGeneticAlgorithm_EndToEnd_AndOptimises(NesCovarianceModel model)
    {
        BasicRandomization.ResetSeed(12345);
        var previous = RandomizationProvider.Current;
        RandomizationProvider.Current = new BasicRandomization();
        try
        {
            var metaHeuristic = NewNes(model, maxGenerations: 80).Build();
            var chromosome = new RandomDoubleChromosome(min: -10.0, max: 10.0, length: 5);
            var fitness = new FuncFitness(c =>
            {
                var values = ((RandomDoubleChromosome)c).GetDoubleValues();
                double s = 0.0;
                for (int i = 0; i < values.Length; i++) s += values[i] * values[i];
                return -s;
            });

            var population = new MetaPopulation(30, 30, chromosome);
            var ga = new MetaGeneticAlgorithm(population, fitness, new EliteSelection(), new UniformCrossover(0.5f), new UniformMutation(true), metaHeuristic)
            {
                Termination = new GenerationNumberTermination(80)
            };

            ga.Start();

            Assert.That(ga.BestChromosome, Is.Not.Null);
            Assert.That(ga.BestChromosome.Fitness, Is.Not.Null);
            double finalSumSq = -ga.BestChromosome.Fitness!.Value;
            Assert.Multiple(() =>
            {
                Assert.That(ga.GenerationsNumber, Is.EqualTo(80));
                Assert.That(ga.State, Is.EqualTo(GeneticAlgorithmState.TerminationReached));
                Assert.That(finalSumSq, Is.LessThan(1e-2), $"{model} NES should converge into the origin well; got sum-of-squares {finalSumSq}");
            });
        }
        finally
        {
            RandomizationProvider.Current = previous;
        }
    }

    private static void AssertMatrixEqual(double[,] actual, double[,] expected, double tolerance)
    {
        Assert.That(actual.GetLength(0), Is.EqualTo(expected.GetLength(0)));
        Assert.That(actual.GetLength(1), Is.EqualTo(expected.GetLength(1)));
        for (int i = 0; i < expected.GetLength(0); i++)
        {
            for (int j = 0; j < expected.GetLength(1); j++)
            {
                Assert.That(actual[i, j], Is.EqualTo(expected[i, j]).Within(tolerance), $"entry ({i},{j})");
            }
        }
    }

    /// <summary>A chromosome that randomises each gene in [min, max] on CreateNew, so the initial population is diverse.</summary>
    private sealed class RandomDoubleChromosome : ChromosomeBase
    {
        private readonly double _min;
        private readonly double _max;

        public RandomDoubleChromosome(double min, double max, int length) : base(length)
        {
            _min = min;
            _max = max;
            var rnd = RandomizationProvider.Current;
            for (int i = 0; i < Length; i++)
                ReplaceGene(i, new Gene(_min + rnd.GetDouble() * (_max - _min)));
        }

        public override IChromosome CreateNew() => new RandomDoubleChromosome(_min, _max, Length);

        public override Gene GenerateGene(int geneIndex) =>
            new Gene(_min + RandomizationProvider.Current.GetDouble() * (_max - _min));

        public double[] GetDoubleValues() => GetGenes().Select(g => (double)g.Value).ToArray();
    }
}
