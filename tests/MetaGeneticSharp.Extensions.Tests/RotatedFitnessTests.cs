using GeneticSharp;
using MetaGeneticSharp;

namespace MetaGeneticSharp.Extensions.Tests;

/// <summary>
/// Unit tests for the axis-alignment tooling: <see cref="RotatedFitness"/> and the
/// <see cref="RotationMatrices"/> factory.
///
/// The decorator rotates a benchmark function's coordinates by an orthogonal matrix
/// <c>M</c>: <c>f_rotated(x) = f_inner(M * x)</c>. These tests pin:
///   1. <b>Factory shapes</b>: Identity builds the identity matrix; Seeded is
///      reproducible per (n, seed), differs across seeds, and is ALWAYS orthogonal
///      (<c>M * Mᵀ = I</c>) by the Givens-product construction.
///   2. <b>Decorator semantics on a rotationally SYMMETRIC function (Sphere)</b>: an
///      orthogonal rotation preserves the norm, so rotated Sphere == Sphere everywhere —
///      the sanity check that the decorator is correct (a rotation should be a no-op on a
///      rotation-invariant landscape).
///   3. <b>Decorator semantics on a rotationally ASYMMETRIC function (Rosenbrock)</b>:
///      rotation reshapes the banana valley and relocates the optimum — the axis-bias
///      exposure that is the whole point (CEC-style rotated benchmarks).
///   4. <b>Composition with ShiftedFitness</b> (the full CEC shifted-then-rotated variant)
///      and defensive copy + argument guards.
/// </summary>
[TestFixture]
public class RotatedFitnessTests
{
    private static DoubleArrayChromosome Chrom(params double[] xs) => new DoubleArrayChromosome(xs);

    private static double Evaluate(IFitness fitness, params double[] xs) => fitness.Evaluate(Chrom(xs));

    private static double Sqrt2Half => Math.Sqrt(2.0) / 2.0;

    // A 45-degree rotation matrix [[c,-s],[s,c]] with c=s=sqrt(2)/2.
    private static double[,] Rotation45() => new[,] { { Sqrt2Half, -Sqrt2Half }, { Sqrt2Half, Sqrt2Half } };

    // =========================================================================
    // RotationMatrices.Identity: the un-rotated baseline.
    // =========================================================================
    [Test]
    public void Identity_BuildsIdentityMatrix()
    {
        double[,] m = RotationMatrices.Identity(3);
        Assert.That(m.GetLength(0), Is.EqualTo(3));
        Assert.That(m.GetLength(1), Is.EqualTo(3));
        Assert.That(RotationMatrices.IsOrthogonal(m), Is.True);
        // Diagonal 1, off-diagonal 0.
        for (int i = 0; i < 3; i++)
            for (int j = 0; j < 3; j++)
                Assert.That(m[i, j], Is.EqualTo(i == j ? 1.0 : 0.0));
    }

