using GeneticSharp;

namespace MetaGeneticSharp
{
    // ===========================================================================
    // Standard continuous benchmark functions for metaheuristic comparison.
    // ---------------------------------------------------------------------------
    // Each function minimizes a continuous surface; GeneticSharp *maximizes*
    // fitness, so every Evaluate() returns the negation of the true objective.
    // Global optimum is at x* for all functions (f(x*) = 0 unless noted); the
    // benchmark harness measures how close each metaheuristic gets.
    //
    // Gene access is GEOMETRY-AGNOSTIC: we read gene values via GetGenes() and
    // cast each Gene.Value to double. This works for any chromosome that stores
    // double gene values transparently (e.g. a DoubleArrayChromosome), which is
    // exactly the representation the geometric compound metaheuristics (WOA, EO)
    // require via their GeometricConverter<double>. FloatingPointChromosome is
    // NOT used because its binary-encoded genes are not directly castable to
    // double and the geometric compounds do not operate on it.
    //
    // All functions are dimension-agnostic: they read as many genes as the
    // chromosome provides. Recommended bounds per function are documented on
    // each class; the harness seeds the initial DoubleArrayChromosome within
    // those bounds.
    // ===========================================================================

    /// <summary>
    /// Reads the gene values of any chromosome storing double gene values as a double[].
    /// This is the representation contract the geometric compound metaheuristics rely on.
    /// </summary>
    public static class KnownFunctionGenes
    {
        public static double[] AsDoubles(IChromosome chromosome)
            => chromosome.GetGenes().Select(g => Convert.ToDouble(g.Value, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
    }

    /// <summary>
    /// Sphere (De Jong F1). f(x) = sum(x_i^2). Convex, unimodal.
    /// Optimum f(0)=0. Recommended bounds [-5.12, 5.12].
    /// </summary>
    public class SphereFitness : IFitness
    {
        public double Evaluate(IChromosome chromosome)
        {
            double[] x = KnownFunctionGenes.AsDoubles(chromosome);
            double s = 0.0;
            for (int i = 0; i < x.Length; i++) s += x[i] * x[i];
            return -s;
        }
    }

    /// <summary>
    /// Rastrigin. f(x) = 10*n + sum(x_i^2 - 10*cos(2*pi*x_i)). Highly multimodal.
    /// Optimum f(0)=0. Recommended bounds [-5.12, 5.12].
    /// </summary>
    public class RastriginFitness : IFitness
    {
        public double Evaluate(IChromosome chromosome)
        {
            double[] x = KnownFunctionGenes.AsDoubles(chromosome);
            double s = 10.0 * x.Length;
            for (int i = 0; i < x.Length; i++) s += x[i] * x[i] - 10.0 * Math.Cos(2.0 * Math.PI * x[i]);
            return -s;
        }
    }

    /// <summary>
    /// Rosenbrock (Valley). f(x) = sum(100*(x_{i+1}-x_i^2)^2 + (1-x_i)^2).
    /// Flat valley toward (1,1,...,1). Optimum f(1)=0. Recommended bounds [-2.048, 2.048].
    /// </summary>
    public class RosenbrockFitness : IFitness
    {
        public double Evaluate(IChromosome chromosome)
        {
            double[] x = KnownFunctionGenes.AsDoubles(chromosome);
            double s = 0.0;
            for (int i = 0; i < x.Length - 1; i++)
            {
                double a = x[i + 1] - x[i] * x[i];
                double b = 1.0 - x[i];
                s += 100.0 * a * a + b * b;
            }
            return -s;
        }
    }

    /// <summary>
    /// Ackley. f(x) = -20*exp(-0.2*sqrt(mean(x^2))) - exp(mean(cos(2*pi*x))) + 20 + e.
    /// Many local minima, one global. Optimum f(0)=0. Recommended bounds [-32.0, 32.0].
    /// </summary>
    public class AckleyFitness : IFitness
    {
        public double Evaluate(IChromosome chromosome)
        {
            double[] x = KnownFunctionGenes.AsDoubles(chromosome);
            int n = x.Length;
            double sumSq = 0.0, sumCos = 0.0;
            for (int i = 0; i < n; i++) { sumSq += x[i] * x[i]; sumCos += Math.Cos(2.0 * Math.PI * x[i]); }
            double root = Math.Sqrt(sumSq / n);
            double f = -20.0 * Math.Exp(-0.2 * root) - Math.Exp(sumCos / n) + 20.0 + Math.E;
            return -f;
        }
    }

    /// <summary>
    /// Griewank. f(x) = sum(x_i^2)/4000 - prod(cos(x_i/sqrt(i))) + 1.
    /// Many local minima, decreasing amplitude. Optimum f(0)=0. Recommended bounds [-600, 600].
    /// </summary>
    public class GriewankFitness : IFitness
    {
        public double Evaluate(IChromosome chromosome)
        {
            double[] x = KnownFunctionGenes.AsDoubles(chromosome);
            double sumSq = 0.0, prod = 1.0;
            for (int i = 0; i < x.Length; i++)
            {
                sumSq += x[i] * x[i];
                prod *= Math.Cos(x[i] / Math.Sqrt(i + 1));
            }
            double f = sumSq / 4000.0 - prod + 1.0;
            return -f;
        }
    }

    /// <summary>
    /// Schwefel. f(x) = 418.9829*n - sum(x_i*sin(sqrt(|x_i|))). Deceptive global optimum far from next-best.
    /// Optimum at x_i=420.9687, f=0. Recommended bounds [-500, 500].
    /// </summary>
    public class SchwefelFitness : IFitness
    {
        public double Evaluate(IChromosome chromosome)
        {
            double[] x = KnownFunctionGenes.AsDoubles(chromosome);
            double s = 418.9829 * x.Length;
            for (int i = 0; i < x.Length; i++) s -= x[i] * Math.Sin(Math.Sqrt(Math.Abs(x[i])));
            return -s;
        }
    }

    /// <summary>
    /// Michalewicz. f(x) = -sum(sin(x_i)*sin(i*x_i^2/pi)^(2*m)). Steep ridges, m=10.
    /// Optimum depends on n (n=2: ~-1.8013). Recommended bounds [0, pi].
    /// </summary>
    public class MichalewiczFitness : IFitness
    {
        private readonly double _m = 10.0;
        public double Evaluate(IChromosome chromosome)
        {
            double[] x = KnownFunctionGenes.AsDoubles(chromosome);
            double s = 0.0;
            for (int i = 0; i < x.Length; i++)
            {
                double arg = (i + 1) * x[i] * x[i] / Math.PI;
                s += Math.Sin(x[i]) * Math.Pow(Math.Sin(arg), 2.0 * _m);
            }
            return s; // Michalewicz minimizes -s; fitness (maximize) = s, so return s directly.
        }
    }

    /// <summary>
    /// Zakharov. f(x) = sum(x_i^2) + (sum(0.5*i*x_i))^2 + (sum(0.5*i*x_i))^4.
    /// Unimodal, ill-conditioned. Optimum f(0)=0. Recommended bounds [-5, 10].
    /// </summary>
    public class ZakharovFitness : IFitness
    {
        public double Evaluate(IChromosome chromosome)
        {
            double[] x = KnownFunctionGenes.AsDoubles(chromosome);
            double sumSq = 0.0, weighted = 0.0;
            for (int i = 0; i < x.Length; i++) { sumSq += x[i] * x[i]; weighted += 0.5 * (i + 1) * x[i]; }
            double f = sumSq + weighted * weighted + Math.Pow(weighted, 4);
            return -f;
        }
    }

    /// <summary>
    /// Booth (2D). f(x,y) = (x+2y-7)^2 + (2x+y-5)^2. Unimodal, plate-shaped.
    /// Optimum f(1,3)=0. Recommended bounds [-10, 10]. Fixed 2D.
    /// </summary>
    public class BoothFitness : IFitness
    {
        public double Evaluate(IChromosome chromosome)
        {
            double[] x = KnownFunctionGenes.AsDoubles(chromosome);
            // L4 guard (additive robustness): Booth is a fixed 2-D function and indexes x[0]/x[1]
            // directly. A chromosome with fewer than 2 genes would otherwise throw a bare
            // IndexOutOfRangeException with no context; this surfaces the dimensional contract as
            // a clear ArgumentException instead. Credit jsboige @ d05826fd for the benchmark suite.
            if (x.Length < 2)
                throw new ArgumentException(
                    $"Booth is a fixed 2-D function and requires at least 2 genes; got {x.Length}.",
                    nameof(chromosome));
            double a = x[0] + 2.0 * x[1] - 7.0;
            double b = 2.0 * x[0] + x[1] - 5.0;
            return -(a * a + b * b);
        }
    }

    /// <summary>
    /// Dixon-Price. f(x) = (x_1-1)^2 + sum(i*(2*x_i^2 - x_{i-1})^2).
    /// Unimodal, narrow valley. Optimum x_i = 2^(-(2^i-2)/2^i), f=0. Bounds [-10, 10].
    /// </summary>
    public class DixonPriceFitness : IFitness
    {
        public double Evaluate(IChromosome chromosome)
        {
            double[] x = KnownFunctionGenes.AsDoubles(chromosome);
            // L4 guard (additive robustness): Dixon-Price indexes x[0] directly (the (x_1-1)^2
            // term). An empty chromosome would otherwise throw a bare IndexOutOfRangeException;
            // this surfaces the requirement as a clear ArgumentException. A single gene is a valid
            // degenerate case (the valley sum is empty). Credit jsboige @ d05826fd.
            if (x.Length < 1)
                throw new ArgumentException(
                    "Dixon-Price requires at least 1 gene; got an empty chromosome.",
                    nameof(chromosome));
            double s = (x[0] - 1.0) * (x[0] - 1.0);
            for (int i = 1; i < x.Length; i++)
            {
                double t = 2.0 * x[i] * x[i] - x[i - 1];
                s += (i + 1) * t * t;
            }
            return -s;
        }
    }

    // ===========================================================================
    // Bounds registry: recommended (min, max) per function, keyed by type.
    // The benchmark harness uses this to seed the initial DoubleArrayChromosome.
    // ===========================================================================

    /// <summary>
    /// Recommended search bounds [min, max] for each benchmark function.
    /// </summary>
    public static class KnownFunctionsBounds
    {
        public static (double min, double max) For(Type fitnessType) => fitnessType.Name switch
        {
            nameof(SphereFitness)        => (-5.12, 5.12),
            nameof(RastriginFitness)     => (-5.12, 5.12),
            nameof(RosenbrockFitness)    => (-2.048, 2.048),
            nameof(AckleyFitness)        => (-32.0, 32.0),
            nameof(GriewankFitness)      => (-600.0, 600.0),
            nameof(SchwefelFitness)      => (-500.0, 500.0),
            nameof(MichalewiczFitness)   => (0.0, Math.PI),
            nameof(ZakharovFitness)      => (-5.0, 10.0),
            nameof(BoothFitness)         => (-10.0, 10.0),
            nameof(DixonPriceFitness)    => (-10.0, 10.0),
            _ => (-5.12, 5.12)
        };
    }
}
