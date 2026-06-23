#nullable disable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using GeneticSharp;

namespace MetaGeneticSharp
{
    /// <summary>
    ///   Registry / factory for compound metaheuristics: discovers the available
    ///   <see cref="IMetaHeuristic"/> types and builds a named instance of each
    ///   <see cref="KnownCompoundMetaheuristics"/> (Default GA, the reconstructed
    ///   geometric compounds WOA/EO/FBI, and the heterogeneous-island variants),
    ///   wiring a default identity <see cref="TypedGeometricConverter"/> when the
    ///   caller does not supply one. Ported from GeneticSharp.Domain.Metaheuristics
    ///   (PR giacomelli/GeneticSharp#87).
    /// </summary>
    /// <remarks>
    /// The PR#87 source used <c>GeneticSharp.Infrastructure.Framework.Reflection.TypeHelper</c>
    /// for Reflection-based discovery. That assembly is a private (<c>PrivateAssets="all"</c>)
    /// dependency of the pinned upstream GeneticSharp.Domain, so it is not transitively
    /// referenceable here. The discovery helpers are reproduced with plain <see cref="Reflection"/>
    /// to keep the registry self-contained (same pattern as GeometricExtensions).
    /// </remarks>
    public static class MetaHeuristicsService
    {
        private static readonly string[] compoundNames = Enum.GetNames(typeof(KnownCompoundMetaheuristics));