    [Test]
    public void Identity_NegativeDimension_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => RotationMatrices.Identity(-1));
    }

    // =========================================================================
    // RotationMatrices.Seeded: reproducible, distinct, ALWAYS orthogonal.
    // =========================================================================
    [Test]
    public void Seeded_SameSeed_IsReproducible()
    {
        double[,] a = RotationMatrices.Seeded(5, 42);
        double[,] b = RotationMatrices.Seeded(5, 42);
        Assert.That(b, Is.EqualTo(a));
    }

    [Test]
    public void Seeded_DifferentSeed_Differs()
    {
        double[,] a = RotationMatrices.Seeded(4, 42);
        double[,] b = RotationMatrices.Seeded(4, 7);
        Assert.That(b, Is.Not.EqualTo(a));
    }

    [Test]
    public void Seeded_IsAlwaysOrthogonal_GivensProduct()
    {
        // The whole point of the Givens-product construction: every seeded matrix is a
        // valid rotation (M * Mᵀ = I), at every dimension and seed.
        foreach (int n in new[] { 2, 3, 5, 10 })
            foreach (int seed in new[] { 0, 1, 42, 99 })
                Assert.That(RotationMatrices.IsOrthogonal(RotationMatrices.Seeded(n, seed)),
                    Is.True, $"non-orthogonal at n={n}, seed={seed}");
    }

    [Test]
    public void Seeded_IsNonTrivial_NotIdentity()
    {
        // A dense rotation differs from the identity (otherwise it would not mix axes).
        double[,] m = RotationMatrices.Seeded(3, 42);
        Assert.That(m, Is.Not.EqualTo(RotationMatrices.Identity(3)));
    }

    [Test]
    public void IsOrthogonal_RejectsNonOrthogonal()
    {
        // A shear/scaling matrix is not orthogonal.
        double[,] bad = { { 2.0, 0.0 }, { 0.0, 1.0 } };
        Assert.That(RotationMatrices.IsOrthogonal(bad), Is.False);
    }

    [Test]
    public void Seeded_NegativeDimension_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => RotationMatrices.Seeded(-1, 42));
    }

    // =========================================================================
    // RotatedFitness on a rotationally SYMMETRIC function (Sphere): an orthogonal
    // rotation preserves the norm, so f_rotated(x) == f_inner(x) everywhere. This is
    // the correctness sanity check — a rotation should be a no-op on a rotationally
    // invariant landscape.
    // =========================================================================
    [Test]
    public void RotatedFitness_Identity_EqualsInner()
    {
        var inner = new SphereFitness();
        var rotated = new RotatedFitness(inner, RotationMatrices.Identity(2));
        Assert.That(Evaluate(rotated, 1.0, 1.0), Is.EqualTo(Evaluate(inner, 1.0, 1.0)));
        Assert.That(Evaluate(rotated, 0.0, 0.0), Is.EqualTo(0.0).Within(1e-12));
    }

    [Test]
    public void RotatedFitness_OnSphere_IsRotationInvariant()
    {
        // Orthogonal M preserves ||x||, so Sphere(M*x) == Sphere(x) at every point — a
        // symmetric landscape is unchanged by rotation. (SphereFitness returns the
        // negated sum-of-squares, but the equality holds identically.)
        var inner = new SphereFitness();
        double[,] m = RotationMatrices.Seeded(2, 42);
        var rotated = new RotatedFitness(inner, m);
        foreach (double[] x in new[] { new[] { 1.0, 1.0 }, new[] { -2.0, 3.0 }, new[] { 0.5, -0.5 } })
            Assert.That(Evaluate(rotated, x), Is.EqualTo(Evaluate(inner, x)).Within(1e-9),
                $"Sphere not rotation-invariant at ({x[0]},{x[1]})");
    }

    // =========================================================================
    // RotatedFitness on a rotationally ASYMMETRIC function (Rosenbrock): rotation
    // reshapes the banana valley and relocates the optimum — the axis-bias exposure.
    // =========================================================================
    [Test]
    public void RotatedFitness_OnRosenbrock_RotationChangesTheLandscape()
    {
        // Rosenbrock is NOT rotation-invariant: the same point maps to a different value
        // once the coordinates are rotated. This is exactly the axis-alignment bias a
        // rotated benchmark is meant to expose.
        var inner = new RosenbrockFitness();
        var rotated = new RotatedFitness(inner, Rotation45());
        Assert.That(Evaluate(rotated, 1.0, 1.0),
            Is.Not.EqualTo(Evaluate(inner, 1.0, 1.0)).Within(1e-9));
    }

    [Test]
    public void RotatedFitness_OnRosenbrock_RelocatesTheOptimum()
    {
        // Rosenbrock's optimum sits at (1,1). A 45-degree rotation moves the optimum to
        // Mᵀ*(1,1) = (sqrt(2), 0): f_rotated(sqrt(2),0) = f_inner(M*(sqrt(2),0)) =
        // f_inner(1,1) = optimum (0). The original center is no longer optimal.
        var rotated = new RotatedFitness(new RosenbrockFitness(), Rotation45());
        Assert.That(Evaluate(rotated, Math.Sqrt(2.0), 0.0), Is.EqualTo(0.0).Within(1e-9));
        Assert.That(Evaluate(rotated, 1.0, 1.0), Is.LessThan(0.0));
    }

    // =========================================================================
    // Composition with ShiftedFitness: the full CEC shifted-then-rotated variant.
    // new RotatedFitness(new ShiftedFitness(inner, offset), Q) relocates AND rotates.
    // =========================================================================
    [Test]
    public void RotatedFitness_ComposesWithShiftedFitness()
    {
        // Shift the Sphere optimum to offset o, then rotate by 45 degrees. The combined
        // optimum lands at the rotated offset; the value is still the Sphere optimum (0).
        var offset = new[] { 2.0, 0.0 };
        var combined = new RotatedFitness(new ShiftedFitness(new SphereFitness(), offset), Rotation45());
        // ShiftedFitness(Sphere,o).Evaluate(x) = Sphere(x-o); rotated applies M:
        // Sphere(M*x - o). Optimum where M*x - o = 0 -> x = Mᵀ*o. For o=(2,0) and a 45-deg
        // rotation M=[[c,-s],[s,c]], Mᵀ*(2,0) = (2c, -2s) = (sqrt(2), -sqrt(2)).
        Assert.That(Evaluate(combined, Math.Sqrt(2.0), -Math.Sqrt(2.0)), Is.EqualTo(0.0).Within(1e-9));
        // The un-rotated shifted optimum (2,0) is no longer optimal after rotation.
        Assert.That(Evaluate(combined, 2.0, 0.0), Is.LessThan(0.0));
    }

    // =========================================================================
    // Defensive copy + argument guards (parity with ShiftedFitness).
    // =========================================================================
    [Test]
    public void RotatedFitness_DoesNotMutateCallerChromosome()
    {
        var rotated = new RotatedFitness(new SphereFitness(), Rotation45());
        var chromosome = Chrom(5.0, 5.0);
        rotated.Evaluate(chromosome);
        Assert.That(KnownFunctionGenes.AsDoubles(chromosome), Is.EqualTo(new[] { 5.0, 5.0 }));
    }

    [Test]
    public void RotatedFitness_ExposesMatrix_DefensivelyCopied()
    {
        double[,] m = Rotation45();
        var rotated = new RotatedFitness(new SphereFitness(), m);
        m[0, 0] = 999.0; // mutating the source matrix must not affect the decorator.
        double[,] exposed = rotated.Matrix;
        exposed[1, 1] = -999.0; // mutating the exposed matrix must not affect the decorator.
        Assert.That(rotated.Matrix[0, 0], Is.EqualTo(Sqrt2Half).Within(1e-12));
        Assert.That(rotated.Matrix[1, 1], Is.EqualTo(Sqrt2Half).Within(1e-12));
    }

    [Test]
    public void RotatedFitness_NullInner_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new RotatedFitness(null!, Rotation45()));
    }

    [Test]
    public void RotatedFitness_NullMatrix_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new RotatedFitness(new SphereFitness(), null!));
    }

    [Test]
    public void RotatedFitness_EmptyMatrix_Throws()
    {
        Assert.Throws<ArgumentException>(() => new RotatedFitness(new SphereFitness(), new double[0, 0]));
    }
}
