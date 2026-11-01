using GeneticSharp;
using MetaGeneticSharp;

namespace MetaGeneticSharp.Domain.Tests.Compound;

/// <summary>
///   Acceptance tests for the <see cref="CovarianceMatrixAdaptationEvolutionStrategy"/> compound
///   (Hansen, "The CMA Evolution Strategy: A Tutorial", 2024). The structural tests verify the
///   assembled root and its comma-strategy reinsertion; the constant tests pin the paper's
///   recombination weights, effective population size and learning rates; the spectral tests pin the
///   Jacobi eigendecomposition and the two <c>C^{±1/2}</c> applications the sampler and the CSA path
///   rely on; the golden test reproduces one full update against an independent transcription of the
///   tutorial's equations; the keystone drives a real <see cref="MetaGeneticAlgorithm"/> end-to-end.
///   The restart test pins the state reset that the stall policy applies.
/// </summary>
public class CovarianceMatrixAdaptationEvolutionStrategyTests
{
    private static CovarianceMatrixAdaptationEvolutionStrategy NewCmaEs(int maxGenerations = 20)
    {
        var cma = new CovarianceMatrixAdaptationEvolutionStrategy { MaxGenerations = maxGenerations };
        cma.SetGeometricConverter(new GeometricConverter<double>
        {
            GeneToDoubleConverter = (_, v) => v,
            DoubleToGeneConverter = (_, d) => d,
        });
        return cma;
    }

    // --- Structure -------------------------------------------------------------------------

    [Test]
    public void GetDefaultReinsertion_ReturnsPureReinsertion()
    {
        // CMA-ES is a comma strategy: the update is estimated from samples of the current distribution only.
        Assert.That(new CovarianceMatrixAdaptationEvolutionStrategy().GetDefaultReinsertion(), Is.InstanceOf<PureReinsertion>());
    }

    [Test]
    public void Build_AssemblesCmaEsRoot()
    {
        var built = NewCmaEs().Build();

        Assert.That(built, Is.InstanceOf<CovarianceMatrixAdaptationEvolutionStrategyMetaHeuristic>());
        Assert.That(((NamedEntity)built).Name, Is.EqualTo("Covariance Matrix Adaptation Evolution Strategy"));
    }

    [Test]
    public void Build_PropagatesConfigurationToRoot()
    {
        var covariance = NaturalEvolutionStrategyMath.Identity(3);
        var cma = NewCmaEs();
        cma.InitialCovariance = covariance;
        cma.InitialStepSize = 0.25;
        cma.StallGenerations = 7;
        cma.MaxRestarts = 3;
        cma.RestartStepSize = 1.5;

        var root = (CovarianceMatrixAdaptationEvolutionStrategyMetaHeuristic)cma.Build();

        Assert.Multiple(() =>
        {
            Assert.That(root.InitialCovariance, Is.SameAs(covariance));
            Assert.That(root.InitialStepSize, Is.EqualTo(0.25));
            Assert.That(root.StallGenerations, Is.EqualTo(7));
            Assert.That(root.MaxRestarts, Is.EqualTo(3));
            Assert.That(root.RestartStepSize, Is.EqualTo(1.5));
            Assert.That(root.GeometricConverter, Is.SameAs(cma.GeometricConverter));
        });
    }

    // --- Constants -------------------------------------------------------------------------

    [TestCase(1, 4)]
    [TestCase(2, 6)]
    [TestCase(5, 8)]
    [TestCase(10, 10)]
    [TestCase(100, 17)]
    public void DefaultPopulationSize_IsFourPlusFloorThreeLogD(int dimension, int expected)
    {
        Assert.That(CovarianceMatrixAdaptationEvolutionStrategyMath.DefaultPopulationSize(dimension), Is.EqualTo(expected));
    }

    [TestCase(2)]
    [TestCase(8)]
    [TestCase(30)]
    public void RecombinationWeights_SumToOne_Decrease_AndZeroTheWorseHalf(int lambda)
    {
        var w = CovarianceMatrixAdaptationEvolutionStrategyMath.RecombinationWeights(lambda);
        int mu = lambda / 2;

        Assert.Multiple(() =>
        {
            Assert.That(w.Length, Is.EqualTo(lambda));
            Assert.That(w.Take(mu).Sum(), Is.EqualTo(1.0).Within(1e-12));
            Assert.That(w.Skip(mu).All(x => x == 0.0), Is.True, "the worse half must not take part in the recombination");
            Assert.That(w.Take(mu).All(x => x > 0.0), Is.True, "the better half must have strictly positive weights");
        });
        for (int i = 1; i < mu; i++)
        {
            Assert.That(w[i], Is.LessThan(w[i - 1]), $"weights must decrease at rank {i}");
        }
    }

