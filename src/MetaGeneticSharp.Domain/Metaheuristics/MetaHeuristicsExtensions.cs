#nullable disable

namespace MetaGeneticSharp
{
    /// <summary>
    /// The fluent grammar for composing metaheuristics. Only the Match-related verbs are
    /// ported so far; the full grammar (typed parameters, expression fusion, operator
    /// verbs) is the keystone of Phase 3 (see ROADMAP.md) and extends this class.
    /// </summary>
    public static class MetaHeuristicsExtensions
    {
        /// <summary>
        /// Defines the sub-metaheuristic after the container definition.
        /// </summary>
        public static T WithSubMetaHeuristic<T>(this T metaHeuristic, IMetaHeuristic subMetaHeuristic) where T : ContainerMetaHeuristic
        {
            metaHeuristic.SubMetaHeuristic = subMetaHeuristic;
            return metaHeuristic;
        }

        /// <summary>
        /// Defines the metaheuristic applied to the selected matches for the crossover itself.
        /// </summary>
        public static T WithCrossoverMetaHeuristic<T>(this T metaHeuristic, IMetaHeuristic crossoverMetaHeuristic) where T : MatchMetaHeuristic
        {
            metaHeuristic.CrossMetaHeuristic = crossoverMetaHeuristic;
            return metaHeuristic;
        }

        /// <summary>
        /// Appends one single-pick directive per given matching kind, each with its default
        /// caching scope.
        /// </summary>
        public static T WithMatches<T>(this T metaHeuristic, params MatchingKind[] matchingKinds) where T : MatchMetaHeuristic
        {
            var settings = matchingKinds.Select(m => new MatchingSettings { MatchingKind = m, CachingScope = MatchingSettings.GetDefaultScope(m) });
            metaHeuristic.Picker.MatchPicks.AddRange(settings);
            return metaHeuristic;
        }
    }
}
