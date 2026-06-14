using GeneticSharp;
using MetaGeneticSharp;

namespace MetaGeneticSharp.Extensions.Tests;

/// <summary>
/// Unit tests for the standard continuous benchmark functions in <see cref="KnownFunctions"/>.
///
/// Each function minimizes a continuous surface, but GeneticSharp *maximizes* fitness,
/// so every <see cref="IFitness.Evaluate"/> returns the negation of the true objective.
/// These tests pin two invariants per function:
///   1. <b>Optimum at x*</b>: the documented global optimum x* evaluates to the documented
///      optimum value (≈ 0 for all but Michalewicz, whose n=2 optimum is ≈ 1.8013).
///   2. <b>Maximize-via-negation convention</b>: a strictly suboptimal point yields a strictly
///      lower fitness than x* — proving the negation turns a minimization into a maximization
///      the GA engine can climb. A few functions get an exact-value spot check too.
/// </summary>
[TestFixture]
public class KnownFunctionsTests
{
    // The chromosome helper stores doubles verbatim; KnownFunctionGenes.AsDoubles reads them back.
    private static DoubleArrayChromosome Chrom(params double[] xs) => new DoubleArrayChromosome(xs);

    private static double Evaluate(IFitness fitness, params double[] xs) => fitness.Evaluate(Chrom(xs));

    // =========================================================================
    // Sphere (De Jong F1): f(x) = sum(x_i^2), optimum f(0)=0, bounds [-5.12, 5.12].
    // =========================================================================
    [Test]
    public void Sphere_OptimumAtZero_IsZero()
    {
        Assert.That(Evaluate(new SphereFitness(), 0, 0, 0), Is.EqualTo(0.0).Within(1e-12));
    }

    [Test]
    public void Sphere_NegatesObjective_SuboptimalIsLower()
    {
        // f(1,1) = 2 -> fitness = -2 (exact negation; ChromosomeBase requires >= 2 genes).
        Assert.That(Evaluate(new SphereFitness(), 1.0, 1.0), Is.EqualTo(-2.0));
        // f(2,1) = 5 -> fitness = -5, strictly worse than the optimum (0).
        Assert.That(Evaluate(new SphereFitness(), 2.0, 1.0), Is.EqualTo(-5.0));
    }

    // =========================================================================
    // Rastrigin: f(x)=10n+sum(x_i^2-10cos(2pi x_i)), optimum f(0)=0, bounds [-5.12,5.12].
    // =========================================================================
    [Test]
    public void Rastrigin_OptimumAtZero_IsZero()
    {
        Assert.That(Evaluate(new RastriginFitness(), 0, 0), Is.EqualTo(0.0).Within(1e-9));
    }

    [Test]
    public void Rastrigin_NegatesObjective_SuboptimalIsLower()
    {
        var opt = Evaluate(new RastriginFitness(), 0, 0);
        // At x=(1,1): cos(2pi)=1, so per-dim term = 1 - 10 = -9; total = 20 + 2*(-9) = 2 -> fitness -2.
        Assert.That(Evaluate(new RastriginFitness(), 1.0, 1.0), Is.EqualTo(-2.0).Within(1e-9));
        Assert.That(Evaluate(new RastriginFitness(), 1.0, 1.0), Is.LessThan(opt));
    }

    // =========================================================================
    // Rosenbrock (Valley): optimum f(1,...,1)=0, bounds [-2.048, 2.048].
    // =========================================================================
    [Test]
    public void Rosenbrock_OptimumAtOne_IsZero()
    {
        Assert.That(Evaluate(new RosenbrockFitness(), 1, 1, 1), Is.EqualTo(0.0).Within(1e-9));
    }

    [Test]
    public void Rosenbrock_NegatesObjective_SuboptimalIsLower()
    {
        var opt = Evaluate(new RosenbrockFitness(), 1, 1);
        // At x=(0,0): 100*(0-0)^2 + (1-0)^2 = 1 -> fitness -1 < 0.
        Assert.That(Evaluate(new RosenbrockFitness(), 0.0, 0.0), Is.EqualTo(-1.0).Within(1e-9));
        Assert.That(Evaluate(new RosenbrockFitness(), 0.0, 0.0), Is.LessThan(opt));
    }

