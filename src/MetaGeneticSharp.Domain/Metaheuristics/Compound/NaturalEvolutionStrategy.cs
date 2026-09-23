#nullable disable
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using GeneticSharp;

namespace MetaGeneticSharp
{
    /// <summary>
    /// The covariance model of a <see cref="NaturalEvolutionStrategy"/> search distribution.
    /// </summary>
    public enum NesCovarianceModel
    {
        /// <summary>
        /// Exponential NES (xNES): full covariance <c>σ² B·Bᵀ</c>, adapted in the distribution's own
        /// natural coordinates. The update is equivariant under any rotation of the search space.
        /// </summary>
        Full,

        /// <summary>
        /// Separable NES (SNES): diagonal covariance <c>diag(s²)</c>, one step size per coordinate.
        /// Same natural-gradient machinery as xNES restricted to the coordinate axes, hence
        /// axis-aligned by construction.
        /// </summary>
        Separable,
    }

    /// <summary>
    ///   Natural Evolution Strategies (Wierstra, Schaul, Glasmachers, Sun, Peters &amp; Schmidhuber,
    ///   "Natural Evolution Strategies", JMLR 15, 2014), expressed as a geometric compound
    ///   metaheuristic. Each generation samples the whole population from a Gaussian search
    ///   distribution, ranks the samples, and follows the <i>natural</i> gradient of the expected
    ///   rank-based utility with respect to the distribution parameters. Two covariance models share
    ///   the code path (<see cref="CovarianceModel"/>): the full-covariance xNES and the diagonal
    ///   SNES.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Why this compound exists.</b> Every Gaussian operator shipped before it (the
    /// <see cref="BareBonesParticleSwarm"/> draw, the difference vectors of
    /// <see cref="DifferentialEvolution"/>, the per-gene arithmetic of the geometric crossovers)
    /// works coordinate by coordinate, so its effective covariance is diagonal in the problem's
    /// axes. A rotated benchmark (<c>RotatedFitness</c>) moves the landscape's principal directions
    /// off those axes. xNES is the smallest Gaussian operator that is not axis-aligned: its update
    /// is equivariant under rotation, so rotating the problem rotates the whole search trajectory
    /// with it. SNES is the same algorithm with the covariance forced back to diagonal, which makes
    /// the pair a controlled experiment: a difference in rotation sensitivity between them is
    /// attributable to the covariance structure alone.
    /// </para>
    /// <para>
    /// <b>Framework mapping.</b> NES is a population-level update, not a pairwise crossover, so the
    /// root is a dedicated <see cref="NaturalEvolutionStrategyMetaHeuristic"/>: on the first
    /// crossover call of a generation it computes the new distribution from the current
    /// generation (the samples drawn one generation earlier), caches it in the evolution-context
    /// store, then emits one fresh sample per reference individual. The default reinsertion is
    /// <see cref="PureReinsertion"/> (the offspring replace the parents), which is what NES
    /// assumes: the gradient is estimated from samples of the current distribution only. The
    /// selection and mutation operators passed to the GA do not enter the update.
    /// </para>
    /// <para>
    /// <b>Initialisation.</b> The first distribution is fitted to the initial population: the mean
    /// is the population centroid, the scale is the root mean per-coordinate variance, and the
    /// shape is the identity (or <see cref="InitialShape"/>, xNES only). The initial covariance is
    /// therefore isotropic for both models, so xNES and SNES start from the same distribution.
    /// </para>
    /// <para>
    /// <b>Learning rates.</b> The defaults are the paper's: <c>η_μ = 1</c>;
    /// xNES <c>η_σ = η_B = 3(3 + ln d) / (5 d √d)</c>; SNES <c>η_s = (3 + ln d) / (5 √d)</c>.
    /// The utilities are the paper's rank-based fitness shaping
    /// (<see cref="NaturalEvolutionStrategyMath.RankUtilities"/>).
    /// </para>
    /// </remarks>
    public class NaturalEvolutionStrategy : GeometricMetaHeuristicBase
    {
        /// <summary>The covariance model: <see cref="NesCovarianceModel.Full"/> (xNES, default) or <see cref="NesCovarianceModel.Separable"/> (SNES).</summary>
        public NesCovarianceModel CovarianceModel { get; set; } = NesCovarianceModel.Full;

