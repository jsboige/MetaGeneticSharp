#nullable disable

using System.Linq.Expressions;
using GeneticSharp;

namespace MetaGeneticSharp
{
    /// <summary>
    /// The fluent grammar for composing metaheuristics. This is the Phase 3 keystone: it lets a
    /// compound heuristic be expressed as a chain of <c>.With...()</c> verbs rather than a tangle
    /// of constructor arguments. Verbs fall into families: naming/parameter wiring (typed and
    /// expression-fused parameters), scoping, control-flow phases, operator wiring (static and
    /// dynamic), and Match/Island/Size composition. The Geometric verbs belong to Phase 4
    /// (compound metaheuristics) and are not ported here.
    /// </summary>
    public static class MetaHeuristicsExtensions
    {
        // ---- Naming ----

        /// <summary>
        /// Adds a name and description to a building block for clarity/maintainability.
        /// </summary>
        public static T WithName<T>(this T metaHeuristic, string paramName, string paramDescription = "") where T : NamedEntity
        {
            metaHeuristic.Name = paramName;
            metaHeuristic.Description = paramDescription;
            return metaHeuristic;
        }

        // ---- Parameter wiring ----

        /// <summary>
        /// Defines a dynamic parameter on the metaheuristic, to be leveraged by child operators
        /// and sub-parameters. Delegate-based: the generator runs per evaluation.
        /// </summary>
        public static T WithParameter<T, TParamType>(this T metaHeuristic, string paramName, string paramDescription, ParamScope scope, ParameterGenerator<TParamType> generator) where T : MetaHeuristicBase
        {
            metaHeuristic.Parameters.Add(paramName, new MetaHeuristicParameter<TParamType> { Name = paramName, Description = paramDescription, Generator = generator, Scope = scope });
            return metaHeuristic;
        }

        /// <summary>
        /// Defines a parameter from a lambda expression. When unscoped, the expression is injected
        /// directly as a sub-expression for global compilation; when scoped, it is checked for a
        /// locally cached value and the compiled expression serves as a factory.
        /// </summary>
        public static T WithParam<T, TParamType>(this T metaHeuristic, string paramName, string paramDescription, ParamScope scope, Expression<ParameterGenerator<TParamType>> generator) where T : MetaHeuristicBase
        {
            metaHeuristic.Parameters.Add(paramName, new ExpressionMetaHeuristicParameter<TParamType> { Name = paramName, Description = paramDescription, DynamicGenerator = generator, Scope = scope });
            return metaHeuristic;
        }

        /// <summary>
        /// Expression parameter with one extra argument. That argument must be named exactly as a
        /// previously defined parameter; it is fused into a nested expression accounting for the
        /// different parameter scopes (see <see cref="ParameterReplacer"/>).
        /// </summary>
        public static T WithParam<T, TParamType, TArg1>(this T metaHeuristic, string paramName, string paramDescription, ParamScope scope, Expression<ParameterGenerator<TParamType, TArg1>> generator) where T : MetaHeuristicBase
        {
            metaHeuristic.Parameters.Add(paramName, new ExpressionMetaHeuristicParameter<TParamType, TArg1> { Name = paramName, Description = paramDescription, DynamicGeneratorWithArg = generator, Scope = scope });
            return metaHeuristic;
        }

        /// <summary>
        /// Expression parameter with two extra arguments, each named after a previously defined
        /// parameter and fused into a nested expression.
        /// </summary>
        public static T WithParam<T, TParamType, TArg1, TArg2>(this T metaHeuristic, string paramName, string paramDescription, ParamScope scope, Expression<ParameterGenerator<TParamType, TArg1, TArg2>> generator) where T : MetaHeuristicBase
        {
            metaHeuristic.Parameters.Add(paramName, new ExpressionMetaHeuristicParameter<TParamType, TArg1, TArg2> { Name = paramName, Description = paramDescription, DynamicGeneratorWithArgs = generator, Scope = scope });
            return metaHeuristic;
        }

        /// <summary>
        /// Expression parameter with three extra arguments, each named after a previously defined
        /// parameter and fused into a nested expression.
        /// </summary>
        public static T WithParam<T, TParamType, TArg1, TArg2, TArg3>(this T metaHeuristic, string paramName, string paramDescription, ParamScope scope, Expression<ParameterGenerator<TParamType, TArg1, TArg2, TArg3>> generator) where T : MetaHeuristicBase
        {
            metaHeuristic.Parameters.Add(paramName, new ExpressionMetaHeuristicParameter<TParamType, TArg1, TArg2, TArg3> { Name = paramName, Description = paramDescription, DynamicGeneratorWithArgs = generator, Scope = scope });
            return metaHeuristic;
        }

