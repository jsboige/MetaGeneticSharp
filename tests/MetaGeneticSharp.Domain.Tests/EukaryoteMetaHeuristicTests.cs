using GeneticSharp;
using MetaGeneticSharp;

namespace MetaGeneticSharp.Domain.Tests;

public class EukaryoteMetaHeuristicTests
{
    private static FloatingPointChromosome CreateChromosome()
    {
        return new FloatingPointChromosome(
            new double[] { 0, 0 },
            new double[] { 100, 100 },
            new int[] { 16, 16 },
            new int[] { 2, 2 });
    }

    [Test]
    public void EukaryoteChromosome_Karyotype_SlicesAndRebuildsParent()
    {
        var parent = CreateChromosome();
        parent.Fitness = 0.5;

        var karyotype = EukaryoteChromosome.GetKaryotype(parent, new[] { 16, 16 });

        Assert.Multiple(() =>
        {
            Assert.That(karyotype, Has.Count.EqualTo(2));
            Assert.That(karyotype[0].Length, Is.EqualTo(16));
            Assert.That(((EukaryoteChromosome)karyotype[1]).StartGeneIndex, Is.EqualTo(16));
            Assert.That(karyotype[0].Fitness, Is.EqualTo(0.5), "Child chromosomes inherit the parent fitness.");
            Assert.That(karyotype[0].GetGenes(), Is.EqualTo(parent.GetGenes().Take(16).ToArray()));
            Assert.That(karyotype[1].GetGenes(), Is.EqualTo(parent.GetGenes().Skip(16).ToArray()));
        });

        var rebuilt = EukaryoteChromosome.GetNewIndividual(karyotype.Cast<EukaryoteChromosome>().ToList());

        Assert.Multiple(() =>
        {
            Assert.That(rebuilt, Is.InstanceOf<FloatingPointChromosome>());
            Assert.That(rebuilt.GetGenes(), Is.EqualTo(parent.GetGenes()));
        });
    }

    [Test]
    public void EukaryoteChromosome_UpdateParent_WritesChildGenesBack()
    {
        var parent = CreateChromosome();
        var other = CreateChromosome();
        var karyotype = EukaryoteChromosome.GetKaryotype(parent, new[] { 16, 16 });

        ((EukaryoteChromosome)karyotype[1]).ReplaceGenes(0, other.GetGenes().Skip(16).ToArray());
        EukaryoteChromosome.UpdateParent(karyotype);

        Assert.Multiple(() =>
        {
            Assert.That(parent.GetGenes().Take(16), Is.EqualTo(karyotype[0].GetGenes()), "First half untouched.");
            Assert.That(parent.GetGenes().Skip(16), Is.EqualTo(other.GetGenes().Skip(16)), "Second half overwritten from the child.");
        });
    }

    [Test]
    public void EukaryoteChromosome_GetSubPopulations_GroupsByPosition()
    {
        var parents = new List<IChromosome> { CreateChromosome(), CreateChromosome(), CreateChromosome() };

        var subPopulations = EukaryoteChromosome.GetSubPopulations(parents, new[] { 16, 16 });

        Assert.Multiple(() =>
        {
            Assert.That(subPopulations, Has.Count.EqualTo(2));
            Assert.That(subPopulations[0], Has.Count.EqualTo(3));
            Assert.That(subPopulations.SelectMany(p => p), Has.All.InstanceOf<EukaryoteChromosome>());
            Assert.That(subPopulations[0].Cast<EukaryoteChromosome>().Select(c => c.ParentIndividual),
                Is.EqualTo(parents), "Sub-population 0 holds one child per parent, in order.");
        });
    }

    [Test]
    public void SubPopulation_TracksParentAndPreservesOrder()
    {
        var parentPopulation = new MetaPopulation(4, 8, CreateChromosome());
        parentPopulation.CreateInitialGeneration();
        foreach (var c in parentPopulation.CurrentGeneration.Chromosomes)
        {
            c.Fitness = 0.1;
        }

        var subChromosomes = EukaryoteChromosome.GetSubPopulations(parentPopulation.CurrentGeneration.Chromosomes, new[] { 16, 16 })[0]
            .Cast<IChromosome>().ToList();
        var subPopulation = new SubPopulation(parentPopulation, subChromosomes);

        Assert.Multiple(() =>
        {
            Assert.That(subPopulation.ParentPopulation, Is.SameAs(parentPopulation));
            Assert.That(subPopulation.GenerationsNumber, Is.EqualTo(parentPopulation.GenerationsNumber));
            Assert.That(subPopulation.MinSize, Is.EqualTo(subChromosomes.Count));
            Assert.That(subPopulation.CurrentGeneration.Chromosomes, Is.EqualTo(subChromosomes), "Chromosome order preserved (no fitness sort).");
        });
    }

    [Test]
    public void Start_EukaryoteMetaHeuristic_CrossoverMutationScope_RunsToTermination()
    {
        var fitness = new FuncFitness(c =>
        {
            var values = ((FloatingPointChromosome)c).ToFloatingPoints();
            return -(Math.Abs(values[0] - 42) + Math.Abs(values[1] - 13));
        });

        var population = new MetaPopulation(40, 40, CreateChromosome());

        // Canonical PR usage: one phase heuristic per 16-gene sub-chromosome, scoped to
        // Crossover | Mutation so reinsertion falls through to the default sub-metaheuristic.
        var metaHeuristic = new EukaryoteMetaHeuristic(16, new DefaultMetaHeuristic(), new DefaultMetaHeuristic())
        {
            Scope = EvolutionStage.Crossover | EvolutionStage.Mutation
        };

        var ga = new MetaGeneticAlgorithm(population, fitness, new EliteSelection(), new UniformCrossover(0.5f), new UniformMutation(true), metaHeuristic)
        {
            Termination = new GenerationNumberTermination(20)
        };

        ga.Start();

        Assert.Multiple(() =>
        {
            Assert.That(ga.State, Is.EqualTo(GeneticAlgorithmState.TerminationReached));
            Assert.That(ga.GenerationsNumber, Is.EqualTo(20));
            Assert.That(ga.BestChromosome.Fitness, Is.Not.Null);
            Assert.That(ga.BestChromosome, Is.InstanceOf<FloatingPointChromosome>(), "Offspring are rebuilt as full individuals, not Eukaryote slices.");
        });
    }

    [Test]
    public void Reinsert_EukaryoteScopedToReinsertion_Throws()
    {
        var metaHeuristic = new EukaryoteMetaHeuristic(16, new DefaultMetaHeuristic(), new DefaultMetaHeuristic());

        Assert.That(
            () => metaHeuristic.Reinsert(new EvolutionContext(), new FitnessBasedElitistReinsertion(), new List<IChromosome>(), new List<IChromosome>()),
            Throws.InvalidOperationException);
    }
}