        /// <summary>The mean learning rate <c>η_μ</c> (paper default 1).</summary>
        public double MeanLearningRate { get; set; } = 1.0;

        /// <summary>The scale learning rate (<c>η_σ</c> for xNES, <c>η_s</c> for SNES); null uses the paper default for the problem dimension.</summary>
        public double? ScaleLearningRate { get; set; }

        /// <summary>The xNES shape learning rate <c>η_B</c>; null uses the paper default. Ignored by SNES.</summary>
        public double? ShapeLearningRate { get; set; }

        /// <summary>
        /// An optional initial shape matrix <c>B₀</c> for xNES (identity when null). An orthogonal
        /// <c>B₀</c> leaves the initial covariance isotropic and only changes the coordinates in
        /// which the samples are drawn. Ignored by SNES.
        /// </summary>
        public double[,] InitialShape { get; set; }

        /// <summary>
        /// NES is a comma strategy: the new samples replace the population so the next gradient is
        /// estimated from the current distribution only.
        /// </summary>
        public override IReinsertion GetDefaultReinsertion()
        {
            return new PureReinsertion();
        }

        /// <inheritdoc />
        protected override IContainerMetaHeuristic BuildMainHeuristic()
        {
            var root = new NaturalEvolutionStrategyMetaHeuristic
            {
                CovarianceModel = CovarianceModel,
                MeanLearningRate = MeanLearningRate,
                ScaleLearningRate = ScaleLearningRate,
                ShapeLearningRate = ShapeLearningRate,
                InitialShape = InitialShape,
                GeometricConverter = GeometricConverter,
            };

            return CovarianceModel == NesCovarianceModel.Full
                ? root.WithName("Exponential Natural Evolution Strategy", "Wierstra et al. (2014), xNES: full-covariance Gaussian search distribution updated along the natural gradient of rank-based utilities, in the distribution's own coordinates (rotation-equivariant).")
                : root.WithName("Separable Natural Evolution Strategy", "Wierstra et al. (2014), SNES: diagonal-covariance Gaussian search distribution updated along the natural gradient of rank-based utilities, one step size per coordinate (axis-aligned).");
        }
    }

    /// <summary>
    /// The root metaheuristic of a <see cref="NaturalEvolutionStrategy"/>: updates the search
    /// distribution once per generation and replaces the crossover by one distribution sample per
    /// reference individual. Selection, reinsertion and mutation are delegated to the
    /// sub-metaheuristic, as in any <see cref="ContainerMetaHeuristic"/>.
    /// </summary>
    [DisplayName("Natural Evolution Strategy")]
    public class NaturalEvolutionStrategyMetaHeuristic : ContainerMetaHeuristic
    {
        /// <summary>The evolution-context store key of the per-generation distribution.</summary>
        public const string DistributionKey = "NES-Distribution";

        public NaturalEvolutionStrategyMetaHeuristic()
            : base(new DefaultMetaHeuristic())
        {
            // One sample per reference, every time: the crossover probability is not a NES concept.
            ProbabilityConfig.Crossover.Strategy = ProbabilityStrategy.TestProbability | ProbabilityStrategy.OverwriteProbability;
        }

        /// <inheritdoc cref="NaturalEvolutionStrategy.CovarianceModel" />
        public NesCovarianceModel CovarianceModel { get; set; } = NesCovarianceModel.Full;

        /// <inheritdoc cref="NaturalEvolutionStrategy.MeanLearningRate" />
        public double MeanLearningRate { get; set; } = 1.0;

        /// <inheritdoc cref="NaturalEvolutionStrategy.ScaleLearningRate" />
        public double? ScaleLearningRate { get; set; }

        /// <inheritdoc cref="NaturalEvolutionStrategy.ShapeLearningRate" />
        public double? ShapeLearningRate { get; set; }

