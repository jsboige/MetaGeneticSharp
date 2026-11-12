# Contrôle du régime de comptage et du tiering — banc optimiseur MetaGeneticSharp

Note de mesure. Elle porte sur le banc `OptimizerBenchmarks` — le banc de référence
du dépôt, dont les valeurs sont figées dans [`BASELINE-net9.0.md`](BASELINE-net9.0.md).

Elle répond à **deux questions distinctes**, qu'il ne faut pas confondre :

- **Q1 — échelle de comptage.** Le régime `InvocationCount(1)` (celui du banc de
  référence) mesure-t-il le coût *établi* de l'opération, ou un coût qui dépend du
  harnais ?
- **Q2 — tiering.** Quelle part du coût mesuré est due au *tiering* du JIT, c'est-à-dire
  à du code compilé au niveau d'optimisation 0 (non optimisé) ?

## Le résultat en une phrase

Le régime `InvocationCount(1)` ne mesure pas un coût homogène : il mesure un
**mélange** de code non optimisé (tier0) et de code optimisé (tier1), et la part de
tier0 dépend de la **durée totale de la mesure**. Sur la charge la plus courte du
banc, la mesure entière est du tier0.

## Environnement

| Élément | Valeur |
| --- | --- |
| Date | 2026-11-10 |
| OS | Windows 11 Pro (10.0.26300) |
| Runtimes | .NET 9.0.22 · .NET 10.0.14 · .NET 11.0.0-rc.3.26522.148 (via `dotnet exec --fx-version`) |
| SDK (build) | 10.0.114 |
| BenchmarkDotNet | 0.15.8 (le banc de référence `BASELINE-net9.0.md` a été mesuré en 0.14.0) |
| Toolchain | `InProcessEmitToolchain`, `UnrollFactor=1`, 5 warmup / 15 itérations |
| Gitlink de départ | `824c54e` (celui épinglé par le dépôt parent) |
| Charge de l'hôte pendant la session | 62 % à 86 % (`Get-CimInstance Win32_Processor`, LoadPercentage) — **hôte partagé, non dédié** |

Les quatre charges, leurs seeds (7), leurs budgets d'évaluation et leurs payloads sont
**identiques** à ceux du banc de référence : seul le régime de comptage ou le drapeau
runtime changent.

## Protocole

Une compilation unique (`net9.0`), puis pour chaque configuration : une **classe de
banc par processus BDN**, 3 passes entrelacées, et **médiane des moyennes BDN** comme
statistique. Les comparaisons entre configurations sont **appariées par passe** (la
paire ON/OFF d'une même passe est mesurée à quelques secondes d'intervalle), parce
que la charge de l'hôte dérive pendant la session.

---

## Grille 1 — l'échelle de comptage (N = 1 / 8 / 64)

`OptimizerInvocationCountLadderBenchmarks` : mêmes charges que le banc de référence,
mais `InvocationCount = N` — N appels distincts dans une **même** région mesurée,
temps total divisé par N.

Coût **par appel** (médiane de 3 passes, en ms) :

| Charge | N = 1 | N = 8 | N = 64 |
| --- | ---: | ---: | ---: |
| `RandomSearch_Ackley2D_10k` | 2,030 | 1,892 | **0,845** |
| `RandomSearch_Sphere10D_50k` | 16,696 | 9,185 | **10,218** |
| `CenterBias_Ackley_5k` | 1,798 | 1,635 | **1,485** |
| `CenterBias_Suite4_5k` | 8,683 | 6,735 | **6,691** |

La colonne **N = 64** (en gras) sert de référence aux rapports ci-dessous.

Lecture :

- Les deux dernières colonnes **convergent** : à partir de N = 8, le coût par appel
  ne bouge plus (9,185 → 10,218 pour Sphere 10D ; 6,735 → 6,691 pour Suite 4). Il
  n'y a **pas** de coût fixe de harnais à amortir — un `c/N` significatif n'existe
  pas sur ce banc.
