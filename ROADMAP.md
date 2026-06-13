# ROADMAP — MetaGeneticSharp

Port plan for reviving [giacomelli/GeneticSharp#87](https://github.com/giacomelli/GeneticSharp/pull/87) as an autonomous child project. Written to allow any agent (or human) to resume work mid-phase. See [README.md](README.md) for the project's intent and the two-inspiration narrative (mealpy x GeneticSharp).

## Source material

- **PR branch**: `MyIntelligenceAgency/GeneticSharp`, branch `Metaheuristics` (head `d05826fd`, merge-base with upstream `cb73ab8`). Re-clone with:
  ```bash
  git clone https://github.com/MyIntelligenceAgency/GeneticSharp -b Metaheuristics
  ```
  All metaheuristics sources live under `src/GeneticSharp.Domain/Metaheuristics/`.
- **Upstream submodule**: `GeneticSharp/` pinned at tag `3.1.4` (`e15ee828db36`), flattened namespace `GeneticSharp`. **Never patched** (architecture decision below).
- **Caveat**: the PR branch carries a "black"-style wholesale reformat of the trunk (acknowledged by the author as not his best idea). When diffing PR files against upstream, ignore pure-formatting churn; only the metaheuristics layer and the trunk semantic changes matter.

## Architecture decision — Option B: autonomous engine, vanilla submodule, zero patch

PR #87 mixed two things: (1) a new metaheuristics layer, and (2) semantic changes to the GeneticSharp trunk that the layer required. The trunk spillover is what killed the PR. The child project keeps (1) and absorbs (2) into its own engine instead of patching upstream:

| PR #87 trunk change | Why the layer needs it | Where it lives now |
|---|---|---|
| Removed implicit fitness sort in `GeneticAlgorithm.EvaluateFitness` | Metaheuristics address individuals by **stable index** (`IEvolutionContext.GetIndividual(i)`, Eukaryote partitions, islands) | `MetaGeneticAlgorithm.EvaluateFitness` (no sort) |
| Removed sort in `Generation.End` | Same | `MetaPopulation.EndCurrentGeneration` (order-preserving, never calls `Generation.End()`) |
| Offspring-scoped fitness evaluation | Parents keep fitness; evaluation budget honest | `MetaGeneticAlgorithm.EvolveOneGeneration` |
| Default reinsertion `ElitistReinsertion` -> `FitnessBasedElitistReinsertion` | Compensates the no-sort engine (elitism must be explicit) | `FitnessBasedElitistReinsertion` ported into this repo |
| `Population.Parameters` dictionary | Evolution-context caching | `IMetaPopulation.Parameters` (upstream `IPopulation` untouched) |
| Unsealed `GeneticAlgorithm`, `Initialise`/`Step`/`Reset` lifecycle | Extensibility + island/eukaryote sub-runs | `MetaGeneticAlgorithm` is self-contained (implements `IGeneticAlgorithm` directly) |

**Verified coupling facts (upstream 3.1.4)** that make Option B viable:
- `Generation` has a public ctor `(int number, IList<IChromosome> chromosomes)`; its `Chromosomes`/`BestChromosome` setters are `internal`, so we construct fresh `Generation` instances and never mutate them.
- `ISelection.SelectChromosomes(int, Generation)` implementations only read `Chromosomes`.
- `IReinsertion`, `ICrossover`, `IMutation` are representation-pure.
- Terminations read only `IGeneticAlgorithm` members (`BestChromosome`, `GenerationsNumber`, `TimeEvolving`) — which `MetaGeneticAlgorithm` implements.

**Known caveats of the approach** (acceptable, documented):
- `Generation.BestChromosome` stays `null` (internal setter; only `Generation.End()` fills it). `MetaPopulation.BestChromosome` is authoritative. Any upstream component reading `CurrentGeneration.BestChromosome` would see null — none of the operators used does.
- A vanilla `IPopulation` (non-`IMetaPopulation`) gets a **fresh evolution context per call** (no place to cache it). Use `MetaPopulation` for real runs.
- Upstream nugget worth a tiny dedicated upstream PR: `TplOperatorsStrategy` offspring-ordering bug (`ConcurrentBag` loses order; the PR's fix uses `ConcurrentDictionary<int,...>` + `OrderBy(key)` — already applied in our `TplMetaOperatorsStrategy`).

## Porting conventions

- Namespace: flat `MetaGeneticSharp` (mirrors upstream v3's flattening). Folders are organizational only.
- Ported files start with `#nullable disable` (2020 code, not nullable-annotated; csproj has `Nullable=enable`). Annotating is a later cleanup task, not a port task.
- Upstream types via a single `using GeneticSharp;`.
- The PR's `Infrastructure.Framework` extensions (e.g. `AddRange` on `IList<T>`) are **inlined**, not ported wholesale.
- PowerShell-only on this repo under Claude Code on Windows (the Bash tool mangles backslashes). SDK 10: `dotnet new sln --format sln` (default is `.slnx`).

## Phases

### Phase 0 — Scaffolding [DONE]

Solution, 3 src + 3 test projects, submodule pinned 3.1.4, smoke test proving the reference chain. Commit `fba672d`.

### Phase 1 — Critical core [DONE — this delivery]

The minimal engine that runs a metaheuristic-driven GA end-to-end. ~25 files in `src/MetaGeneticSharp.Domain/`:

- **Engine**: `MetaGeneticAlgorithm` (merges the PR's patched `GeneticAlgorithm` + PR `MetaGeneticAlgorithm` into one self-contained class), `IMetaPopulation`, `MetaPopulation`.
- **Contexts** (`EvolutionContext/`): `IEvolutionContext`, `EvolutionContext`, `SubEvolutionContext`, `IndividualContext`.
- **Primitives** (`Primitives/`): `MetaHeuristicBase`, `CustomProbabilityMetaHeuristic`, `IContainerMetaHeuristic`, `ContainerMetaHeuristic`, `ScopedMetaHeuristic`, `NoOpMetaHeuristic`, `DefaultMetaHeuristic` (Match path stripped — Phase 2).
- **Strategies** (`OperatorsStrategies/`): `IMetaOperatorsStrategy`, `LinearMetaOperatorsStrategy`, `TplMetaOperatorsStrategy` (with ordering fix).
- **Support**: `EvolutionStage`, `ProbabilityStrategy`, `INamedEntity`/`NamedEntity`, `ParamScope`, `IMetaHeuristicParameter`, `ProbabilityConfig`, `OperatorsProbabilityConfig`, `Reinsertions/FitnessBasedElitistReinsertion`.
- **Tests**: engine convergence (FloatingPointChromosome), elitist monotonicity, order preservation in `MetaPopulation`, context lifecycle (`KeepContextInPopulation`).

Port deviations from the PR (intentional):
- `MetaHeuristicBase.GetContext` branches on `population is IMetaPopulation` for caching (the PR put `Parameters` on `IPopulation` itself).
- `DefaultMetaHeuristic` omits the lazy `MatchMetaHeuristic` property; only the direct-pairing branch is ported. Restore in Phase 2.
- `SubEvolutionContext.GetParam` delegates to the population context (faithful to the PR) — note this drops individual-scoped resolution; revisit in Phase 3 with the parameter system.
- `IMetaOperatorsStrategy` is our own 2-method interface (`Cross`/`Mutate`) rather than the PR's additions to upstream `IOperatorsStrategy`.

### Phase 2 — Match + remaining primitives [DONE]

Sources under `Metaheuristics/Match/` and `Metaheuristics/Primitives/` on the PR branch (sizes in bytes as triage hints):

1. **Match machinery** [DONE]: `MatchingKind`, `MatchingSettings`, `MatchPicker`, `MatchMetaHeuristic` ported; `DefaultMetaHeuristic`'s lazy Match path restored. Port deviations (intentional):
   - `MatchMetaHeuristic.CrossMetaHeuristic` falls back to `SubMetaHeuristic` when null (the PR NREs on a bare instance).
   - `MatchingKind.Best` single-pick reads `ctx.Population.BestChromosome` instead of `CurrentGeneration.BestChromosome` (our engine never calls `Generation.End()`, see caveats above), with random fallback while null.
   - The PR's `Generation.GetBestChromosomes`/`GetWorstChromosomes` trunk additions are absorbed as `GenerationExtensions` extension methods (plain `OrderBy` for now; the PR's `LazyOrderBy` partial sort is a Phase 5 benchmark-driven optimization).
   - `MetaHeuristicParameter<T>` + `IMetaHeuristicParameterGenerator<T>` ported early (Phase 3 material) because `MatchPicker`'s per-scope caching requires them. The expression-tree variants remain Phase 3.
   - `MetaHeuristicsExtensions` seeded with the three Match verbs (`WithSubMetaHeuristic`, `WithCrossoverMetaHeuristic`, `WithMatches`); the full grammar remains Phase 3.
2. **Sub-population machinery** [DONE]: `EukaryoteChromosome`, `SubPopulation`, `SubPopulationContext`, `SubPopulationMetaHeuristicBase<T>`, `EukaryoteMetaHeuristic`. Reinsertion is unsupported by design — the canonical usage (PR Sudoku test) scopes it `EvolutionStage.Crossover | EvolutionStage.Mutation` so reinsertion falls through to the sub-metaheuristic. Port deviations (intentional):
   - `SubPopulation` derives from our `MetaPopulation` (the PR derives from its patched `Population`): keeps generation order stable (no implicit fitness sort) and inherits the parameter store.
   - `EukaryoteMetaHeuristic.ScopedMatchParentsAndCross` passes the **sub-population context** to `subHeuristic.MatchParentsAndCross` where the PR passes the parent ctx — population-reading picks (`MatchingKind.Random`/`Best`) would otherwise pull full-size chromosomes into a sub-chromosome crossover.
   - `EukaryoteChromosome.GetSubPopulations` materializes karyotypes with `.ToList()` (the PR re-enumerates the lazy projection per sub-population index — quadratic slicing, perf only).
   - `PerformSubOperator` lives on `SubPopulationMetaHeuristicBase<T>` as a generic over `T : SubPopulation` (the PR hardcodes `SubPopulation`), ready for `IslandMetaHeuristic` (item 5).
   - **Same-object alignment note** (why `SynchroniseParents` works despite `IndividualContext`'s `SelectedParents` shadow, identical in the PR): `SubPopulation.GetContext` returns the cached `SubPopulationContext` itself when indices already match, so the assignment hits the overriding setter and sticks; misaligned callers get `IndividualContext` wrappers whose reads fall back to that stuck value.
3. **Control-flow primitives** [DONE]: `PhaseMetaHeuristicBase<TIndex>`, `SwitchMetaHeuristic<TIndex>` (+ `IfElseMetaHeuristic`), `SizeBasedMetaHeuristic` (with nested `EnumeratedPhases`), `GenerationMetaHeuristic`, `PopulationMetaHeuristic`, `StageSwitchMetaHeuristic` (PR name: `StagePhaseMetaHeuristic`), `EmptyMetaHeuristic`. Port deviations (intentional):
   - **Two latent PR caching bugs fixed**: the PR gives `PopulationMetaHeuristic` and `StageSwitchMetaHeuristic` a `ParamScope.Generation` cache scope, but the masking semantics zero the individual (resp. widen the stage) out of the cache key — so the first computed value per generation would pin all individuals to one phase (resp. freeze the stage switch). Both now use `ParamScope.None` (the generators are trivial context reads; caching buys nothing). Documented in code comments.
   - `GenerationMetaHeuristic`/`PopulationMetaHeuristic`/`StageSwitchMetaHeuristic` use plain `MetaHeuristicParameter<T>` generators where the PR uses `ExpressionMetaHeuristicParameter` (same runtime semantics; expression-tree fusion is Phase 3).
   - `PositiveMod` does not exist in upstream 3.1.4 `Infrastructure.Framework` — inlined as a private helper in `EnumeratedPhases`.
4. **Operator wrappers** [DONE]: `OperatorMetaHeuristic<TOperator>` (with `ParamScope.Constant` promotion of the generated operator to `StaticOperator`), `CrossoverMetaHeuristic`, `MutationMetaHeuristic`, `SelectionMetaHeuristic`, `ReinsertionMetaHeuristic`. Deviation: `CrossoverMetaHeuristic`'s `DisplayName` fixed to "Crossover" (the PR mislabels it "Container", copy-paste slip).
5. **Islands** [DONE]: `IslandPopulation` (`SubPopulation` + per-island `MigrationRates`), `IslandMetaHeuristic` with `MigrationMode` (None/Static/RandomRing/RandomPermutation/Reinforced), periodic migration in the selection stage (`GenerationsNumber % MigrationsGenerationPeriod`), emigrants picked Best/copied (not removed) and worst targets replaced via `MatchPicker`s. The ROADMAP's earlier "needs sub-GA runs, check `Reset`/`Step`" concern turned out moot: the primitive never runs sub-GAs — islands are `SubPopulation` slices of **full individuals** evolved by the per-island phase heuristics within the parent engine's stages. Port deviations (intentional):
   - `(ctx.GeneticAlgorithm as MetaGeneticAlgorithm)?.Crossover` replaces the PR's hard cast to its patched `GeneticAlgorithm` (the crossover only feeds Child/Custom match picks; Best/Worst ignore it).
   - The hardcoded `crossoverProbability = 1` passed to sub-heuristic cross calls is kept and documented as **load-bearing**: mutation/reinsertion re-slice the global offspring list by fixed island sizes, only exact when every pair produces offspring (`DefaultMetaHeuristic` rolls the dice itself otherwise).
   - `InitMigrationRates`' off-diagonal `GlobalMigrationRate / Count` (arguably `/(Count-1)`) kept faithful — Static mode only, ctor-time snapshot.
   - `SynchroniseGeneration` is dead code under the default `Generation | MetaHeuristic` caching scope (islands are regenerated fresh each generation); kept faithful for user-broadened scopes.
   - PR's commented-out debug blocks dropped; `sourceReinserts` renamed `sourceChromosomes`. `PerformSubOperator` from the generic base is unused by Island (concatenation, not karyotype recombination) — as in the PR.

Acceptance: unit tests per primitive + one composed scenario (e.g. eukaryote over a 2-part chromosome).

### Phase 3 — Parameters + fluent grammar [DONE]

The project's keystone (see README). Sources: `Parameters/` (`ExpressionMetaHeuristicParameter` 4793, `MetaHeuristicParameter` 3705, `ParameterReplacer` 2347 — expression-tree fusion, perf-sensitive), `MetaHeuristicsExtensions.cs` (19903 — the fluent grammar itself), `MetaHeuristicsService` (12036). Depends on the PR's `Infrastructure.Framework` lambda visitors — port what's needed into `MetaGeneticSharp.Infrastructure`.

Split into atomically-shippable slices:

1. **Parameter system foundation** [DONE — this delivery]: `ExpressionMetaHeuristicParameter<T>` (+ 3 multi-arg variants + `WithArgs` base), `IExpressionGeneratorParameter`, `ParameterReplacer` (reduces a lambda referencing named dependencies into a closed expression tree), and the sole Infrastructure.Framework dependency `LambdaExpressionHelper` (`ReplaceParameter`/`UnifyParametersByName` expression visitors) ported into `MetaGeneticSharp.Infrastructure` (first real source file in that project, flat namespace `MetaGeneticSharp`). `ParameterGenerator<>` delegates and the `MetaHeuristicParameter<T>` runtime core were already ported in Phase 2. Acceptance: 4 keystone tests — a parameter referencing a named dependency fuses to a closed tree and matches the hand-wired equivalent; scoped caching verified. Build 0/0, 37/37 tests.
2. **Fluent grammar** [DONE — this delivery]: `MetaHeuristicsExtensions.cs` extended from a 3-verb seed (Phase 2 Match verbs) to the full grammar — naming/parameter wiring (typed `WithParameter` + expression-fused `WithParam` x4 arities), scoping (`WithScope`), container wiring (`WithSubMetaHeuristic`/`WithCrossoverMetaHeuristic`/`WithCrossover/MutationProbabilityStrategy`), control-flow phases (`WithCase`/`WithTrue`/`WithFalse`/`WithCaseGenerator` x2), operator wiring (static + dynamic-delegate + dynamic-expression `WithSelection`/`WithCrossover` x4 arities/`WithMutation`/`WithReinsertion`), Match/Island composition (`WithMatches`/`WithEmigrantMatches`/`WithImmigrantReplaceMatches`/`WithCustomMatchStep`/`WithChildMetaHeuristic`), and size-based phases (`WithSizeMetaHeuristic(s)`). The Geometric verbs (`WithLinear/GeneralGeometricOperator`/`WithGeometryEmbedding`) are deliberately deferred to Phase 4 (they need `GeometricCrossover<TValue>`/`IGeometryEmbedding`, which is compound material). Acceptance: 7 fluent-grammar tests — expression-fused dependency parameter resolves identically to hand-wired, container/scope chain configures, IfElse branches wire, operator verbs configure static+dynamic, and a fluent crossover drives a MetaGeneticAlgorithm to termination. Build 0/0, 44/44 tests.
3. **Service / discovery** [MOVED to Phase 4 — verified against source]: reading the PR's `MetaHeuristicsService.cs` reveals it is not standalone "registration/discovery" but the **compound-metaheuristic factory registry**: `CreateMetaHeuristicByName` instantiates `WhaleOptimisationAlgorithm`/`EquilibriumOptimizer`/`ForensicBasedInvestigation`/`IslandCompoundMetaheuristic`, the `KnownCompoundMetaheuristics` enum, and `GeometricConverter<T>`/`TypedGeometricConverter`. Every one of those is Phase 4 (Compound + Geometric) material that does not exist yet. Porting the service now would require stubbing the whole Phase 4 bestiary — an anti-regression violation (rule D). It therefore ships **with** Phase 4, not as a Phase 3 slice.

Acceptance (whole Phase): a compound heuristic expressed fluently, benchmarked against its hand-wired equivalent (no measurable overhead) — **MET by slice 3b's keystone dependency-fusion test** (`(h,c,v) => v*v` with the named dependency `v` resolved to 6 compiles to one closed expression tree yielding 36, identical to the hand-wired equivalent; no per-call overhead).

### Phase 4 — Compound metaheuristics [TODO]

`Compound/`: `GeometricMetaHeuristicBase` (2765), `WhaleOptimisationAlgorithm` (12346), `EquilibriumOptimizer` (7999), `ForensicBasedInvestigation` (10256), `IslandCompoundMetaheuristic` (2773), `SimpleCompoundMetaheuristic` (719), `KnownCompoundMetaheuristics` (429). These are the mealpy-style bestiary entries reconstructed from primitives — the pedagogical payoff.

**Prerequisite — Geometric infrastructure (ships first) [DONE — this delivery]:** the PR's `Domain.Crossovers.Geometric` layer ported under `src/MetaGeneticSharp.Domain/Crossovers/Geometric/` (flat namespace `MetaGeneticSharp`): converters (`IGeometricConverter`/`GeometricConverter`/`DefaultGeometricConverter`/`TypedGeometricConverter`), embeddings (`IGeometryEmbedding`/`IdentityEmbedding`/`OrderedEmbedding`/`TypedGeometryEmbedding`), the `GeneSelectionMode` enum, and the `GeometricCrossover<TValue>` crossover wrapper (inherits upstream `CrossoverBase`). Port deviations (intentional, anti-regression-faithful):
  - Two PR#87 helpers absent from the pinned 3.1.4 upstream are reproduced in a self-contained `GeometricExtensions`: the `.To<TTarget>()` value converter (used by `GetCentroid`) and the two-argument `FlipGene(int, int)` gene swap (PR#87 `ChromosomeExtensions`). Neither touches upstream.
  - `ExceptionHelper.ThrowIfNull` (PR#87 `Infrastructure.Framework`) replaced with the BCL `ArgumentNullException.ThrowIfNull` (.NET 9), dropping the `GeneticSharp.Infrastructure.Framework.Commons` using — the namespace is not transitively reachable from `MetaGeneticSharp.Domain`, and the BCL call is behaviour-identical.
  - `WeightedCrossoverEmbedding` **deferred** — it depends on `IWeightedCrossover`, which does not exist in upstream 3.1.4 and is itself PR#87 trunk material; porting it needs that interface first (later Phase 4 cycle, not this one).
  - Caveat documented in tests: `DefaultGeometricConverter<TGeneValue>.DoubleToGene` relies on `TypeDescriptor.GetConverter`, whose BCL numeric converters refuse an already-typed `double` source (`DoubleConverter cannot convert from System.Double`). This mirrors the PR#87 behaviour and is not on the default `GeometricCrossover` path (which uses the `LinearGeometricOperator` directly on `TValue` via the embedding); `GeneToDouble` (`Convert.ToDouble`) is robust and is the asserted direction.
  - Acceptance: 11 geometric tests including the keystone — `GeometricCrossover<double>` with the default centroid operator produces the gene-wise midpoint of two parents end-to-end through the embedding layer (`[0,10,20]` x `[30,40,50]` → `[15,25,35]`). Build 0/0, 55/55 tests (44 regression + 11 geometric). The Geometric verbs deferred from Phase 3 (`WithLinear/GeneralGeometricOperator`/`WithGeometryEmbedding`) can now wire against `GeometricCrossover<TValue>` in a follow-up slice.

**Registry cap:** `MetaHeuristicsService` (12036, moved here from Phase 3) and its `KnownCompoundMetaheuristics` enum are the named-factory layer on top of the compound implementations — port them once WOA/EO/FBI exist, so the factory instantiates real compounds rather than stubs.

Acceptance: each compound converges on standard benchmark functions; results comparable to published/mealpy behavior.

### Phase 5 — Extensions + benchmarks [TODO]

`MetaGeneticSharp.Extensions`: TSP, Sudoku, benchmark functions from the PR's Extensions project (`Mathematic/Functions/`: `IKnownFunction`, `KnownFunction`, `KnownFunctions` — Ackley, Rastrigin, Eggholder, Levy, ...). A mealpy-style evaluation harness (same functions, same budgets) to ground the "consolidation without performance loss" claim.

**Center-bias protocol (required, project-owner mandate 2026-06-12).** Many published metaheuristics (WOA among them) exploit benchmark optima sitting at the center/zero of the search domain — shifting the functions exposes the bias (Kudela, *A critical problem in benchmarking and analysis of evolutionary computation methods*, Nature Machine Intelligence 4, 2022). The PR already has an embryo: `KnownFunctionExtensions.Shift` (uniform scalar shift on every coordinate). Generalize it when porting:
- per-dimension shift **vectors** (seeded-random offsets, not one scalar for all dims), keeping the un-shifted variant available for comparison;
- benchmark harness runs every function in both centered and shifted form by default, reporting the delta (a large centered-vs-shifted gap is the bias signature);
- optional rotation can come later; shift is the cheap high-signal first step.

### Phase 6 — Landscape Explorer revival [TODO]

The era's companion visualization/exploration tool: in the PR it lives in `GeneticSharp.Runner.GtkApp/Samples/LandscapeExplorerSampleController.cs` (landscape replot over `KnownFunctions`, heatmap rendering of fitness landscapes + population trajectories). Revive on a modern host (likely a notebook-friendly plotting backend or a small app in `tools/`, consuming this library) rather than porting the GTK shell. First-class use case: **visualizing the center-bias of compound metaheuristics** (Phase 5 shifted functions x Phase 4 WOA/EO/FBI — the heatmap makes the bias visible, not just measurable).

### Continuous — upstream nuggets

Small, self-contained fixes for upstream PRs against giacomelli/GeneticSharp: Tpl ordering fix; possibly unsealing requests. One tiny PR each, no spillover — the lesson of #87.
