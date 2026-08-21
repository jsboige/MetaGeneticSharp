#nullable disable
using System.Collections.Generic;
using System.Linq;
using GeneticSharp;

namespace MetaGeneticSharp
{
    /// <summary>
    /// Projects a candidate transition (current → candidate) onto the ATTRIBUTE space the tabu
    /// machinery reasons over (#12049 axe 4). This is the pluggable "GetTabu" of the classic
    /// template (F. Glover, "Tabu Search -- Part I", ORSA Journal on Computing, 1989; the ygmh
    /// DiscreteTS abstracts exactly this hook): WHAT is memorized and forbidden is a modelling
    /// choice, deliberately decoupled from HOW it is stored (see <see cref="ITabuMemory"/>) and
    /// WHEN the interdict may be lifted (see <see cref="ITabuFilter"/>).
    /// </summary>
    /// <remarks>
    /// Attributes are strings: comparable, hashable, and readable in a trace. The two methods
    /// carry the two halves of the reverse-attribution convention -- <see cref="Forbidden"/> is
    /// consulted BEFORE moving (is this transition interdicted by what we recently did?),
    /// <see cref="Committed"/> AFTER moving (what does doing this transition now interdict?).
    /// </remarks>
    public interface ITabuProjection
    {
        /// <summary>
        /// The attributes whose presence in the tabu memory FORBIDS the transition
        /// current → candidate. A candidate is interdicted when any of these is currently tabu.
        /// </summary>
        IEnumerable<string> Forbidden(IChromosome current, IChromosome candidate);

        /// <summary>
        /// The attributes this transition commits to the tabu memory once performed --
        /// conventionally the REVERSE move(s), so that immediately undoing the transition is
        /// interdicted.
        /// </summary>
        IEnumerable<string> Committed(IChromosome current, IChromosome candidate);
    }

    /// <summary>
    /// Move-level projection: one attribute pair per modified gene, "(geneIndex:from→to)".
    /// Performing a → b commits "b → a" (the undo is interdicted); the classic Glover
    /// reverse-attribution, at gene granularity. The ygmh swap-Sudoku tabu ("(row, digit) may
    /// not move again") is this projection restricted to swap moves.
    /// </summary>
    public class GeneMoveProjection : ITabuProjection
    {
        /// <inheritdoc />
        public IEnumerable<string> Forbidden(IChromosome current, IChromosome candidate)
        {
            return MoveAttributes(current, candidate, forward: true);
        }

        /// <inheritdoc />
        public IEnumerable<string> Committed(IChromosome current, IChromosome candidate)
        {
            return MoveAttributes(current, candidate, forward: false);
        }

        private static IEnumerable<string> MoveAttributes(IChromosome current, IChromosome candidate, bool forward)
        {
            var from = current.GetGenes();
            var to = candidate.GetGenes();
            for (int i = 0; i < from.Length && i < to.Length; i++)
            {
                if (!Equals(from[i].Value, to[i].Value))
                {
                    yield return forward
                        ? $"({i}:{from[i].Value}->{to[i].Value})"
                        : $"({i}:{to[i].Value}->{from[i].Value})";
                }
            }
        }
    }

    /// <summary>
    /// Solution-level projection: the attribute is the visited solution itself (a stable string
    /// digest of its genes). Every visited solution commits its own digest, and entering any
    /// memorized solution is forbidden -- anti-revisit rather than anti-undo. Coarser than
    /// <see cref="GeneMoveProjection"/> (forbids the whole state, not the move), and the right
    /// granularity when the search makes multi-gene moves whose exact reverse is never replayed
    /// but whose RESULT recurs (cycling on a plateau).
    /// </summary>
    public class SolutionHashProjection : ITabuProjection
    {
        /// <inheritdoc />
        public IEnumerable<string> Forbidden(IChromosome current, IChromosome candidate)
        {
            yield return Digest(candidate);
        }

        /// <inheritdoc />
        public IEnumerable<string> Committed(IChromosome current, IChromosome candidate)
        {
            // Both endpoints are marked visited: the one left (cannot come straight back) and
            // the one entered (cannot be re-entered later by another path).
            yield return Digest(current);
            yield return Digest(candidate);
        }

        /// <summary>A stable, collision-reasonable digest of a chromosome's gene values.</summary>
        public static string Digest(IChromosome chromosome)
        {
            var genes = chromosome.GetGenes();
            var builder = new System.Text.StringBuilder(genes.Length * 8);
            foreach (var gene in genes)
            {
                builder.Append('#').Append(gene.Value);
            }

            return builder.ToString();
        }
    }
}
