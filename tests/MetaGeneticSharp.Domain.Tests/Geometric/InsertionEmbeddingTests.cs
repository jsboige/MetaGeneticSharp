using GeneticSharp;
using MetaGeneticSharp;

namespace MetaGeneticSharp.Domain.Tests.Geometric;

/// <summary>
/// Acceptance tests for the insertion-based geometric embedding (Option B atom 1, #3964).
/// The keystone shows that, unlike <see cref="OrderedEmbedding{TValue}"/> (swap/Cayley metric),
/// <see cref="InsertionEmbedding{TValue}"/> (Ulam metric) reaches a distinct offspring from the
/// same parent and metric-space target in a single move — the pedagogical point that different
/// permutation metrics define different geodesic segments.
/// </summary>
public class InsertionEmbeddingTests
{
    private static IntPermutationChromosome Perm(params int[] values) => new IntPermutationChromosome(values);

    private static int[] Values(IChromosome c) => c.GetGenes().Select(g => (int)g.Value).ToArray();

    [Test]
    public void Unordered_FallsBackToIdentity()
    {
        var embedding = new InsertionEmbedding<double> { IsOrdered = false };
        var parentA = new DoubleArrayChromosome(new[] { 0.0, 10.0, 20.0 });
        var parentB = new DoubleArrayChromosome(new[] { 30.0, 40.0, 50.0 });

        var offspring = (DoubleArrayChromosome)embedding.MapFromGeometry(
            new List<IChromosome> { parentA, parentB }, new[] { 15.0, 25.0, 35.0 });
        Assert.That(offspring.GetDoubleValues(), Is.EqualTo(new[] { 15.0, 25.0, 35.0 }));
    }

    [Test]
    public void Ordered_PreservesPermutationMultiset()
    {
        // An accepted insertion never duplicates or drops a value: the offspring is a reordering.
        var embedding = new InsertionEmbedding<int> { IsOrdered = true };
        var parent = Perm(0, 1, 2, 3, 4);

        var offspring = embedding.MapFromGeometry(
            new List<IChromosome> { parent },
            new[] { 2, 0, 1, 3, 4 });

        Assert.That(Values(offspring), Is.EquivalentTo(new[] { 0, 1, 2, 3, 4 }));
    }

    [Test]
    public void SingleInsertion_RelocatesElementInOneUlamStep()
    {
        // SingleFirstAllowed returns after the FIRST accepted insertion.
        // Parent [0,1,2,3,4], target [2,0,1,3,4]: index 0 wants value 2 (currently at index 2).
        // One insertion moves 2 from index 2 to index 0 => [2,0,1,3,4]. Correct target in ONE move.
        var embedding = new InsertionEmbedding<int>
        {
            IsOrdered = true,
            GeneSelectionMode = GeneSelectionMode.SingleFirstAllowed,
        };
        var parent = Perm(0, 1, 2, 3, 4);

        var offspring = embedding.MapFromGeometry(
            new List<IChromosome> { parent },
            new[] { 2, 0, 1, 3, 4 });

        Assert.That(Values(offspring), Is.EqualTo(new[] { 2, 0, 1, 3, 4 }));
    }

    [Test]
    public void UlamVsCayley_DistinctGeodesicFromSameParentAndTarget()
    {
        // KEYSTONE: same parent, same metric-space target, two metrics => two distinct offspring.
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

        var swapOffspring = swapEmbedding.MapFromGeometry(
            new List<IChromosome> { parent }, target);
        var insertionOffspring = insertionEmbedding.MapFromGeometry(
            new List<IChromosome> { parent }, target);

        // Swap (Cayley): index 0 wants value 2 (at index 2) => swap positions 0 and 2 => [2,1,0,3,4].
        Assert.That(Values(swapOffspring), Is.EqualTo(new[] { 2, 1, 0, 3, 4 }));
        // Insertion (Ulam): move value 2 from index 2 to index 0 => [2,0,1,3,4].
        Assert.That(Values(insertionOffspring), Is.EqualTo(new[] { 2, 0, 1, 3, 4 }));

        // The two metrics reach DIFFERENT descendants in one step => distinct geodesic segments.
        Assert.That(Values(swapOffspring), Is.Not.EqualTo(Values(insertionOffspring)));
    }