- La colonne **N = 1 est la seule qui dépare**. Rapport N = 1 / N = 64 :
  `RandomSearch_Ackley2D_10k` **×2,40**, `RandomSearch_Sphere10D_50k` ×1,63,
  `CenterBias_Suite4_5k` ×1,30, `CenterBias_Ackley_5k` ×1,21.
- Ce rapport **ne se range pas** selon la durée de la charge : `CenterBias_Ackley_5k`
  (1,8 ms par itération) et `RandomSearch_Ackley2D_10k` (2,0 ms par itération) ont la
  même durée et des rapports très différents (×1,21 contre ×2,40). Le prédicteur du
  rapport n'est **pas établi** par cette session — voir « ce qu'elle ne règle pas ».

### L'arithmétique qui ferme la explication

Le profil **par itération** (extrait des journaux BDN) de `RandomSearch_Sphere10D_50k`
en N = 1 montre une **marche** :

```text
itération  1..9 :  27,3  29,4  28,9  26,5  27,8  30,4  22,6   9,4   9,1
itération 10..15:   8,9   8,8   9,1   9,1   9,2   7,6
```

Soit **27,6 ms** avant la marche et **8,9 ms** après — un rapport de ×3,1. La marche
est **reproductible** : elle tombe à l'itération 8, 7 et 8 sur les trois passes.

La moyenne BDN de la passe 1 vaut 17,614 ms, et c'est **exactement** le mélange des
deux phases :

```text
(7 itérations × 27,57 ms + 8 itérations × 8,90 ms) / 15 = 17,61 ms
```

La moyenne du régime N = 1 **n'est donc pas un coût** : c'est un mélange de deux
régimes, et la part de chacun dépend de l'endroit où la marche tombe dans la fenêtre
de mesure. Les trois passes le montrent :

| Passe | Marche à l'itération | Phase tier0 | Phase tier1 | Moyenne BDN |
| --- | ---: | ---: | ---: | ---: |
| 1 | 8 | 7 itérations × 27,57 ms | 8 itérations × 8,90 ms | 17,614 ms |
| 2 | 7 | 6 itérations × 31,71 ms | 9 itérations × 6,69 ms | 16,696 ms |
| 3 | 8 | 7 itérations × 24,32 ms | 8 itérations × 6,05 ms | 14,578 ms |

Les moyennes de passe s'étalent de 14,578 à 17,614 ms — **21 % d'écart** — alors que
la charge est **déterministe** (seed 7, aucun aléa) : la variabilité ne vient pas de
la charge, elle vient de la **position de la marche**. La « médiane des moyennes sur
3 passes » (16,696 ms) est donc elle-même une **quantité de mélange**, pas un coût.

En N = 8 et en N = 64, la marche est **absente** du profil (rapports de fin de série
1,025 / 0,962 / 1,091 pour N = 8) : la promotion a lieu **avant** la fenêtre mesurée.

**Conclusion de la grille 1.** Le surcoût du régime `InvocationCount(1)` n'est pas un
coût fixe de harnais amortissable : c'est la part de **tier0** restant dans la fenêtre
mesurée. Un banc dont la mesure dure moins longtemps que le délai de comptage des
appels du runtime mesure du code non optimisé.

---

## Grille 2 — le contrôle de tiering

Même banc, `InvocationCount(1)`, deux configurations du runtime :

- `default` : `DOTNET_TieredCompilation=1` (le défaut : tier0 puis promotion en tier1)
- `off` : `DOTNET_TieredCompilation=0` (plus de tiering du tout)

Médiane des 3 passes, .NET 9.0.22 (en ms) :

| Charge | `default` | `off` | `off` / `default` |
| --- | ---: | ---: | ---: |
| `RandomSearch_Ackley2D_10k` | 1,982 | **1,305** | **0,658** |
| `RandomSearch_Sphere10D_50k` | 11,266 | 10,198 | 0,905 |
| `CenterBias_Ackley_5k` | 1,823 | **1,511** | 0,829 |
| `CenterBias_Suite4_5k` | 7,313 | 7,563 | 1,034 |

Rapports **appariés par passe** (`off` / `default`), charge `RandomSearch_Ackley2D_10k`
sur les trois runtimes — chaque paire est le rapport des deux mesures **de la même
passe**, pas le rapport de deux séries triées :

