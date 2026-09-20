# Upstream nugget 1 — TplOperatorsStrategy offspring ordering (qualification)

Statut : QUALIFIE (bug confirme firsthand au master upstream) — PR upstream pas encore ouverte.
Scope : UNE tiny PR, zero spillover (lecon PR #87 : le trunk spillover tue la PR).

## Bug

`giacomelli/GeneticSharp`, `src/GeneticSharp.Domain/OperatorsStrategy/TplOperatorsStrategy.cs`,
master `4406ad713bb551be1508a0a9989979fd35048388` (fetch 2026-09-20) :

```csharp
var offspring = new ConcurrentBag<IChromosome>();
Parallel.ForEach(...parents indexes..., i => {
    var children = SelectParentsAndCross(...);
    foreach (var item in children) offspring.Add(item);
});
return offspring.ToList();
```

`ConcurrentBag<T>` ne preserve AUCUN ordre global : `ToList()` rend les offspring dans un
ordre qui depend du scheduling des threads (sacs thread-locaux). Pour les memes parents et le
meme RNG, deux runs peuvent rendre des listes d'offspring ordonnees differemment.

Consequences pour un utilisateur upstream :
- reproductibilite : un run seeder ne se rejoue pas identiquement (debug, tests, benchmarks) ;
- lisibilite debug : la i-eme offspring ne correspond pas aux parents (i, i+1, ...) annonces ;
- tout consommateur qui suppose l'index-stabilite (seeded benchmarks, golden tests) est casse.

## Verification concurrentielle (2026-09-20)

Issues ouvertes upstream (10 plus recentes) : aucune sur TplOperatorsStrategy / ordonnancement /
determinisme. PRs ouvertes : #136 (IFitness collection), #144 (IAsyncFitness), #145 (.Net 8) —
aucune concurrente. Le nugget est libre.

## Correctif candidat (pattern deja prouve dans notre fork)

Notre `src/MetaGeneticSharp.Domain/OperatorsStrategies/TplMetaOperatorsStrategy.cs` applique
depuis v0.1.0 le pattern index-stable :

```csharp
var offspring = new ConcurrentDictionary<int, IList<IChromosome>>();
Parallel.ForEach(..., i => { offspring[i] = SelectParentsAndCross(...); });
return offspring.OrderBy(pair => pair.Key)
               .Where(pair => pair.Value != null)
               .SelectMany(pair => pair.Value)
               .ToList();
```

Patch upstream minimal (meme signature, meme base class) : remplacer le ConcurrentBag par la
collecte par index + OrderBy(key) + SelectMany, null-filter inclus (upstream skippe deja les
children null via le if). ~10 lignes, un seul fichier, aucun changement d'API.

## Test de non-regression propose (dans la PR upstream)

Test xunit bornant la probabilite de faux positif : population large (>= 1000 parents indexes),
crossover deterministe (one-point), RNG seed fixe ; assert que l'ordre des offspring EST stable
sur N repetitions. Le test doit verifier la PROPRIETE (stabilite de l'ordre), pas un ordre
specifique — evite le couplage au contenu.

## Pre-requis de publication (coordination ai-01)

- fork personnel ou MyIntelligenceAgency/GeneticSharp : reutiliser l'org du fork existant ;
- branch `fix/tpl-offspring-ordering` depuis master propre (PAS depuis la branch Metaheuristics) ;
- body en anglais, reference a la reproductibilite, pas de mention du projet interne ;
- checklist no-spillover : 1 fichier src + 1 fichier test, rien d'autre.
