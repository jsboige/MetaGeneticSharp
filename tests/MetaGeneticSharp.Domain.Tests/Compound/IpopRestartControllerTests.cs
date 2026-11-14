using GeneticSharp;
using MetaGeneticSharp;

namespace MetaGeneticSharp.Domain.Tests.Compound;

public class IpopRestartControllerTests
{
    private sealed class RandomDoubleChromosome : ChromosomeBase
    {
        private readonly double min;
        private readonly double max;

        public RandomDoubleChromosome(double min, double max, int length) : base(length)
        {
            this.min = min;
            this.max = max;
            for (int i = 0; i < Length; i++) ReplaceGene(i, GenerateGene(i));
        }

        public override IChromosome CreateNew() => new RandomDoubleChromosome(min, max, Length);
        public override Gene GenerateGene(int index) => new(min + RandomizationProvider.Current.GetDouble() * (max - min));
        public double[] GetDoubleValues() => GetGenes().Select(g => (double)g.Value).ToArray();
    }

    [Test]
    public void Run_DoublesPopulationAndKeepsBestAcrossRuns()
    {
        var fitness = new[] { 1.0, 4.0, 2.0 };
        var result = IpopRestartController.Run(6, 2,
            (size, restart) => new IpopRunResult<int>(size, fitness[restart], false));

        Assert.Multiple(() =>
        {
            Assert.That(result.PopulationSizes, Is.EqualTo(new[] { 6, 12, 24 }));
            Assert.That(result.Value, Is.EqualTo(12));
            Assert.That(result.Fitness, Is.EqualTo(4.0));
            Assert.That(result.Stopped, Is.False);
        });
    }

    [Test]
    public void Run_StopsWithoutStartingAnotherRun()
    {
        var result = IpopRestartController.Run(4, 10,
            (size, restart) => new IpopRunResult<int>(size, restart, restart == 1));
        Assert.That(result.PopulationSizes, Is.EqualTo(new[] { 4, 8 }));
        Assert.That(result.Stopped, Is.True);
    }

    [Test]
    public void Run_ExecutesIndependentCmaEsPopulations()
    {
        var observed = new List<int>();
        var result = IpopRestartController.Run(4, 2, (size, restart) =>
        {
            var strategy = new CovarianceMatrixAdaptationEvolutionStrategy { MaxGenerations = 5 };
            strategy.SetGeometricConverter(new GeometricConverter<double>
            {
                GeneToDoubleConverter = (_, value) => value,
                DoubleToGeneConverter = (_, value) => value,
            });
            var chromosome = new RandomDoubleChromosome(min: -10, max: 10, length: 2);
            var population = new MetaPopulation(size, size, chromosome);
            var fitness = new FuncFitness(c => -((RandomDoubleChromosome)c).GetDoubleValues().Sum(x => x * x));
            var ga = new MetaGeneticAlgorithm(population, fitness, new EliteSelection(),
                new UniformCrossover(0.5f), new UniformMutation(true), strategy.Build())
            {
                Termination = new GenerationNumberTermination(5)
            };
            ga.Start();
            observed.Add(population.CurrentGeneration.Chromosomes.Count);
            return new IpopRunResult<IChromosome>(ga.BestChromosome, ga.BestChromosome.Fitness!.Value, false);
        });

        Assert.That(observed, Is.EqualTo(new[] { 4, 8, 16 }));
        Assert.That(result.PopulationSizes, Is.EqualTo(observed));
        Assert.That(double.IsFinite(result.Fitness), Is.True);
    }

    [Test]
    public void Run_RejectsInvalidInputsAndNanFitness()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => IpopRestartController.Run(1, 0,
            (size, restart) => new IpopRunResult<int>(size, 0, false)));
        Assert.Throws<ArgumentOutOfRangeException>(() => IpopRestartController.Run(2, -1,
            (size, restart) => new IpopRunResult<int>(size, 0, false)));
        Assert.Throws<InvalidOperationException>(() => IpopRestartController.Run(2, 0,
            (size, restart) => new IpopRunResult<int>(size, double.NaN, false)));
        Assert.Throws<OverflowException>(() => IpopRestartController.Run(int.MaxValue, 1,
            (size, restart) => new IpopRunResult<int>(size, 0, false)));
    }
}