    [TestCase(2)]
    [TestCase(8)]
    [TestCase(30)]
    public void EffectivePopulationSize_StaysBetweenOneAndMu(int lambda)
    {
        double mueff = CovarianceMatrixAdaptationEvolutionStrategyMath.EffectivePopulationSize(lambda);

        Assert.That(mueff, Is.GreaterThanOrEqualTo(1.0));
        Assert.That(mueff, Is.LessThanOrEqualTo(lambda / 2.0));
    }

    /// <summary>
    /// The constants for d = 3, λ = 8, against the tutorial's closed forms. The weights, c_c, c_1 and
    /// c_μ also match pycma 4.5's own <c>sp.weights</c> / <c>sp.cc</c> / <c>sp.c1</c> bit for bit; its
    /// <c>cs</c> and <c>damps</c> do <i>not</i>, because pycma 4.5 ships the meta-parameterised CSA of
    /// Akimoto &amp; Hansen (2020) rather than the classical formula pinned here.
    /// </summary>
    [Test]
    public void Constants_MatchHansen2024_ForThreeDimensionsAndEightSamples()
    {
        var c = CmaEsConstants.For(3, 8);

        Assert.Multiple(() =>
        {
            Assert.That(c.Lambda, Is.EqualTo(8));
            Assert.That(c.Mu, Is.EqualTo(4));
            Assert.That(c.MuEffective, Is.EqualTo(2.600178826113179).Within(1e-14));
            Assert.That(c.CSigma, Is.EqualTo(0.4339718132660928).Within(1e-14));
            Assert.That(c.DSigma, Is.EqualTo(1.4339718132660928).Within(1e-14));
            Assert.That(c.CCovariance, Is.EqualTo(0.5572511268704967).Within(1e-14));
            Assert.That(c.COne, Is.EqualTo(0.09483086969009787).Within(1e-14));
            Assert.That(c.CMu, Is.EqualTo(0.0713595201817421).Within(1e-14));
            Assert.That(c.ChiN, Is.EqualTo(1.5968775302586076).Within(1e-14));
        });

        var weights = CovarianceMatrixAdaptationEvolutionStrategyMath.RecombinationWeights(8);
        Assert.That(weights.Take(4).ToArray(), Is.EqualTo(new[]
        {
            0.5299301844787793, 0.28571428571428575, 0.14285714285714285, 0.04149838694979222,
        }).Within(1e-15));
    }

    // --- Spectral helpers ------------------------------------------------------------------

    [Test]
    public void SymmetricEigen_ReconstructsTheMatrix()
    {
        // V diag(D) V^T must give the matrix back: the invariant both C^{±1/2} applications rest on.
        var a = new double[,] { { 1.0, 0.2, 0.1 }, { 0.2, 1.5, -0.3 }, { 0.1, -0.3, 0.8 } };

        CovarianceMatrixAdaptationEvolutionStrategyMath.SymmetricEigen(a, out var values, out var vectors);

        var reconstructed = new double[3, 3];
        for (int i = 0; i < 3; i++)
        {
            for (int j = 0; j < 3; j++)
            {
                double s = 0.0;
                for (int k = 0; k < 3; k++) s += vectors[i, k] * values[k] * vectors[j, k];
                reconstructed[i, j] = s;
            }
        }
        AssertMatrixEqual(reconstructed, a, 1e-12);
        // The matrix has strictly positive eigenvalues, so the inverse root is well defined.
        Assert.That(values.All(v => v > 0.0), Is.True);
    }

    [Test]
    public void ScaleBySquareRoot_ComposedWithInverse_IsIdentity()
    {
        var a = new double[,] { { 1.0, 0.2, 0.1 }, { 0.2, 1.5, -0.3 }, { 0.1, -0.3, 0.8 } };
        CovarianceMatrixAdaptationEvolutionStrategyMath.SymmetricEigen(a, out var values, out var vectors);
        var v = new[] { 0.7, -1.3, 2.1 };

        var half = CovarianceMatrixAdaptationEvolutionStrategyMath.ScaleBySquareRoot(values, vectors, v);
        var back = CovarianceMatrixAdaptationEvolutionStrategyMath.ScaleByInverseSquareRoot(values, vectors, half);

        Assert.That(back, Is.EqualTo(v).Within(1e-12));
    }