        // ---- Scoping ----

        /// <summary>
        /// Defines the evolution stages to which the metaheuristic applies.
        /// </summary>
        public static T WithScope<T>(this T metaHeuristic, EvolutionStage stage) where T : ScopedMetaHeuristic
        {
            metaHeuristic.Scope = stage;
            return metaHeuristic;
        }

        // ---- Container wiring ----

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
        /// Defines how input crossover probability is processed and/or passed to the sub-operators.
        /// </summary>
        public static T WithCrossoverProbabilityStrategy<T>(this T metaHeuristic, ProbabilityStrategy strategy) where T : ContainerMetaHeuristic
        {
            metaHeuristic.ProbabilityConfig.Crossover.Strategy = strategy;
            return metaHeuristic;
        }

        /// <summary>
        /// Defines how input mutation probability is processed and/or passed to the sub-operators.
        /// </summary>
        public static T WithMutationProbabilityStrategy<T>(this T metaHeuristic, ProbabilityStrategy strategy) where T : ContainerMetaHeuristic
        {
            metaHeuristic.ProbabilityConfig.Mutation.Strategy = strategy;
            return metaHeuristic;
        }

        // ---- Control-flow phases ----

        /// <summary>
        /// Adds a phase heuristic at the given index in a phase-based metaheuristic.
        /// </summary>
        public static T WithCase<T, TIndex>(this T metaHeuristic, TIndex phaseIndex, IMetaHeuristic subMetaHeuristic) where T : PhaseMetaHeuristicBase<TIndex>
        {
            metaHeuristic.PhaseHeuristics.Add(phaseIndex, subMetaHeuristic);
            return metaHeuristic;
        }

        /// <summary>
        /// Defines the true case of an IfElse compound metaheuristic.
        /// </summary>
        public static T WithTrue<T>(this T metaHeuristic, IMetaHeuristic subMetaHeuristic) where T : IfElseMetaHeuristic
        {
            return WithCase(metaHeuristic, true, subMetaHeuristic);
        }

        /// <summary>
        /// Defines the false case of an IfElse compound metaheuristic.
        /// </summary>
        public static T WithFalse<T>(this T metaHeuristic, IMetaHeuristic subMetaHeuristic) where T : IfElseMetaHeuristic
        {
            return WithCase(metaHeuristic, false, subMetaHeuristic);
        }

        /// <summary>
        /// Defines the phase generator (which phase heuristic to run according to the context) for a
        /// switch metaheuristic. Delegate-based.
        /// </summary>
        public static T WithCaseGenerator<T, TIndex>(this T metaHeuristic, ParamScope scope, ParameterGenerator<TIndex> phaseGenerator) where T : SwitchMetaHeuristic<TIndex>
        {
            metaHeuristic.DynamicParameter = new MetaHeuristicParameter<TIndex>
            {
                Name = $"{metaHeuristic.Guid}_CaseGenerator",
                Generator = phaseGenerator,
                Scope = scope
            };

            return metaHeuristic;
        }

        /// <summary>
        /// Expression-based phase generator with one extra parameter. That parameter must be
        /// registered to the context before the dynamic generator is invoked; it is fused into a
        /// parameter-less expression leveraging the context cache or the upstream expression.
        /// </summary>
        public static T WithCaseGenerator<T, TIndex, TArg1>(this T metaHeuristic, ParamScope scope, Expression<ParameterGenerator<TIndex, TArg1>> dynamicPhaseGenerator) where T : SwitchMetaHeuristic<TIndex>
        {
            metaHeuristic.DynamicParameter = new ExpressionMetaHeuristicParameter<TIndex, TArg1>
            {
                DynamicGeneratorWithArg = dynamicPhaseGenerator,
                Scope = scope
            };
            return metaHeuristic;
        }

        // ---- Operator wiring ----

        /// <summary>
        /// Defines a static selection to apply within a selection metaheuristic.
        /// </summary>
        public static T WithSelection<T>(this T metaHeuristic, ISelection selection) where T : SelectionMetaHeuristic
        {
            metaHeuristic.StaticOperator = selection;
            return metaHeuristic;
        }

