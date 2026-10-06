// Hot paths vises (digest Toub .NET 11) : boucles d'evaluation fitness, selection du meilleur
// (comparaisons Min/Max), allocations par evaluation (GC write barriers / boxings), et la
// double-course centered/shifted de CenterBiasBenchmark. Tout passe par l'API publique.
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Toolchains.InProcess.Emit;
using GeneticSharp;
using MetaGeneticSharp;

namespace MetaGeneticSharp.Benchmarks;

// InProcess : evite le spawn d'un process enfant par benchmark (bloque par Defender
// sur certains postes Windows — cf. warning BDN), et garde la mesure dans le meme runtime.
[MemoryDiagnoser]
[Config(typeof(InProcessShortConfig))]
public class OptimizerBenchmarks
{
    private class InProcessShortConfig : ManualConfig
    {
        public InProcessShortConfig()
            => AddJob(Job.ShortRun
                .WithToolchain(InProcessEmitToolchain.Instance)
                .WithInvocationCount(1)
                .WithUnrollFactor(1)
                .WithWarmupCount(5)
                .WithIterationCount(15));
    }

    private const int Seed = 7;

    // --- Boucle random search complete : evaluations + tracking du meilleur (2D) ---
    [Benchmark(Baseline = true)]
    public double RandomSearch_Ackley2D_10k()
        => new RandomSearchOptimizer(Seed).Run(
            new OptimizerRequest(new AckleyFitness(), (-32.768, 32.768), 2, 10_000));

    // --- Meme boucle, dimension superieure : plus d'allocations de gene par evaluation ---
    [Benchmark]
    public double RandomSearch_Sphere10D_50k()
        => new RandomSearchOptimizer(Seed).Run(
            new OptimizerRequest(new SphereFitness(), (-5.12, 5.12), 10, 50_000));

    // --- CenterBiasBenchmark.Run : deux courses (centered + shifted) sous budget partage ---
    [Benchmark]
    public CenterBiasResult CenterBias_Ackley_5k()
        => CenterBiasBenchmark.Run(
            new AckleyFitness(), 2, new EvaluationBudget(5_000),
            request => new RandomSearchOptimizer(Seed).Run(request),
            shiftMagnitude: 0.5, seed: Seed);

    // --- Suite multi-fonctions : le chemin complet consomme par les carnets de la serie ---
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
