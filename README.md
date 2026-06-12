# MetaGeneticSharp

Component-based metaheuristics framework built on [GeneticSharp](https://github.com/giacomelli/GeneticSharp) — **primitives over bestiary** (cf. Sorensen 2015, "Metaheuristics — the metaphor exposed").

## Origin

This project continues the work from [PR #87](https://github.com/giacomelli/GeneticSharp/pull/87) (MyIntelligenceAgency/GeneticSharp, 2020-2022), which was closed by @giacomelli with the suggestion to create a child project:

> *"Maybe this PR could be a 'child' project of GeneticSharp, like 'GeneticSharp-Contributions' or something like that."*

MetaGeneticSharp implements that vision: a separate library consuming GeneticSharp as a submodule, adding metaheuristic primitives, compound heuristics, and a fluent API for composing optimization strategies.

## Philosophy

Following the critique by @ktnr (operations research, 2020-11-20) and the [EC-Bestiary](https://github.com/fcampelo/EC-Bestiary):

- **Primitives, not metaphors**: `EukaryoteMetaHeuristic`, `IslandMetaHeuristic`, `SizeBasedMetaHeuristic`, `SwitchMetaHeuristic` — composable building blocks
- **Compound heuristics from primitives**: Whale Optimization Algorithm, Equilibrium Optimizer reconstructed from primitives (deconstruction pedagogique)
- **Fluent API with Lambda Expression visitors**: Performance-optimized parameter binding via expression tree fusion

## Structure

```
MetaGeneticSharp/
  src/
    MetaGeneticSharp.Domain/          # Core metaheuristic engine
    MetaGeneticSharp.Extensions/      # TSP, Sudoku, Benchmark functions
    MetaGeneticSharp.Infrastructure/  # Utility framework (from PR #87)
  tests/
    MetaGeneticSharp.Domain.Tests/
    MetaGeneticSharp.Extensions.Tests/
    MetaGeneticSharp.Infrastructure.Tests/
  notebooks/                          # .NET Interactive pedagogical notebooks
  GeneticSharp/                       # Upstream submodule
```

## Building

```bash
git clone --recurse-submodules https://github.com/jsboige/MetaGeneticSharp.git
cd MetaGeneticSharp
dotnet build
dotnet test
```

## References

- Sorensen, K. (2015). "Metaheuristics — the metaphor exposed." *International Transactions in Operational Research*, 22(1), 3-18. [DOI](https://doi.org/10.1111/itor.12001)
- Ruiz, R., Stutzle, T. (2007). "A simple and effective iterated greedy algorithm for the permutation flowshop scheduling problem." *European Journal of Operational Research*, 177(3), 2033-2049. [DOI](https://doi.org/10.1016/j.ejor.2005.12.009)
- Moraglio, A. (2007). "Towards a Geometric Unification of Evolutionary Algorithms." PhD thesis, University of Essex.
- Campelo, F., Aranha, C. "EC-Bestiary." [GitHub](https://github.com/fcampelo/EC-Bestiary)

## License

MIT (consistent with GeneticSharp upstream)

## Links

- Upstream: [giacomelli/GeneticSharp](https://github.com/giacomelli/GeneticSharp)
- Original PR: [giacomelli/GeneticSharp#87](https://github.com/giacomelli/GeneticSharp/pull/87)
- Consumed by: [CoursIA Search Part4](https://github.com/jsboige/CoursIA) (submodule)
