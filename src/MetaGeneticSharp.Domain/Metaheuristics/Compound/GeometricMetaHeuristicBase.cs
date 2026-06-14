#nullable disable
using GeneticSharp;

namespace MetaGeneticSharp
{
    /// <summary>
    /// An abstract base for geometric compound metaheuristics, holding the common configuration
    /// (mutation toggle, forced reinsertion, geometric converter) and the <see cref="Build"/>
    /// assembly routine that wraps each concrete <see cref="BuildMainHeuristic"/> with a
    /// No-Mutation scope and a forced reinsertion layer. Ported from
    /// GeneticSharp.Domain.Metaheuristics.Compound (PR giacomelli/GeneticSharp#87).
    /// </summary>
    public abstract class GeometricMetaHeuristicBase : ICompoundMetaheuristic
    {
        /// <summary>
        /// Toggle the mutation operator (default <c>true</c> switches mutation off by wrapping the
        /// main heuristic in a scoped No-Mutation heuristic).
        /// </summary>
        public bool NoMutation { get; set; } = true;

        /// <summary>
        /// Whether to force a reinsertion layer on top of the built heuristic (default <c>true</c>).
        /// </summary>
        public bool ForceReinsertion { get; set; } = true;

        /// <summary>
        /// An optional custom reinsertion operator; falls back to <see cref="GetDefaultReinsertion"/>.
        /// </summary>
        public IReinsertion CustomReinsertion { get; set; }

        /// <summary>
        /// Max expected generations, for parameter calibration.
        /// </summary>
        public int MaxGenerations { get; set; }

        /// <summary>
        /// A converter providing a gene↔double conversion and an optional geometrisation embedding.
        /// </summary>
        public IGeometricConverter GeometricConverter { get; set; }

        /// <summary>
        /// Binds a typed geometric converter and stores it (untyped) in <see cref="GeometricConverter"/>.
        /// </summary>
        public virtual void SetGeometricConverter<TGeneValue>(IGeometricConverter<TGeneValue> converter)
        {
            var typedNoEmbeddingConverter = new TypedGeometricConverter();
            typedNoEmbeddingConverter.SetTypedConverter(converter);
            GeometricConverter = typedNoEmbeddingConverter;
        }

        /// <summary>
        /// The default reinsertion used when <see cref="ForceReinsertion"/> is on and
        /// <see cref="CustomReinsertion"/> is null.
        /// </summary>
        public virtual IReinsertion GetDefaultReinsertion()
        {
            return new FitnessBasedElitistReinsertion();
        }

        /// <inheritdoc />
        public IContainerMetaHeuristic Build()
        {
            var toReturn = BuildMainHeuristic();

            // Removing default mutation operator.
            if (NoMutation)
            {
                toReturn.SubMetaHeuristic = new DefaultMetaHeuristic()
                    .WithScope(EvolutionStage.Selection | EvolutionStage.Crossover | EvolutionStage.Reinsertion)
                    .WithName("No-Mutation MetaHeuristic");
            }

            // Enforcing pairwise reinsertion.
            if (ForceReinsertion)
            {
                var subHeuristic = toReturn.SubMetaHeuristic;
                var reinsertion = CustomReinsertion ?? GetDefaultReinsertion();
                toReturn.SubMetaHeuristic = new ReinsertionMetaHeuristic
                {
                    StaticOperator = reinsertion,
                    SubMetaHeuristic = subHeuristic,
                }.WithName($"Forced {reinsertion.GetType().Name} Reinsertion MetaHeuristic");
            }

            return toReturn;
        }

        /// <summary>
        /// Builds the main (problem-specific) heuristic that <see cref="Build"/> wraps.
        /// </summary>
        protected abstract IContainerMetaHeuristic BuildMainHeuristic();
    }
}
