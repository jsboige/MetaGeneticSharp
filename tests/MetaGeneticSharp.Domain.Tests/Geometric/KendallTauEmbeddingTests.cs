using GeneticSharp;
using MetaGeneticSharp;

namespace MetaGeneticSharp.Domain.Tests.Geometric;

/// <summary>
/// Acceptance tests for the adjacent-transposition geometric embedding (Kendall-Tau metric, #3964).
/// The keystone shows that, unlike <see cref="OrderedEmbedding{TValue}"/> (swap/Cayley metric) and
/// <see cref="InsertionEmbedding{TValue}"/> (Ulam metric), <see cref="KendallTauEmbedding{TValue}"/>
/// reaches a THIRD distinct offspring from the same parent and metric-space target in a single move
/// — the pedagogical point that three permutation metrics define three distinct geodesic segments.
/// </summary>
public class KendallTauEmbeddingTests
{
    private static IntPermutationChromosome Perm(params int[] values) => new IntPermutationChromosome(values);

    private static int[] Values(IChromosome c) => c.GetGenes().Select(g => (int)g.Value).ToArray();

    [Test]
    public void Unordered_FallsBackToIdentity()
    {
        var embedding = new KendallTauEmbedding<double> { IsOrdered = false };
        var parentA = new DoubleArrayChromosome(new[] { 0.0, 10.0, 20.0 });
        var parentB = new DoubleArrayChromosome(new[] { 30.0, 40.0, 50.0 });

        var offspring = (DoubleArrayChromosome)embedding.MapFromGeometry(
            new List<IChromosome> { parentA, parentB }, new[] { 15.0, 25.0, 35.0 });
        Assert.That(offspring.GetDoubleValues(), Is.EqualTo(new[] { 15.0, 25.0, 35.0 }));
    }

    [Test]
    public void Ordered_PreservesPermutationMultiset()
    {
        // An accepted adjacent swap never duplicates or drops a value: the offspring is a reordering.
        var embedding = new KendallTauEmbedding<int> { IsOrdered = true };
        var parent = Perm(0, 1, 2, 3, 4);

        var offspring = embedding.MapFromGeometry(
            new List<IChromosome> { parent },
            new[] { 2, 0, 1, 3, 4 });

        Assert.That(Values(offspring), Is.EquivalentTo(new[] { 0, 1, 2, 3, 4 }));
    }

    [Test]
    public void SingleAdjacentSwap_MovesElementByOneSlot()
    {
        // SingleFirstAllowed returns after the FIRST accepted adjacent swap.
        // Parent [0,1,2,3,4], target [2,0,1,3,4]: targetRank = {2:0, 0:1, 1:2, 3:3, 4:4}.
        // First adjacent inversion: positions 1,2 carry values (1,2) whose ranks are (2,0) -> 2>0,
        // so swap them => [0,2,1,3,4]. Correct one-Kendall-Tau-step offspring.
        var embedding = new KendallTauEmbedding<int>
        {
            IsOrdered = true,
            GeneSelectionMode = GeneSelectionMode.SingleFirstAllowed,
        };
        var parent = Perm(0, 1, 2, 3, 4);

        var offspring = embedding.MapFromGeometry(
            new List<IChromosome> { parent },
            new[] { 2, 0, 1, 3, 4 });

        Assert.That(Values(offspring), Is.EqualTo(new[] { 0, 2, 1, 3, 4 }));
    }

    [Test]
    public void KendallTauVsCayleyAndUlam_DistinctGeodesicFromSameParentAndTarget()
    {
        // KEYSTONE: same parent, same metric-space target, three metrics => three distinct offspring.
        // Parent [0,1,2,3,4], target [2,0,1,3,4], SingleFirstAllowed.
        var parent = Perm(0, 1, 2, 3, 4);
        var target = new[] { 2, 0, 1, 3, 4 };

        var swapEmbedding = new OrderedEmbedding<int>
        {
            IsOrdered = true,
            GeneSelectionMode = GeneSelectionMode.SingleFirstAllowed,
        };
        var insertionEmbedding = new InsertionEmbedding<int>
        {
            IsOrdered = true,
            GeneSelectionMode = GeneSelectionMode.SingleFirstAllowed,
        };
        var adjacentEmbedding = new KendallTauEmbedding<int>
        {
            IsOrdered = true,
            GeneSelectionMode = GeneSelectionMode.SingleFirstAllowed,
        };

        var swapOffspring = swapEmbedding.MapFromGeometry(
            new List<IChromosome> { parent }, target);
        var insertionOffspring = insertionEmbedding.MapFromGeometry(
            new List<IChromosome> { parent }, target);
        var adjacentOffspring = adjacentEmbedding.MapFromGeometry(
            new List<IChromosome> { parent }, target);

        // Swap (Cayley): index 0 wants value 2 (at index 2) => swap positions 0 and 2 => [2,1,0,3,4].
        Assert.That(Values(swapOffspring), Is.EqualTo(new[] { 2, 1, 0, 3, 4 }));
        // Insertion (Ulam): move value 2 from index 2 to index 0 => [2,0,1,3,4].
        Assert.That(Values(insertionOffspring), Is.EqualTo(new[] { 2, 0, 1, 3, 4 }));
        // Adjacent transposition (Kendall-Tau): swap the first inverted adjacent pair => [0,2,1,3,4].
        Assert.That(Values(adjacentOffspring), Is.EqualTo(new[] { 0, 2, 1, 3, 4 }));

        // The three metrics reach THREE DIFFERENT descendants in one step => distinct geodesic segments.
        Assert.That(Values(adjacentOffspring), Is.Not.EqualTo(Values(swapOffspring)));
        Assert.That(Values(adjacentOffspring), Is.Not.EqualTo(Values(insertionOffspring)));
        Assert.That(Values(swapOffspring), Is.Not.EqualTo(Values(insertionOffspring)));
    }

