using GeneticSharp;
using MetaGeneticSharp;

namespace MetaGeneticSharp.Domain.Tests.Reinsertions;

/// <summary>
/// Contract of the default reinsertion (#13778, admission tranche).
///
/// Two properties are asserted, and they are the two that the Sudoku measurement depended on:
///
///  * elitism on the VALUE — a strictly better chromosome is never displaced by a worse one;
///  * admission on the TIE — at equal fitness the newcomer takes the slot, because the
///    inherited candidate order lists parents first and a stable sort would then always keep
///    the incumbent. On an integer objective (a conflict count) ties are the norm, and that
///    was the mechanism that closed the population for 140 generations out of 160.
///
/// The chromosomes here deliberately share the same gene values: ChromosomeBase derives
/// <c>Equals</c> from a value comparison, so a value-keyed implementation would collapse them
/// into one individual. The contract is about which REFERENCE keeps the slot.
/// </summary>
public class FitnessBasedElitistReinsertionTests
{
    private sealed class FixedFitnessChromosome : ChromosomeBase
    {
        public FixedFitnessChromosome(double fitness)
            : base(2)
        {
            CreateGenes();
            Fitness = fitness;
        }

        public override Gene GenerateGene(int geneIndex) => new Gene(0.0);

        public override IChromosome CreateNew() => new FixedFitnessChromosome(0.0);
    }

    private static IList<IChromosome> Reinsert(int minSize, IList<IChromosome> parents, IList<IChromosome> offspring)
    {
        var population = new MetaPopulation(minSize, minSize, new FixedFitnessChromosome(0.0));
        return new FitnessBasedElitistReinsertion().SelectChromosomes(population, offspring, parents);
    }

    [Test]
    public void EqualFitness_TheOffspringTakesTheSlot()
    {
        var parentA = new FixedFitnessChromosome(5.0);
        var parentB = new FixedFitnessChromosome(5.0);
        var childA = new FixedFitnessChromosome(5.0);
        var childB = new FixedFitnessChromosome(5.0);

        var result = Reinsert(2, new List<IChromosome> { parentA, parentB },
            new List<IChromosome> { childA, childB });

        Assert.That(result, Is.EquivalentTo(new IChromosome[] { childA, childB }),
            "at equal fitness the newcomers must win: arrival order must not decide admission");
    }

    [Test]
    public void BetterParent_IsNeverDisplacedByAWorseOffspring()
    {
        var best = new FixedFitnessChromosome(10.0);
        var second = new FixedFitnessChromosome(9.0);
        var weakChild = new FixedFitnessChromosome(1.0);

        var result = Reinsert(2, new List<IChromosome> { best, second }, new List<IChromosome> { weakChild });

        Assert.That(result, Is.EquivalentTo(new IChromosome[] { best, second }),
            "elitism on the value still decides first; the tie rule only reopens ties");
    }

    [Test]
    public void BetterOffspring_IsAdmitted()
    {
        var strongParent = new FixedFitnessChromosome(1.0);
        var weakParent = new FixedFitnessChromosome(0.5);
        var child = new FixedFitnessChromosome(10.0);

        var result = Reinsert(2, new List<IChromosome> { strongParent, weakParent },
            new List<IChromosome> { child });

        Assert.That(result, Is.EquivalentTo(new IChromosome[] { child, strongParent }),
            "a strictly better newcomer takes the slot of the weakest parent");
    }

    [Test]
    public void Result_IsOrderedByFitnessDescending()
    {
        var result = Reinsert(3,
            new List<IChromosome> { new FixedFitnessChromosome(4.0), new FixedFitnessChromosome(1.0) },
            new List<IChromosome> { new FixedFitnessChromosome(9.0), new FixedFitnessChromosome(2.0) });

        Assert.That(result.Select(c => c.Fitness!.Value), Is.Ordered.Descending,
            "MetaPopulation addresses individuals by index, so the fitness order is the contract");
        Assert.That(result, Has.Count.EqualTo(3));
    }

    [Test]
    public void MixedTies_KeepTheBestValueAndAdmitTheNewcomers()
    {
        var bestParent = new FixedFitnessChromosome(8.0);
        var tiedParent = new FixedFitnessChromosome(8.0);
        var tiedChild = new FixedFitnessChromosome(8.0);
        var weakChild = new FixedFitnessChromosome(3.0);

        var result = Reinsert(2, new List<IChromosome> { bestParent, tiedParent },
            new List<IChromosome> { tiedChild, weakChild });

        Assert.That(result.First().Fitness, Is.EqualTo(8.0));
        Assert.That(result, Does.Contain(tiedChild), "the tie goes to the newcomer");
        Assert.That(result, Does.Not.Contain(weakChild), "a worse newcomer still loses");
        Assert.That(result, Has.Count.EqualTo(2));
    }
}
