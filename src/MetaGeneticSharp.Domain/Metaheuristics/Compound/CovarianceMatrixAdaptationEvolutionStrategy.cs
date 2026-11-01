#nullable disable
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using GeneticSharp;
using CmaMath = MetaGeneticSharp.CovarianceMatrixAdaptationEvolutionStrategyMath;

namespace MetaGeneticSharp
{
    /// <summary>
    ///   Covariance Matrix Adaptation Evolution Strategy (Hansen, "The CMA Evolution Strategy: A
    ///   Tutorial", 2024), expressed as a geometric compound metaheuristic alongside
    ///   <see cref="NaturalEvolutionStrategy"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Why this compound exists, and what it adds to xNES.</b> xNES already carries the
    /// rank-μ part of CMA-ES: it follows the natural gradient of rank-based utilities, which is the
    /// same weighted recombination of the better half. What it does not carry is the two devices
    /// that make CMA-ES work in practice on narrow valleys:
    /// </para>
    /// <list type="bullet">
    ///   <item><description>
    ///   the <b>rank-one update</b>: an evolution path <c>p_c</c> accumulates successive mean steps,
    ///   so the covariance learns the <i>correlation between generations</i> rather than only the
    ///   spread of the current one;
    ///   </description></item>
    ///   <item><description>
    ///   the <b>cumulative step-size adaptation</b> (CSA): a second path <c>p_σ</c> tracks the length
    ///   of the mean's trajectory against its expected length under random selection, and modulates
    ///   the step size from that comparison alone.
    ///   </description></item>
    /// </list>
    /// <para>
    /// Both paths are what the paper calls the "evolution path" machinery; they are why CMA-ES keeps
    /// its step size when the population happens to drift, and shrinks it when the drift stalls. xNES
    /// instead adapts the scale and the shape by the natural gradient of the same utilities, without
    /// memory across generations. The pair therefore forms a controlled experiment on the value of
    /// that memory, in the same way xNES/SNES form one on the covariance structure.
    /// </para>
    /// <para>
    /// <b>Framework mapping.</b> As in <see cref="NaturalEvolutionStrategy"/> the update is a
    /// population-level operation, not a pairwise crossover: the root
    /// <see cref="CovarianceMatrixAdaptationEvolutionStrategyMetaHeuristic"/> updates the distribution
    /// once per generation from the current population (the samples the previous distribution
    /// produced), caches it in the evolution-context store, then emits one fresh sample per reference
    /// individual. The default reinsertion is <see cref="PureReinsertion"/>: CMA-ES estimates its
    /// update from samples of the current distribution only.
    /// </para>
    /// <para>
    /// <b>Restarts.</b> The issue that motivates this class also names IPOP (increasing population
    /// on restart). The restart half is implemented — the strategy resets its covariance, its paths
    /// and its step size, and restarts at the best point seen so far — but the <i>increasing
    /// population</i> half is not reachable from inside a compound here: the population size belongs
    /// to the <see cref="MetaGeneticAlgorithm"/> and cannot change mid-run. A caller that wants IPOP
    /// proper can read <see cref="CovarianceMatrixAdaptationEvolutionStrategyMath.DefaultPopulationSize"/>
    /// and build the next run with a multiplied population. This ceiling is declared rather than
    /// papered over: the tests measure restart-on-stall, not IPOP.
    /// </para>
    /// </remarks>
    public class CovarianceMatrixAdaptationEvolutionStrategy : GeometricMetaHeuristicBase
    {
        /// <summary>
        /// An explicit initial covariance matrix <c>C₀</c> (identity when null). It must be square
        /// and symmetric positive definite; the fitted step size still comes from the population.
        /// </summary>
        public double[,] InitialCovariance { get; set; }

        /// <summary>
        /// An explicit initial step size <c>σ₀</c>; <see cref="double.NaN"/> (the default) fits it to
        /// the initial population, as <see cref="NaturalEvolutionStrategy"/> does.
        /// </summary>
        public double InitialStepSize { get; set; } = double.NaN;

        /// <summary>
        /// The number of generations without improvement of the best-ever point after which the
        /// strategy restarts (default 0 = never restart). See the class remarks on the IPOP ceiling.
        /// </summary>
        public int StallGenerations { get; set; }

        /// <summary>The maximum number of restarts (default 1 when <see cref="StallGenerations"/> is positive).</summary>
        public int MaxRestarts { get; set; } = 1;

        /// <summary>
        /// The step size a restart starts from. <see cref="double.NaN"/> (the default) reuses the
        /// <i>initial</i> step size of the run, which is the population-fitted one.
        /// </summary>
        public double RestartStepSize { get; set; } = double.NaN;