    // =========================================================================
    // Ackley: optimum f(0)=0, bounds [-32, 32].
    // =========================================================================
    [Test]
    public void Ackley_OptimumAtZero_IsZero()
    {
        Assert.That(Evaluate(new AckleyFitness(), 0, 0, 0), Is.EqualTo(0.0).Within(1e-9));
    }

    [Test]
    public void Ackley_NegatesObjective_SuboptimalIsLower()
    {
        var opt = Evaluate(new AckleyFitness(), 0, 0);
        Assert.That(Evaluate(new AckleyFitness(), 5.0, 5.0), Is.LessThan(opt));
    }

    // =========================================================================
    // Griewank: optimum f(0)=0, bounds [-600, 600].
    // =========================================================================
    [Test]
    public void Griewank_OptimumAtZero_IsZero()
    {
        Assert.That(Evaluate(new GriewankFitness(), 0, 0, 0), Is.EqualTo(0.0).Within(1e-9));
    }

    [Test]
    public void Griewank_NegatesObjective_SuboptimalIsLower()
    {
        var opt = Evaluate(new GriewankFitness(), 0, 0);
        Assert.That(Evaluate(new GriewankFitness(), 100.0, 100.0), Is.LessThan(opt));
    }

    // =========================================================================
    // Schwefel: optimum at x_i=420.9687, f≈0. The published constant 418.9829 is the
    // approximate max of x*sin(sqrt|x|), so the optimum fitness is ≈ 0 (not exact).
    // bounds [-500, 500].
    // =========================================================================
    [Test]
    public void Schwefel_OptimumAt420_IsNearZero()
    {
        // At the optimum, each x*sin(sqrt|x|) term ≈ 418.9829, cancelling the leading constant.
        Assert.That(Evaluate(new SchwefelFitness(), 420.9687, 420.9687), Is.EqualTo(0.0).Within(0.5));
    }

    [Test]
    public void Schwefel_NegatesObjective_SuboptimalIsLower()
    {
        var opt = Evaluate(new SchwefelFitness(), 420.9687, 420.9687);
        // At x=(0,0): both sin terms vanish, so f = 418.9829*2 = 837.97 -> fitness ≈ -838 (deep minimum).
        Assert.That(Evaluate(new SchwefelFitness(), 0.0, 0.0), Is.LessThan(opt));
        Assert.That(Evaluate(new SchwefelFitness(), 0.0, 0.0), Is.Negative);
    }

    // =========================================================================
    // Michalewicz (n=2): optimum f≈-1.8013 at x≈(2.2029, 1.5708). The code returns s
    // directly (fitness = s = -f), so the *maximum* fitness at the optimum is ≈ +1.8013.
    // bounds [0, pi].
    // =========================================================================
    [Test]
    public void Michalewicz_Optimum2D_IsNear1_8013()
    {
        // s(2.2029, 1.5708) ≈ 0.7992 + 1.0 ≈ 1.7992 ≈ 1.8013 (m=10 makes the surface steep).
        Assert.That(Evaluate(new MichalewiczFitness(), 2.2029, 1.5708), Is.EqualTo(1.8013).Within(0.05));
    }

    [Test]
    public void Michalewicz_NegatesObjective_SuboptimalIsLower()
    {
        var opt = Evaluate(new MichalewiczFitness(), 2.2029, 1.5708);
        // At x=(0,0): sin(0)=0, so s=0 < 1.8013.
        Assert.That(Evaluate(new MichalewiczFitness(), 0.0, 0.0), Is.EqualTo(0.0).Within(1e-12));
        Assert.That(Evaluate(new MichalewiczFitness(), 0.0, 0.0), Is.LessThan(opt));
    }

    // =========================================================================
    // Zakharov: f(x)=sum(x_i^2)+(sum(0.5*i*x_i))^2+(sum(0.5*i*x_i))^4, optimum f(0)=0.
    // bounds [-5, 10].
    // =========================================================================
    [Test]
    public void Zakharov_OptimumAtZero_IsZero()
    {
        Assert.That(Evaluate(new ZakharovFitness(), 0, 0, 0), Is.EqualTo(0.0).Within(1e-12));
    }