        /// <inheritdoc cref="NaturalEvolutionStrategy.InitialShape" />
        public double[,] InitialShape { get; set; }

        /// <summary>The gene/double converter; a plain numeric conversion is used when null.</summary>
        public IGeometricConverter GeometricConverter { get; set; }

        /// <summary>
        /// The search distribution of the current generation, computed on first access and cached in
        /// the evolution-context store: fitted to the initial population at the first generation,
        /// otherwise one natural-gradient step away from the previous generation's distribution,
        /// estimated from the current generation (the samples that distribution produced).
        /// </summary>
        public NesDistribution GetDistribution(IEvolutionContext ctx)
        {
            ArgumentNullException.ThrowIfNull(ctx);
            int generation = ctx.Population?.GenerationsNumber ?? 0;
            return ctx.GetOrAdd((DistributionKey, generation, EvolutionStage.Crossover, (IMetaHeuristic)this, 0), () =>
            {
                var previous = ctx.GetOrAdd((DistributionKey, generation - 1, EvolutionStage.Crossover, (IMetaHeuristic)this, 0), () => (NesDistribution)null);

                // Best first; a missing fitness ranks last. OrderByDescending is stable, so ties keep population order.
                var ranked = ctx.Population.CurrentGeneration.Chromosomes
                    .OrderByDescending(c => c.Fitness ?? double.NegativeInfinity)
                    .Select(ToPoint)
                    .ToList();

                if (previous == null)
                {
                    return NesDistribution.Fit(CovarianceModel, ranked, InitialShape);
                }

                int d = previous.Dimension;
                double scaleRate = ScaleLearningRate ?? NaturalEvolutionStrategyMath.DefaultScaleLearningRate(CovarianceModel, d);
                double shapeRate = ShapeLearningRate ?? NaturalEvolutionStrategyMath.DefaultShapeLearningRate(d);
                return previous.Update(ranked, MeanLearningRate, scaleRate, shapeRate);
            });
        }

        protected override IList<IChromosome> DoMatchParentsAndCross(IEvolutionContext ctx, ICrossover crossover, float crossoverProbability, IList<IChromosome> parents)
        {
            var distribution = GetDistribution(ctx);
            var toReturn = new List<IChromosome>(crossover.ParentsNumber);
            var rnd = RandomizationProvider.Current;

            // Same skipping window as MatchMetaHeuristic: one sample per reference parent, so the
            // offspring count equals the population size under the linear operators strategy.
            for (int matchIndex = 0; matchIndex < crossover.ParentsNumber; matchIndex++)
            {
                var referenceIndex = ctx.LocalIndex + matchIndex;
                if (referenceIndex < parents.Count)
                {
                    // The reference only supplies the chromosome type; every gene is overwritten.
                    var child = parents[referenceIndex].CreateNew();
                    var sample = distribution.Sample(rnd);
                    for (int geneIndex = 0; geneIndex < sample.Length; geneIndex++)
                    {
                        object geneValue = GeometricConverter == null ? sample[geneIndex] : GeometricConverter.DoubleToGene(geneIndex, sample[geneIndex]);
                        child.ReplaceGene(geneIndex, new Gene(geneValue));
                    }
                    toReturn.Add(child);
                }
            }

            return toReturn;
        }

        private double[] ToPoint(IChromosome chromosome)
        {
            var genes = chromosome.GetGenes();
            var point = new double[genes.Length];
            for (int i = 0; i < genes.Length; i++)
            {
                point[i] = GeometricConverter == null ? Convert.ToDouble(genes[i].Value) : GeometricConverter.GeneToDouble(i, genes[i].Value);
            }
            return point;
        }
    }

    /// <summary>
    /// A NES search distribution. xNES (<see cref="NesCovarianceModel.Full"/>) samples
    /// <c>x = μ + σ B z</c>; SNES (<see cref="NesCovarianceModel.Separable"/>) samples
    /// <c>x = μ + s ⊙ z</c>; in both cases <c>z ~ N(0, I)</c>. Instances are immutable:
    /// <see cref="Update"/> returns the next distribution.
    /// </summary>
    public sealed class NesDistribution
    {
        private NesDistribution(NesCovarianceModel model, double[] mean, double sigma, double[,] shape, double[,] inverseShape, double[] scales)
        {
            Model = model;
            Mean = mean;
            Sigma = sigma;
            Shape = shape;
            InverseShape = inverseShape;
            Scales = scales;
        }

