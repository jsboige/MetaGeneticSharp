#nullable disable
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using GeneticSharp;

namespace MetaGeneticSharp
{
    /// <summary>
    /// <para>
    /// A Bayesian estimation-of-distribution operator (CoursIA #24013): at each generation the
    /// <see cref="EliteCount"/> best individuals are fitted with a mixture of
    /// <see cref="Components"/> Gaussians whose posteriors (weights, means, precisions) are
    /// inferred by Infer.NET -- see <see cref="BayesianGaussianMixture"/> -- and the next
    /// generation is drawn from the posterior predictive, one sample per reference parent.
    /// </para>
    /// <para>
    /// Unlike a single-Gaussian search distribution (xNES, CMA-ES), the mixture can represent
    /// several basins of attraction at once, which is the property the plugin exists to test:
    /// hybridating the GeneticSharp geometric metaheuristic machinery with Bayesian inference.
    /// The inference cost is bounded by construction and declared, not hidden: dimension at most
    /// 5, components at most 3, elites at most 200 -- every bound enforced by an explicit
    /// exception in <see cref="BayesianGaussianMixture.Fit"/>, and the measured per-generation
    /// inference time is carried by the fitted model (<see cref="BayesianGaussianMixture.InferenceTimeMs"/>).
    /// </para>
    /// </summary>
    public class BayesianEdaMetaHeuristic : GeometricMetaHeuristicBase
    {
        /// <summary>
        /// The number of mixture components fitted on the elites (bound: 1..3, enforced at fit time).
        /// </summary>
        public int Components { get; set; } = 2;

        /// <summary>
        /// The number of best-ranked individuals the mixture is fitted from each generation
        /// (bound: at most 200, enforced at fit time).
        /// </summary>
        public int EliteCount { get; set; } = 20;

        /// <summary>The inference algorithm, Expectation Propagation by default.</summary>
        public EdaInferenceAlgorithm Algorithm { get; set; } = EdaInferenceAlgorithm.ExpectationPropagation;

        /// <summary>The EP/VMP message-passing iterations per fit (bound: 1..100, enforced at fit time).</summary>
        public int InferenceIterations { get; set; } = 30;

        /// <summary>
        /// Optional seed for the Infer.NET random source, making posterior-parameter sampling
        /// reproducible run for run. The GA-side random stream (component selection) stays under
        /// the harness seed.
        /// </summary>
        public int? Seed { get; set; }

        /// <summary>
        /// The EDA is a comma strategy, like NES: the distribution is estimated from the current
        /// generation's elites and the whole offspring is sampled from it, so only the offspring
        /// are reinserted.
        /// </summary>
        public override IReinsertion GetDefaultReinsertion()
        {
            return new PureReinsertion();
        }

        /// <inheritdoc />
        protected override IContainerMetaHeuristic BuildMainHeuristic()
        {
            var root = new BayesianEdaMetaHeuristicMetaHeuristic
            {
                Components = Components,
                EliteCount = EliteCount,
                Algorithm = Algorithm,
                InferenceIterations = InferenceIterations,
                Seed = Seed,
                GeometricConverter = GeometricConverter,
            };
            return root.WithName("Bayesian Estimation-of-Distribution",
                "Estimation-of-distribution operator whose search distribution is a Bayesian mixture of Gaussians: weights, means and precisions inferred by Infer.NET (EP or VMP) from the generation's elites, offspring drawn one per reference from the posterior predictive.");
        }
    }

    /// <summary>
    /// The assembled root of the Bayesian EDA: fits the mixture once per generation (cached in the
    /// evolution-context store) on the best-ranked chromosomes and replaces the crossover by one
    /// posterior-predictive sample per reference individual. Selection, reinsertion and mutation
    /// are delegated to the sub-metaheuristic, as in any <see cref="ContainerMetaHeuristic"/>.
    /// </summary>
    [DisplayName("Bayesian Estimation-of-Distribution")]
    public class BayesianEdaMetaHeuristicMetaHeuristic : ContainerMetaHeuristic
    {
        /// <summary>The evolution-context store key of the per-generation mixture model.</summary>
        public const string ModelKey = "BayesianEDA-Model";

        /// <summary>Creates the root around a default sub-metaheuristic (selection and reinsertion only).</summary>
        public BayesianEdaMetaHeuristicMetaHeuristic()
            : base(new DefaultMetaHeuristic())
        {
            // One sample per reference, every time: the crossover probability is not an EDA concept.
            ProbabilityConfig.Crossover.Strategy = ProbabilityStrategy.TestProbability | ProbabilityStrategy.OverwriteProbability;
        }

        /// <inheritdoc cref="BayesianEdaMetaHeuristic.Components" />
        public int Components { get; set; } = 2;

        /// <inheritdoc cref="BayesianEdaMetaHeuristic.EliteCount" />
        public int EliteCount { get; set; } = 20;

        /// <inheritdoc cref="BayesianEdaMetaHeuristic.Algorithm" />
        public EdaInferenceAlgorithm Algorithm { get; set; } = EdaInferenceAlgorithm.ExpectationPropagation;

        /// <inheritdoc cref="BayesianEdaMetaHeuristic.InferenceIterations" />
        public int InferenceIterations { get; set; } = 30;

        /// <inheritdoc cref="BayesianEdaMetaHeuristic.Seed" />
        public int? Seed { get; set; }

        /// <summary>The gene/double converter; a plain numeric conversion is used when null.</summary>
        public IGeometricConverter GeometricConverter { get; set; }

        /// <summary>
        /// The mixture model of the current generation, fitted on first access and cached in the
        /// evolution-context store: posteriors inferred from the <see cref="EliteCount"/> best
        /// chromosomes of the current generation (the initial population at the first generation).
        /// </summary>
        public BayesianGaussianMixture GetModel(IEvolutionContext ctx)
        {
            ArgumentNullException.ThrowIfNull(ctx);
            int generation = ctx.Population?.GenerationsNumber ?? 0;
            return ctx.GetOrAdd((ModelKey, generation, EvolutionStage.Crossover, (IMetaHeuristic)this, 0), () =>
            {
                // Best first; a missing fitness ranks last. OrderByDescending is stable, so ties keep population order.
                var elites = ctx.Population.CurrentGeneration.Chromosomes
                    .OrderByDescending(c => c.Fitness ?? double.NegativeInfinity)
                    .Select(ToPoint)
                    .Take(EliteCount)
                    .ToList();

                return BayesianGaussianMixture.Fit(elites, Components, Algorithm, InferenceIterations, Seed);
            });
        }

        /// <inheritdoc />
        protected override IList<IChromosome> DoMatchParentsAndCross(IEvolutionContext ctx, ICrossover crossover, float crossoverProbability, IList<IChromosome> parents)
        {
            var model = GetModel(ctx);
            var toReturn = new List<IChromosome>(crossover.ParentsNumber);
            var rnd = RandomizationProvider.Current;

            // Same skipping window as MatchMetaHeuristic: one posterior-predictive sample per
            // reference parent, so the offspring count equals the population size.
            for (int matchIndex = 0; matchIndex < crossover.ParentsNumber; matchIndex++)
            {
                var referenceIndex = ctx.LocalIndex + matchIndex;
                if (referenceIndex < parents.Count)
                {
                    // The reference only supplies the chromosome type; every gene is overwritten.
                    var child = parents[referenceIndex].CreateNew();
                    var sample = model.Sample(rnd);
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
}
