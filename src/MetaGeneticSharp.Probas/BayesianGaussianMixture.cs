#nullable disable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using GeneticSharp;
using Microsoft.ML.Probabilistic.Algorithms;
using Microsoft.ML.Probabilistic.Distributions;
using Microsoft.ML.Probabilistic.Math;
using Microsoft.ML.Probabilistic.Models;
using Range = Microsoft.ML.Probabilistic.Models.Range;

namespace MetaGeneticSharp
{
    /// <summary>
    /// The deterministic message-passing algorithm used to infer the mixture posteriors.
    /// </summary>
    public enum EdaInferenceAlgorithm
    {
        /// <summary>
        /// Expectation Propagation: the reference algorithm for mixture models in the Infer.NET
        /// documentation; approximates the posterior with factorised messages, usually tighter than
        /// VMP on mixtures.
        /// </summary>
        ExpectationPropagation,

        /// <summary>
        /// Variational Message Passing: the conjugate-dual algorithm; deterministic too, tends to
        /// under-disperse the posterior on mixtures (useful as a comparison arm, not as a default).
        /// </summary>
        VariationalMessagePassing,
    }

    /// <summary>
    /// <para>
    /// A Bayesian mixture of Gaussians fitted with Infer.NET: posteriors over the component weights
    /// (Dirichlet), the component means (Vector Gaussian, one per component) and the component
    /// precisions (Wishart, one per component) are inferred by EP or VMP from the observed elite
    /// points. Instances are immutable; offspring are drawn from the <b>posterior predictive</b> --
    /// each sample first draws weights, a mean and a precision from their posteriors, then a point
    /// from the resulting Gaussian -- so early generations keep exploring the parameter uncertainty
    /// instead of committing to a point estimate.
    /// </para>
    /// <para>
    /// Two measured engineering constraints shape the implementation:
    /// <list type="bullet">
    /// <item><b>Symmetry.</b> With a uniform Dirichlet prior, the posterior has a degenerate mode
    /// where all components collapse onto one wide Gaussian covering the data (measured: weights
    /// 0.99/0.01, both means at the data centroid, under EP and VMP alike). Random initialisation
    /// of the assignments does NOT escape it. The fit therefore seeds the assignments with a few
    /// seeded k-means (Lloyd) iterations -- a deterministic, data-aligned split that the message
    /// passing then refines into a full Bayesian posterior. The k-means is an initialiser, not the
    /// estimator: on later fits through the cached model it only has to provide <i>some</i> split
    /// to break the permutation symmetry, the posterior re-fits from the observed data alone.</item>
    /// <item><b>Compilation.</b> InferenceEngine compiles the model on the first Infer (measured:
    /// 1-3 s); a per-generation fit would pay it 40 times per run. The compiled model is cached
    /// per (point count, dimension, components, algorithm, iterations) and reused by re-observing
    /// the data -- only message passing is paid per fit.</item>
    /// </list>
    /// The inference cost stays bounded by construction and declared, not hidden: dimension at
    /// most <see cref="MaxDimension"/>, components at most <see cref="MaxComponents"/>, points at
    /// most <see cref="MaxPoints"/>, iterations at most <see cref="MaxIterations"/>; every bound is
    /// enforced by an explicit exception, and the wall-clock cost of the inference is measured and
    /// exposed as <see cref="InferenceTimeMs"/> (CoursIA #24013).
    /// </para>
    /// </summary>
    public sealed class BayesianGaussianMixture
    {
        /// <summary>The declared dimension bound: inference cost grows with d^2 (covariance), the bound keeps it trivial.</summary>
        public const int MaxDimension = 5;

        /// <summary>The declared component bound of the EDA plugin (#24013): K &lt;= 3.</summary>
        public const int MaxComponents = 3;

        /// <summary>The declared elite bound of the EDA plugin (#24013): at most 200 points per fit.</summary>
        public const int MaxPoints = 200;

        /// <summary>The declared iteration bound: EP/VMP message passing budget per fit.</summary>
        public const int MaxIterations = 100;

        private readonly Dirichlet _weightsPosterior;
        private readonly VectorGaussian[] _meansPosterior;
        private readonly Wishart[] _precisionsPosterior;

