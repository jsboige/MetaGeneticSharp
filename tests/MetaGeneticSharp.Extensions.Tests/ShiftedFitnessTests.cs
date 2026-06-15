using GeneticSharp;
using MetaGeneticSharp;

namespace MetaGeneticSharp.Extensions.Tests;

/// <summary>
/// Unit tests for the center-bias tooling: <see cref="ShiftedFitness"/> and the
/// <see cref="ShiftVectors"/> factory.
///
/// The decorator translates a benchmark function's coordinates so the optimum moves
/// from x* to x* + offset: <c>f_shifted(x) = f_inner(x - offset)</c>. These tests pin:
///   1. <b>Factory shapes</b>: Uniform/None build the documented constant vectors;
///      Seeded is reproducible per (n, magnitude, seed), differs across seeds, stays
///      within +/-magnitude, and varies per dimension (not a single scalar).
///   2. <b>Decorator semantics</b>: a zero offset is the identity (centered baseline);
///      a non-zero offset relocates the optimum and matches the inner function at the
///      translated point; a short offset leaves the extra dimensions unshifted.
///   3. <b>Defensive copy + argument guards</b>.
/// </summary>
[TestFixture]
public class ShiftedFitnessTests
{
    private static DoubleArrayChromosome Chrom(params double[] xs) => new DoubleArrayChromosome(xs);

    private static double Evaluate(IFitness fitness, params double[] xs) => fitness.Evaluate(Chrom(xs));

    // =========================================================================
    // ShiftVectors.Uniform / None: the legacy scalar shift and centered baseline.
    // =========================================================================
    [Test]
    public void Uniform_BuildsConstantVector()
    {
        Assert.That(ShiftVectors.Uniform(3, 2.5), Is.EqualTo(new[] { 2.5, 2.5, 2.5 }));
    }

    [Test]
    public void None_BuildsZeroVector()
    {
        Assert.That(ShiftVectors.None(4), Is.EqualTo(new[] { 0.0, 0.0, 0.0, 0.0 }));
    }