        /// <summary>The covariance model.</summary>
        public NesCovarianceModel Model { get; }

        /// <summary>The mean <c>μ</c>.</summary>
        public double[] Mean { get; }

        /// <summary>The global scale <c>σ</c> (xNES; NaN for SNES).</summary>
        public double Sigma { get; }

        /// <summary>The shape matrix <c>B</c> (xNES; null for SNES).</summary>
        public double[,] Shape { get; }

        /// <summary>The inverse shape matrix <c>B⁻¹</c>, maintained multiplicatively (xNES; null for SNES).</summary>
        public double[,] InverseShape { get; }

        /// <summary>The per-coordinate scales <c>s</c> (SNES; null for xNES).</summary>
        public double[] Scales { get; }

        /// <summary>The problem dimension.</summary>
        public int Dimension => Mean.Length;

        /// <summary>
        /// Fits the first distribution to a set of points: centroid mean, isotropic scale equal to the
        /// root mean per-coordinate (population) variance, identity or <paramref name="initialShape"/>
        /// shape.
        /// </summary>
        public static NesDistribution Fit(NesCovarianceModel model, IList<double[]> points, double[,] initialShape = null)
        {
            ArgumentNullException.ThrowIfNull(points);
            if (points.Count == 0)
            {
                throw new ArgumentException("at least one point is required", nameof(points));
            }

            int d = points[0].Length;
            var mean = new double[d];
            foreach (var p in points)
            {
                for (int i = 0; i < d; i++) mean[i] += p[i];
            }
            for (int i = 0; i < d; i++) mean[i] /= points.Count;

            double variance = 0.0;
            foreach (var p in points)
            {
                for (int i = 0; i < d; i++) variance += (p[i] - mean[i]) * (p[i] - mean[i]);
            }
            double sigma = Math.Sqrt(variance / (points.Count * (double)d));

            if (model == NesCovarianceModel.Separable)
            {
                return new NesDistribution(model, mean, double.NaN, null, null, Enumerable.Repeat(sigma, d).ToArray());
            }

            var shape = initialShape == null ? NaturalEvolutionStrategyMath.Identity(d) : (double[,])initialShape.Clone();
            if (shape.GetLength(0) != d || shape.GetLength(1) != d)
            {
                throw new ArgumentException($"initial shape must be {d}x{d}", nameof(initialShape));
            }
            return new NesDistribution(model, mean, sigma, shape, NaturalEvolutionStrategyMath.Invert(shape), null);
        }

        /// <summary>Draws one sample from the distribution.</summary>
        public double[] Sample(IRandomization rnd)
        {
            ArgumentNullException.ThrowIfNull(rnd);
            int d = Dimension;
            var z = new double[d];
            for (int i = 0; i < d; i++) z[i] = NaturalEvolutionStrategyMath.StandardNormal(rnd);
            return FromNatural(z);
        }

        /// <summary>Maps natural coordinates <c>z</c> to the search space: <c>μ + σ B z</c> or <c>μ + s ⊙ z</c>.</summary>
        public double[] FromNatural(double[] z)
        {
            int d = Dimension;
            var x = new double[d];
            if (Model == NesCovarianceModel.Separable)
            {
                for (int i = 0; i < d; i++) x[i] = Mean[i] + Scales[i] * z[i];
                return x;
            }

            var bz = NaturalEvolutionStrategyMath.Multiply(Shape, z);
            for (int i = 0; i < d; i++) x[i] = Mean[i] + Sigma * bz[i];
            return x;
        }