        // All concrete IMetaHeuristic types loadable in the current AppDomain (Reflection discovery,
        // reproducing TypeHelper.GetTypesByInterface<T> without the private Framework dependency).
        private static IList<Type> GetTypesByInterface<T>()
        {
            var result = new List<Type>();
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] types;
                try { types = assembly.GetTypes(); }
                catch (ReflectionTypeLoadException) { continue; }
                foreach (var t in types)
                {
                    if (typeof(T).IsAssignableFrom(t) && t.IsClass && !t.IsAbstract)
                    {
                        result.Add(t);
                    }
                }
            }
            return result;
        }

        // Reproduces TypeHelper.GetTypeByName<T>: find a loadable type whose simple name matches.
        private static Type GetTypeByName<T>(string name)
            => GetTypesByInterface<T>().FirstOrDefault(t => t.Name == name || t.FullName == name);

        /// <summary>
        /// Gets every available metaheuristic type (interface implementations discoverable via Reflection).
        /// </summary>
        /// <returns>All available metaheuristic types.</returns>
        public static IList<Type> GetMetaHeuristicTypes()
        {
            return GetTypesByInterface<IMetaHeuristic>();
        }

        /// <summary>
        /// Gets the available compound-metaheuristic names (the <see cref="KnownCompoundMetaheuristics"/> enum).
        /// </summary>
        /// <returns>The compound-metaheuristic names.</returns>
        public static IList<string> GetMetaHeuristicNames()
        {
            return compoundNames;
        }

        /// <summary>
        /// Creates the metaheuristic implementation with the specified name.
        /// </summary>
        /// <returns>The metaheuristic implementation instance.</returns>
        /// <param name="name">The metaheuristic name (a <see cref="KnownCompoundMetaheuristics"/> value).</param>
        /// <param name="maxGenerations">Generation budget forwarded to the geometric compounds.</param>
        /// <param name="populationSize">Population size used to size the island archipelagos.</param>
        /// <param name="geometricConverter">Defines how geometric operators transform between gene and metric space; a double-identity converter is built when null.</param>
        /// <param name="noMutation">Whether the geometric compounds disable mutation.</param>
        public static IMetaHeuristic CreateMetaHeuristicByName(string name, int maxGenerations = 1000, int populationSize = 100, IGeometricConverter geometricConverter = null, bool noMutation = true)
        {
            if (compoundNames.Contains(name))
            {
                Enum.TryParse<KnownCompoundMetaheuristics>(name, out var enumName);
                switch (enumName)
                {
                    case KnownCompoundMetaheuristics.None:
                        return null;
                    case KnownCompoundMetaheuristics.Default:
                        return new DefaultMetaHeuristic();
                    case KnownCompoundMetaheuristics.DefaultRandomHyperspeed:
                        var hyperspeed = new DefaultMetaHeuristic();
                        hyperspeed.MatchMetaHeuristic.Picker.MatchPicks[1] = new MatchingSettings { MatchingKind = MatchingKind.Random };
                        hyperspeed.MatchMetaHeuristic.EnableHyperSpeed = true;
                        return hyperspeed;
                    default:
                        if (geometricConverter == null)
                        {
                            // A double-identity converter (no embedding): genes already are bare doubles.
                            var noEmbeddingConverter = new GeometricConverter<double>
                            {
                                IsOrdered = false,
                                DoubleToGeneConverter = (geneIndex, geomValue) => geomValue,
                                GeneToDoubleConverter = (genIndex, geneValue) => geneValue
                            };
                            var typedNoEmbeddingConverter = new TypedGeometricConverter();
                            typedNoEmbeddingConverter.SetTypedConverter(noEmbeddingConverter);
                            geometricConverter = typedNoEmbeddingConverter;
                        }

                        switch (enumName)
                        {
                            case KnownCompoundMetaheuristics.WhaleOptimisation:
                            case KnownCompoundMetaheuristics.WhaleOptimisationNaive:
                                var woa = new WhaleOptimisationAlgorithm()
                                {
                                    MaxGenerations = maxGenerations,
                                    GeometricConverter = geometricConverter,
                                    NoMutation = noMutation
                                };
                                if (enumName == KnownCompoundMetaheuristics.WhaleOptimisationNaive)
                                {
                                    woa.BubbleOperator = WhaleOptimisationAlgorithm.GetSimpleBubbleNetOperator();
                                }
                                return woa.Build();
                            case KnownCompoundMetaheuristics.EquilibriumOptimizer:
                                var eo = new EquilibriumOptimizer()
                                {
                                    MaxGenerations = maxGenerations,
                                    GeometricConverter = geometricConverter,
                                    NoMutation = noMutation
                                };
                                return eo.Build();
                            case KnownCompoundMetaheuristics.ForensicBasedInvestigation:
                                var fbi = new ForensicBasedInvestigation()
                                {
                                    MaxGenerations = maxGenerations,
                                    GeometricConverter = geometricConverter,
                                    NoMutation = noMutation
                                };
                                return fbi.Build();
                            case KnownCompoundMetaheuristics.DifferentialEvolution:
                                var de = new DifferentialEvolution()
                                {
                                    MaxGenerations = maxGenerations,
                                    GeometricConverter = geometricConverter,
                                    NoMutation = noMutation
                                };
                                return de.Build();
                            case KnownCompoundMetaheuristics.Islands5Default:
                            case KnownCompoundMetaheuristics.Islands5DefaultNoMigration:
                            case KnownCompoundMetaheuristics.Islands5BestMixture:
                            case KnownCompoundMetaheuristics.Islands5BestMixtureNoMigration:
                                var islandNb = 5;
                                IslandCompoundMetaheuristic islandCompound;
                                switch (enumName)
                                {
                                    case KnownCompoundMetaheuristics.Islands5Default:
                                    case KnownCompoundMetaheuristics.Islands5DefaultNoMigration:
                                        var defaultGA = new DefaultMetaHeuristic();
                                        ICompoundMetaheuristic targetCompoundHeuristic = new SimpleCompoundMetaheuristic(defaultGA);
                                        islandCompound = new IslandCompoundMetaheuristic(populationSize / islandNb, islandNb,
                                            targetCompoundHeuristic);
                                        islandCompound.GlobalMigrationRate = IslandMetaHeuristic.MediumMigrationRate;
                                        break;
                                    case KnownCompoundMetaheuristics.Islands5BestMixture:
                                    case KnownCompoundMetaheuristics.Islands5BestMixtureNoMigration:
                                        var woaIsland = new WhaleOptimisationAlgorithm()
                                        {
                                            MaxGenerations = maxGenerations,
                                            GeometricConverter = geometricConverter,
                                            NoMutation = noMutation
                                        };
                                        var eoIsland = new EquilibriumOptimizer()
                                        {
                                            MaxGenerations = maxGenerations,
                                            GeometricConverter = geometricConverter,
                                            NoMutation = noMutation
                                        };
                                        var defaultGABest = new DefaultMetaHeuristic();
                                        var defaultIsland = new SimpleCompoundMetaheuristic(defaultGABest);
                                        islandCompound = new IslandCompoundMetaheuristic(populationSize,
                                            (2, woaIsland),
                                            (2, eoIsland),
                                            (1, woaIsland));
                                        islandCompound.GlobalMigrationRate = IslandMetaHeuristic.SmallMigrationRate;
                                        break;
                                    default:
                                        throw new InvalidOperationException("Unsupported Island configuration");
                                }
                                if (enumName == KnownCompoundMetaheuristics.Islands5DefaultNoMigration ||
                                    enumName == KnownCompoundMetaheuristics.Islands5BestMixtureNoMigration)
                                {
                                    islandCompound.MigrationMode = MigrationMode.None;
                                }
                                return islandCompound.Build();
                            default:
                                throw new ArgumentOutOfRangeException(nameof(name));
                        }
                }
            }
            return CreateInstanceByName<IMetaHeuristic>(name);
        }

        /// <summary>
        /// Gets the root metaheuristic type produced by <see cref="CreateMetaHeuristicByName"/> for the given name.
        /// </summary>
        /// <returns>The root metaheuristic type.</returns>
        /// <param name="name">The name of the metaheuristic.</param>
        public static Type GetMetaHeuristicTypeByName(string name)
        {
            if (compoundNames.Contains(name))
            {
                Enum.TryParse<KnownCompoundMetaheuristics>(name, out var enumName);
                switch (enumName)
                {
                    case KnownCompoundMetaheuristics.None:
                        return null;
                    case KnownCompoundMetaheuristics.Default:
                    case KnownCompoundMetaheuristics.DefaultRandomHyperspeed:
                        return typeof(DefaultMetaHeuristic);
                    case KnownCompoundMetaheuristics.WhaleOptimisation:
                    case KnownCompoundMetaheuristics.WhaleOptimisationNaive:
                        return typeof(IfElseMetaHeuristic);
                    case KnownCompoundMetaheuristics.ForensicBasedInvestigation:
                        return typeof(GenerationMetaHeuristic);
                    case KnownCompoundMetaheuristics.EquilibriumOptimizer:
                    case KnownCompoundMetaheuristics.DifferentialEvolution:
                        return typeof(MatchMetaHeuristic);
                    case KnownCompoundMetaheuristics.Islands5Default:
                    case KnownCompoundMetaheuristics.Islands5DefaultNoMigration:
                    case KnownCompoundMetaheuristics.Islands5BestMixture:
                    case KnownCompoundMetaheuristics.Islands5BestMixtureNoMigration:
                        return typeof(IslandMetaHeuristic);
                    default:
                        throw new ArgumentOutOfRangeException(nameof(name));
                }
            }
            return GetTypeByName<IMetaHeuristic>(name);
        }

        // Reproduces TypeHelper.CreateInstanceByName<T>: locate the type by name then activate it.
        private static T CreateInstanceByName<T>(string name)
        {
            var type = GetTypeByName<T>(name);
            return type == null ? default : (T)Activator.CreateInstance(type);
        }
    }
}