        /// <summary>
        /// Defines a dynamic selection (generated per the context and cached by scope).
        /// </summary>
        public static T WithSelection<T>(this T metaHeuristic, ParameterGenerator<ISelection> dynamicOperator, ParamScope scope) where T : SelectionMetaHeuristic
        {
            metaHeuristic.DynamicParameter = new MetaHeuristicParameter<ISelection>
            {
                Generator = dynamicOperator,
                Scope = scope
            };
            return metaHeuristic;
        }

        /// <summary>
        /// Defines a static crossover to apply within a crossover metaheuristic.
        /// </summary>
        public static T WithCrossover<T>(this T metaHeuristic, ICrossover crossover) where T : CrossoverMetaHeuristic
        {
            metaHeuristic.StaticOperator = crossover;
            return metaHeuristic;
        }

        /// <summary>
        /// Defines a dynamic crossover (delegate-based, cached by scope).
        /// </summary>
        public static T WithCrossover<T>(this T metaHeuristic, ParamScope scope, ParameterGenerator<ICrossover> dynamicOperator) where T : CrossoverMetaHeuristic
        {
            metaHeuristic.DynamicParameter = new MetaHeuristicParameter<ICrossover>
            {
                Generator = dynamicOperator,
                Scope = scope
            };
            return metaHeuristic;
        }

        /// <summary>
        /// Defines a dynamic crossover from a one-argument expression; the argument is fused away.
        /// </summary>
        public static T WithCrossover<T, TArg1>(this T metaHeuristic, ParamScope scope, Expression<ParameterGenerator<ICrossover, TArg1>> dynamicOperator) where T : CrossoverMetaHeuristic
        {
            metaHeuristic.DynamicParameter = new ExpressionMetaHeuristicParameter<ICrossover, TArg1>
            {
                DynamicGeneratorWithArg = dynamicOperator,
                Scope = scope
            };
            return metaHeuristic;
        }

        /// <summary>
        /// Defines a dynamic crossover from a two-argument expression; the arguments are fused away.
        /// </summary>
        public static T WithCrossover<T, TArg1, TArg2>(this T metaHeuristic, ParamScope scope, Expression<ParameterGenerator<ICrossover, TArg1, TArg2>> dynamicOperator) where T : CrossoverMetaHeuristic
        {
            metaHeuristic.DynamicParameter = new ExpressionMetaHeuristicParameter<ICrossover, TArg1, TArg2>
            {
                DynamicGeneratorWithArgs = dynamicOperator,
                Scope = scope
            };
            return metaHeuristic;
        }

        /// <summary>
        /// Defines a dynamic crossover from a three-argument expression; the arguments are fused away.
        /// </summary>
        public static T WithCrossover<T, TArg1, TArg2, TArg3>(this T metaHeuristic, ParamScope scope, Expression<ParameterGenerator<ICrossover, TArg1, TArg2, TArg3>> dynamicOperator) where T : CrossoverMetaHeuristic
        {
            metaHeuristic.DynamicParameter = new ExpressionMetaHeuristicParameter<ICrossover, TArg1, TArg2, TArg3>
            {
                DynamicGeneratorWithArgs = dynamicOperator,
                Scope = scope
            };
            return metaHeuristic;
        }

        /// <summary>
        /// Defines a static mutation to apply within a mutation metaheuristic.
        /// </summary>
        public static T WithMutation<T>(this T metaHeuristic, IMutation mutation) where T : MutationMetaHeuristic
        {
            metaHeuristic.StaticOperator = mutation;
            return metaHeuristic;
        }

        /// <summary>
        /// Defines a dynamic mutation (delegate-based, cached by scope).
        /// </summary>
        public static T WithMutation<T>(this T metaHeuristic, ParamScope scope, ParameterGenerator<IMutation> dynamicOperator) where T : MutationMetaHeuristic
        {
            metaHeuristic.DynamicParameter = new MetaHeuristicParameter<IMutation>
            {
                Generator = dynamicOperator,
                Scope = scope
            };
            return metaHeuristic;
        }

        /// <summary>
        /// Defines a static reinsertion to apply within a reinsertion metaheuristic.
        /// </summary>
        public static T WithReinsertion<T>(this T metaHeuristic, IReinsertion reinsertion) where T : ReinsertionMetaHeuristic
        {
            metaHeuristic.StaticOperator = reinsertion;
            return metaHeuristic;
        }