        /// <summary>Maps a search-space point to natural coordinates: <c>B⁻¹(x − μ)/σ</c> or <c>(x − μ)/s</c>.</summary>
        public double[] ToNatural(double[] x)
        {
            int d = Dimension;
            var centered = new double[d];
            for (int i = 0; i < d; i++) centered[i] = x[i] - Mean[i];
            if (Model == NesCovarianceModel.Separable)
            {
                for (int i = 0; i < d; i++) centered[i] /= Scales[i];
                return centered;
            }

            var z = NaturalEvolutionStrategyMath.Multiply(InverseShape, centered);
            for (int i = 0; i < d; i++) z[i] /= Sigma;
            return z;
        }

        /// <summary>
        /// One natural-gradient step, estimated from samples of this distribution ranked best first.
        /// xNES: <c>μ += η_μ σ B ∇δ</c>, <c>σ *= exp(η_σ/2 · ∇σ)</c>, <c>B *= expm(η_B/2 · ∇B)</c>
        /// with <c>∇δ = Σ u_k z_k</c>, <c>∇M = Σ u_k (z_k z_kᵀ − I)</c>, <c>∇σ = tr(∇M)/d</c>,
        /// <c>∇B = ∇M − ∇σ I</c>. SNES: <c>μ += η_μ s ⊙ ∇μ</c>, <c>s *= exp(η_s/2 · ∇s)</c>
        /// with <c>∇μ = Σ u_k z_k</c>, <c>∇s = Σ u_k (z_k² − 1)</c>.
        /// </summary>
        /// <param name="rankedSamples">Samples of this distribution, best first.</param>
        /// <param name="meanRate">The mean learning rate <c>η_μ</c>.</param>
        /// <param name="scaleRate"><c>η_σ</c> (xNES) or <c>η_s</c> (SNES).</param>
        /// <param name="shapeRate"><c>η_B</c> (xNES only).</param>
        public NesDistribution Update(IList<double[]> rankedSamples, double meanRate, double scaleRate, double shapeRate)
        {
            ArgumentNullException.ThrowIfNull(rankedSamples);
            int lambda = rankedSamples.Count;
            int d = Dimension;
            var utilities = NaturalEvolutionStrategyMath.RankUtilities(lambda);
            var zs = rankedSamples.Select(ToNatural).ToList();

            var gradMean = new double[d];
            for (int k = 0; k < lambda; k++)
            {
                for (int i = 0; i < d; i++) gradMean[i] += utilities[k] * zs[k][i];
            }

            if (Model == NesCovarianceModel.Separable)
            {
                var mean = new double[d];
                var scales = new double[d];
                for (int i = 0; i < d; i++)
                {
                    double gradScale = 0.0;
                    for (int k = 0; k < lambda; k++) gradScale += utilities[k] * (zs[k][i] * zs[k][i] - 1.0);
                    mean[i] = Mean[i] + meanRate * Scales[i] * gradMean[i];
                    scales[i] = Scales[i] * Math.Exp(0.5 * scaleRate * gradScale);
                }
                return new NesDistribution(Model, mean, double.NaN, null, null, scales);
            }

            var gradM = new double[d, d];
            double utilitySum = utilities.Sum();
            for (int k = 0; k < lambda; k++)
            {
                for (int i = 0; i < d; i++)
                {
                    for (int j = 0; j < d; j++) gradM[i, j] += utilities[k] * zs[k][i] * zs[k][j];
                }
            }
            for (int i = 0; i < d; i++) gradM[i, i] -= utilitySum;

            double trace = 0.0;
            for (int i = 0; i < d; i++) trace += gradM[i, i];
            double gradSigma = trace / d;

            var halfStep = new double[d, d];
            var negHalfStep = new double[d, d];
            for (int i = 0; i < d; i++)
            {
                for (int j = 0; j < d; j++)
                {
                    double gradB = gradM[i, j] - (i == j ? gradSigma : 0.0);
                    halfStep[i, j] = 0.5 * shapeRate * gradB;
                    negHalfStep[i, j] = -halfStep[i, j];
                }
            }

            var step = NaturalEvolutionStrategyMath.Multiply(Shape, gradMean);
            var newMean = new double[d];
            for (int i = 0; i < d; i++) newMean[i] = Mean[i] + meanRate * Sigma * step[i];
            double newSigma = Sigma * Math.Exp(0.5 * scaleRate * gradSigma);
            var newShape = NaturalEvolutionStrategyMath.Multiply(Shape, NaturalEvolutionStrategyMath.MatrixExponential(halfStep));
            var newInverseShape = NaturalEvolutionStrategyMath.Multiply(NaturalEvolutionStrategyMath.MatrixExponential(negHalfStep), InverseShape);
            return new NesDistribution(Model, newMean, newSigma, newShape, newInverseShape, null);
        }
    }

