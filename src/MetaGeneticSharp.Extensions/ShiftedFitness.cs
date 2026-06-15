using GeneticSharp;

namespace MetaGeneticSharp
{
    // ===========================================================================
    // Center-bias tooling: translate a benchmark function's optimum away from the
    // domain center to expose metaheuristics that exploit centered optima.
    // ---------------------------------------------------------------------------
    // Many published metaheuristics (WOA among them) score well on the standard
    // benchmarks partly because the optimum sits at the center/zero of the search
    // domain; moving the optimum away from the center exposes that bias (Kudela,
    // "A critical problem in benchmarking and analysis of evolutionary computation
    // methods", Nature Machine Intelligence 4, 2022). A large centered-vs-shifted
    // performance gap is the bias signature.
    //
    // The PR#87 embryo was a single uniform SCALAR shift applied to every
    // coordinate; this generalizes it to per-dimension offset VECTORS (seeded for
    // reproducibility) while keeping the uniform and un-shifted variants available
    // for side-by-side comparison.
    // ===========================================================================

    /// <summary>
    /// Wraps any <see cref="IFitness"/> so it is evaluated on coordinate-translated
    /// genes: <c>f_shifted(x) = f_inner(x - offset)</c>. Where the inner function has
    /// its optimum at x*, the shifted function has its optimum at <c>x* + offset</c>.
    ///
    /// AUTHORSHIP: a thin compositional decorator. The inner benchmark math is reused
    /// unchanged (never reimplemented); only the coordinates fed to it are translated.
    /// It is geometry-agnostic — it reads and writes gene values as doubles, the same
    /// representation contract <see cref="KnownFunctionGenes"/> relies on, so it works
    /// for any chromosome storing double gene values (e.g. a DoubleArrayChromosome).
    /// </summary>
    public class ShiftedFitness : IFitness
    {
        private readonly IFitness _inner;
        private readonly double[] _offset;

        /// <summary>The per-dimension offset applied to the optimum (x* becomes x* + offset).</summary>
        public IReadOnlyList<double> Offset => _offset;

        /// <param name="inner">The benchmark function to translate (reused as-is).</param>
        /// <param name="offset">
        /// Per-dimension offset vector. Coordinates beyond its length are left unshifted,
        /// so the decorator stays dimension-agnostic like the functions it wraps.
        /// </param>
        public ShiftedFitness(IFitness inner, double[] offset)
        {
            ArgumentNullException.ThrowIfNull(inner);
            ArgumentNullException.ThrowIfNull(offset);
            _inner = inner;
            _offset = (double[])offset.Clone();
        }

        public double Evaluate(IChromosome chromosome)
        {
            // Translate the chromosome's coordinates by -offset, then delegate to the
            // inner function. Cloning keeps the caller's chromosome untouched and works
            // for any ChromosomeBase representation (Clone + ReplaceGene are standard).
            double[] x = KnownFunctionGenes.AsDoubles(chromosome);
            IChromosome translated = chromosome.Clone();
            for (int i = 0; i < x.Length; i++)
            {
                double off = i < _offset.Length ? _offset[i] : 0.0;
                translated.ReplaceGene(i, new Gene(x[i] - off));
            }
            return _inner.Evaluate(translated);
        }
    }

    /// <summary>
    /// Factory for the shift vectors fed to <see cref="ShiftedFitness"/>. Provides the
    /// legacy uniform scalar shift, the centered (no-shift) baseline, and the seeded
    /// per-dimension vector that the center-bias protocol calls for.
    /// </summary>
    public static class ShiftVectors
    {
        /// <summary>
        /// Uniform scalar shift (the PR#87 embryo): every one of <paramref name="n"/>
        /// coordinates is set to <paramref name="value"/>. Kept for back-compatible,
        /// easy-to-reason-about comparisons.
        /// </summary>
        public static double[] Uniform(int n, double value)
        {
            if (n < 0) throw new ArgumentOutOfRangeException(nameof(n));
            double[] v = new double[n];
            for (int i = 0; i < n; i++) v[i] = value;
            return v;
        }

        /// <summary>
        /// No shift: an all-zero vector of length <paramref name="n"/>. Wrapping a
        /// function with this is equivalent to the function itself — the centered
        /// baseline of a centered-vs-shifted comparison.
        /// </summary>
        public static double[] None(int n) => Uniform(n, 0.0);

        /// <summary>
        /// Per-dimension shift vector with each coordinate drawn uniformly from
        /// <c>[-magnitude, +magnitude]</c> using a seeded RNG. Same
        /// <c>(n, magnitude, seed)</c> always yields the same vector, so a shifted
        /// benchmark is reproducible across runs and machines. Unlike a single scalar,
        /// the offset differs per dimension — defeating optima that line up on the
        /// domain diagonal as well as those at the center.
        /// </summary>
        public static double[] Seeded(int n, double magnitude, int seed)
        {
            if (n < 0) throw new ArgumentOutOfRangeException(nameof(n));
            if (magnitude < 0) throw new ArgumentOutOfRangeException(nameof(magnitude));
            var rng = new Random(seed);
            double[] v = new double[n];
            for (int i = 0; i < n; i++) v[i] = (rng.NextDouble() * 2.0 - 1.0) * magnitude;
            return v;
        }
    }
}