        /// <summary>
        /// CMA-ES is a comma strategy: the new samples replace the population so the next update is
        /// estimated from the current distribution only.
        /// </summary>
        public override IReinsertion GetDefaultReinsertion()
        {
            return new PureReinsertion();
        }

        /// <inheritdoc />
        protected override IContainerMetaHeuristic BuildMainHeuristic()
        {
            var root = new CovarianceMatrixAdaptationEvolutionStrategyMetaHeuristic
            {
                InitialCovariance = InitialCovariance,
                InitialStepSize = InitialStepSize,
                StallGenerations = StallGenerations,
                MaxRestarts = MaxRestarts,
                RestartStepSize = RestartStepSize,
                GeometricConverter = GeometricConverter,
            };

            return root.WithName(
                "Covariance Matrix Adaptation Evolution Strategy",
                "Hansen (2024), CMA-ES: full-covariance Gaussian search distribution with rank-one and rank-μ covariance updates, cumulative step-size adaptation and restart-on-stall, over the weighted recombination of the better half.");
        }
    }

    /// <summary>
    /// The root metaheuristic of a <see cref="CovarianceMatrixAdaptationEvolutionStrategy"/>: updates
    /// the search distribution once per generation and replaces the crossover by one distribution
    /// sample per reference individual.
    /// </summary>
    [DisplayName("Covariance Matrix Adaptation Evolution Strategy")]
    public class CovarianceMatrixAdaptationEvolutionStrategyMetaHeuristic : ContainerMetaHeuristic
    {
        /// <summary>The evolution-context store key of the per-generation distribution.</summary>
        public const string DistributionKey = "CMA-ES-Distribution";

        public CovarianceMatrixAdaptationEvolutionStrategyMetaHeuristic()
            : base(new DefaultMetaHeuristic())
        {
            // One sample per reference, every time: the sampling probability is not a CMA-ES concept.
            ProbabilityConfig.Crossover.Strategy = ProbabilityStrategy.TestProbability | ProbabilityStrategy.OverwriteProbability;
        }

        /// <inheritdoc cref="CovarianceMatrixAdaptationEvolutionStrategy.InitialCovariance" />
        public double[,] InitialCovariance { get; set; }

        /// <inheritdoc cref="CovarianceMatrixAdaptationEvolutionStrategy.InitialStepSize" />
        public double InitialStepSize { get; set; } = double.NaN;

        /// <inheritdoc cref="CovarianceMatrixAdaptationEvolutionStrategy.StallGenerations" />
        public int StallGenerations { get; set; }

        /// <inheritdoc cref="CovarianceMatrixAdaptationEvolutionStrategy.MaxRestarts" />
        public int MaxRestarts { get; set; } = 1;

        /// <inheritdoc cref="CovarianceMatrixAdaptationEvolutionStrategy.RestartStepSize" />
        public double RestartStepSize { get; set; } = double.NaN;

        /// <summary>The gene/double converter; a plain numeric conversion is used when null.</summary>
        public IGeometricConverter GeometricConverter { get; set; }

        /// <summary>
        /// The search distribution of the current generation, computed on first access and cached in
        /// the evolution-context store: fitted to the initial population at the first generation,
        /// otherwise one CMA-ES update away from the previous generation's distribution, estimated
        /// from the current generation (the samples that distribution produced).
        /// </summary>
        public CmaEsDistribution GetDistribution(IEvolutionContext ctx)
        {
            ArgumentNullException.ThrowIfNull(ctx);
            int generation = ctx.Population?.GenerationsNumber ?? 0;
            return ctx.GetOrAdd((DistributionKey, generation, EvolutionStage.Crossover, (IMetaHeuristic)this, 0), () =>
            {
                var previous = ctx.GetOrAdd((DistributionKey, generation - 1, EvolutionStage.Crossover, (IMetaHeuristic)this, 0), () => (CmaEsDistribution)null);

                // Best first; a missing fitness ranks last. OrderByDescending is stable, so ties keep population order.
                var ordered = ctx.Population.CurrentGeneration.Chromosomes
                    .OrderByDescending(c => c.Fitness ?? double.NegativeInfinity)
                    .ToList();
                var ranked = ordered.Select(ToPoint).ToList();
                var rankedFitness = ordered.Select(c => c.Fitness ?? double.NegativeInfinity).ToList();

                if (previous == null)
                {
                    var fitted = CmaEsDistribution.Fit(ranked, InitialCovariance, InitialStepSize);
                    _restartStepSize = fitted.Sigma;
                    return fitted;
                }

                var next = previous.UpdateWithFitness(ranked, rankedFitness);

                // Restart-on-stall. The population is not increased: see the ceiling documented on the
                // compound (IPOP's growth needs a population this operator does not own).
                if (StallGenerations > 0
                    && next.GenerationsWithoutImprovement >= StallGenerations
                    && next.Restarts < MaxRestarts)
                {
                    return next.Restart(next.BestPoint, double.IsNaN(RestartStepSize) ? _restartStepSize : RestartStepSize);
                }

                return next;
            });
        }