        /// <summary>
        /// Defines a dynamic reinsertion (delegate-based, cached by scope).
        /// </summary>
        public static T WithReinsertion<T>(this T metaHeuristic, ParamScope scope, ParameterGenerator<IReinsertion> dynamicOperator) where T : ReinsertionMetaHeuristic
        {
            metaHeuristic.DynamicParameter = new MetaHeuristicParameter<IReinsertion>
            {
                Generator = dynamicOperator,
                Scope = scope
            };
            return metaHeuristic;
        }

        // ---- Match composition ----

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

        /// <summary>
        /// Appends emigrant-pick directives for an island metaheuristic, each contributing
        /// <paramref name="nbPicks"/> picks (additional picks = nbPicks - 1).
        /// </summary>
        public static T WithEmigrantMatches<T>(this T metaHeuristic, int nbPicks, params MatchingKind[] matchingKinds) where T : IslandMetaHeuristic
        {
            var settings = matchingKinds.Select(m => new MatchingSettings { MatchingKind = m, AdditionalPicks = nbPicks - 1, CachingScope = MatchingSettings.GetDefaultScope(m) });
            metaHeuristic.EmigrantPicker.MatchPicks.AddRange(settings);
            return metaHeuristic;
        }

        /// <summary>
        /// Appends immigrant-replacement-pick directives for an island metaheuristic.
        /// </summary>
        public static T WithImmigrantReplaceMatches<T>(this T metaHeuristic, int nbPicks, params MatchingKind[] matchingKinds) where T : IslandMetaHeuristic
        {
            var settings = matchingKinds.Select(m => new MatchingSettings { MatchingKind = m, AdditionalPicks = nbPicks - 1, CachingScope = MatchingSettings.GetDefaultScope(m) });
            metaHeuristic.ImigrantReplacePicker.MatchPicks.AddRange(settings);
            return metaHeuristic;
        }

        /// <summary>
        /// Appends a custom match step (a sequence of pick settings applied as one stage) to the
        /// match metaheuristic's picker.
        /// </summary>
        public static T WithCustomMatchStep<T>(this T metaHeuristic, params MatchingSettings[] stepSettings) where T : MatchMetaHeuristic
        {
            if (metaHeuristic.Picker.CustomMatch == null)
            {
                metaHeuristic.Picker.CustomMatch = new List<List<MatchingSettings>>();
            }
            metaHeuristic.Picker.CustomMatch.Add(new List<MatchingSettings>(stepSettings));
            return metaHeuristic;
        }

        /// <summary>
        /// Defines the child metaheuristic applied to the matched children of a match metaheuristic.
        /// </summary>
        public static T WithChildMetaHeuristic<T>(this T metaHeuristic, IMetaHeuristic childMetaHeuristic) where T : MatchMetaHeuristic
        {
            metaHeuristic.Picker.ChildMetaHeuristic = childMetaHeuristic;
            return metaHeuristic;
        }

        // ---- Size-based phases ----

        /// <summary>
        /// Adds one size-based phase: a duration (in individuals) followed by its sub-heuristic.
        /// </summary>
        public static T WithSizeMetaHeuristic<T>(this T metaHeuristic, int phaseSize, IMetaHeuristic subMetaHeuristic) where T : SizeBasedMetaHeuristic
        {
            metaHeuristic.PhaseSizes.Add(metaHeuristic.PhaseSizes.Phases.Count, phaseSize);
            metaHeuristic.PhaseHeuristics[metaHeuristic.PhaseSizes.Phases.Count - 1] = subMetaHeuristic;
            return metaHeuristic;
        }

        /// <summary>
        /// Adds <paramref name="repeatNb"/> identical size-based phases.
        /// </summary>
        public static T WithSizeMetaHeuristics<T>(this T metaHeuristic, int phaseSize, int repeatNb, IMetaHeuristic subMetaHeuristic) where T : SizeBasedMetaHeuristic
        {
            for (int i = 0; i < repeatNb; i++)
            {
                metaHeuristic.PhaseSizes.Add(metaHeuristic.PhaseSizes.Phases.Count, phaseSize);
                metaHeuristic.PhaseHeuristics[metaHeuristic.PhaseSizes.Phases.Count - 1] = subMetaHeuristic;
            }
            return metaHeuristic;
        }
    }
}