    /// <summary>
    /// Numerical helpers of <see cref="NaturalEvolutionStrategy"/>: rank utilities, paper learning
    /// rates, Box-Muller normal draws, and the small dense linear algebra the xNES update needs
    /// (matrix products, Gauss-Jordan inverse, matrix exponential by scaling and squaring).
    /// </summary>
    public static class NaturalEvolutionStrategyMath
    {
        /// <summary>
        /// The NES rank-based utilities for <paramref name="lambda"/> samples ranked best first:
        /// <c>u_k = max(0, ln(λ/2 + 1) − ln k) / Σ_j max(0, ln(λ/2 + 1) − ln j) − 1/λ</c>.
        /// They sum to zero, are non-increasing in rank, and only the better half weighs positively.
        /// </summary>
        public static double[] RankUtilities(int lambda)
        {
            if (lambda <= 0) throw new ArgumentOutOfRangeException(nameof(lambda));
            var raw = new double[lambda];
            double anchor = Math.Log(lambda / 2.0 + 1.0);
            double total = 0.0;
            for (int k = 0; k < lambda; k++)
            {
                raw[k] = Math.Max(0.0, anchor - Math.Log(k + 1));
                total += raw[k];
            }
            var utilities = new double[lambda];
            for (int k = 0; k < lambda; k++) utilities[k] = raw[k] / total - 1.0 / lambda;
            return utilities;
        }

        /// <summary>The paper's scale learning rate: xNES <c>3(3 + ln d)/(5 d √d)</c>, SNES <c>(3 + ln d)/(5 √d)</c>.</summary>
        public static double DefaultScaleLearningRate(NesCovarianceModel model, int dimension)
        {
            if (dimension <= 0) throw new ArgumentOutOfRangeException(nameof(dimension));
            double d = dimension;
            return model == NesCovarianceModel.Separable
                ? (3.0 + Math.Log(d)) / (5.0 * Math.Sqrt(d))
                : DefaultShapeLearningRate(dimension);
        }

        /// <summary>The paper's xNES shape learning rate <c>η_B = 3(3 + ln d)/(5 d √d)</c> (equal to <c>η_σ</c>).</summary>
        public static double DefaultShapeLearningRate(int dimension)
        {
            if (dimension <= 0) throw new ArgumentOutOfRangeException(nameof(dimension));
            double d = dimension;
            return 3.0 * (3.0 + Math.Log(d)) / (5.0 * d * Math.Sqrt(d));
        }

        /// <summary>
        /// A standard normal draw by Box-Muller (<see cref="IRandomization"/> exposes uniform draws
        /// only). <c>1 − GetDouble()</c> keeps <c>u1</c> in (0, 1] so the logarithm never sees 0.
        /// </summary>
        public static double StandardNormal(IRandomization rnd)
        {
            double u1 = 1.0 - rnd.GetDouble();
            double u2 = rnd.GetDouble();
            return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
        }

        /// <summary>The <paramref name="n"/>x<paramref name="n"/> identity matrix.</summary>
        public static double[,] Identity(int n)
        {
            var m = new double[n, n];
            for (int i = 0; i < n; i++) m[i, i] = 1.0;
            return m;
        }

        /// <summary>The matrix-vector product <c>A v</c>.</summary>
        public static double[] Multiply(double[,] a, double[] v)
        {
            int rows = a.GetLength(0);
            int cols = a.GetLength(1);
            var result = new double[rows];
            for (int i = 0; i < rows; i++)
            {
                double s = 0.0;
                for (int j = 0; j < cols; j++) s += a[i, j] * v[j];
                result[i] = s;
            }
            return result;
        }