        /// <summary>
        /// The step size fitted to the initial population, remembered so a restart can come back to it
        /// when <see cref="RestartStepSize"/> is left at its NaN default.
        /// </summary>
        private double _restartStepSize = double.NaN;

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
    /// A CMA-ES search distribution: samples <c>x = m + σ B z</c> with <c>z ~ N(0, I)</c> and
    /// <c>C = B Bᵀ</c>. Instances are immutable: <see cref="Update"/> returns the next distribution.
    /// </summary>
    /// <remarks>
    /// The covariance is carried as the full symmetric matrix <c>C</c>; its eigendecomposition is
    /// recomputed on demand (<see cref="Symmetrised"/>) because both the sampler (<c>B = C^{1/2}</c>)
    /// and the CSA path (<c>C^{-1/2}</c>) need it. Dimensions in this library are small (the
    /// benchmarks top out at a handful of coordinates), so the Jacobi solver is the right tool: no
    /// external dependency, and its accuracy is independent of the conditioning in practice.
    /// </remarks>
    public sealed class CmaEsDistribution
    {
        private readonly double[] _covarianceDiagonalCache;
        private readonly double[] _eigenvaluesCache;
        private readonly double[,] _eigenvectorsCache;

        private CmaEsDistribution(
            double[] mean,
            double sigma,
            double[,] covariance,
            double[] pathSigma,
            double[] pathCovariance,
            int generation,
            double[] bestPoint,
            double bestFitness,
            int generationsWithoutImprovement,
            int restarts)
        {
            Mean = mean;
            Sigma = sigma;
            Covariance = covariance;
            PathSigma = pathSigma;
            PathCovariance = pathCovariance;
            Generation = generation;
            BestPoint = bestPoint;
            BestFitness = bestFitness;
            GenerationsWithoutImprovement = generationsWithoutImprovement;
            Restarts = restarts;

            CovarianceMatrixAdaptationEvolutionStrategyMath.SymmetricEigen(covariance, out var eigenvalues, out var eigenvectors);            _eigenvaluesCache = eigenvalues;
            _eigenvectorsCache = eigenvectors;
            _covarianceDiagonalCache = new double[eigenvalues.Length];
            for (int i = 0; i < eigenvalues.Length; i++) _covarianceDiagonalCache[i] = Math.Max(eigenvalues[i], 0.0);
        }

        /// <summary>The mean <c>m</c> of the search distribution.</summary>
        public double[] Mean { get; }

        /// <summary>The step size <c>σ</c>.</summary>
        public double Sigma { get; }

        /// <summary>The covariance matrix <c>C</c> (symmetric).</summary>
        public double[,] Covariance { get; }

        /// <summary>The cumulative step-size path <c>p_σ</c>.</summary>
        public double[] PathSigma { get; }

        /// <summary>The rank-one covariance path <c>p_c</c>.</summary>
        public double[] PathCovariance { get; }

        /// <summary>The number of updates applied so far (the CSA correction uses it).</summary>
        public int Generation { get; }

        /// <summary>The best search-space point seen by this strategy since its last restart.</summary>
        public double[] BestPoint { get; }

        /// <summary>The fitness of <see cref="BestPoint"/> (best first ranking assumes higher is better).</summary>
        public double BestFitness { get; }

        /// <summary>The number of generations since <see cref="BestFitness"/> last improved.</summary>
        public int GenerationsWithoutImprovement { get; }

        /// <summary>The number of restarts this strategy's lineage has performed.</summary>
        public int Restarts { get; }

        /// <summary>The problem dimension.</summary>
        public int Dimension => Mean.Length;

        /// <summary>The eigenvalues of <see cref="Covariance"/> (non-negative, unsorted).</summary>
        public double[] Eigenvalues => _eigenvaluesCache;

        /// <summary>The eigenvectors of <see cref="Covariance"/> (columns), matching <see cref="Eigenvalues"/>.</summary>
        public double[,] Eigenvectors => _eigenvectorsCache;

        /// <summary>
        /// The per-coordinate standard deviations <c>σ √diag(C)</c>: the honest scale of the search
        /// in each axis, and the quantity a convergence test reads.
        /// </summary>
        public double[] CoordinateStepSizes()
        {
            int d = Dimension;
            var steps = new double[d];
            for (int i = 0; i < d; i++) steps[i] = Sigma * Math.Sqrt(_covarianceDiagonalCache[i]);
            return steps;
        }