    [Test]
    public void Uniform_NegativeLength_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ShiftVectors.Uniform(-1, 1.0));
    }

    // =========================================================================
    // ShiftVectors.Seeded: per-dimension seeded offsets.
    // =========================================================================
    [Test]
    public void Seeded_SameSeed_IsReproducible()
    {
        double[] a = ShiftVectors.Seeded(5, 10.0, 42);
        double[] b = ShiftVectors.Seeded(5, 10.0, 42);
        Assert.That(b, Is.EqualTo(a));
    }

    [Test]
    public void Seeded_DifferentSeed_DiffersElementwise()
    {
        double[] a = ShiftVectors.Seeded(5, 10.0, 42);
        double[] b = ShiftVectors.Seeded(5, 10.0, 7);
        Assert.That(b, Is.Not.EqualTo(a));
    }

    [Test]
    public void Seeded_StaysWithinMagnitudeBounds()
    {
        double[] v = ShiftVectors.Seeded(64, 10.0, 99);
        Assert.That(v, Has.Length.EqualTo(64));
        foreach (double x in v)
            Assert.That(Math.Abs(x), Is.LessThanOrEqualTo(10.0));
    }

    [Test]
    public void Seeded_VariesPerDimension_NotAScalar()
    {
        // The whole point of generalizing the scalar shift: the offsets are not all equal.
        double[] v = ShiftVectors.Seeded(8, 10.0, 42);
        Assert.That(v.Distinct().Count(), Is.GreaterThan(1));
    }

    [Test]
    public void Seeded_ZeroMagnitude_IsAllZero()
    {
        Assert.That(ShiftVectors.Seeded(3, 0.0, 42), Is.EqualTo(new[] { 0.0, 0.0, 0.0 }));
    }

    [Test]
    public void Seeded_NegativeMagnitude_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ShiftVectors.Seeded(3, -1.0, 42));
    }

    // =========================================================================
    // ShiftedFitness: decorator semantics over Sphere (optimum f(0)=0).
    // =========================================================================
    [Test]
    public void ShiftedFitness_ZeroOffset_EqualsInner()
    {
        // Wrapping with a no-shift vector reproduces the inner function exactly:
        // this is the centered baseline of a centered-vs-shifted comparison.
        var inner = new SphereFitness();
        var shifted = new ShiftedFitness(inner, ShiftVectors.None(2));
        Assert.That(Evaluate(shifted, 1.0, 1.0), Is.EqualTo(Evaluate(inner, 1.0, 1.0)));
        Assert.That(Evaluate(shifted, 0.0, 0.0), Is.EqualTo(0.0).Within(1e-12));
    }

    [Test]
    public void ShiftedFitness_RelocatesOptimum_ToXStarPlusOffset()
    {
        // Sphere optimum is at 0; shifting by (2,3) moves the optimum to (2,3).
        var shifted = new ShiftedFitness(new SphereFitness(), new[] { 2.0, 3.0 });
        // New optimum value is 0 at the translated point...
        Assert.That(Evaluate(shifted, 2.0, 3.0), Is.EqualTo(0.0).Within(1e-12));
        // ...and the old center is now strictly suboptimal: f_inner(-2,-3) = -(4+9) = -13.
        Assert.That(Evaluate(shifted, 0.0, 0.0), Is.EqualTo(-13.0).Within(1e-12));
        Assert.That(Evaluate(shifted, 0.0, 0.0), Is.LessThan(Evaluate(shifted, 2.0, 3.0)));
    }

    [Test]
    public void ShiftedFitness_MatchesInnerAtTranslatedPoint()
    {
        // f_shifted(x) must equal f_inner(x - offset) for an arbitrary x.
        var inner = new SphereFitness();
        var shifted = new ShiftedFitness(inner, new[] { 2.0, 3.0 });
        // At x=(5,5): inner((3,2)) = -(9+4) = -13.
        Assert.That(Evaluate(shifted, 5.0, 5.0), Is.EqualTo(Evaluate(inner, 3.0, 2.0)).Within(1e-12));
        Assert.That(Evaluate(shifted, 5.0, 5.0), Is.EqualTo(-13.0).Within(1e-12));
    }

    [Test]
    public void ShiftedFitness_ShorterOffset_LeavesExtraDimsUnshifted()
    {
        // Offset of length 1 on a 2D problem: only dimension 0 is translated.
        var shifted = new ShiftedFitness(new SphereFitness(), new[] { 2.0 });
        // Optimum now at (2, 0): dim0 shifted by 2, dim1 unshifted.
        Assert.That(Evaluate(shifted, 2.0, 0.0), Is.EqualTo(0.0).Within(1e-12));
        // f_inner(-2, 0) = -4.
        Assert.That(Evaluate(shifted, 0.0, 0.0), Is.EqualTo(-4.0).Within(1e-12));
    }

    [Test]
    public void ShiftedFitness_DoesNotMutateCallerChromosome()
    {
        var shifted = new ShiftedFitness(new SphereFitness(), new[] { 2.0, 3.0 });
        var chromosome = Chrom(5.0, 5.0);
        shifted.Evaluate(chromosome);
        // The caller's chromosome is untouched (the decorator clones before translating).
        Assert.That(KnownFunctionGenes.AsDoubles(chromosome), Is.EqualTo(new[] { 5.0, 5.0 }));
    }

    [Test]
    public void ShiftedFitness_ExposesOffset_DefensivelyCopied()
    {
        double[] offset = { 2.0, 3.0 };
        var shifted = new ShiftedFitness(new SphereFitness(), offset);
        offset[0] = 999.0; // mutating the source array must not affect the decorator.
        Assert.That(shifted.Offset, Is.EqualTo(new[] { 2.0, 3.0 }));
    }

    [Test]
    public void ShiftedFitness_NullInner_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new ShiftedFitness(null!, new[] { 1.0 }));
    }

    [Test]
    public void ShiftedFitness_NullOffset_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new ShiftedFitness(new SphereFitness(), null!));
    }

    // =========================================================================
    // Center-bias signature: a shifted multimodal surface still has its optimum,
    // just relocated — wrapping Rastrigin by a seeded vector keeps f(x*+offset)=0.
    // =========================================================================
    [Test]
    public void ShiftedFitness_SeededVector_KeepsOptimumValueAtRelocatedPoint()
    {
        double[] offset = ShiftVectors.Seeded(2, 3.0, 42);
        var shifted = new ShiftedFitness(new RastriginFitness(), offset);
        // Rastrigin optimum f(0)=0 relocates to x = offset, value still 0.
        Assert.That(Evaluate(shifted, offset[0], offset[1]), Is.EqualTo(0.0).Within(1e-9));
        // The original center is no longer optimal.
        Assert.That(Evaluate(shifted, 0.0, 0.0), Is.LessThan(0.0));
    }
}