    // --- The update ------------------------------------------------------------------------

    [Test]
    public void Fit_UsesCentroidMean_AndRootMeanPerCoordinateVariance()
    {
        var points = new List<double[]> { new[] { 0.0, 0.0 }, new[] { 2.0, 0.0 }, new[] { 0.0, 4.0 }, new[] { 2.0, 4.0 } };

        var fitted = CmaEsDistribution.Create(new[] { 1.0, 2.0 }, 1.0, NaturalEvolutionStrategyMath.Identity(2));
        var fromPopulation = CmaEsDistribution.Fit(points);

        Assert.Multiple(() =>
        {
            Assert.That(fitted.Mean, Is.EqualTo(new[] { 1.0, 2.0 }).Within(1e-12));
            Assert.That(fromPopulation.Mean, Is.EqualTo(new[] { 1.0, 2.0 }).Within(1e-12));
            // Per-coordinate population variances are 1 and 4: sigma = sqrt((1 + 4) / 2).
            Assert.That(fromPopulation.Sigma, Is.EqualTo(Math.Sqrt(2.5)).Within(1e-12));
            AssertMatrixEqual(fromPopulation.Covariance, NaturalEvolutionStrategyMath.Identity(2), 0.0);
        });
        Assert.Throws<ArgumentException>(() => CmaEsDistribution.Fit(points, NaturalEvolutionStrategyMath.Identity(3)));
    }

    /// <summary>
    /// The keystone of the update: one CMA-ES step on a state the reference transcription also built.
    /// Every quantity is pinned — mean, step size, both evolution paths and the full covariance —
    /// because a bug in the rank-one path or in the CSA would move at most one of them.
    /// </summary>
    [Test]
    public void Update_ReproducesTheTutorialEquations_OnAFixedGeneration()
    {
        var start = CmaEsDistribution.Create(
            new[] { 0.5, -0.3, 0.2 },
            0.7,
            new double[,] { { 1.0, 0.2, 0.1 }, { 0.2, 1.5, -0.3 }, { 0.1, -0.3, 0.8 } });
        var ranked = new List<double[]>
        {
            new[] { 0.9, -0.1, 0.35 }, new[] { 0.6, -0.55, 0.05 }, new[] { 1.2, 0.2, 0.5 }, new[] { 0.35, -0.8, -0.1 },
            new[] { -0.4, 0.9, 1.1 }, new[] { 1.6, -1.2, 0.0 }, new[] { -1.0, 0.1, -0.9 }, new[] { 2.1, 1.4, 1.6 },
        };

        var next = start.Update(ranked);

        Assert.Multiple(() =>
        {
            Assert.That(next.Mean, Is.EqualTo(new[]
            {
                0.8343187443204714, -0.2147631565791403, 0.2670400115868792,
            }).Within(1e-12));
            Assert.That(next.Sigma, Is.EqualTo(0.5840249081026014).Within(1e-12));
            Assert.That(next.PathCovariance, Is.EqualTo(new[]
            {
                0.6905341245144660, 0.17605638346014765, 0.13847089490204986,
            }).Within(1e-12));
            Assert.That(next.PathSigma, Is.EqualTo(new[]
            {
                0.6222696134041177, 0.09915686149346593, 0.11813843017410633,
            }).Within(1e-12));
            Assert.That(next.Generation, Is.EqualTo(1));
        });

        AssertMatrixEqual(next.Covariance, new double[,]
        {
            { 0.9021227384220115, 0.19115937771472102, 0.10109583062671901 },
            { 0.19115937771472102, 1.2660533301119494, -0.23992825252652905 },
            { 0.10109583062671901, -0.23992825252652905, 0.6739549460534303 },
        }, 1e-12);
    }