        /// <summary>
        /// Fits the first distribution to a set of points ranked best first: centroid mean, isotropic
        /// covariance (or <paramref name="initialCovariance"/>), step size
        /// <paramref name="initialStepSize"/> or the root mean per-coordinate population variance —
        /// the same initialisation <see cref="NesDistribution"/> uses, so the two strategies start
        /// from the same distribution.
        /// </summary>
        public static CmaEsDistribution Fit(IList<double[]> rankedPoints, double[,] initialCovariance = null, double initialStepSize = double.NaN)
        {
            ArgumentNullException.ThrowIfNull(rankedPoints);
            if (rankedPoints.Count == 0)
            {
                throw new ArgumentException("at least one point is required", nameof(rankedPoints));
            }

            int d = rankedPoints[0].Length;
            var mean = new double[d];
            foreach (var p in rankedPoints)
            {
                for (int i = 0; i < d; i++) mean[i] += p[i];
            }
            for (int i = 0; i < d; i++) mean[i] /= rankedPoints.Count;

            double variance = 0.0;
            foreach (var p in rankedPoints)
            {
                for (int i = 0; i < d; i++) variance += (p[i] - mean[i]) * (p[i] - mean[i]);
            }
            double sigma = double.IsNaN(initialStepSize)
                ? Math.Sqrt(variance / (rankedPoints.Count * (double)d))
                : initialStepSize;
            if (!(sigma > 0.0))
            {
                // A degenerate initial population (all identical points) leaves the scale undefined;
                // 1.0 is the neutral choice and keeps the run well-defined.
                sigma = 1.0;
            }

            var covariance = NaturalEvolutionStrategyMath.Identity(d);
            if (initialCovariance != null)
            {
                if (initialCovariance.GetLength(0) != d || initialCovariance.GetLength(1) != d)
                {
                    throw new ArgumentException($"initial covariance must be {d}x{d}", nameof(initialCovariance));
                }
                covariance = (double[,])initialCovariance.Clone();
            }

            var best = rankedPoints[0];
            return new CmaEsDistribution(
                mean,
                sigma,
                covariance,
                new double[d],
                new double[d],
                generation: 0,
                bestPoint: (double[])best.Clone(),
                // No fitness has been observed yet: the first update that carries fitnesses takes the record.
                bestFitness: double.NegativeInfinity,
                generationsWithoutImprovement: 0,
                restarts: 0);
        }

        /// <summary>
        /// Rebuilds a distribution from an explicit state: mean, step size, covariance and,
        /// optionally, the two evolution paths and the generation counter. This is the resume entry
        /// point (and the only way to pin an exact starting model, which a test needs).
        /// </summary>
        public static CmaEsDistribution Create(
            double[] mean,
            double sigma,
            double[,] covariance,
            double[] pathSigma = null,
            double[] pathCovariance = null,
            int generation = 0)
        {
            ArgumentNullException.ThrowIfNull(mean);
            ArgumentNullException.ThrowIfNull(covariance);
            int d = mean.Length;
            if (!(sigma > 0.0))
            {
                throw new ArgumentOutOfRangeException(nameof(sigma), "the step size must be positive");
            }
            if (covariance.GetLength(0) != d || covariance.GetLength(1) != d)
            {
                throw new ArgumentException($"covariance must be {d}x{d}", nameof(covariance));
            }
            return new CmaEsDistribution(
                (double[])mean.Clone(),
                sigma,
                (double[,])covariance.Clone(),
                pathSigma == null ? new double[d] : (double[])pathSigma.Clone(),
                pathCovariance == null ? new double[d] : (double[])pathCovariance.Clone(),
                generation,
                bestPoint: (double[])mean.Clone(),
                bestFitness: double.NegativeInfinity,
                generationsWithoutImprovement: 0,
                restarts: 0);
        }

        /// <summary>Draws one sample from the distribution.</summary>
        public double[] Sample(IRandomization rnd)        {
            ArgumentNullException.ThrowIfNull(rnd);
            int d = Dimension;
            var z = new double[d];
            for (int i = 0; i < d; i++) z[i] = NaturalEvolutionStrategyMath.StandardNormal(rnd);
            return FromNatural(z);
        }

        /// <summary>
        /// Maps natural coordinates <c>z</c> to the search space: <c>m + σ C^{1/2} z</c>, with
        /// <c>C^{1/2} = V √D Vᵀ</c> from the cached eigendecomposition.
        /// </summary>
        public double[] FromNatural(double[] z)
        {
            int d = Dimension;
            var x = new double[d];
            var bz = CmaMath.ScaleBySquareRoot(_eigenvaluesCache, _eigenvectorsCache, z);
            for (int i = 0; i < d; i++) x[i] = Mean[i] + Sigma * bz[i];
            return x;
        }