        private BayesianGaussianMixture(Dirichlet weights, VectorGaussian[] means, Wishart[] precisions,
            int dimension, int points, int components, double inferenceTimeMs, bool degraded)
        {
            _weightsPosterior = weights;
            _meansPosterior = means;
            _precisionsPosterior = precisions;
            Dimension = dimension;
            PointsFitted = points;
            Components = components;
            InferenceTimeMs = inferenceTimeMs;
            DegradedToFewerComponents = degraded;
        }

        /// <summary>The problem dimension of the fitted points.</summary>
        public int Dimension { get; }

        /// <summary>The number of points the posteriors were fitted from.</summary>
        public int PointsFitted { get; }

        /// <summary>The number of mixture components actually fitted (may be lower than requested, see <see cref="DegradedToFewerComponents"/>).</summary>
        public int Components { get; }

        /// <summary>The measured wall-clock inference time in milliseconds: the cost the caller declares rather than hides.</summary>
        public double InferenceTimeMs { get; }

        /// <summary>True when too few points were available for the requested K and the fit fell back to fewer components.</summary>
        public bool DegradedToFewerComponents { get; }

        /// <summary>The posterior mean component weights (sum to 1).</summary>
        public double[] Weights => _weightsPosterior.GetMean().ToArray();

        /// <summary>The posterior mean of each component mean, as plain coordinate arrays.</summary>
        public double[][] Means => _meansPosterior.Select(m => m.GetMean().ToArray()).ToArray();

        /// <summary>
        /// Fits the mixture posteriors. The model is the Infer.NET mixture of Gaussians: weights ~
        /// Dirichlet(1..1), means[k] ~ VectorGaussian(centroid of the data, large variance),
        /// precisions[k] ~ Wishart(d+2, small identity scale), z[i] ~ Discrete(weights),
        /// x[i] | z ~ VectorGaussian(means[z], precisions[z]^-1); the assignment vector z is
        /// initialised from seeded k-means to break the symmetric collapse. Passing a
        /// <paramref name="seed"/> initialises the Infer.NET global random source so the whole
        /// pipeline (k-means seeding, posterior-parameter sampling) is reproducible run for run.
        /// </summary>
        /// <exception cref="InvalidOperationException">any declared bound is exceeded.</exception>
        public static BayesianGaussianMixture Fit(IList<double[]> points, int components = 2,
            EdaInferenceAlgorithm algorithm = EdaInferenceAlgorithm.ExpectationPropagation,
            int iterations = 30, int? seed = null)
        {
            ArgumentNullException.ThrowIfNull(points);
            if (points.Count == 0) throw new ArgumentException("at least one point is required", nameof(points));

            int d = points[0].Length;
            if (d > MaxDimension)
            {
                throw new InvalidOperationException(
                    $"BayesianGaussianMixture is bounded to d <= {MaxDimension} (declared cost bound, #24013); got d = {d}. Reduce the dimension or pick another operator.");
            }

            if (components < 1 || components > MaxComponents)
            {
                throw new InvalidOperationException(
                    $"BayesianGaussianMixture is bounded to 1 <= K <= {MaxComponents} components (declared cost bound, #24013); got K = {components}.");
            }

            if (points.Count > MaxPoints)
            {
                throw new InvalidOperationException(
                    $"BayesianGaussianMixture is bounded to {MaxPoints} points per fit (declared elite bound, #24013); got {points.Count}. Truncate the elites.");
            }

            if (iterations < 1 || iterations > MaxIterations)
            {
                throw new ArgumentOutOfRangeException(nameof(iterations), $"1..{MaxIterations}");
            }

            for (int i = 0; i < points.Count; i++)
            {
                if (points[i].Length != d) throw new ArgumentException($"point {i} has dimension {points[i].Length}, expected {d}", nameof(points));
            }

            if (seed.HasValue) Rand.Restart(seed.Value);

            // Degradation rule: a K-component posterior needs several points per component to stay
            // proper; below 4 points per requested component the fit degrades K rather than throwing.
            int k = Math.Min(components, Math.Max(1, points.Count / 4));

            var data = new Vector[points.Count];
            for (int i = 0; i < points.Count; i++) data[i] = Vector.FromArray(points[i]);

            var model = GetCompiledModel(points.Count, d, k, algorithm, iterations, points);
            model.X.ObservedValue = data;
            model.MeanCenter.ObservedValue = Vector.FromArray(Centroid(points));

            // Per-fit symmetry breaking: the compiled model carries the initial messages of the
            // FIRST fit that built it; a later fit through the cache re-seeds them from the CURRENT
            // data, otherwise the message passing restarts from a stale split and can fall back
            // into the degenerate one-component mode (measured on the bimodal bench: discovery
            // without convergence).
            if (k > 1)
            {
                int[] assignment = KMeansAssign(points, d, k);
                var zInit = new Discrete[points.Count];
                for (int i = 0; i < points.Count; i++) zInit[i] = Discrete.PointMass(assignment[i], k);
                model.Z.InitialiseTo(Distribution<int>.Array(zInit));
            }

            var watch = Stopwatch.StartNew();
            var weightsPosterior = model.Engine.Infer<Dirichlet>(model.Weights);
            var meansPosterior = model.Engine.Infer<VectorGaussian[]>(model.Means);
            var precisionsPosterior = model.Engine.Infer<Wishart[]>(model.Precisions);
            watch.Stop();

            return new BayesianGaussianMixture(weightsPosterior, meansPosterior, precisionsPosterior,
                d, points.Count, k, watch.Elapsed.TotalMilliseconds, k < components);
        }

