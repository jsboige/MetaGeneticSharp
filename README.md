# MetaGeneticSharp

Composable metaheuristics for .NET, built on [GeneticSharp](https://github.com/giacomelli/GeneticSharp).

## Why

Python's [mealpy](https://github.com/thieu1995/mealpy) shows how far a metaheuristics library can go when a large catalog of algorithms shares a common trunk: genes are vectors, operators are vector operations, and everything stays compact, fast, and uniformly benchmarkable. GeneticSharp starts from the opposite end: chromosomes, fitness functions and operators are plain .NET interfaces that presume nothing about the underlying representation — bit strings, permutations, trees and floating-point vectors alike.

MetaGeneticSharp aims at both at once: mealpy-grade expressiveness — an algorithm stated in a few declarative lines — over GeneticSharp's representation-agnostic components, without giving up performance. Its core abstraction is the **metaheuristic**, a composable unit that can intercept each stage of the evolution loop (selection, crossover, mutation, reinsertion), together with a **fluent grammar** to assemble them. Published algorithms (Whale Optimization, Equilibrium Optimizer, island models, ...) are reconstructed from reusable primitives rather than shipped as opaque monoliths — components over metaphors.

## Origin

The metaheuristics layer was first developed in [GeneticSharp PR #87](https://github.com/giacomelli/GeneticSharp/pull/87) (2020-2022). The PR grew too large for the upstream trunk and was closed with the suggestion that it become a *"child project of GeneticSharp"*. MetaGeneticSharp is that child project: it consumes GeneticSharp as a vanilla, unpatched submodule (pinned at v3.1.4) and ports the PR's metaheuristics layer on top of it.

## Architecture

Metaheuristics address individuals by **stable index** across evolution stages, while the stock `GeneticAlgorithm`/`Population` sort generations by fitness as a side effect — the trunk changes this forced were a large part of what doomed PR #87. Rather than patching upstream, MetaGeneticSharp ships its own engine over the unmodified library:

| Component | Role |
|-----------|------|
| `MetaGeneticAlgorithm` | Autonomous evolution engine (`IGeneticAlgorithm`): metaheuristic-driven loop, offspring-scoped fitness evaluation, no implicit fitness sort |
| `MetaPopulation` | Order-preserving population (`IMetaPopulation : IPopulation`) with a parameter store for evolution-context caching |
| `IMetaHeuristic` + primitives | Composable units (`Container`, `Scoped`, `NoOp`, `Default`, ...) intercepting each evolution stage |
| `IEvolutionContext` | Per-population / per-individual context: stable indices, stage products, scoped parameters |
| Fluent grammar | Declarative composition of primitives into complete algorithms (see [ROADMAP.md](ROADMAP.md)) |

GeneticSharp's operator catalog (selections, crossovers, mutations, terminations, randomization) is consumed as-is through its public interfaces.

## Structure

```
MetaGeneticSharp/
  src/
    MetaGeneticSharp.Domain/          # Core metaheuristic engine + primitives
    MetaGeneticSharp.Extensions/      # TSP, Sudoku, benchmark functions
    MetaGeneticSharp.Infrastructure/  # Utility framework
  tests/
  notebooks/                          # .NET Interactive pedagogical notebooks
  GeneticSharp/                       # Upstream submodule (vanilla, v3.1.4)
```

## Building

```bash
git clone --recurse-submodules https://github.com/jsboige/MetaGeneticSharp.git
cd MetaGeneticSharp
dotnet build
dotnet test
```

## References

- Van Thieu, N., Mirjalili, S. (2023). "MEALPY: An open-source library for latest meta-heuristic algorithms in Python." *Journal of Systems Architecture*, 139. [GitHub](https://github.com/thieu1995/mealpy)
- Sörensen, K. (2015). "Metaheuristics — the metaphor exposed." *International Transactions in Operational Research*, 22(1). [DOI](https://doi.org/10.1111/itor.12001)
- Moraglio, A. (2007). "Towards a Geometric Unification of Evolutionary Algorithms." PhD thesis, University of Essex.
- Campelo, F., Aranha, C. "EC-Bestiary." [GitHub](https://github.com/fcampelo/EC-Bestiary)

## License

MIT (consistent with GeneticSharp upstream)

## Links

- Upstream: [giacomelli/GeneticSharp](https://github.com/giacomelli/GeneticSharp)
- Original PR: [giacomelli/GeneticSharp#87](https://github.com/giacomelli/GeneticSharp/pull/87)
- Consumed by: [CoursIA](https://github.com/jsboige/CoursIA)
