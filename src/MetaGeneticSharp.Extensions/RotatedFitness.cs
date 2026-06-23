using GeneticSharp;

namespace MetaGeneticSharp
{
    // ===========================================================================
    // Axis-alignment tooling: rotate a benchmark function's landscape by an
    // orthogonal matrix to expose metaheuristics that exploit coordinate-axis
    // structure (CEC-style "rotated" benchmark variants).
    // ---------------------------------------------------------------------------
    // Shifting the optimum (ShiftedFitness) defeats algorithms that exploit a
    // CENTRAL optimum; rotating the coordinates defeats a different, complementary
    // bias — algorithms whose search is aligned with the coordinate axes (axis-
    // aligned crossover/mutation, component-wise arithmetic operators). On a
    // rotationally symmetric function (Sphere, Rastrigin, Ackley) a rotation is a
    // no-op, which is itself the sanity check that the decorator is correct; on a
    // rotationally asymmetric, non-separable function (Rosenbrock's banana valley,
    // Schwefel) the same rotation reshapes the basin and a rotation-invariant
    // optimizer is the one that keeps its performance. The CEC competition suites
    // compose exactly this shift-then-rotate construction.
    //
    // Like ShiftedFitness, this is a thin compositional decorator: the inner
    // benchmark math is reused unchanged (never reimplemented), only the
    // coordinates fed to it are transformed.
    // ===========================================================================

    /// <summary>
    /// Wraps any <see cref="IFitness"/> so it is evaluated on rotated coordinates:
    /// <c>f_rotated(x) = f_inner(M * x)</c>, where <c>M</c> is a (typically orthogonal)
    /// matrix applied to the chromosome's gene coordinates. When <c>M</c> is orthogonal
    /// (<c>M * Mᵀ = I</c>) the transformation is a pure rotation/reflection of the search
    /// space: it preserves distances and the inner optimum's value, but rotates the basin.
    ///
    /// AUTHORSHIP: a thin compositional decorator, the rotation analogue of
    /// <see cref="ShiftedFitness"/>. The inner benchmark math is reused unchanged; only
    /// the coordinates fed to it are linearly transformed. Geometry-agnostic: reads and
    /// writes gene values as doubles, so it works for any chromosome storing double gene
    /// values (e.g. a DoubleArrayChromosome). Composes with ShiftedFitness for the full
    /// CEC shifted-then-rotated variant:
    /// <c>new RotatedFitness(new ShiftedFitness(inner, offset), Q)</c>.
    /// </summary>
    public class RotatedFitness : IFitness
    {
        private readonly IFitness _inner;
        private readonly double[,] _matrix;
        private readonly int _rows;
        private readonly int _cols;

        /// <summary>The rotation matrix applied to the gene coordinates (defensively copied).</summary>
        public double[,] Matrix => (double[,])_matrix.Clone();

        /// <param name="inner">The benchmark function to rotate (reused as-is).</param>
        /// <param name="matrix">
        /// The linear map <c>M</c> applied to the chromosome coordinates. For a pure
        /// rotation it should be square and orthogonal; non-square or non-orthogonal
        /// matrices are accepted for flexibility (a general linear map) but the
        /// optimum-preservation guarantee holds only for orthogonal <c>M</c>. The first
        /// <c>rows</c> gene coordinates are replaced by <c>M * x</c>; any coordinates
        /// beyond <c>rows</c> pass through unchanged, so the decorator stays
        /// dimension-agnostic like the functions it wraps.
        /// </param>
        public RotatedFitness(IFitness inner, double[,] matrix)
        {
            ArgumentNullException.ThrowIfNull(inner);
            ArgumentNullException.ThrowIfNull(matrix);
            if (matrix.GetLength(0) == 0 || matrix.GetLength(1) == 0)
                throw new ArgumentException("rotation matrix must have at least one row and one column", nameof(matrix));
            _inner = inner;
            _matrix = (double[,])matrix.Clone();
            _rows = matrix.GetLength(0);
            _cols = matrix.GetLength(1);
        }