        /// <summary>
        /// Draws one point from the posterior predictive. The component is chosen by the caller's
        /// random source (the GA's seeded stream, so selection is reproducible by the harness seed);
        /// the posterior parameter draws and the final point use the Infer.NET global source,
        /// reproducible through the <paramref name="seed"/> passed to <see cref="Fit"/>.
        /// </summary>
        public double[] Sample(IRandomization rnd)
        {
            ArgumentNullException.ThrowIfNull(rnd);

            var drawnWeights = _weightsPosterior.Sample();
            double u = rnd.GetDouble();
            int component = drawnWeights.Count - 1;
            double cumulative = 0.0;
            for (int i = 0; i < drawnWeights.Count; i++)
            {
                cumulative += drawnWeights[i];
                if (u < cumulative)
                {
                    component = i;
                    break;
                }
            }

            var mean = _meansPosterior[component].Sample();
            var precision = _precisionsPosterior[component].Sample();
            return VectorGaussian.FromMeanAndPrecision(mean, precision).Sample().ToArray();
        }

        private static double[] Centroid(IList<double[]> points)
        {
            int d = points[0].Length;
            var centroid = new double[d];
            foreach (var p in points)
            {
                for (int i = 0; i < d; i++) centroid[i] += p[i];
            }
            for (int i = 0; i < d; i++) centroid[i] /= points.Count;
            return centroid;
        }

        // -----------------------------------------------------------------------
        // Compiled-model cache: the model structure depends only on (n, d, K, algorithm,
        // iterations); re-observing the data reuses the compiled inference program and pays only
        // the message passing. The k-means initialisation of the assignments is baked at
        // compilation time from the FIRST data set of the key -- on later fits it only breaks the
        // permutation symmetry, the posterior re-fits from the observed data alone.
        // -----------------------------------------------------------------------

        private static readonly ConcurrentDictionary<string, CompiledModel> ModelCache = new();

        private static CompiledModel GetCompiledModel(int n, int d, int k, EdaInferenceAlgorithm algorithm, int iterations, IList<double[]> firstFitData)
        {
            return ModelCache.GetOrAdd(
                $"{n}|{d}|{k}|{algorithm}|{iterations}",
                _ => CompileModel(n, d, k, algorithm, iterations, firstFitData));
        }

        private sealed class CompiledModel
        {
            public InferenceEngine Engine;
            public Variable<Vector> Weights;
            public VariableArray<Vector> Means;
            public VariableArray<PositiveDefiniteMatrix> Precisions;
            public VariableArray<Vector> X;
            public VariableArray<int> Z;
            public Variable<Vector> MeanCenter;
        }