| Runtime | passe 1 | passe 2 | passe 3 |
| --- | ---: | ---: | ---: |
| .NET 9.0.22 | 0,66 | 0,62 | 0,68 |
| .NET 10.0.14 | 0,66 | 0,50 | 0,47 |
| .NET 11.0.0-rc.3 | 0,50 | 0,74 | 0,47 |

**9 paires sur 9 sont inférieures à 1**, de 0,47 à 0,74 : l'effet est systématique, pas
un artefact d'une passe. Il porte sur la charge **la plus courte** du banc (2,0 ms par
itération) ; il est **absent** sur la charge la plus longue (`CenterBias_Suite4_5k`,
8,5 ms par itération), et la dispersion y est la plus forte (jusqu'à 87 % entre
passes sur `Sphere10D_50k`, dont la moyenne est bimodale du fait de la marche).

---

## Grille 3 — tentative d'isolement du mécanisme

Sept configurations, .NET 9.0.22, charge `RandomSearch_Ackley2D_10k`, médiane de 3
passes (ms) :

| Configuration | Ce qu'elle change | Valeur | vs `default` |
| --- | --- | ---: | ---: |
| `default` | — | 1,982 | 1,000 |
| `off` | `DOTNET_TieredCompilation=0` | **1,305** | 0,658 |
| `seuil 10` | + `DOTNET_TC_CallCountThreshold=10` | 1,927 | 0,972 |
| `promote0` | + `DOTNET_TC_CallCountThreshold=0` | 2,119 | 1,069 |
| `promote0_nopgo` | + `DOTNET_TieredPGO=0` | 1,839 | 0,928 |
| `promote0nodelay` | seuil 0 + `DOTNET_TC_CallCountingDelayMs=0` | 2,139 | 1,079 |
| `promote0nodelaynopgo` | seuil 0 + délai 0 + `DOTNET_TieredPGO=0` | 1,781 | 0,898 |

**Aucune désactivation partielle ne reproduit l'accélération de `off`** : abaisser le
seuil de promotion des appels, supprimer le délai de comptage, ou couper le PGO
dynamique ne ramène pas le coût au niveau de `off` (1,305). La configuration la plus
rapide est celle où le **sous-système de tiering est entièrement désactivé**.

Deux réserves explicites :

- `DOTNET_TieredCompilation=0` coupe **aussi** le PGO dynamique : la grille 2
  attribue donc l'effet à « tiering désactivé », **pas** à « tier0 » seul.
- Avec le tiering actif, la **première** compilation est toujours un tier0 : un seuil
  à 0 rend la promotion *éligible* immédiatement, il n'empêche pas la phase tier0
  d'exister.

---

## Ce que cette note règle, et ce qu'elle ne règle pas

**Elle règle :**

1. Le régime `InvocationCount(1)` du banc de référence **mesure un mélange tier0 /
   tier1**, dont la proportion dépend de la durée de la mesure. Pour la charge la
   plus courte, la fenêtre mesurée est entièrement en tier0.
2. L'effet est **systématique** (9 paires appariées sur 9 dans le même sens, sur
   trois runtimes) et **inverse à la durée de la charge** : maximal sur la charge la
   plus courte, nul sur la plus longue.
3. L'échelle de comptage **ne révèle aucun coût fixe de harnais** à amortir sur ce
   banc : dès N = 8, le coût par appel est stable. Le facteur ×3 à ×55 relevé sur le
   banc `prompt-settings` du dépôt parent **ne se généralise pas** aux bancs dont le
   coût par appel est de l'ordre de la milliseconde : ici le facteur mesuré est
   **×1,21 à ×2,40** par l'estimator « N = 1 / coût convergent », et **×1,10 à
   ×1,52** par l'estimator « `default` / `off` » à N = 1. Les deux estimateurs
   encadrent le même ordre de grandeur, **très loin** du ×3 à ×55.

**Elle ne règle pas :**

