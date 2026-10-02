# Baseline .NET 9.0 — MetaGeneticSharp.Benchmarks

Mesures de référence de l'API publique de MetaGeneticSharp sur .NET 9, point de
départ des comparaisons .NET 10 / .NET 11 (EPIC gains runtime — CoursIA issues
18770 et 18695, digest Stephen Toub 2026-09-15).

## Environnement

| Élément | Valeur |
| --- | --- |
| Date | 2026-10-02 |
| OS | Windows 11 Pro (10.0.26300) |
| Runtime | .NET 9.0.20 (9.0.2026.41315), X64 RyuJIT AVX2 |
| SDK (build) | 10.0.204 |
| BenchmarkDotNet | 0.14.0 |
| Toolchain | InProcessEmitToolchain, `InvocationCount=1`, `UnrollFactor=1`, 5 warmup / 15 itérations |
| Machine | poste de travail portable (laptop), alimentation secteur |

## Charges mesurées

Toutes passent par l'API publique (`RandomSearchOptimizer`, `CenterBiasBenchmark`),
seed fixe 7, budgets d'évaluation pédagogiques — aucune I/O, aucun réseau :

| Benchmark | Charge |
| --- | --- |
| `RandomSearch_Ackley2D_10k` (baseline) | random search complet, Ackley 2D, 10 000 évaluations |
| `RandomSearch_Sphere10D_50k` | random search, Sphere 10D, 50 000 évaluations |
| `CenterBias_Ackley_5k` | `CenterBiasBenchmark.Run`, Ackley 2D, budget 5 000 (double course centered/shifted) |
| `CenterBias_Suite4_5k` | `RunSuite` sur Ackley 2D, Booth 2D, Sphere 5D, DixonPrice 5D — budget 5 000 chacun |

## Résultats

| Method | Mean | Error | StdDev | Ratio | Gen0 | Allocated | Alloc Ratio |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| RandomSearch_Ackley2D_10k | 2.146 ms | 0.3584 ms | 0.3177 ms | 1.02 | - | 2.14 MB | 1.00 |
| RandomSearch_Sphere10D_50k | 7.757 ms | 1.0508 ms | 0.9829 ms | 3.69 | 2000.0000 | 25.94 MB | 12.13 |
| CenterBias_Ackley_5k | 1.928 ms | 0.2019 ms | 0.1888 ms | 0.92 | - | 3.63 MB | 1.70 |
| CenterBias_Suite4_5k | 8.520 ms | 1.2260 ms | 1.0868 ms | 4.05 | 1000.0000 | 18.85 MB | 8.81 |

Lecture hot-path (axes Toub visés) :

- **Allocations par évaluation** : Sphere 10D alloue ~25,94 MB pour 50 000 évaluations,
  soit ~540 o/évaluation (chromosome + gènes par tirage) — c'est le levier principal
  qu'une runtime avec write barriers / allocations moins chères peut améliorer.
- **GC pression** : Gen0 = 2000/1000 collectes pour 1000 opérations sur les deux
  charges 50 k / suite — la boucle fitness est allocation-bound.
- **Scaling dimension** : 5x évaluations + 5x dimensions (2D→10D) ≈ 3,6x temps
  (7,76 ms vs 2,15 ms) — Sphere coûte moins cher par évaluation qu'Ackley
  (pas de cosinus/exp), le coût marginal par dimension reste ~linéaire.

## Protocole / reproduction

```bash
cd benchmarks/MetaGeneticSharp.Benchmarks
dotnet run -c Release -- --filter '*'
```

- Configuration Release, InProcess (évite le spawn d'un process enfant par
  benchmark, bloqué par Windows Defender sur le poste de mesure).
- Seed 7 fixe : mêmes opérandes à chaque itération, résultats reproductibles
  à la variance du scheduler près.
- BDN a retiré 1 outlier (3,57 ms) sur Ackley2D_10k et 1 (14,64 ms) sur Suite4_5k.

## Limites connues (honnêteté des mesures)

- `MinIterationTime` : chaque itération dure 1,7–7,4 ms (< 100 ms recommandé par
  BDN). Les budgets sont volontairement pédagogiques (ceux des carnets de la
  série) ; grossir les budgets changerait la sémantique de la charge mesurée.
- Error ≈ 10–17 % du Mean : poste partagé (GUI active), ShortRun borné. Suffisant
  pour détecter un delta runtime majeur (.NET 9 → 11) ; ne pas trancher un delta
  < 20 % sur ces chiffres seuls.
- Une seule machine ; la comparaison .NET 10/11 (#18771) devra rejouer sur le
  MÊME poste, même toolchain, pour être comparable.