        /// <summary>The matrix product <c>A B</c>.</summary>
        public static double[,] Multiply(double[,] a, double[,] b)
        {
            int rows = a.GetLength(0);
            int inner = a.GetLength(1);
            int cols = b.GetLength(1);
            if (b.GetLength(0) != inner) throw new ArgumentException("inner dimensions differ", nameof(b));
            var result = new double[rows, cols];
            for (int i = 0; i < rows; i++)
            {
                for (int k = 0; k < inner; k++)
                {
                    double aik = a[i, k];
                    for (int j = 0; j < cols; j++) result[i, j] += aik * b[k, j];
                }
            }
            return result;
        }

        /// <summary>The inverse of a square matrix, by Gauss-Jordan elimination with partial pivoting.</summary>
        public static double[,] Invert(double[,] a)
        {
            int n = a.GetLength(0);
            if (a.GetLength(1) != n) throw new ArgumentException("matrix must be square", nameof(a));
            var work = (double[,])a.Clone();
            var inverse = Identity(n);
            for (int col = 0; col < n; col++)
            {
                int pivot = col;
                for (int row = col + 1; row < n; row++)
                {
                    if (Math.Abs(work[row, col]) > Math.Abs(work[pivot, col])) pivot = row;
                }
                if (Math.Abs(work[pivot, col]) < 1e-300) throw new ArgumentException("matrix is singular", nameof(a));
                if (pivot != col)
                {
                    for (int j = 0; j < n; j++)
                    {
                        (work[col, j], work[pivot, j]) = (work[pivot, j], work[col, j]);
                        (inverse[col, j], inverse[pivot, j]) = (inverse[pivot, j], inverse[col, j]);
                    }
                }
                double scale = work[col, col];
                for (int j = 0; j < n; j++)
                {
                    work[col, j] /= scale;
                    inverse[col, j] /= scale;
                }
                for (int row = 0; row < n; row++)
                {
                    if (row == col) continue;
                    double factor = work[row, col];
                    if (factor == 0.0) continue;
                    for (int j = 0; j < n; j++)
                    {
                        work[row, j] -= factor * work[col, j];
                        inverse[row, j] -= factor * inverse[col, j];
                    }
                }
            }
            return inverse;
        }

        /// <summary>
        /// The matrix exponential <c>expm(A)</c> by scaling and squaring: <c>A</c> is scaled by
        /// <c>2^-s</c> until its infinity norm is at most 1/2, the exponential of the scaled matrix is
        /// summed as a truncated Taylor series (to machine precision), then squared <c>s</c> times.
        /// </summary>
        public static double[,] MatrixExponential(double[,] a)
        {
            int n = a.GetLength(0);
            if (a.GetLength(1) != n) throw new ArgumentException("matrix must be square", nameof(a));

            double norm = 0.0;
            for (int i = 0; i < n; i++)
            {
                double rowSum = 0.0;
                for (int j = 0; j < n; j++) rowSum += Math.Abs(a[i, j]);
                norm = Math.Max(norm, rowSum);
            }

            int squarings = 0;
            while (norm > 0.5)
            {
                norm /= 2.0;
                squarings++;
            }
            double factor = Math.Pow(2.0, -squarings);

            var scaled = new double[n, n];
            for (int i = 0; i < n; i++)
            {
                for (int j = 0; j < n; j++) scaled[i, j] = a[i, j] * factor;
            }

            // With ||scaled|| <= 1/2 the Taylor remainder after 20 terms is below 2^-21 / 21!, far under machine epsilon.
            var result = Identity(n);
            var term = Identity(n);
            for (int k = 1; k <= 20; k++)
            {
                term = Multiply(term, scaled);
                for (int i = 0; i < n; i++)
                {
                    for (int j = 0; j < n; j++)
                    {
                        term[i, j] /= k;
                        result[i, j] += term[i, j];
                    }
                }
            }

            for (int s = 0; s < squarings; s++) result = Multiply(result, result);
            return result;
        }
    }
}
