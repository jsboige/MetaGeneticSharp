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

### Phase 2 — Match + remaining primitives [TODO]

Sources under `Metaheuristics/Match/` and `Metaheuristics/Primitives/` on the PR branch (sizes in bytes as triage hints):

1. **Match machinery**: `MatchingKind` (266), `MatchingSettings` (1276), `MatchPicker` (13585), `MatchMetaHeuristic` (4544). Then restore `DefaultMetaHeuristic`'s Match path.
2. **Sub-population machinery**: `SubPopulationContext` (1937, context), `SubPopulationMetaHeuristicBase` (2591), `EukaryoteMetaHeuristic` (6467 — chromosome partitioning; check its dependence on PR `Population` virtuals, adapt to `MetaPopulation`).
3. **Control-flow primitives**: `SwitchMetaHeuristic` (4206), `SizeBasedMetaHeuristic` (4269), `GenerationMetaHeuristic` (1332), `PopulationMetaHeuristic` (1037), `PhaseMetaHeuristicBase` (1218), `StagePhaseMetaHeuristic` (606), `EmptyMetaHeuristic` (1282).
4. **Operator wrappers**: `OperatorMetaHeuristic` (801), `CrossoverMetaHeuristic` (718), `MutationMetaHeuristic` (685), `SelectionMetaHeuristic` (628), `ReinsertionMetaHeuristic` (706).
5. **Islands**: `IslandMetaHeuristic` (18844 — biggest primitive; needs sub-GA runs, check `Reset`/`Step` usage).

Acceptance: unit tests per primitive + one composed scenario (e.g. eukaryote over a 2-part chromosome).

### Phase 3 — Parameters + fluent grammar [TODO]

The project's keystone (see README). Sources: `Parameters/` (`ExpressionMetaHeuristicParameter` 4793, `MetaHeuristicParameter` 3705, `ParameterReplacer` 2347 — expression-tree fusion, perf-sensitive), `MetaHeuristicsExtensions.cs` (19903 — the fluent grammar itself), `MetaHeuristicsService` (12036). Depends on the PR's `Infrastructure.Framework` lambda visitors — port what's needed into `MetaGeneticSharp.Infrastructure`.

Acceptance: a compound heuristic expressed fluently, benchmarked against its hand-wired equivalent (no measurable overhead).

### Phase 4 — Compound metaheuristics [TODO]

`Compound/`: `GeometricMetaHeuristicBase` (2765), `WhaleOptimisationAlgorithm` (12346), `EquilibriumOptimizer` (7999), `ForensicBasedInvestigation` (10256), `IslandCompoundMetaheuristic` (2773), `SimpleCompoundMetaheuristic` (719), `KnownCompoundMetaheuristics` (429). These are the mealpy-style bestiary entries reconstructed from primitives — the pedagogical payoff.

Acceptance: each compound converges on standard benchmark functions; results comparable to published/mealpy behavior.

### Phase 5 — Extensions + benchmarks [TODO]

`MetaGeneticSharp.Extensions`: TSP, Sudoku, benchmark functions from the PR's Extensions project. A mealpy-style evaluation harness (same functions, same budgets) to ground the "consolidation without performance loss" claim.

### Phase 6 — Landscape Explorer revival [TODO]

The era's companion visualization/exploration tool (to be revived per the project owner). Scope to be defined when Phases 1-4 are stable — likely a separate repo or `tools/` folder consuming this library.

### Continuous — upstream nuggets

Small, self-contained fixes for upstream PRs against giacomelli/GeneticSharp: Tpl ordering fix; possibly unsealing requests. One tiny PR each, no spillover — the lesson of #87.