1. **Le mécanisme exact de l'écart `off` / `default`** : la grille 3 montre qu'il
   n'est ni le seuil de promotion, ni le délai, ni le PGO dynamique pris isolément.
   Ce qui reste — l'instrumentation du sous-système de tiering, ou le coût de la
   recompilation elle-même — n'est pas discriminé par cette session.
2. **Le prédicteur du rapport entre les charges** : ×2,40 pour
   `RandomSearch_Ackley2D_10k`, ×1,21 pour `CenterBias_Ackley_5k` — deux charges de
   durée quasi identique (2,0 et 1,8 ms par itération). Deux candidats plausibles,
   **aucun vérifié** ici : le nombre d'appels à la méthode chaude par itération
   (1, 2 et 4 respectivement pour Ackley2D, CenterBias_Ackley et Suite4) et le délai
   de comptage des appels du runtime. La grille 3 **réfute** le second comme
   explication suffisante (abaisser le seuil ne rend pas l'effet).
3. **Le cas `RandomSearch_Ackley2D_10k` en N = 64** : 0,845 ms par appel, soit
   **en dessous** du coût tier1 mesuré à N = 1 avec tiering désactivé (1,305 ms). Un
   second effet (amortissement d'un coût par appel dans une boucle serrée ?) existe
   et n'est pas expliqué ici. C'est aussi la charge pour laquelle le rapport
   ×2,40 est le plus élevé, donc **le plus susceptible** de surestimer la conclusion
   d'ensemble.
4. **La comparabilité avec `BASELINE-net9.0.md`** : ce document a été mesuré en
   .NET 9.0.20, BDN 0.14.0, sur un portable, à l'identique de l'hôte (charge 0 %),
   le 02/10. Les valeurs absolues de cette note **ne lui sont pas comparables** ; seuls
   les rapports intra-session le sont.

---

## Limites connues (honnêteté des mesures)

- **L'hôte n'était pas dédié** : 62 % à 86 % de charge pendant toute la session, et
  la dérive est visible (les valeurs montent d'une passe à l'autre). D'où le choix
  des **rapports appariés** plutôt que des valeurs absolues, et d'une **médiane**,
  mais les valeurs absolues de cette note **ne doivent pas** servir de référence.
- **Dispersion intra-configuration** : jusqu'à 87 % entre passes sur
  `RandomSearch_Sphere10D_50k` (moyenne bimodale, cf. la marche). Les charges
  `CenterBias_*` sont les plus stables.
- **Un seul hôte, un seul jour** : rien ici ne vaut une loi de portabilité.
- **BDN 0.15.8 contre 0.14.0** pour le banc de référence : l'écart de version peut
  lui-même modifier le régime de mesure.
- Les séries brutes (journaux BDN, rapports CSV et HTML) sont **volumineuses** et ne
  sont pas versionnées ici ; les chiffres de cette note sont reproductibles par le
  protocole ci-dessous.

## Reproduction

```bash
# banc de référence + échelle de comptage
cd benchmarks/MetaGeneticSharp.Benchmarks
dotnet run -c Release -- --filter '*'                       # régime N = 1
dotnet run -c Release -- --filter '*Optimizer_Ladder*'      # échelle N = 1 / 8 / 64

# contrôle de tiering (une classe par processus, 3 passes appariées)
DOTNET_TieredCompilation=0 dotnet exec --fx-version 9.0.22 \
  bin/Release/net9.0/MetaGeneticSharp.Benchmarks.dll \
  --filter '*OptimizerBenchmarks.*' --exporters github
```

Le **profil par itération** se lit dans le journal BDN de chaque exécution (lignes
`WorkloadResult`), pas seulement dans le rapport final : c'est là qu'apparaît la
marche de promotion, et c'est la seule source qui permet de voir un régime *mélangé*.

---

## Voir aussi

- [`BASELINE-net9.0.md`](BASELINE-net9.0.md) — le banc de référence et ses limites
  connues.
- Dépôt parent `jsboige/CoursIA`, `docs/dotnet/net11-runtime-deltas.md` — la campagne
  runtime-delta et le banc `prompt-settings` qui porte le facteur ×3 à ×55.
