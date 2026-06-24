using GeneticSharp;
using MetaGeneticSharp;

namespace MetaGeneticSharp.Domain.Tests.Geometric;

/// <summary>
/// Acceptance tests for the edge-based geometric embedding (TSP geometrisation, #3964 Option C).
/// The keystone shows that the offspring CONTAINS every edge common to both parents — the
/// provably-on-the-geodesic core under the edge (Hamming-on-edges) metric — whereas the swap/Cayley
/// and insertion/Ulam embeddings operate on city-label indices and do not. This is the third
/// permutation metric: where Config 3 (swap) and Config 4 (insertion) degrade symmetrically on the
/// naive centroid, the edge embedding reaches a genuinely novel offspring that inherits the shared
/// tour structure of both parents (the geometric reading of edge-recombination / EAX).
/// </summary>
public class EdgeEmbeddingTests
{
    private static IntPermutationChromosome Perm(params int[] values) => new IntPermutationChromosome(values);

    private static int[] Values(IChromosome c) => c.GetGenes().Select(g => (int)g.Value).ToArray();

    // Undirected edges of a cyclic tour, as canonical (min, max) pairs.
    private static HashSet<(int, int)> TourEdges(int[] tour)
    {
        var edges = new HashSet<(int, int)>();
        int n = tour.Length;
        for (int k = 0; k < n; k++)
        {
            int a = tour[k];
            int b = tour[(k + 1) % n];
            edges.Add(a < b ? (a, b) : (b, a));
        }

        return edges;
    }

    [Test]
    public void MapToGeometry_MarksTourEdgesWithOnes()
    {
        // The geometry vector of [0,1,2,3,4] has a 1 exactly on its cyclic edges
        // {0,1},{1,2},{2,3},{3,4},{0,4} and 0 elsewhere, over canonical pair order.
        var embedding = new EdgeEmbedding<int>();
        var parent = Perm(0, 1, 2, 3, 4);

        var vectors = embedding.MapToGeometry(new List<IChromosome> { parent });

        // Canonical pairs for n=5: (0,1)(0,2)(0,3)(0,4)(1,2)(1,3)(1,4)(2,3)(2,4)(3,4).
        Assert.That(vectors[0], Is.EqualTo(new[] { 1, 0, 0, 1, 1, 0, 0, 1, 0, 1 }));
    }

    [Test]
    public void MapFromGeometry_OffspringIsAValidPermutation()
    {
        // Whatever common-edge core is requested, the reassembled offspring is a permutation
        // (no city dropped or duplicated).
        var embedding = new EdgeEmbedding<int>();
        var parentA = Perm(0, 1, 2, 3, 4);
        var parentB = Perm(0, 2, 1, 3, 4);

        // Common edges {{1,2},{3,4},{0,4}} => centroid vector marks (0,4),(1,2),(3,4).
        var centroid = new[] { 0, 0, 0, 1, 1, 0, 0, 0, 0, 1 };

        var offspring = embedding.MapFromGeometry(
            new List<IChromosome> { parentA, parentB }, centroid);

        Assert.That(Values(offspring), Is.EquivalentTo(new[] { 0, 1, 2, 3, 4 }));
    }

    [Test]
    public void Offspring_PreservesAllCommonEdges()
    {
        // KEYSTONE: parents [0,1,2,3,4] and [0,2,1,3,4] share edges {{1,2},{3,4},{0,4}}.
        // The edge-embedding offspring CONTAINS every common edge — the provably-on-the-geodesic core.
        var embedding = new EdgeEmbedding<int>();
        var parentA = Perm(0, 1, 2, 3, 4);
        var parentB = Perm(0, 2, 1, 3, 4);

        var common = TourEdges(new[] { 0, 1, 2, 3, 4 });
        common.IntersectWith(TourEdges(new[] { 0, 2, 1, 3, 4 }));
        Assert.That(common, Is.EquivalentTo(new[] { (0, 4), (1, 2), (3, 4) }));

        // Centroid = edge-intersection: 1 on (0,4),(1,2),(3,4), else 0.
        var centroid = new[] { 0, 0, 0, 1, 1, 0, 0, 0, 0, 1 };

        var offspring = embedding.MapFromGeometry(
            new List<IChromosome> { parentA, parentB }, centroid);
        var offspringEdges = TourEdges(Values(offspring));

        // Every common edge is present in the offspring (superset; stitches add completion edges).
        Assert.That(offspringEdges, Is.SupersetOf(common));
        // Deterministic stitch order => [0,4,3,1,2] (blocks [0,4,3] then [1,2]).
        Assert.That(Values(offspring), Is.EqualTo(new[] { 0, 4, 3, 1, 2 }));
    }

