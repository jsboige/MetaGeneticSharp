#nullable disable

using System.Collections.Concurrent;
using GeneticSharp;

namespace MetaGeneticSharp
{
    /// <summary>
    /// Order-preserving population for metaheuristic evolution. Unlike GeneticSharp's
    /// Population, ending a generation never sorts chromosomes by fitness: metaheuristics
    /// address individuals by stable index across stages, so the order produced by
    /// reinsertion is the order kept.
    /// </summary>
    public class MetaPopulation : IMetaPopulation
    {
        public MetaPopulation(int minSize, int maxSize, IChromosome adamChromosome)
        {
            if (minSize < 2)
            {
                throw new ArgumentOutOfRangeException(nameof(minSize), "The minimum size for a population is 2 chromosomes.");
            }

            if (maxSize < minSize)
            {
                throw new ArgumentOutOfRangeException(nameof(maxSize), "The maximum size for a population should be equal or greater than minimum size.");
            }

            AdamChromosome = adamChromosome ?? throw new ArgumentNullException(nameof(adamChromosome));
            CreationDate = DateTime.Now;
            MinSize = minSize;
            MaxSize = maxSize;
            Generations = new List<Generation>();
            GenerationStrategy = new PerformanceGenerationStrategy(10);
        }

        public event EventHandler BestChromosomeChanged;

        public DateTime CreationDate { get; protected set; }

        public IList<Generation> Generations { get; protected set; }

        public Generation CurrentGeneration { get; protected set; }

        public int GenerationsNumber { get; protected set; }

        public int MinSize { get; set; }

        public int MaxSize { get; set; }

        public IChromosome BestChromosome { get; protected set; }

        public IGenerationStrategy GenerationStrategy { get; set; }

        public IChromosome AdamChromosome { get; }

        /// <summary>
        /// Thread-safe parameter store; metaheuristics persist their evolution context here.
        /// </summary>
        public IDictionary<string, object> Parameters { get; } = new ConcurrentDictionary<string, object>();

        public virtual void CreateInitialGeneration()
        {
            Generations = new List<Generation>();
            GenerationsNumber = 0;

            var chromosomes = new List<IChromosome>(MinSize);

            for (int i = 0; i < MinSize; i++)
            {
                var chromosome = AdamChromosome.CreateNew();

                if (chromosome == null)
                {
                    throw new InvalidOperationException("The Adam chromosome's 'CreateNew' method generated a null chromosome. This is a invalid behavior, please, check your chromosome code.");
                }

                chromosomes.Add(chromosome);
            }

            CreateNewGeneration(chromosomes);
        }

        public virtual void CreateNewGeneration(IList<IChromosome> chromosomes)
        {
            if (chromosomes == null)
            {
                throw new ArgumentNullException(nameof(chromosomes));
            }

            chromosomes.ValidateGenes();

            CurrentGeneration = new Generation(++GenerationsNumber, chromosomes);
            Generations.Add(CurrentGeneration);
            GenerationStrategy.RegisterNewGeneration(this);
        }

        public virtual void EndCurrentGeneration()
        {
            // Unlike GeneticSharp's Generation.End(), chromosome order is preserved:
            // metaheuristics address individuals by stable index across stages.
            var chromosomes = CurrentGeneration.Chromosomes;

            if (chromosomes.Any(c => !c.Fitness.HasValue))
            {
                throw new InvalidOperationException("There is unknown problem in current generation, because a chromosome has no fitness value.");
            }

            if (chromosomes.Count > MaxSize)
            {
                // Positional truncation (no fitness sort). The generation is rebuilt because
                // upstream Generation.Chromosomes setter is internal.
                CurrentGeneration = new Generation(GenerationsNumber, chromosomes.Take(MaxSize).ToList());
                Generations[Generations.Count - 1] = CurrentGeneration;
                chromosomes = CurrentGeneration.Chromosomes;
            }

            var best = chromosomes.MaxBy(c => c.Fitness.Value);

            if (BestChromosome == null || best.Fitness > BestChromosome.Fitness)
            {
                BestChromosome = best;
                BestChromosomeChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }
}