    [Test]
    public void FullWalk_ConvergesToTargetPermutation()
    {
        // AllIndexed (no SingleFirstAllowed): sweep passes of adjacent swaps until convergence,
        // and the offspring reaches the target permutation exactly (bubble sort).
        var embedding = new KendallTauEmbedding<int>
        {
            IsOrdered = true,
            GeneSelectionMode = GeneSelectionMode.AllIndexed,
        };
        var parent = Perm(0, 1, 2, 3, 4);
        var target = new[] { 4, 3, 2, 1, 0 }; // full reversal

        var offspring = embedding.MapFromGeometry(
            new List<IChromosome> { parent }, target);

        Assert.That(Values(offspring), Is.EqualTo(target));
    }

    [Test]
    public void InjectedEmbedding_IsHonouredThroughGeometricCrossover()
    {
        // Integration of the notebook usage pattern: GeometricCrossover<int>(ordered:true)
        // DEFAULTS its GeometryEmbedding to OrderedEmbedding (swap/Cayley). To explore the
        // Kendall-Tau geometry one injects KendallTauEmbedding AFTER construction. This test
        // guards the injection contract end-to-end through PerformCross: the injected embedding is
        // honoured and produces a valid permutation offspring. (Same naive-centroid caveat as the
        // other embeddings — see the class XML doc.)
        var parents = new List<IChromosome> { Perm(0, 1, 2, 3, 4), Perm(2, 0, 1, 3, 4) };

        var adjacentCrossover = new GeometricCrossover<int>(ordered: true, parentNb: 2, generateTwin: false);
        adjacentCrossover.GeometryEmbedding = new KendallTauEmbedding<int> { IsOrdered = true };

        var children = adjacentCrossover.Cross(parents);
        Assert.That(children, Has.Count.EqualTo(1));
        var offspring = Values(children[0]);

        // The injected KendallTauEmbedding is honoured: the offspring is a valid permutation.
        Assert.That(offspring, Is.EquivalentTo(new[] { 0, 1, 2, 3, 4 }));
        Assert.That(adjacentCrossover.GeometryEmbedding, Is.InstanceOf<KendallTauEmbedding<int>>());
    }

    [Test]
    public void TargetEqualsParent_NoAcceptedSwap_OffspringIsParent()
    {
        // EDGE CASE: when the metric-space target equals the parent permutation, every adjacent
        // pair is already in target-rank order, so NO swap is accepted and the bubble-sort walk is
        // a no-op. The offspring is the parent unchanged — the geometric-crossover contract that an
        // offspring walked toward an already-reached target stays put (parity with
        // EdgeEmbedding.IdenticalParents_OffspringIsTheParent).
        var embedding = new KendallTauEmbedding<int>
        {
            IsOrdered = true,
            GeneSelectionMode = GeneSelectionMode.SingleFirstAllowed,
        };
        var parent = Perm(0, 1, 2, 3, 4);

        var offspring = embedding.MapFromGeometry(
            new List<IChromosome> { parent },
            new[] { 0, 1, 2, 3, 4 });

        Assert.That(Values(offspring), Is.EqualTo(new[] { 0, 1, 2, 3, 4 }));
    }

    [Test]
    public void MinimalLengthTwo_SingleAdjacentSwapReachesReversedTarget()
    {
        // BOUNDARY: at the minimum supported chromosome length (ChromosomeBase rejects length < 2),
        // a single adjacent swap is the only available move and is also a full reversal — one
        // Kendall-Tau step reaches the target. Both SingleFirstAllowed (one swap, return) and
        // AllIndexed (the sole swap, then a no-op second pass) converge to the reversed target.
        var parent = Perm(0, 1);
        var target = new[] { 1, 0 };

        var singleFirst = new KendallTauEmbedding<int>
        {
            IsOrdered = true,
            GeneSelectionMode = GeneSelectionMode.SingleFirstAllowed,
        };
        var allIndexed = new KendallTauEmbedding<int>
        {
            IsOrdered = true,
            GeneSelectionMode = GeneSelectionMode.AllIndexed,
        };

        Assert.That(Values(singleFirst.MapFromGeometry(
            new List<IChromosome> { parent }, target)), Is.EqualTo(new[] { 1, 0 }));
        Assert.That(Values(allIndexed.MapFromGeometry(
            new List<IChromosome> { parent }, target)), Is.EqualTo(new[] { 1, 0 }));
    }
}