        /// <summary>
        /// One CMA-ES update from the current generation, ranked best first (Hansen 2024, equations
        /// 39-53). The population size is the number of samples actually ranked, so the strategy
        /// follows whatever population the genetic algorithm runs.
        /// </summary>
        /// <param name="rankedPoints">
        /// The search-space points of the current generation, best first. Their fitnesses are not
        /// passed: the ranking <i>is</i> the information CMA-ES uses (plus the best value, see
        /// <see cref="UpdateWithFitness"/>).
        /// </param>
        public CmaEsDistribution Update(IList<double[]> rankedPoints)
        {
            return UpdateWithFitness(rankedPoints, null);
        }

        /// <summary>
        /// The same update, carrying the fitnesses so the strategy can track its best-ever point and
        /// count the generations without improvement that drive a restart (higher is better).
        /// </summary>
        public CmaEsDistribution UpdateWithFitness(IList<double[]> rankedPoints, IList<double> rankedFitness)
        {
            ArgumentNullException.ThrowIfNull(rankedPoints);
            int lambda = rankedPoints.Count;
            int d = Dimension;
            if (lambda < 2)
            {
                throw new ArgumentException("CMA-ES needs at least two ranked samples", nameof(rankedPoints));
            }

            int mu = lambda / 2;
            var weights = CmaMath.RecombinationWeights(lambda);
            double muEffective = CmaMath.EffectivePopulationSize(lambda);
            var constants = CmaEsConstants.For(d, lambda);

            // y_i = (x_i - m) / σ, best first.
            var y = new double[lambda][];
            for (int k = 0; k < lambda; k++)
            {
                var point = rankedPoints[k];
                y[k] = new double[d];
                for (int i = 0; i < d; i++) y[k][i] = (point[i] - Mean[i]) / Sigma;
            }

            // y_w = Σ_{i<=μ} w_i y_i  and  m' = m + σ y_w.
            var yWeighted = new double[d];
            for (int k = 0; k < mu; k++)
            {
                for (int i = 0; i < d; i++) yWeighted[i] += weights[k] * y[k][i];
            }
            var newMean = new double[d];
            for (int i = 0; i < d; i++) newMean[i] = Mean[i] + Sigma * yWeighted[i];

            // p_σ = (1 - c_σ) p_σ + √(c_σ (2 - c_σ) μ_eff) C^{-1/2} y_w
            var inverseSqrtTimesY = CmaMath.ScaleByInverseSquareRoot(_eigenvaluesCache, _eigenvectorsCache, yWeighted);
            var newPathSigma = new double[d];
            double pathCoefficient = Math.Sqrt(constants.CSigma * (2.0 - constants.CSigma) * muEffective);
            for (int i = 0; i < d; i++)
            {
                newPathSigma[i] = (1.0 - constants.CSigma) * PathSigma[i] + pathCoefficient * inverseSqrtTimesY[i];
            }

            // h_σ: the path length compared to its expectation under random selection, with the
            // (1 - (1 - c_σ)^{2(g+1)}) correction for the length still owed to the initial path.
            double pathNorm = CmaMath.Norm(newPathSigma);
            double decay = Math.Pow(1.0 - constants.CSigma, 2.0 * (Generation + 1));
            double normalisedPath = decay < 1.0 ? pathNorm / Math.Sqrt(1.0 - decay) : pathNorm;
            bool heavyTail = normalisedPath < (1.4 + 2.0 / (d + 1.0)) * constants.ChiN;

            // p_c = (1 - c_c) p_c + h_σ √(c_c (2 - c_c) μ_eff) y_w
            var newPathCovariance = new double[d];
            double covariancePathCoefficient = heavyTail
                ? Math.Sqrt(constants.CCovariance * (2.0 - constants.CCovariance) * muEffective)
                : 0.0;
            for (int i = 0; i < d; i++)
            {
                newPathCovariance[i] = (1.0 - constants.CCovariance) * PathCovariance[i] + covariancePathCoefficient * yWeighted[i];
            }

            // C' = (1 - c_1 - c_μ) C + c_1 (p_c p_cᵀ + (1 - h_σ) c_c (2 - c_c) C) + c_μ Σ w_i y_i y_iᵀ
            double rankOneWeight = heavyTail ? 0.0 : constants.CCovariance * (2.0 - constants.CCovariance);
            double decayWeight = 1.0 - constants.COne - constants.CMu + constants.COne * rankOneWeight;
            var newCovariance = new double[d, d];
            for (int i = 0; i < d; i++)
            {
                for (int j = 0; j < d; j++)
                {
                    double rankMu = 0.0;
                    for (int k = 0; k < mu; k++) rankMu += weights[k] * y[k][i] * y[k][j];
                    newCovariance[i, j] = decayWeight * Covariance[i, j]
                        + constants.COne * newPathCovariance[i] * newPathCovariance[j]
                        + constants.CMu * rankMu;
                }
            }
            CmaMath.Symmetrise(newCovariance);

            // σ' = σ exp((c_σ / d_σ)(‖p_σ‖ / E‖N(0,I)‖ − 1))
            double newSigma = Sigma * Math.Exp((constants.CSigma / constants.DSigma) * (pathNorm / constants.ChiN - 1.0));

            // Best-ever tracking drives the restart.
            double bestFitness = BestFitness;
            double[] bestPoint = BestPoint;
            int stalled = GenerationsWithoutImprovement + 1;
            if (rankedFitness != null && rankedFitness.Count == lambda)
            {
                for (int k = 0; k < lambda; k++)
                {
                    if (rankedFitness[k] > bestFitness)
                    {
                        bestFitness = rankedFitness[k];
                        bestPoint = (double[])rankedPoints[k].Clone();
                        stalled = 0;
                    }
                }
            }

            return new CmaEsDistribution(
                newMean,
                newSigma,
                newCovariance,
                newPathSigma,
                newPathCovariance,
                Generation + 1,
                bestPoint,
                bestFitness,
                stalled,
                Restarts);
        }

