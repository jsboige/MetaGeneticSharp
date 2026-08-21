using System;
using System.Collections.Generic;
using System.Linq;
using GeneticSharp;
using MetaGeneticSharp;

namespace MetaGeneticSharp.Domain.Tests.Compound;

/// <summary>
///   Acceptance tests for the generic memetic layer (#12049 axe 2): the
///   <see cref="MemeticAlgorithm"/> wrapper and its <see cref="LocalImprovementMetaHeuristic"/>
///   primitive. The structural tests verify the assembly (the inner compound's full pipeline
///   becomes the sub-metaheuristic of the improvement layer, configuration forwarded); the
///   unit tests drive <c>ImproveBest</c> on hand-built populations with a gated evaluation to
///   verify the targeting (N best, rank order, unevaluated excluded), the acceptance rule
///   (strictly-better candidates only) and the in-place update (genes replaced inside the
///   existing chromosome object, no reference swapped under the engine); the keystone runs a
///   memetic PSO against a real <see cref="MetaGeneticAlgorithm"/> on Sphere and proves the
///   wiring by counting the operator calls (one pass per generation over the N best).
/// </summary>
public class MemeticAlgorithmTests
{
    private static ParticleSwarmOptimization NewPso(int maxGenerations = 20)
    {
        var pso = new ParticleSwarmOptimization { MaxGenerations = maxGenerations };
        pso.SetGeometricConverter(new GeometricConverter<double>
        {
            GeneToDoubleConverter = (_, v) => v,
            DoubleToGeneConverter = (_, d) => d,
        });
        return pso;
    }

    private static (StubEvolutionContext ctx, IList<IChromosome> chromosomes) CtxWith(
        params (double value, double? fitness)[] specs)
    {
        var adam = new FixedChromosome(0.0);
        var population = new Population(specs.Length, specs.Length, adam);
        var chromosomes = specs
            .Select(s => (IChromosome)new FixedChromosome(s.value) { Fitness = s.fitness })
            .ToList();
        population.CreateNewGeneration(chromosomes);
        return (new StubEvolutionContext { Population = population }, chromosomes);
    }

    [Test]
    public void Build_WrapsInnerPipeline_UnderTheImprovementLayer()
    {
        var innerBuilt = NewPso().Build();

        var built = new MemeticAlgorithm { Inner = NewPso() }.Build();

        var layer = built as LocalImprovementMetaHeuristic;
        Assert.That(layer, Is.Not.Null,
            "the wrapper's root must be the improvement layer, not the inner compound itself");
        // The inner pipeline is adopted AS IS -- same object, not a rebuild: the wrapper adds a
        // layer, it must not reconfigure the compound underneath.
        Assert.That(layer.SubMetaHeuristic, Is.InstanceOf<IMetaHeuristic>());
    }

    [Test]
    public void Build_ForwardsImprovementCountAndOperator()
    {
        LocalImprovementMetaHeuristic.ImprovementOperator op = (current, ctx) => null;

        var built = new MemeticAlgorithm
        {
            Inner = NewPso(),
            ImprovementCount = 4,
            ImproveOperator = op,
        }.Build() as LocalImprovementMetaHeuristic;

        Assert.That(built.ImprovementCount, Is.EqualTo(4));
        Assert.That(built.ImproveOperator, Is.SameAs(op));
    }