    [Test]
    public void Zakharov_NegatesObjective_SuboptimalIsLower()
    {
        var opt = Evaluate(new ZakharovFitness(), 0, 0);
        // At x=(1,1): sumSq=2, weighted=0.5*1+0.5*2=1.5, f=2+1.5^2+1.5^4=9.3125 -> fitness -9.3125.
        Assert.That(Evaluate(new ZakharovFitness(), 1.0, 1.0), Is.EqualTo(-9.3125).Within(1e-9));
        Assert.That(Evaluate(new ZakharovFitness(), 1.0, 1.0), Is.LessThan(opt));
    }

    // =========================================================================
    // Booth (2D, fixed): f(x,y)=(x+2y-7)^2+(2x+y-5)^2, optimum f(1,3)=0. bounds [-10,10].
    // =========================================================================
    [Test]
    public void Booth_OptimumAt1_3_IsZero()
    {
        Assert.That(Evaluate(new BoothFitness(), 1.0, 3.0), Is.EqualTo(0.0).Within(1e-12));
    }

    [Test]
    public void Booth_NegatesObjective_SuboptimalIsLower()
    {
        var opt = Evaluate(new BoothFitness(), 1.0, 3.0);
        // At x=(0,0): a=-7, b=-5, f=49+25=74 -> fitness -74.
        Assert.That(Evaluate(new BoothFitness(), 0.0, 0.0), Is.EqualTo(-74.0).Within(1e-9));
        Assert.That(Evaluate(new BoothFitness(), 0.0, 0.0), Is.LessThan(opt));
    }

    // =========================================================================
    // Dixon-Price: optimum x_i=2^(-(2^i-2)/2^i) -> x1=1, x2=2^-0.5≈0.7071, f=0.
    // bounds [-10, 10].
    // =========================================================================
    [Test]
    public void DixonPrice_OptimumAtClosedForm_IsZero()
    {
        double x1 = 1.0;                                  // 2^(-(2^1-2)/2^1) = 2^0 = 1
        double x2 = Math.Pow(2.0, -0.5);                  // 2^(-(2^2-2)/2^2) = 2^-0.5
        Assert.That(Evaluate(new DixonPriceFitness(), x1, x2), Is.EqualTo(0.0).Within(1e-9));
    }

    [Test]
    public void DixonPrice_NegatesObjective_SuboptimalIsLower()
    {
        var opt = Evaluate(new DixonPriceFitness(), 1.0, Math.Pow(2.0, -0.5));
        // At x=(0,0): (0-1)^2 + 2*(0-0)^2 = 1 -> fitness -1.
        Assert.That(Evaluate(new DixonPriceFitness(), 0.0, 0.0), Is.EqualTo(-1.0).Within(1e-9));
        Assert.That(Evaluate(new DixonPriceFitness(), 0.0, 0.0), Is.LessThan(opt));
    }

    // =========================================================================
    // KnownFunctionsBounds registry: recommended (min, max) per function type.
    // =========================================================================
    [Test]
    public void Bounds_ForEachFunction_MatchesRecommendedRange()
    {
        Assert.That(KnownFunctionsBounds.For(typeof(SphereFitness)), Is.EqualTo((-5.12, 5.12)));
        Assert.That(KnownFunctionsBounds.For(typeof(AckleyFitness)), Is.EqualTo((-32.0, 32.0)));
        Assert.That(KnownFunctionsBounds.For(typeof(GriewankFitness)), Is.EqualTo((-600.0, 600.0)));
        Assert.That(KnownFunctionsBounds.For(typeof(SchwefelFitness)), Is.EqualTo((-500.0, 500.0)));
        Assert.That(KnownFunctionsBounds.For(typeof(MichalewiczFitness)), Is.EqualTo((0.0, Math.PI)));
        Assert.That(KnownFunctionsBounds.For(typeof(BoothFitness)), Is.EqualTo((-10.0, 10.0)));
    }

    [Test]
    public void Bounds_ForUnknownType_FallsBackToSphereRange()
    {
        Assert.That(KnownFunctionsBounds.For(typeof(object)), Is.EqualTo((-5.12, 5.12)));
    }
}