    [Test]
    public void Update_MovesTheMeanTowardTheBetterSamples_AndKeepsTheCovarianceSymmetric()
    {
        // Mean 0, unit scale, identity covariance. The better half sits near +0.5 on axis 0, the worse
        // half far out at -3 and +/-3 on axis 1: the step must move the mean toward +x.
        var start = CmaEsDistribution.Fit(new List<double[]>
        {
            new[] { 1.0, 1.0 }, new[] { -1.0, -1.0 }, new[] { 1.0, -1.0 }, new[] { -1.0, 1.0 },
        });
        var ranked = new List<double[]>
        {
            new[] { 0.5, 0.1 }, new[] { 0.4, -0.1 }, new[] { 0.6, 0.0 }, new[] { 0.5, 0.05 },
            new[] { -3.0, 3.0 }, new[] { -3.0, -3.0 }, new[] { -2.5, 3.0 }, new[] { -3.0, 2.5 },
        };

        var next = start.Update(ranked);

        Assert.Multiple(() =>
        {
            Assert.That(next.Mean[0], Is.GreaterThan(start.Mean[0]));
            Assert.That(next.Covariance[0, 1], Is.EqualTo(next.Covariance[1, 0]).Within(0.0), "the covariance must stay symmetric");
        });
    }

    [Test]
    public void Update_ShrinksTheStepSize_WhenThePathIsShorterThanItsExpectation()
    {
        // A population that barely moves relative to the scale it was drawn from produces a short
        // path: CSA must read that as "too large a step" and contract. This is the behaviour xNES does
        // not have, so it is worth pinning separately from the golden numbers.
        var start = CmaEsDistribution.Create(new[] { 0.0, 0.0 }, 5.0, NaturalEvolutionStrategyMath.Identity(2));
        var ranked = new List<double[]>
        {
            new[] { 0.01, 0.01 }, new[] { -0.01, 0.01 }, new[] { 0.01, -0.01 }, new[] { -0.01, -0.01 },
        };

        var next = start.Update(ranked);

        Assert.That(next.Sigma, Is.LessThan(start.Sigma));
    }

    [Test]
    public void Sample_HasTheCovarianceOfTheDistribution()
    {
        BasicRandomization.ResetSeed(2024);
        var distribution = CmaEsDistribution.Create(
            new[] { 1.0, -2.0 },
            0.5,
            new double[,] { { 2.0, 0.6 }, { 0.6, 1.0 } });
        var rnd = new BasicRandomization();
        const int n = 40000;
        var samples = Enumerable.Range(0, n).Select(_ => distribution.Sample(rnd)).ToList();

        for (int i = 0; i < 2; i++)
        {
            double mean = samples.Average(s => s[i]);
            // Standard error of the mean is sd / sqrt(n); 6 of them is a ~1e-9 tail.
            Assert.That(mean, Is.EqualTo(distribution.Mean[i]).Within(6.0 * 1.5 / Math.Sqrt(n)));
            for (int j = 0; j < 2; j++)
            {
                double covariance = samples.Average(s => (s[i] - distribution.Mean[i]) * (s[j] - distribution.Mean[j]));
                double expected = 0.25 * distribution.Covariance[i, j]; // sigma^2 * C
                Assert.That(covariance, Is.EqualTo(expected).Within(0.02 * Math.Max(0.25, Math.Abs(expected))), $"covariance ({i},{j})");
            }
        }
    }

    /// <summary>
    /// The update commutes with rotations: given an orthogonal <c>Q</c>, updating the state
    /// <c>(m, C)</c> on the points <c>x_i</c> and then rotating gives the same thing as rotating the
    /// state (<c>Qm</c>, <c>QCQᵀ</c>) and the points first. This is the algebraic form of rotation
    /// invariance — the property xNES also has, and the one a naive per-coordinate implementation
    /// would lose.
    /// </summary>
    [Test]
    public void Update_CommutesWithRotations()
    {
        var q = Givens(new[] { 0.7, -1.1 }); // three-dimensional: two Givens planes
        var mean = new[] { 0.5, -0.3, 0.2 };
        var covariance = new double[,] { { 1.0, 0.2, 0.1 }, { 0.2, 1.5, -0.3 }, { 0.1, -0.3, 0.8 } };
        var ranked = new List<double[]>
        {
            new[] { 0.9, -0.1, 0.35 }, new[] { 0.6, -0.55, 0.05 }, new[] { 1.2, 0.2, 0.5 }, new[] { 0.35, -0.8, -0.1 },
        };
        var start = CmaEsDistribution.Create(mean, 0.7, covariance);

        var plain = start.Update(ranked);

        var rotatedStart = CmaEsDistribution.Create(Apply(q, mean), 0.7, Rotate(q, covariance));
        var rotated = rotatedStart.Update(ranked.Select(p => Apply(q, p)).ToList());

        Assert.Multiple(() =>
        {
            Assert.That(rotated.Mean, Is.EqualTo(Apply(q, plain.Mean)).Within(1e-10));
            Assert.That(rotated.PathSigma, Is.EqualTo(Apply(q, plain.PathSigma)).Within(1e-10));
            Assert.That(rotated.Sigma, Is.EqualTo(plain.Sigma).Within(1e-10));
        });
        AssertMatrixEqual(rotated.Covariance, Rotate(q, plain.Covariance), 1e-10);
    }