        /// <summary>
        /// Restarts the strategy from <paramref name="bestPoint"/> with an isotropic covariance, reset
        /// paths and the given step size. See the ceiling documented on
        /// <see cref="CovarianceMatrixAdaptationEvolutionStrategy"/>: the population is not increased.
        /// </summary>
        public CmaEsDistribution Restart(double[] bestPoint, double stepSize)
        {
            ArgumentNullException.ThrowIfNull(bestPoint);
            int d = Dimension;
            if (bestPoint.Length != d)
            {
                throw new ArgumentException($"restart point must have {d} coordinates", nameof(bestPoint));
            }

            return new CmaEsDistribution(
                (double[])bestPoint.Clone(),
                stepSize > 0.0 ? stepSize : Sigma,
                NaturalEvolutionStrategyMath.Identity(d),
                new double[d],
                new double[d],
                generation: 0,
                bestPoint: (double[])bestPoint.Clone(),
                bestFitness: BestFitness,
                generationsWithoutImprovement: 0,
                restarts: Restarts + 1);
        }
    }

    /// <summary>
    /// The strategy constants of CMA-ES for a given dimension and population size (Hansen 2024,
    /// table 1 and equations 47-53), computed once per generation from the parameters actually in
    /// force: the population size is the one the run really uses, not a nominal default.
    /// </summary>
    public readonly struct CmaEsConstants
    {
        private CmaEsConstants(int dimension, int lambda, int mu, double muEffective, double cSigma, double dSigma, double cCovariance, double cOne, double cMu, double chiN)
        {
            Dimension = dimension;
            Lambda = lambda;
            Mu = mu;
            MuEffective = muEffective;
            CSigma = cSigma;
            DSigma = dSigma;
            CCovariance = cCovariance;
            COne = cOne;
            CMu = cMu;
            ChiN = chiN;
        }

        /// <summary>The problem dimension.</summary>
        public int Dimension { get; }

        /// <summary>The population size <c>λ</c>.</summary>
        public int Lambda { get; }

        /// <summary>The number of recombined samples <c>μ = ⌊λ/2⌋</c>.</summary>
        public int Mu { get; }

        /// <summary>The variance-effective selection mass <c>μ_eff</c>.</summary>
        public double MuEffective { get; }

        /// <summary>The cumulation rate for the step-size path, <c>c_σ = (μ_eff + 2)/(d + μ_eff + 5)</c>.</summary>
        public double CSigma { get; }

        /// <summary>The damping for the step-size path, <c>d_σ</c>.</summary>
        public double DSigma { get; }

        /// <summary>The cumulation rate for the covariance path, <c>c_c = (4 + μ_eff/d)/(d + 4 + 2 μ_eff/d)</c>.</summary>
        public double CCovariance { get; }

        /// <summary>The rank-one learning rate, <c>c_1 = 2/((d + 1.3)² + μ_eff)</c>.</summary>
        public double COne { get; }

        /// <summary>The rank-μ learning rate, <c>c_μ = min(1 − c_1, 2(μ_eff − 2 + 1/μ_eff)/((d + 2)² + μ_eff))</c>.</summary>
        public double CMu { get; }

        /// <summary>The expected norm of a standard normal vector, <c>E‖N(0,I)‖ ≈ √d (1 − 1/(4d) + 1/(21 d²))</c>.</summary>
        public double ChiN { get; }

        /// <summary>Computes the constants for <paramref name="dimension"/> coordinates and a population of <paramref name="lambda"/>.</summary>
        public static CmaEsConstants For(int dimension, int lambda)
        {
            if (dimension <= 0) throw new ArgumentOutOfRangeException(nameof(dimension));
            if (lambda < 2) throw new ArgumentOutOfRangeException(nameof(lambda));

            double d = dimension;
            int mu = lambda / 2;
            var weights = CovarianceMatrixAdaptationEvolutionStrategyMath.RecombinationWeights(lambda);
            double squares = 0.0;
            for (int i = 0; i < mu; i++) squares += weights[i] * weights[i];
            double muEffective = 1.0 / squares;

            double cSigma = (muEffective + 2.0) / (d + muEffective + 5.0);
            double dSigma = 1.0 + 2.0 * Math.Max(0.0, Math.Sqrt((muEffective - 1.0) / (d + 1.0)) - 1.0) + cSigma;
            double cCovariance = (4.0 + muEffective / d) / (d + 4.0 + 2.0 * muEffective / d);
            double cOne = 2.0 / ((d + 1.3) * (d + 1.3) + muEffective);
            double cMu = Math.Min(1.0 - cOne, 2.0 * (muEffective - 2.0 + 1.0 / muEffective) / ((d + 2.0) * (d + 2.0) + muEffective));
            double chiN = Math.Sqrt(d) * (1.0 - 1.0 / (4.0 * d) + 1.0 / (21.0 * d * d));

            return new CmaEsConstants(dimension, lambda, mu, muEffective, cSigma, dSigma, cCovariance, cOne, cMu, chiN);
        }
    }