    [Test]
    public void EdgeVsSwap_DistinctOffspringFromSameParents()
    {
        // THIRD GEOMETRY: on the same parents, the edge embedding reaches a different offspring than
        // the swap/Cayley embedding. Both are valid permutations, but they are DIFFERENT tours.
        //
        // Under the naive 2-parent centroid, swap (city-label indices) DEGENERATES: it collapses onto
        // one of the parents (here parentB [0,2,1,3,4]) — the documented Config 3/4 limit. The edge
        // embedding instead inherits the SHARED tour structure and reaches a NOVEL tour that is
        // neither parent. This is the distinction: swap copies a parent, edge recombines.
        //
        // NOTE: "preserves the common edges" cannot separate the two, because every parent trivially
        // contains the common edges (they are a subset of each parent's edges by definition). The real
        // separator is novelty: the edge offspring is a tour that belongs to NEITHER parent.
        var parents = new List<IChromosome> { Perm(0, 1, 2, 3, 4), Perm(0, 2, 1, 3, 4) };

        var swapCrossover = new GeometricCrossover<int>(ordered: true, parentNb: 2, generateTwin: false);
        // GeometryEmbedding defaults to OrderedEmbedding (swap/Cayley).
        var swapOffspring = Values(swapCrossover.Cross(parents)[0]);

        var edgeCrossover = new GeometricCrossover<int>(ordered: true, parentNb: 2, generateTwin: false);
        edgeCrossover.GeometryEmbedding = new EdgeEmbedding<int>();
        var edgeOffspring = Values(edgeCrossover.Cross(parents)[0]);

        // Both are valid permutations.
        Assert.That(swapOffspring, Is.EquivalentTo(new[] { 0, 1, 2, 3, 4 }));
        Assert.That(edgeOffspring, Is.EquivalentTo(new[] { 0, 1, 2, 3, 4 }));

        // They are DIFFERENT tours => a third geometry.
        Assert.That(edgeOffspring, Is.Not.EqualTo(swapOffspring));

        // Swap degenerates onto a parent (the centroid-on-labels collapse).
        var parentA = new[] { 0, 1, 2, 3, 4 };
        var parentB = new[] { 0, 2, 1, 3, 4 };
        Assert.That(swapOffspring, Is.EqualTo(parentA).Or.EqualTo(parentB),
            "swap/Cayley collapses onto a parent under the naive centroid");

        // The edge offspring is a NOVEL tour: it belongs to NEITHER parent.
        Assert.That(edgeOffspring, Is.Not.EqualTo(parentA));
        Assert.That(edgeOffspring, Is.Not.EqualTo(parentB));
    }

    [Test]
    public void InjectedEmbedding_IsHonouredThroughGeometricCrossover()
    {
        // Integration of the notebook Config 5 usage pattern (#3964):
        // inject EdgeEmbedding into GeometricCrossover<int> AFTER construction (mirroring Config 4).
        // The injected embedding is honoured end-to-end through PerformCross and yields a valid
        // permutation offspring that preserves the common-edge core.
        var parents = new List<IChromosome> { Perm(0, 1, 2, 3, 4), Perm(0, 2, 1, 3, 4) };

        var edgeCrossover = new GeometricCrossover<int>(ordered: true, parentNb: 2, generateTwin: false);
        edgeCrossover.GeometryEmbedding = new EdgeEmbedding<int>();

        var children = edgeCrossover.Cross(parents);
        Assert.That(children, Has.Count.EqualTo(1));
        var offspring = Values(children[0]);

        // The injected EdgeEmbedding is honoured: valid permutation, preserves common edges.
        Assert.That(offspring, Is.EquivalentTo(new[] { 0, 1, 2, 3, 4 }));
        Assert.That(edgeCrossover.GeometryEmbedding, Is.InstanceOf<EdgeEmbedding<int>>());

        var common = TourEdges(new[] { 0, 1, 2, 3, 4 });
        common.IntersectWith(TourEdges(new[] { 0, 2, 1, 3, 4 }));
        Assert.That(TourEdges(offspring), Is.SupersetOf(common));
    }
}
