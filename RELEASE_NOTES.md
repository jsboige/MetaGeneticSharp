# Release Notes — MetaGeneticSharp

## v0.1.0 — 2026-07-04

First tagged release. MetaGeneticSharp revives the metaheuristics layer of
[GeneticSharp PR #87](https://github.com/giacomelli/GeneticSharp/pull/87)
(2020–2022, closed unmerged with the suggestion it become a child project)
as an autonomous child library that consumes
[GeneticSharp](https://github.com/giacomelli/GeneticSharp) as a vanilla,
unpatched submodule pinned at **v3.1.4**. All seven ROADMAP phases
(Phase 0 — Scaffolding through Phase 6 — Landscape Explorer revival) are
**DONE**; the library is buildable, tested, and exercised end-to-end by
19 pedagogical notebooks in
[CoursIA `Search/Part4-Metaheuristics/`](https://github.com/jsboige/CoursIA/tree/main/MyIA.AI.Notebooks/Search/Part4-Metaheuristics).

This release is **code-complete**; NuGet packaging is deferred (requires
a user-hand publish step) — consume by submodule until then.

### Highlights

- **Autonomous engine, zero upstream patch.** `MetaGeneticAlgorithm`
  implements `IGeneticAlgorithm` directly over the unmodified
  `GeneticSharp` trunk. The two trunk changes that doomed PR #87
  (removing the implicit fitness sort, offspring-scoped evaluation) are
  absorbed into this engine rather than patched upstream. Individuals are
  addressed by **stable index** across evolution stages.
- **Components over metaphors.** Published algorithms are reconstructed
  from reusable, inspectable primitives — not shipped as opaque
  monoliths (Sørensen 2015).
- **Geometric-crossover trunk.** Every compound flows through one
  `GeometricCrossover<TValue>` realizing Moraglio's geometric-crossover
  theory (Moraglio 2007); the metric is carried by a swappable
  `IGeometryEmbedding<TValue>`, so the same motor explores a different
  landscape when the embedding changes.
- **Honest benchmarking.** A dedicated de-bias toolchain exposes the
  central-bias and axis-alignment biases that inflat classic benchmark
  numbers (Kudela, *Nature Machine Intelligence* 2022).

### Core library (`MetaGeneticSharp.Domain`)

| Component | Role |
|-----------|------|
| `MetaGeneticAlgorithm` | Autonomous evolution engine: metaheuristic-driven loop, offspring-scoped fitness evaluation, no implicit fitness sort |
| `MetaPopulation` (`IMetaPopulation : IPopulation`) | Order-preserving population with a parameter store for evolution-context caching |
| `IMetaHeuristic` + primitives | Composable units (`Container`, `Scoped`, `NoOp`, `Default`, `IfElse`, `Match`, `Generation`) intercepting each evolution stage |
| `IEvolutionContext` | Per-population / per-individual context: stable indices, stage products, scoped parameters |
| Fluent grammar (`MetaHeuristicsExtensions`) | Declarative composition of primitives into complete algorithms |

GeneticSharp's operator catalog (selections, crossovers, mutations,
terminations, randomization) is consumed as-is through its public
interfaces.

### Compound metaheuristics (`Metaheuristics/Compound/`)

Eight published algorithms reconstructed from primitives, all buildable
by name via `MetaHeuristicsService.CreateMetaHeuristicByName` and
enumerated in `KnownCompoundMetaheuristics` (11 enum values covering the
single-compound + archipelago variants):

- `WhaleOptimisationAlgorithm` — WOA (Mirjalili 2016)
- `EquilibriumOptimizer` — EO (Faramarzi 2020)
- `ForensicBasedInvestigation` — FBI (Chou & Nguyen 2020)
- `DifferentialEvolution` — DE/rand/1/bin (Storn & Price 1997)
- `BareBonesParticleSwarm` — BBPSO (Kennedy 2003)
- `SimulatedAnnealing` — Metropolis 1953
- `IslandCompoundMetaheuristic` — heterogeneous archipelago (each island a different compound)
- `EukaryoteChromosome` / Eukaryote multi-compartment model

Every compound converges on standard benchmark functions; results
comparable to published / mealpy behaviour. WOA wins or ties 7 of 10
`KnownFunctions`; divergence on Schwefel is documented as
No-Free-Lunch, not a port defect.

### Geometric crossover & permutation embeddings (`Crossovers/Geometric/`)

`GeometricCrossover<TValue>` realizes Moraglio's theory: a crossover is
*geometric* when its offspring lies on the geodesic segment between the
parents under a chosen metric. Four embeddings ship, each realizing a
distinct natural metric (all subclassing `IdentityEmbedding<TValue>`):

| Embedding | Metric | Single-step walk |
|-----------|--------|------------------|
| `OrderedEmbedding<TValue>` | Swap / Cayley | `FlipGene` — transposition |
| `InsertionEmbedding<TValue>` | Insertion / Ulam | `InsertAt` — extract + shift + reinsert |
| `KendallTauEmbedding<TValue>` | Adjacent transposition / Kendall-Tau | adjacent swap — bubble-sort one inverted pair |
| `EdgeEmbedding<TValue>` | Edge / edge-adjacency (TSP) | edge recombination |

Fluent verbs `WithLinearGeometricOperator`, `WithGeneralGeometricOperator`
and `WithGeometryEmbedding` compose on one expression (closes the
Phase 3 slice 3b deferral).

### De-bias & benchmark tooling (`MetaGeneticSharp.Extensions`)

The two biases targeted by modern (CEC-style) suites are exposed, not
hidden:

| Tool | Role |
|------|------|
| `KnownFunctions` | Canonical benchmark functions (Sphere, Rastrigin, Rosenbrock, Ackley, Schwefel, Eggholder, Levy, Booth, Dixon-Price, …) — each asserts its optimum at x* and the max/min convention |
| `ShiftedFitness` + `ShiftVectors` | Compositional decorator that relocates the optimum off-center with a per-dimension seeded offset — defeats central-bias (geometry-agnostic: reuses inner math unchanged) |
| `RotatedFitness` + `RotationMatrices` | Compositional decorator that rotates coordinates by an orthogonal matrix (`RotationMatrices.Seeded` = reproducible product of Givens rotations) — defeats axis-alignment bias |
| `CenterBiasBenchmark` | Centered-vs-displaced protocol (Kudela 2022): drives any optimizer under an equal `EvaluationBudget` and reports the delta; `RandomSearchOptimizer` is the unbiased control whose delta sits near zero |
| `LandscapeRenderer` / `KnownFunctionLandscape` | Heatmap rendering of the fitness surface with convergence overlays and heightmap landscapes |

`ShiftedFitness` and `RotatedFitness` are thin compositional decorators:
they reuse the canonical function math unchanged and compose for the full
CEC shifted-then-rotated variant —
`new RotatedFitness(new ShiftedFitness(inner, offset), M)`.

### Landscape tooling (`MetaGeneticSharp.Extensions/Landscape`)

A faithful revival of the gtk# `LandscapeExplorerSampleController` from
PR #87, cross-platform via **SkiaSharp** (PRs #21–#30):

- **H1/H2/H3 heatmaps & heightmaps** — PNG rendering, additive bilinear
  IDW vs bilinear sibling, parallelized canvas, opt-in
  `ColorQuantization.Round` de-bias HSV truncation
- **M1 animated-GIF encoder** — convergence flipbook
- **M2 colored-islands overlay** — per-individual marker colours
- **L4 dimensional guards** on Booth / Dixon-Price

Consumed by notebook `MGS-7-LandscapeExplorer` (ASCII-grayscale ramp
under papermill where `#r "nuget:"` hangs; feature preserved).

### Tests

38 test files, 111+ Domain tests covering: primitive composition,
evolution-context stable indices, each compound's convergence on
`KnownFunctions`, geometric-crossover centroids through every embedding
(keystone: gene-wise midpoint of two parents end-to-end), shift/rotation
decorator reproducibility, and the keystone registry chain
(`Islands5BestMixture` builds an archipelago whose islands are
themselves WOA/EO compounds, `.Build()` chained — a single assertion
covers the whole assembly chain).

### Notebooks (CoursIA, 19 notebooks)

The release is exercised end-to-end by
[`MGS-1` through `MGS-19`](https://github.com/jsboige/CoursIA/tree/main/MyIA.AI.Notebooks/Search/Part4-Metaheuristics):
Introduction, Composition, Eukaryote, Islands, CompoundMetaheuristics,
Benchmarks, TSP, LandscapeExplorer, EverestRelief, CenterBias,
IslandSynergy, AxisAlignment, LandscapeDebias, IslandSynergyFound,
LandscapeAnalysis, AlgorithmSelection, ParameterControl, CecBanc,
MetropolisReinsertion.

### Known caveats (documented, acceptable)

- `Generation.BestChromosome` stays `null` (upstream `internal` setter;
  only `Generation.End()` fills it, which `MetaPopulation` never calls).
  `MetaPopulation.BestChromosome` is authoritative; none of the consumed
  operators reads `CurrentGeneration.BestChromosome`.
- A vanilla `IPopulation` (non-`IMetaPopulation`) gets a fresh evolution
  context per call (nowhere to cache). Use `MetaPopulation` for real runs.
- `WeightedCrossoverEmbedding` deferred — depends on `IWeightedCrossover`,
  which does not exist in upstream 3.1.4 (PR #87 trunk material).
- `DefaultGeometricConverter<TGeneValue>.DoubleToGene` relies on
  `TypeDescriptor.GetConverter` (BCL numeric converters refuse an
  already-typed `double`). Not on the default `GeometricCrossover` path;
  `GeneToDouble` (`Convert.ToDouble`) is the asserted direction.

### Origin & attribution

Metaheuristics layer first developed in
[GeneticSharp PR #87](https://github.com/giacomelli/GeneticSharp/pull/87)
(2020-11-18 → 2022-09-04). The PR grew too large for the upstream trunk
and was closed with @giacomelli's suggestion that it become a *"child
project of GeneticSharp"*. MetaGeneticSharp is that child project.
@ktnr's 2020-11-20 operations-research framing — *host extensions in a
different library that uses GeneticSharp as a submodule* — is the
architecture this release implements. See [ROADMAP.md](ROADMAP.md) for
the full six-phase port plan (all phases DONE) and the Option B
architecture decision.

### Not in this release

- **NuGet package** — user-hand publish; consume by submodule until then.
- **`WeightedCrossoverEmbedding`** — depends on a PR #87 trunk interface
  not present in upstream 3.1.4.
- **Upstream nuggets** — `TplOperatorsStrategy` offspring-ordering fix
  and unsealing requests ride as small self-contained PRs against
  giacomelli/GeneticSharp (ROADMAP §Continuous).

### References

- Moraglio, A. *Towards a geometric unification of evolutionary
  algorithms* (PhD, 2007).
- Sørensen, K. *Metaheuristics—the metaphor exposed* (International
  Transactions in Operational Research, 2015).
- Kudela, J. *A critical problem in benchmarking and analysis of
  evolutionary computation methods* (Nature Machine Intelligence 4, 2022).
- Storn, R. & Price, K. *Differential Evolution* (Journal of Global
  Optimization, 1997).
- Kennedy, J. *Bare Bones Particle Swarms* (SIS 2003).
- Mirjalili, S. *The Ant Lion Optimizer / WOA* (Advances in Engineering
  Software, 2016).
- Faramarzi, A. et al. *Equilibrium Optimizer* (Knowledge-Based Systems, 2020).
- Chou, J.-S. & Nguyen, N.-M. *Forensic-Based Investigation* (IEEE Access, 2020).