    /// <summary>
    /// The numerical helpers specific to <see cref="CovarianceMatrixAdaptationEvolutionStrategy"/>:
    /// the recombination weights, the effective population size, the default population size, the
    /// symmetrisation, and the two <c>C^{±1/2}</c> applications that the sampler and the CSA path
    /// need. The dense linear algebra both strategies share stays in
    /// <see cref="NaturalEvolutionStrategyMath"/>.
    /// </summary>
    public static class CovarianceMatrixAdaptationEvolutionStrategyMath
    {
        /// <summary>The paper's default population size <c>λ = 4 + ⌊3 ln d⌋</c>.</summary>
        public static int DefaultPopulationSize(int dimension)
        {
            if (dimension <= 0) throw new ArgumentOutOfRangeException(nameof(dimension));
            return 4 + (int)Math.Floor(3.0 * Math.Log(dimension));
        }

        /// <summary>
        /// The recombination weights of the first <c>μ = ⌊λ/2⌋</c> samples, normalised to sum to one:
        /// <c>w_i ∝ ln((λ + 1)/2) − ln i</c>. Only these <c>μ</c> entries take part in the
        /// recombination; the vector returned is the length of the full population, with the worse
        /// half left at zero, so an index into the ranking can index it directly.
        /// </summary>
        public static double[] RecombinationWeights(int lambda)
        {
            if (lambda < 2) throw new ArgumentOutOfRangeException(nameof(lambda));
            int mu = lambda / 2;
            var weights = new double[lambda];
            double anchor = Math.Log((lambda + 1.0) / 2.0);
            double sum = 0.0;
            for (int i = 0; i < mu; i++)
            {
                weights[i] = anchor - Math.Log(i + 1.0);
                sum += weights[i];
            }
            for (int i = 0; i < mu; i++) weights[i] /= sum;
            return weights;
        }

        /// <summary>The variance-effective selection mass <c>μ_eff = 1 / Σ_{i≤μ} w_i²</c> (always in [1, μ]).</summary>
        public static double EffectivePopulationSize(int lambda)
        {
            var weights = RecombinationWeights(lambda);
            int mu = lambda / 2;
            double squares = 0.0;
            for (int i = 0; i < mu; i++) squares += weights[i] * weights[i];
            return 1.0 / squares;
        }

        /// <summary>The Euclidean norm of a vector.</summary>
        public static double Norm(double[] v)
        {
            ArgumentNullException.ThrowIfNull(v);
            double sum = 0.0;
            for (int i = 0; i < v.Length; i++) sum += v[i] * v[i];
            return Math.Sqrt(sum);
        }

        /// <summary>Replaces <paramref name="a"/> by its symmetric part <c>(A + Aᵀ)/2</c>, in place.</summary>
        public static void Symmetrise(double[,] a)
        {
            ArgumentNullException.ThrowIfNull(a);
            int n = a.GetLength(0);
            for (int i = 0; i < n; i++)
            {
                for (int j = i + 1; j < n; j++)
                {
                    double half = 0.5 * (a[i, j] + a[j, i]);
                    a[i, j] = half;
                    a[j, i] = half;
                }
            }
        }