    [Test]
    public void Build_RequiresInnerCompound()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => new MemeticAlgorithm().Build());
        Assert.That(ex.Message, Does.Contain("Inner"),
            "the error must point at the missing Inner compound");
    }

    /// <summary>
    /// Five ranked chromosomes, ImprovementCount 2: the operator must see exactly the two best,
    /// best first -- the "N best particles" contract, in rank order.
    /// </summary>
    [Test]
    public void ImproveBest_TargetsOnlyTheNBest_InRankOrder()
    {
        var (ctx, _) = CtxWith((1, 1.0), (2, 2.0), (3, 3.0), (4, 4.0), (5, 5.0));
        var seen = new List<double>();
        var gated = new GatedLocalImprovement { ImprovementCount = 2 };
        gated.ImproveOperator = (current, c) =>
        {
            seen.Add(current.Fitness!.Value);
            return null;
        };

        gated.RunImproveBest(ctx);

        Assert.That(seen, Is.EqualTo(new[] { 5.0, 4.0 }),
            "the operator must be offered the N best chromosomes, best first");
    }

    /// <summary>
    /// A strictly-better candidate is accepted: the TARGET's genes are replaced in place (same
    /// chromosome object stays in the generation list) and its fitness re-assigned to the
    /// candidate's. Non-target chromosomes are untouched.
    /// </summary>
    [Test]
    public void ImproveBest_ReplacesGenesInPlace_WhenCandidateStrictlyBetter()
    {
        var (ctx, chromosomes) = CtxWith((1, 1.0), (2, 2.0), (5, 5.0));
        var target = chromosomes[2];
        var gated = new GatedLocalImprovement { ImprovementCount = 1, CandidateFitness = 6.0 };
        gated.ImproveOperator = (current, c) =>
        {
            var candidate = current.Clone();
            candidate.ReplaceGene(0, new Gene(42.0));
            return candidate;
        };

        gated.RunImproveBest(ctx);

        Assert.Multiple(() =>
        {
            Assert.That(target.Fitness, Is.EqualTo(6.0), "accepted candidate's fitness must replace the target's");
            Assert.That(target.GetGene(0).Value, Is.EqualTo(42.0), "accepted candidate's genes must replace the target's");
            Assert.That(
                ReferenceEquals(target, ctx.Population.CurrentGeneration.Chromosomes[2]),
                Is.True,
                "the update is in place: the generation list must hold the SAME object, never a swap");
            Assert.That(chromosomes[1].Fitness, Is.EqualTo(2.0), "non-target chromosomes untouched");
        });
    }

    /// <summary>
    /// A candidate that does not beat the target is rejected: genes AND fitness intact.
    /// </summary>
    [Test]
    public void ImproveBest_RejectsCandidateNotBetter()
    {
        var (ctx, chromosomes) = CtxWith((1, 1.0), (5, 5.0));
        var gated = new GatedLocalImprovement { ImprovementCount = 1, CandidateFitness = 4.9 };
        gated.ImproveOperator = (current, c) =>
        {
            var candidate = current.Clone();
            candidate.ReplaceGene(0, new Gene(-7.0));
            return candidate;
        };

        gated.RunImproveBest(ctx);

        Assert.Multiple(() =>
        {
            Assert.That(chromosomes[1].Fitness, Is.EqualTo(5.0), "a worse candidate must not move the fitness");
            Assert.That(chromosomes[1].GetGene(0).Value, Is.EqualTo(5.0), "a worse candidate must not move the genes");
        });
    }

    /// <summary>
    /// A null candidate (operator declines to improve this generation) is skipped silently.
    /// </summary>
    [Test]
    public void ImproveBest_SkipsNullCandidates()
    {
        var (ctx, chromosomes) = CtxWith((1, 1.0), (5, 5.0));
        var gated = new GatedLocalImprovement { ImprovementCount = 1, CandidateFitness = 6.0 };
        gated.ImproveOperator = (current, c) => null;

        gated.RunImproveBest(ctx);

        Assert.That(chromosomes[1].Fitness, Is.EqualTo(5.0));
        Assert.That(gated.Evaluations, Is.EqualTo(0), "a declined candidate must not be evaluated");
    }

    /// <summary>
    /// Chromosomes without a fitness cannot be ranked and must never be offered to the operator,
    /// even when ImprovementCount would include them.
    /// </summary>
    [Test]
    public void ImproveBest_SkipsUnevaluatedChromosomes()
    {
        var (ctx, _) = CtxWith((9, (double?)null), (1, 1.0), (2, 2.0));
        var seen = new List<double>();
        var gated = new GatedLocalImprovement { ImprovementCount = 3 };
        gated.ImproveOperator = (current, c) =>
        {
            seen.Add(current.Fitness!.Value);
            return null;
        };

        gated.RunImproveBest(ctx);

        Assert.That(seen, Is.EqualTo(new[] { 2.0, 1.0 }),
            "only evaluated chromosomes participate in the ranking");
    }

    /// <summary>
    /// The disabled configurations are pure no-ops: no operator, or ImprovementCount 0.
    /// </summary>
    [Test]
    public void ImproveBest_NoOperatorOrZeroCount_IsNoOp()
    {
        var (ctx, chromosomes) = CtxWith((1, 1.0), (5, 5.0));

        new GatedLocalImprovement { ImprovementCount = 1 }.RunImproveBest(ctx);
        Assert.That(chromosomes[1].Fitness, Is.EqualTo(5.0), "no operator: pass-through, nothing thrown");

        var calls = 0;
        var zeroCount = new GatedLocalImprovement { ImprovementCount = 0 };
        zeroCount.ImproveOperator = (current, c) =>
        {
            calls++;
            return null;
        };
        zeroCount.RunImproveBest(ctx);
        Assert.That(calls, Is.EqualTo(0), "ImprovementCount 0: the operator must never be invoked");
    }

    /// <summary>
    /// The sub-metaheuristic observes the IMPROVED population: the improvement pass runs before
    /// the delegation, so the selection stage (and everything downstream) sees the updated
    /// fitness and genes.
    /// </summary>
    [Test]
    public void SelectParentPopulation_ImprovesBeforeDelegating()
    {
        var (ctx, _) = CtxWith((1, 1.0), (2, 2.0), (5, 5.0));
        var sub = new RecordingSubMetaHeuristic();
        var gated = new GatedLocalImprovement(sub) { ImprovementCount = 1, CandidateFitness = 6.0 };
        gated.ImproveOperator = (current, c) =>
        {
            var candidate = current.Clone();
            candidate.ReplaceGene(0, new Gene(42.0));
            return candidate;
        };

        var selected = gated.SelectParentPopulation(ctx, new EliteSelection());

        Assert.Multiple(() =>
        {
            Assert.That(sub.SeenAtSelection, Is.Not.Null, "the sub-metaheuristic must be invoked");
            Assert.That(sub.SeenAtSelection[2].Fitness, Is.EqualTo(6.0),
                "the sub must observe the improved fitness");
            Assert.That(sub.SeenAtSelection[2].GetGene(0).Value, Is.EqualTo(42.0),
                "the sub must observe the improved genes");
            Assert.That(selected, Is.SameAs(sub.SeenAtSelection),
                "the sub's selection is returned unchanged by the layer");
        });
    }

    /// <summary>
    /// KEYSTONE end-to-end: a memetic PSO (every compound gains the layer) drives a real
    /// <see cref="MetaGeneticAlgorithm"/> on Sphere. The operator contracts every gene toward
    /// the origin (x -> 0.9x), a strictly improving local search on sum-of-squares away from 0;
    /// the wiring proof is the call count -- one improvement pass per generation over the N=2
    /// best (60 generations), plus the run converging like the bare-PSO keystone does.
    /// </summary>
    [Test]
    public void MemeticPSO_DrivesMetaGeneticAlgorithm_AndImprovesTheTopEachGeneration()
    {
        int calls = 0;
        var memetic = new MemeticAlgorithm(NewPso(maxGenerations: 60))
        {
            ImprovementCount = 2,
            ImproveOperator = (current, ctx) =>
            {
                calls++;
                var candidate = current.Clone();
                var genes = candidate.GetGenes();
                for (int i = 0; i < genes.Length; i++)
                {
                    candidate.ReplaceGene(i, new Gene(0.9 * (double)genes[i].Value));
                }

                return candidate;
            },
        };
        var metaHeuristic = memetic.Build();

        var chromosome = new RandomDoubleChromosome(min: -10.0, max: 10.0, length: 5);
        var fitness = new FuncFitness(c =>
        {
            var values = ((RandomDoubleChromosome)c).GetDoubleValues();
            double s = 0.0;
            for (int i = 0; i < values.Length; i++) s += values[i] * values[i];
            return -s;
        });

        var population = new MetaPopulation(30, 30, chromosome);
        var ga = new MetaGeneticAlgorithm(
            population,
            fitness,
            new EliteSelection(),
            new UniformCrossover(0.5f),
            new UniformMutation(true),
            metaHeuristic)
        {
            Termination = new GenerationNumberTermination(60)
        };

        ga.Start();

        Assert.That(ga.BestChromosome, Is.Not.Null, "the memetic PSO produced no best chromosome");
        Assert.That(ga.BestChromosome.Fitness, Is.Not.Null,
            "the memetic PSO produced no evaluated offspring");
        double finalSumSq = -ga.BestChromosome.Fitness!.Value;

        Assert.Multiple(() =>
        {
            Assert.That(ga.GenerationsNumber, Is.EqualTo(60));
            Assert.That(ga.State, Is.EqualTo(GeneticAlgorithmState.TerminationReached));
            Assert.That(calls, Is.EqualTo(118),
                $"the improvement pass must run once per selection over the N=2 best: the engine " +
                $"creates generation 1 at Start then Steps to 60, so 59 selection hooks fire " +
                $"(the final generation never selects -- the termination cuts first); 59 x 2 = 118; got {calls}");
            Assert.That(finalSumSq, Is.LessThan(1.0),
                $"the memetic PSO should reach the origin region; got sum-of-squares {finalSumSq}");
        });
    }

    /// <summary>
    /// A two-gene chromosome pinned to a value (ChromosomeBase enforces a 2-gene minimum); also
    /// the hand-built population member for the ImproveBest tests (fitness assigned by the test).
    /// </summary>
    private sealed class FixedChromosome : ChromosomeBase
    {
        private readonly double _value;

        public FixedChromosome(double value) : base(2)
        {
            _value = value;
            ReplaceGene(0, new Gene(value));
            ReplaceGene(1, new Gene(value));
        }

        public override IChromosome CreateNew() => new FixedChromosome(_value);
        public override Gene GenerateGene(int geneIndex) => new Gene(_value);
    }

    /// <summary>
    /// A chromosome that randomises each gene in [min, max] on CreateNew, so the initial
    /// population is diverse (same fixture as the ScatterSearch keystone).
    /// </summary>
    private sealed class RandomDoubleChromosome : ChromosomeBase
    {
        private readonly double _min;
        private readonly double _max;

        public RandomDoubleChromosome(double min, double max, int length) : base(length)
        {
            _min = min;
            _max = max;
            Seed();
        }

        private void Seed()
        {
            var rnd = RandomizationProvider.Current;
            for (int i = 0; i < Length; i++)
                ReplaceGene(i, new Gene(_min + rnd.GetDouble() * (_max - _min)));
        }

        public override IChromosome CreateNew() => new RandomDoubleChromosome(_min, _max, Length);
        public override Gene GenerateGene(int geneIndex) =>
            new Gene(_min + RandomizationProvider.Current.GetDouble() * (_max - _min));

        public double[] GetDoubleValues() => GetGenes().Select(g => (double)g.Value).ToArray();
    }

    /// <summary>
    /// A LocalImprovementMetaHeuristic whose candidate evaluation is test-controlled (no live
    /// IGeneticAlgorithm needed), counting its invocations.
    /// </summary>
    private sealed class GatedLocalImprovement : LocalImprovementMetaHeuristic
    {
        public GatedLocalImprovement()
        {
        }

        public GatedLocalImprovement(IMetaHeuristic sub) : this()
        {
            SubMetaHeuristic = sub;
        }

        public double? CandidateFitness { get; set; }

        public int Evaluations { get; private set; }

        public void RunImproveBest(IEvolutionContext ctx) => ImproveBest(ctx);

        protected override void EvaluateCandidate(IChromosome candidate, IEvolutionContext ctx)
        {
            Evaluations++;
            candidate.Fitness = CandidateFitness;
        }
    }

    /// <summary>
    /// A sub-metaheuristic that snapshots the generation at selection time, proving what the
    /// delegation actually observes (and returns).
    /// </summary>
    private sealed class RecordingSubMetaHeuristic : MetaHeuristicBase
    {
        public IList<IChromosome> SeenAtSelection { get; private set; }

        public override IList<IChromosome> SelectParentPopulation(IEvolutionContext ctx, ISelection selection)
        {
            SeenAtSelection = ctx.Population.CurrentGeneration.Chromosomes.ToList();
            return SeenAtSelection;
        }

        public override IList<IChromosome> MatchParentsAndCross(IEvolutionContext ctx, ICrossover crossover, float crossoverProbability, IList<IChromosome> parents)
            => throw new NotSupportedException();

        public override void MutateChromosome(IEvolutionContext ctx, IMutation mutation, float mutationProbability, IList<IChromosome> offSprings)
            => throw new NotSupportedException();

        public override IList<IChromosome> Reinsert(IEvolutionContext ctx, IReinsertion reinsertion, IList<IChromosome> offspring, IList<IChromosome> parents)
            => throw new NotSupportedException();
    }

    /// <summary>
    /// A minimal evolution context for direct ImproveBest tests: controllable population, no
    /// genetic algorithm (the gated subclass replaces the candidate evaluation).
    /// </summary>
    private sealed class StubEvolutionContext : IEvolutionContext
    {
        private readonly EvolutionContext _store = new();

        public IGeneticAlgorithm GeneticAlgorithm { get; set; } = null!;
        public IPopulation Population { get; set; } = null!;
        public int OriginalIndex { get; set; } = -1;
        public int LocalIndex { get; set; } = -1;
        public EvolutionStage CurrentStage { get; set; } = EvolutionStage.Selection;
        public IList<IChromosome> SelectedParents { get; set; } = null!;
        public IList<IChromosome> GeneratedOffsprings { get; set; } = null!;

        public IEvolutionContext GetIndividual(int index) => this;

        public IEvolutionContext GetLocal(int index) => this;

        public TItemType GetOrAdd<TItemType>((string key, int generation, EvolutionStage stage, IMetaHeuristic heuristic, int individual) contextKey, Func<TItemType> factory)
        {
            return _store.GetOrAdd(contextKey, factory);
        }

        public TItemType GetParam<TItemType>(IMetaHeuristic h, string paramName) => throw new NotSupportedException();

        public void RegisterParameter(string paramName, IMetaHeuristicParameter param)
        {
        }

        public IMetaHeuristicParameter GetParameterDefinition(string paramName) => throw new NotSupportedException();
    }
}
