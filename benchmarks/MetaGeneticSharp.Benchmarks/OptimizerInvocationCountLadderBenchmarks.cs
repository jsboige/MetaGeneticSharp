using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Toolchains.InProcess.Emit;
using GeneticSharp;
using MetaGeneticSharp;

namespace MetaGeneticSharp.Benchmarks;

/// <summary>
/// Échelle de comptage d'invocations : les mêmes charges, appelées N fois dans une même
/// région mesurée, pour N = 1 / 8 / 64.
///
/// <para><b>Ce que cet instrument est, et ce qu'il n'est pas.</b> Il est <b>complémentaire</b>
/// de <see cref="OptimizerBenchmarks"/> : celui-ci utilise <c>InvocationCount=1</c> (une
/// invocation par itération de mesure), le présent instrument fait <c>InvocationCount=N</c>
/// (N appels distincts dans une même région mesurée, temps total divisé par N). Les deux ne
/// mesurent pas la même chose : si la région mesurée porte un coût fixe <c>c</c> (le
/// harnais) indépendant du nombre d'appels, le régime N = 1 mesure <c>w + c</c> et le
/// régime N = 64 mesure <c>w + c/64</c>.</para>
///
/// <para><b>Pourquoi cette échelle ici.</b> La campagne runtime-delta du dépôt a établi
/// (CoursIA <c>docs/dotnet/net11-runtime-deltas.md</c>, section « Le régime à N croissant »)
/// que, sur un banc dont le coût par appel est de l'ordre de la microseconde, le régime
/// épinglé <c>InvocationCount(1)</c> <b>sur-estime</b> le coût par opération d'un facteur
/// 3 à 55 — une part par coût fixe de la région mesurée (<c>c/N</c>), une part par le
/// tiering du JIT. Ce banc est le contre-exemple annoncé : ses appels coûtent 2 à 8,5 ms
/// (10 000 à 50 000 évaluations de fitness), soit mille fois plus.</para>
///
/// <para><b>Ce que la mesure a donné (2026-11-10).</b> Les résultats et leur protocole
/// sont dans <c>benchmarks/INVOCATION-COUNT-TIERING-META.md</c> ; ils sont résumés ici
/// parce qu'ils changent la lecture qu'on peut faire du régime épinglé :
/// <list type="bullet">
/// <item>À partir de N = 8, le coût par appel <b>ne bouge plus</b> : il n'y a donc
/// <b>aucun coût fixe de harnais</b> à amortir sur ce banc, et le facteur « ×3 à ×55 »
/// <b>ne s'y généralise pas</b>.</item>
/// <item>L'écart du régime N = 1 n'est pas un coût de harnais : le profil
/// <b>par itération</b> (lignes <c>WorkloadResult</c> du journal BDN) montre une
/// <b>marche</b> — le passage du code non optimisé (tier0) au code optimisé (tier1) —
/// qui tombe <i>à l'intérieur</i> de la fenêtre mesurée. La moyenne du régime N = 1
/// est donc un <b>mélange</b> de deux régimes, dont la part dépend de l'endroit où la
/// marche tombe.</item>
/// <item>Rapport N = 1 / N = 64 : ×2,40 (Ackley2D), ×1,63 (Sphere 10D), ×1,30
/// (Suite4), ×1,21 (CenterBias Ackley).</item>
/// </list>
/// Le mécanisme de la marche n'est pas isolé : aucune désactivation partielle du
/// tiering (seuil de promotion, délai de comptage, PGO dynamique) ne reproduit le
/// gain obtenu par <c>DOTNET_TieredCompilation=0</c>. Ce qui reste ouvert est écrit
/// dans la note.</para>
///
/// <para><b>Fidélité.</b> Les quatre charges, leurs seeds, leurs budgets d'évaluation et
/// leurs payloads sont <b>identiques</b> à ceux de <see cref="OptimizerBenchmarks"/> ; seul
/// le régime de comptage change. Les noms de classes sont préfixés <c>Ladder</c> pour que
/// les rapports BDN restent distinguables de ceux du banc de référence.</para>
/// </summary>
public abstract class OptimizerLadderBase
{
    private const int Seed = 7;

    [Benchmark(Baseline = true)]
    public double RandomSearch_Ackley2D_10k()
        => new RandomSearchOptimizer(Seed).Run(
            new OptimizerRequest(new AckleyFitness(), (-32.768, 32.768), 2, 10_000));

    [Benchmark]
    public double RandomSearch_Sphere10D_50k()
        => new RandomSearchOptimizer(Seed).Run(
            new OptimizerRequest(new SphereFitness(), (-5.12, 5.12), 10, 50_000));

    [Benchmark]
    public CenterBiasResult CenterBias_Ackley_5k()
        => CenterBiasBenchmark.Run(
            new AckleyFitness(), 2, new EvaluationBudget(5_000),
            request => new RandomSearchOptimizer(Seed).Run(request),
            shiftMagnitude: 0.5, seed: Seed);

    [Benchmark]
    public int CenterBias_Suite4_5k()
        => CenterBiasBenchmark.RunSuite(
            new (IFitness, int)[]
            {
                (new AckleyFitness(), 2),
                (new BoothFitness(), 2),
                (new SphereFitness(), 5),
                (new DixonPriceFitness(), 5),
            },
            new EvaluationBudget(5_000),
            request => new RandomSearchOptimizer(Seed).Run(request),
            shiftMagnitude: 0.5, seed: Seed).Count;
}

/// <summary>Référence : 1 invocation par itération (régime du banc de référence).</summary>
[MemoryDiagnoser]
[Config(typeof(LadderConfig1))]
public class Optimizer_Ladder_N1 : OptimizerLadderBase
{
}

/// <summary>Régime intermédiaire : 8 invocations par itération.</summary>
[MemoryDiagnoser]
[Config(typeof(LadderConfig8))]
public class Optimizer_Ladder_N8 : OptimizerLadderBase
{
}

/// <summary>Régime long : 64 invocations par itération.</summary>
[MemoryDiagnoser]
[Config(typeof(LadderConfig64))]
public class Optimizer_Ladder_N64 : OptimizerLadderBase
{
}

/// <summary>
/// Configuration in-process : ShortRun du dépôt, avec un nombre d'invocations par
/// itération paramétrable. Le réglage UnrollFactor=1 reste celui du protocole de
/// référence (même contrainte antivirus documentée dans le banc d'origine).
/// </summary>
public abstract class LadderConfigBase : ManualConfig
{
    protected LadderConfigBase(int invocationCount)
    {
        AddJob(Job.ShortRun
            .WithToolchain(InProcessEmitToolchain.Instance)
            .WithInvocationCount(invocationCount)
            .WithUnrollFactor(1)
            .WithWarmupCount(5)
            .WithIterationCount(15));
    }
}

public class LadderConfig1 : LadderConfigBase
{
    public LadderConfig1() : base(1) { }
}

public class LadderConfig8 : LadderConfigBase
{
    public LadderConfig8() : base(8) { }
}

public class LadderConfig64 : LadderConfigBase
{
    public LadderConfig64() : base(64) { }
}