    /// <summary>
    /// The discriminating demo the issue asks for: a rotated, badly conditioned ellipsoid
    /// <c>f(x) = Σ c_i (Qx)_i²</c> with <c>c_i</c> spread over five decades. CMA-ES and xNES run the
    /// same GA wiring, the same budget and the same population; the counter records the evaluation at
    /// which the best-so-far first fell to 1e-8. CMA-ES must reach the target inside the budget — the
    /// rank-one path and the trajectory-based step size are what buy that on a narrow valley.
    /// </summary>
    [Test]
    public void Evaluations_ToReachOneMinusEight_OnARotatedEllipsoid_CMAEsVersusXNes()
    {
        double[] scales = { 1e4, 1e3, 1e2, 1e1, 1.0 };
        var q = Givens(new[] { 0.6, -1.2, 0.35, 2.1 });

        int cmaEvals = CountEvaluationsToTarget(new CovarianceMatrixAdaptationEvolutionStrategy(), q, scales, out double cmaBest);
        int nesEvals = CountEvaluationsToTarget(new NaturalEvolutionStrategy(), q, scales, out double nesBest);

        TestContext.Progress.WriteLine($"[ellipsoid tourne] cibles 1e-8 -- CMA-ES {cmaEvals} evals (best {cmaBest:E3}), xNES {nesEvals} evals (best {nesBest:E3})");

        Assert.Multiple(() =>
        {
            Assert.That(cmaBest, Is.LessThanOrEqualTo(1e-8), "CMA-ES must reach f <= 1e-8 on the rotated ellipsoid inside the budget");
            Assert.That(cmaEvals, Is.GreaterThan(0));
        });
    }

    /// <summary>
    /// Runs the built compound inside a real <see cref="MetaGeneticAlgorithm"/> on the rotated
    /// ellipsoid and returns the evaluation index at which the best-so-far first reached
    /// <c>1e-8</c> (or <c>-1</c> if it never did), with the best value reached written to
    /// <paramref name="best"/>.
    /// </summary>
    private static int CountEvaluationsToTarget(
        GeometricMetaHeuristicBase strategy,
        double[,] rotation,
        double[] scales,
        out double best)
    {
        strategy.SetGeometricConverter(new GeometricConverter<double>
        {
            GeneToDoubleConverter = (_, v) => v,
            DoubleToGeneConverter = (_, d) => d,
        });
        BasicRandomization.ResetSeed(4242);
        var previous = RandomizationProvider.Current;
        RandomizationProvider.Current = new BasicRandomization();
        try
        {
            var built = strategy.Build();
            var chromosome = new RandomDoubleChromosome(min: -10.0, max: 10.0, length: scales.Length);
            int evaluations = 0;
            int reachedAt = -1;
            double runningBest = double.PositiveInfinity;
            var fitness = new FuncFitness(c =>
            {
                evaluations++;
                var rotated = Apply(rotation, ((RandomDoubleChromosome)c).GetDoubleValues());
                double f = 0.0;
                for (int i = 0; i < scales.Length; i++) f += scales[i] * rotated[i] * rotated[i];
                if (f < runningBest)
                {
                    runningBest = f;
                    if (f <= 1e-8 && reachedAt < 0) reachedAt = evaluations;
                }
                return -f;
            });

            var population = new MetaPopulation(30, 30, chromosome);
            var ga = new MetaGeneticAlgorithm(population, fitness, new EliteSelection(), new UniformCrossover(0.5f), new UniformMutation(true), built)
            {
                Termination = new GenerationNumberTermination(120)
            };
            ga.Start();

            best = runningBest;
            return reachedAt;
        }
        finally
        {
            RandomizationProvider.Current = previous;
        }
    }