        public double Evaluate(IChromosome chromosome)
        {
            // Read the chromosome's coordinates, apply the linear map M to the first
            // `rows` coordinates, then delegate to the inner function. Cloning keeps the
            // caller's chromosome untouched and works for any ChromosomeBase
            // representation (Clone + ReplaceGene are standard).
            double[] x = KnownFunctionGenes.AsDoubles(chromosome);
            int n = x.Length;
            int activeCols = Math.Min(_cols, n);
            double[] y = (double[])x.Clone();
            for (int i = 0; i < _rows && i < n; i++)
            {
                double s = 0.0;
                for (int j = 0; j < activeCols; j++)
                    s += _matrix[i, j] * x[j];
                y[i] = s;
            }
            IChromosome rotated = chromosome.Clone();
            for (int i = 0; i < y.Length; i++)
                rotated.ReplaceGene(i, new Gene(y[i]));
            return _inner.Evaluate(rotated);
        }
    }

    /// <summary>
    /// Factory for the rotation matrices fed to <see cref="RotatedFitness"/>. Provides
    /// the identity (no-rotation baseline) and a seeded reproducible orthogonal matrix
    /// built as a product of Givens plane rotations — guaranteed orthogonal
    /// (<c>M * Mᵀ = I</c>) by construction, and reproducible per <c>(n, seed)</c>.
    /// </summary>
    public static class RotationMatrices
    {
        /// <summary>
        /// The <paramref name="n"/>×<paramref name="n"/> identity matrix. Wrapping a
        /// function with this is equivalent to the function itself — the un-rotated
        /// baseline of a rotated-vs-unrotated comparison.
        /// </summary>
        public static double[,] Identity(int n)
        {
            if (n < 0) throw new ArgumentOutOfRangeException(nameof(n));
            double[,] m = new double[n, n];
            for (int i = 0; i < n; i++) m[i, i] = 1.0;
            return m;
        }

        /// <summary>
        /// A reproducible <paramref name="n"/>×<paramref name="n"/> orthogonal matrix,
        /// built as a product of seeded Givens plane rotations (one per upper-triangular
        /// index pair). Each Givens rotation is orthogonal, and a product of orthogonal
        /// matrices is orthogonal, so the result always satisfies <c>M * Mᵀ = I</c>. The
        /// same <c>(n, seed)</c> always yields the same matrix, so a rotated benchmark is
        /// reproducible across runs and machines.
        /// </summary>
        public static double[,] Seeded(int n, int seed)
        {
            if (n < 0) throw new ArgumentOutOfRangeException(nameof(n));
            double[,] m = Identity(n);
            if (n < 2) return m;
            var rng = new Random(seed);
            // Pre-multiply by a Givens rotation in plane (i,j) for each upper-triangular
            // pair. This mixes every coordinate with every other exactly once, producing a
            // dense (non-trivial) rotation rather than a single-plane twirl.
            for (int i = 0; i < n; i++)
            {
                for (int j = i + 1; j < n; j++)
                {
                    double theta = rng.NextDouble() * 2.0 * Math.PI;
                    double c = Math.Cos(theta);
                    double s = Math.Sin(theta);
                    PreMultiplyGivens(m, i, j, c, s);
                }
            }
            return m;
        }

        /// <summary>
        /// Checks whether <paramref name="m"/> is (approximately) orthogonal:
        /// <c>M * Mᵀ = I</c> within <paramref name="tolerance"/> per entry. Used by tests
        /// and by notebooks to confirm a seeded matrix is a valid rotation.
        /// </summary>
        public static bool IsOrthogonal(double[,] m, double tolerance = 1e-9)
        {
            ArgumentNullException.ThrowIfNull(m);
            int n = m.GetLength(0);
            if (m.GetLength(1) != n) return false;
            for (int i = 0; i < n; i++)
            {
                for (int k = 0; k < n; k++)
                {
                    double dot = 0.0;
                    for (int j = 0; j < n; j++)
                        dot += m[i, j] * m[k, j];
                    double expected = (i == k) ? 1.0 : 0.0;
                    if (Math.Abs(dot - expected) > tolerance)
                        return false;
                }
            }
            return true;
        }

        // Pre-multiplies m in place by the Givens rotation in plane (i,j) with cosine c
        // and sine s: the rotation mixes rows i and j of m (columns of the applied map).
        private static void PreMultiplyGivens(double[,] m, int i, int j, double c, double s)
        {
            int cols = m.GetLength(1);
            for (int k = 0; k < cols; k++)
            {
                double mi = m[i, k];
                double mj = m[j, k];
                m[i, k] = c * mi - s * mj;
                m[j, k] = s * mi + c * mj;
            }
        }
    }
}