        /// <summary>
        /// Applies <c>C^{1/2}</c> to <paramref name="v"/> through the cached eigendecomposition:
        /// <c>C^{1/2} = V √D Vᵀ</c>, with the negative eigenvalues clamped to zero.
        /// </summary>
        public static double[] ScaleBySquareRoot(double[] eigenvalues, double[,] eigenvectors, double[] v)
        {
            return ApplySpectral(eigenvalues, eigenvectors, v, squareRoot: true);
        }

        /// <summary>
        /// Applies <c>C^{-1/2}</c> to <paramref name="v"/>: <c>V D^{-1/2} Vᵀ</c>. Eigenvalues below
        /// <c>1e-20</c> are floored there rather than inverted outright, so a momentarily degenerate
        /// covariance degrades the path instead of producing an infinity.
        /// </summary>
        public static double[] ScaleByInverseSquareRoot(double[] eigenvalues, double[,] eigenvectors, double[] v)
        {
            return ApplySpectral(eigenvalues, eigenvectors, v, squareRoot: false);
        }

        private static double[] ApplySpectral(double[] eigenvalues, double[,] eigenvectors, double[] v, bool squareRoot)
        {
            ArgumentNullException.ThrowIfNull(eigenvalues);
            ArgumentNullException.ThrowIfNull(eigenvectors);
            ArgumentNullException.ThrowIfNull(v);
            int n = eigenvalues.Length;
            if (v.Length != n) throw new ArgumentException($"vector must have {n} coordinates", nameof(v));

            // w = Vᵀ v
            var rotated = new double[n];
            for (int j = 0; j < n; j++)
            {
                double s = 0.0;
                for (int i = 0; i < n; i++) s += eigenvectors[i, j] * v[i];
                rotated[j] = s;
            }

            // scaled = D^{±1/2} w
            for (int j = 0; j < n; j++)
            {
                rotated[j] = squareRoot
                    ? Math.Sqrt(Math.Max(eigenvalues[j], 0.0)) * rotated[j]
                    : rotated[j] / Math.Sqrt(Math.Max(eigenvalues[j], 1e-20));
            }

            // result = V scaled
            var result = new double[n];
            for (int i = 0; i < n; i++)
            {
                double s = 0.0;
                for (int j = 0; j < n; j++) s += eigenvectors[i, j] * rotated[j];
                result[i] = s;
            }
            return result;
        }

        /// <summary>
        /// The eigendecomposition of a symmetric matrix by the cyclic Jacobi method: rotations are
        /// applied until the off-diagonal mass is negligible, accumulating them into the eigenvector
        /// matrix. <paramref name="eigenvalues"/> comes out unsorted, which is all the spectral
        /// applications above need.
        /// </summary>
        public static void SymmetricEigen(double[,] matrix, out double[] eigenvalues, out double[,] eigenvectors)
        {
            ArgumentNullException.ThrowIfNull(matrix);
            int n = matrix.GetLength(0);
            if (matrix.GetLength(1) != n) throw new ArgumentException("matrix must be square", nameof(matrix));

            var a = (double[,])matrix.Clone();
            var v = NaturalEvolutionStrategyMath.Identity(n);

            for (int sweep = 0; sweep < 100; sweep++)
            {
                double offDiagonal = 0.0;
                for (int i = 0; i < n; i++)
                {
                    for (int j = i + 1; j < n; j++) offDiagonal += a[i, j] * a[i, j];
                }
                if (offDiagonal <= 1e-30) break;

                for (int p = 0; p < n - 1; p++)
                {
                    for (int q = p + 1; q < n; q++)
                    {
                        if (Math.Abs(a[p, q]) < 1e-300) continue;
                        double theta = (a[q, q] - a[p, p]) / (2.0 * a[p, q]);
                        double t = theta == 0.0
                            ? 1.0
                            : Math.Sign(theta) / (Math.Abs(theta) + Math.Sqrt(theta * theta + 1.0));
                        double c = 1.0 / Math.Sqrt(t * t + 1.0);
                        double s = t * c;

                        for (int k = 0; k < n; k++)
                        {
                            double akp = a[k, p];
                            double akq = a[k, q];
                            a[k, p] = c * akp - s * akq;
                            a[k, q] = s * akp + c * akq;
                        }
                        for (int k = 0; k < n; k++)
                        {
                            double apk = a[p, k];
                            double aqk = a[q, k];
                            a[p, k] = c * apk - s * aqk;
                            a[q, k] = s * apk + c * aqk;
                        }
                        for (int k = 0; k < n; k++)
                        {
                            double vkp = v[k, p];
                            double vkq = v[k, q];
                            v[k, p] = c * vkp - s * vkq;
                            v[k, q] = s * vkp + c * vkq;
                        }
                    }
                }
            }

            eigenvalues = new double[n];
            for (int i = 0; i < n; i++) eigenvalues[i] = a[i, i];
            eigenvectors = v;
        }
    }
}