    /// <summary>Orthogonal matrix built as a product of Givens rotations, one per (i, i+1) plane.</summary>
    private static double[,] Givens(IReadOnlyList<double> angles)
    {
        int n = angles.Count + 1;
        var q = new double[n, n];
        for (int i = 0; i < n; i++) q[i, i] = 1.0;
        for (int plane = 0; plane < angles.Count; plane++)
        {
            var g = new double[n, n];
            for (int i = 0; i < n; i++) g[i, i] = 1.0;
            double c = Math.Cos(angles[plane]), s = Math.Sin(angles[plane]);
            g[plane, plane] = c;
            g[plane, plane + 1] = -s;
            g[plane + 1, plane] = s;
            g[plane + 1, plane + 1] = c;
            q = Multiply(q, g);
        }
        return q;
    }

    private static double[] Apply(double[,] matrix, double[] vector)
    {
        var result = new double[matrix.GetLength(0)];
        for (int i = 0; i < result.Length; i++)
        {
            double s = 0.0;
            for (int j = 0; j < vector.Length; j++) s += matrix[i, j] * vector[j];
            result[i] = s;
        }
        return result;
    }

    private static double[,] Rotate(double[,] q, double[,] covariance) =>
        Multiply(Multiply(q, covariance), Transpose(q));

    private static double[,] Multiply(double[,] a, double[,] b)
    {
        int n = a.GetLength(0), m = b.GetLength(0), p = b.GetLength(1);
        var result = new double[n, p];
        for (int i = 0; i < n; i++)
        {
            for (int j = 0; j < p; j++)
            {
                double s = 0.0;
                for (int k = 0; k < m; k++) s += a[i, k] * b[k, j];
                result[i, j] = s;
            }
        }
        return result;
    }

    private static double[,] Transpose(double[,] a)
    {
        var result = new double[a.GetLength(1), a.GetLength(0)];
        for (int i = 0; i < a.GetLength(0); i++)
            for (int j = 0; j < a.GetLength(1); j++)
                result[j, i] = a[i, j];
        return result;
    }

    // --- Restart ----------------------------------------------------------------------------

    [Test]
    public void Restart_ResetsTheCovarianceAndThePaths_AndCountsTheRestart()
    {
        var start = CmaEsDistribution.Create(
            new[] { 0.5, -0.3 },
            0.7,
            new double[,] { { 2.0, 0.4 }, { 0.4, 1.0 } });
        var evolved = start
            .Update(new List<double[]> { new[] { 0.6, -0.2 }, new[] { 0.4, -0.4 }, new[] { 0.9, 0.1 }, new[] { 0.1, -0.8 } })
            .Update(new List<double[]> { new[] { 0.7, -0.1 }, new[] { 0.5, -0.3 }, new[] { 1.0, 0.2 }, new[] { 0.2, -0.7 } });

        var restarted = evolved.Restart(new[] { 3.0, -4.0 }, 2.5);

        Assert.Multiple(() =>
        {
            Assert.That(restarted.Mean, Is.EqualTo(new[] { 3.0, -4.0 }).Within(0.0));
            Assert.That(restarted.Sigma, Is.EqualTo(2.5));
            Assert.That(restarted.PathSigma, Is.EqualTo(new[] { 0.0, 0.0 }).Within(0.0));
            Assert.That(restarted.PathCovariance, Is.EqualTo(new[] { 0.0, 0.0 }).Within(0.0));
            Assert.That(restarted.Generation, Is.EqualTo(0));
            Assert.That(restarted.Restarts, Is.EqualTo(1));
        });
        AssertMatrixEqual(restarted.Covariance, NaturalEvolutionStrategyMath.Identity(2), 0.0);
    }

    // --- Keystone ---------------------------------------------------------------------------

    /// <summary>
    /// KEYSTONE: the built compound drives a real <see cref="MetaGeneticAlgorithm"/> end-to-end on
    /// Sphere(5) (fitness = -sum of squares). The run completes the planned generations and ends deep
    /// in the origin well.
    /// </summary>
    [Test]
    public void Build_DrivesMetaGeneticAlgorithm_EndToEnd_AndOptimises()
    {
        BasicRandomization.ResetSeed(12345);
        var previous = RandomizationProvider.Current;
        RandomizationProvider.Current = new BasicRandomization();
        try
        {
            var metaHeuristic = NewCmaEs(maxGenerations: 80).Build();
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
                Assert.That(finalSumSq, Is.LessThan(1e-8), $"CMA-ES should converge into the origin well; got sum-of-squares {finalSumSq}");
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
