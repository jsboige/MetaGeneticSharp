#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using GeneticSharp;

namespace MetaGeneticSharp
{
    /// <summary>
    /// An edge-based geometric embedding for the Travelling-Salesman (permutation) representation.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Where <see cref="OrderedEmbedding{TValue}"/> (swap / Cayley metric) and
    /// <see cref="InsertionEmbedding{TValue}"/> (insertion / Ulam metric) measure distances between
    /// permutations on their index-positions, this embedding measures distance on the EDGES of the tour
    /// — the unordered pairs of adjacent cities. This is the metric natural to the TSP: two tours are close
    /// when they share many edges, regardless of where each city sits.
    /// </para>
    /// <para>
    /// The metric space is the vector of edge-indicators over all unordered city pairs (canonical
    /// lexicographic order), i.e. the Hamming metric on edge-incidence vectors. Under this metric the
    /// gene-wise centroid (the default geometric operator in <see cref="GeometricCrossover{TValue}"/>) is
    /// the set of edges COMMON to both parents: an edge present in both parents has indicator
    /// (1 + 1) / 2 = 1; an edge present in exactly one has (0 + 1) / 2 = 0.5, which the framework's
    /// <c>To&lt;int&gt;</c> truncates to 0 (<see cref="GeometricExtensions"/>, <c>Convert.ChangeType</c>).
    /// So the centroid, unchanged, becomes the edge-intersection — no operator replacement is needed.
    /// </para>
    /// <para>
    /// The offspring is then reassembled to CONTAIN those common edges — the provably-on-the-geodesic
    /// core (Moraglio &amp; Poli, 2011; Merlevede &amp; Troein, 2020, "perfect transmission of edges").
    /// This is the geometric interpretation of edge-recombination (Whitley) and Edge-Assembly Crossover
    /// (EAX): the known-good TSP crossovers, reconstructed here from the swappable-embedding primitive.
    /// </para>
    /// <para>
    /// Honest caveat (G.9). The common-edge graph has degree at most 2 (every city has degree exactly 2 in
    /// a valid tour). A PROPER sub-cycle among the common edges is impossible: it would trap its vertices,
    /// forcing both parents to coincide on it — contradicting that a Hamiltonian cycle has no proper
    /// sub-cycle. So the common edges are either a disjoint union of PATHS plus isolated vertices, OR (the
    /// single degenerate case of identical parents) the full Hamiltonian cycle itself. Both are handled by
    /// the path-trace, which stops when no unvisited neighbour remains: paths stitch into a cycle, a full
    /// cycle traces out as the parent. Either way the offspring can be reassembled preserving every common
    /// edge. The edges used to STITCH the path-blocks together are not guaranteed to belong to either
    /// parent — they are heuristic completion edges. The offspring is on the geodesic w.r.t. its common-edge
    /// core, not provably so w.r.t. the completion. See MGS-7 (Config 5).
    /// </para>
    /// </remarks>
    public class EdgeEmbedding<TValue> : IdentityEmbedding<TValue>
    {
        /// <summary>
        /// Rebuilds the offspring from the common-edge core. Every edge whose metric-space centroid value
        /// is strictly positive (shared by both parents under the default centroid) is preserved; the
        /// resulting path-blocks are stitched into a Hamiltonian cycle.
        /// </summary>
        public override IChromosome MapFromGeometry(IList<IChromosome> parents, IList<TValue> offSpringValues)
        {
            var template = parents.First();
            int n = template.Length;
            var pairs = CanonicalPairs(n);
            var pairIndex = new Dictionary<(int, int), int>(pairs.Count);
            for (int i = 0; i < pairs.Count; i++)
            {
                pairIndex[pairs[i]] = i;
            }

            // Reconstruct the common-edge adjacency from the centroid vector
            // (value > 0 => the edge is shared by both parents under the default centroid).
            var adjacency = new HashSet<int>[n];
            for (int c = 0; c < n; c++)
            {
                adjacency[c] = new HashSet<int>();
            }

            for (int idx = 0; idx < pairs.Count && idx < offSpringValues.Count; idx++)
            {
                if (offSpringValues[idx].To<double>() > 0)
                {
                    var (a, b) = pairs[idx];
                    adjacency[a].Add(b);
                    adjacency[b].Add(a);
                }
            }

            var arranged = StitchPathBlocks(adjacency, n);

            var offspring = template.CreateNew();
            for (int i = 0; i < n; i++)
            {
                offspring.ReplaceGene(i, new Gene(arranged[i].To<TValue>()));
            }

            return offspring;
        }