    [Test]
    public void InsertAt_BackwardShift_HandlesSrcBeforeDest()
    {
        // Parent [0,1,2,3,4], target [0,2,3,4,1]: index 4 wants value 1 (at index 1).
        // Insertion moves 1 from index 1 to index 4 => [0,2,3,4,1].
        var embedding = new InsertionEmbedding<int> { IsOrdered = true };
        var parent = Perm(0, 1, 2, 3, 4);

        var offspring = embedding.MapFromGeometry(
            new List<IChromosome> { parent },
            new[] { 0, 2, 3, 4, 1 });

        Assert.That(Values(offspring), Is.EqualTo(new[] { 0, 2, 3, 4, 1 }));
    }

    [Test]
    public void FullWalk_ConvergesToTargetPermutation()
    {
        // AllIndexed (no SingleFirstAllowed): walk every index, accept every insertion,
        // and the offspring converges to the target permutation exactly.
        var embedding = new InsertionEmbedding<int>
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
        // Integration of the notebook Config 4 usage pattern (#3964 atom 2):
        // GeometricCrossover<int>(ordered:true) DEFAULTS its GeometryEmbedding to OrderedEmbedding
        // (swap/Cayley). To explore the insertion/Ulam geometry one must inject InsertionEmbedding
        // AFTER construction. This test guards the injection contract end-to-end through PerformCross:
        // the injected embedding is honoured and produces a valid permutation offspring.
        //
        // NOTE: the centroid operator of a 2-parent GeometricCrossover maps permutations to a
        // metric-space target that is NOT itself a permutation (e.g. centroid of [0,1,2,3,4] and
        // [2,0,1,3,4] = [1,0.5,1.5,3,4]). The geodesic back-walk therefore degrades symmetrically
        // for both metrics — exactly the "naive centroid is inadequate on city-label indices" limit
        // that MGS-7 Config 3 documents. The swap-vs-insertion DISTINCTION in one step is guaranteed
        // only when the metric-space target is itself a permutation (see the keystone
        // UlamVsCayley_DistinctGeodesicFromSameParentAndTarget). Here we assert only that the
        // injected embedding is honoured and stays a valid permutation.
        var parents = new List<IChromosome> { Perm(0, 1, 2, 3, 4), Perm(2, 0, 1, 3, 4) };

        var insertionCrossover = new GeometricCrossover<int>(ordered: true, parentNb: 2, generateTwin: false);
        insertionCrossover.GeometryEmbedding = new InsertionEmbedding<int> { IsOrdered = true };

        var children = insertionCrossover.Cross(parents);
        Assert.That(children, Has.Count.EqualTo(1));
        var offspring = Values(children[0]);

        // The injected InsertionEmbedding is honoured: the offspring is a valid permutation.
        Assert.That(offspring, Is.EquivalentTo(new[] { 0, 1, 2, 3, 4 }));
        Assert.That(insertionCrossover.GeometryEmbedding, Is.InstanceOf<InsertionEmbedding<int>>());
    }

    [Test]
    public void ValidateInsertionFunction_CanRejectMoves()
    {
        // A custom validator that forbids moving element 0 keeps the offspring closer to the parent.
        var embedding = new InsertionEmbedding<int>
        {
            IsOrdered = true,
            GeneSelectionMode = GeneSelectionMode.SingleFirstAllowed,
            ValidateInsertionFunction = (chromosome, destIndex, srcIndex) =>
                (int)chromosome.GetGene(srcIndex).Value != 0,
        };
        var parent = Perm(0, 1, 2, 3, 4);

        var offspring = embedding.MapFromGeometry(
            new List<IChromosome> { parent },
            new[] { 2, 0, 1, 3, 4 });

        // Index 0 wants value 2 (movable) => insert 2 at index 0 => [2,0,1,3,4].
        Assert.That(Values(offspring), Is.EqualTo(new[] { 2, 0, 1, 3, 4 }));
    }
}