        private static CompiledModel CompileModel(int n, int d, int k, EdaInferenceAlgorithm algorithm, int iterations, IList<double[]> data)
        {
            Range item = new Range(n).Named("item");
            Range cmp = new Range(k).Named("component");

            var weights = Variable.Dirichlet(cmp, Vector.Constant(k, 1.0)).Named("weights");
            var means = Variable.Array<Vector>(cmp).Named("means");
            var precisions = Variable.Array<PositiveDefiniteMatrix>(cmp).Named("precisions");

            // Weakly informative priors: means free to roam one box around the data centroid
            // (re-observed per fit, variance 100 per coordinate), precisions around a broad covariance.
            var meanCenter = Variable.New<Vector>().Named("meanCenter");
            means[cmp] = Variable.VectorGaussianFromMeanAndVariance(meanCenter, PositiveDefiniteMatrix.IdentityScaledBy(d, 100.0)).ForEach(cmp);
            precisions[cmp] = Variable.WishartFromShapeAndScale(d + 2, PositiveDefiniteMatrix.IdentityScaledBy(d, 1e-2)).ForEach(cmp);

            var z = Variable.Array<int>(item).Named("z");
            var x = Variable.Array<Vector>(item).Named("x");
            using (Variable.ForEach(item))
            {
                z[item] = Variable.Discrete(weights);
                using (Variable.Switch(z[item]))
                {
                    x[item] = Variable.VectorGaussianFromMeanAndPrecision(means[z[item]], precisions[z[item]]);
                }
            }

            // Symmetry breaking, seeded: a few k-means (Lloyd) iterations give the message passing a
            // split starting point. Random assignment alone measured as insufficient (collapse to
            // weights 0.99/0.01); k-means is an initialiser -- the posterior remains fully Bayesian.
            if (k > 1)
            {
                int[] assignment = KMeansAssign(data, d, k);
                var zInit = new Discrete[n];
                for (int i = 0; i < n; i++) zInit[i] = Discrete.PointMass(assignment[i], k);
                z.InitialiseTo(Distribution<int>.Array(zInit));
            }

            var engine = algorithm == EdaInferenceAlgorithm.ExpectationPropagation
                ? new InferenceEngine(new ExpectationPropagation())
                : new InferenceEngine(new VariationalMessagePassing());
            engine.NumberOfIterations = iterations;
            engine.ShowProgress = false;
            engine.ShowWarnings = false;

            return new CompiledModel
            {
                Engine = engine,
                Weights = weights,
                Means = means,
                Precisions = precisions,
                X = x,
                Z = z,
                MeanCenter = meanCenter,
            };
        }

        /// <summary>
        /// Seeded k-means (Lloyd) assignment of the data: centre selection picks seeded data points
        /// (draws from a LOCAL System.Random with a fixed seed -- deliberately NOT the Infer.NET
        /// source, so that compilation consumes no draws from it and the posterior-predictive
        /// sampling stream depends only on the seed passed to <see cref="Fit"/>, whether the model
        /// is compiled fresh or reused from the cache), then 8 Lloyd iterations. The returned
        /// assignment initialises the mixture assignments, nothing more.
        /// </summary>
        private static int[] KMeansAssign(IList<double[]> points, int d, int k)
        {
            int n = points.Count;
            var centres = new double[k][];
            var chosen = new HashSet<int>();
            var localRnd = new Random(20260101);
            for (int c = 0; c < k; c++)
            {
                int idx;
                int guard = 0;
                do
                {
                    idx = localRnd.Next(n);
                    guard++;
                }
                while (chosen.Contains(idx) && guard < 4 * n); // duplicate draw tolerated after a few retries: two equal centres still split
                chosen.Add(idx);
                centres[c] = (double[])points[idx].Clone();
            }

            var assignment = new int[n];
            for (int iter = 0; iter < 8; iter++)
            {
                for (int i = 0; i < n; i++)
                {
                    double best = double.PositiveInfinity;
                    int bestC = 0;
                    for (int c = 0; c < k; c++)
                    {
                        double dist = 0.0;
                        for (int j = 0; j < d; j++)
                        {
                            double diff = points[i][j] - centres[c][j];
                            dist += diff * diff;
                        }
                        if (dist < best)
                        {
                            best = dist;
                            bestC = c;
                        }
                    }
                    assignment[i] = bestC;
                }

                var sums = new double[k][];
                var counts = new int[k];
                for (int c = 0; c < k; c++) sums[c] = new double[d];
                for (int i = 0; i < n; i++)
                {
                    counts[assignment[i]]++;
                    for (int j = 0; j < d; j++) sums[assignment[i]][j] += points[i][j];
                }
                for (int c = 0; c < k; c++)
                {
                    if (counts[c] == 0) continue; // empty centre stays put; the point-mass init tolerates it
                    for (int j = 0; j < d; j++) centres[c][j] = sums[c][j] / counts[c];
                }
            }

            return assignment;
        }
    }
}