        /// <summary>
        /// Maps each parent tour to its edge-indicator vector (1 if the unordered city-pair is an edge of
        /// the cyclic tour, else 0), over the canonical lexicographic pair order.
        /// </summary>
        public override IList<IList<TValue>> MapToGeometry(IList<IChromosome> parents)
        {
            int n = parents.First().Length;
            var pairs = CanonicalPairs(n);
            var pairIndex = new Dictionary<(int, int), int>(pairs.Count);
            for (int i = 0; i < pairs.Count; i++)
            {
                pairIndex[pairs[i]] = i;
            }

            var result = new List<IList<TValue>>(parents.Count);
            foreach (var parent in parents)
            {
                var tour = parent.GetGenes().Select(g => g.Value.To<int>()).ToArray();
                var vector = new TValue[pairs.Count];
                for (int i = 0; i < vector.Length; i++)
                {
                    vector[i] = 0.To<TValue>();
                }

                for (int k = 0; k < n; k++)
                {
                    int a = tour[k];
                    int b = tour[(k + 1) % n];
                    var key = a < b ? (a, b) : (b, a);
                    vector[pairIndex[key]] = 1.To<TValue>();
                }

                result.Add(vector);
            }

            return result;
        }

        // All unordered pairs (a, b) with a < b, in lexicographic order. City labels are 0..n-1.
        private static List<(int, int)> CanonicalPairs(int n)
        {
            var pairs = new List<(int, int)>(n * (n - 1) / 2);
            for (int a = 0; a < n; a++)
            {
                for (int b = a + 1; b < n; b++)
                {
                    pairs.Add((a, b));
                }
            }

            return pairs;
        }

        // The common-edge graph has degree <= 2. A proper sub-cycle is impossible (it would trap its
        // vertices), so it is a disjoint union of paths plus isolated vertices — unless the parents are
        // identical, in which case it is the full Hamiltonian cycle. We trace each maximal path from its
        // smaller endpoint (deterministic); the trace stops when no unvisited neighbour remains, so a
        // full cycle traces out as the parent. We then stitch the blocks in order of their smallest vertex
        // into one permutation. Internal block edges (the common edges) are preserved exactly; the stitches
        // are heuristic completion edges.
        private static int[] StitchPathBlocks(HashSet<int>[] adjacency, int n)
        {
            var visited = new bool[n];
            var blocks = new List<List<int>>();

            // Path endpoints first (degree 1), then isolated vertices (degree 0). Any leftover degree-2
            // vertex is an interior node already absorbed by an earlier trace; it is visited and skipped.
            var starts = Enumerable.Range(0, n)
                .OrderBy(c => adjacency[c].Count == 1 ? 0 : 1)
                .ThenBy(c => c)
                .ToList();

            foreach (var start in starts)
            {
                if (visited[start])
                {
                    continue;
                }

                var path = new List<int>();
                int current = start;
                int previous = -1;
                while (current != -1 && !visited[current])
                {
                    visited[current] = true;
                    path.Add(current);
                    int next = -1;
                    foreach (var neighbour in adjacency[current].OrderBy(x => x))
                    {
                        if (neighbour != previous && !visited[neighbour])
                        {
                            next = neighbour;
                            break;
                        }
                    }

                    previous = current;
                    current = next;
                }

                blocks.Add(path);
            }

            // Orient each path from its smaller endpoint, then order blocks by their smallest vertex.
            var oriented = blocks
                .Select(block =>
                {
                    if (block.Count > 1 && block[1] < block[0])
                    {
                        block.Reverse();
                    }

                    return block;
                })
                .OrderBy(block => block[0])
                .ToList();

            var arranged = new int[n];
            int w = 0;
            foreach (var block in oriented)
            {
                foreach (var city in block)
                {
                    arranged[w++] = city;
                }
            }

            return arranged;
        }
    }
}
