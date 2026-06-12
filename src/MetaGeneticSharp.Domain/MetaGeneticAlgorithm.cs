#nullable disable

using System.Diagnostics;
using GeneticSharp;

namespace MetaGeneticSharp
{
    /// <summary>
    /// Autonomous metaheuristic-driven evolution engine implementing GeneticSharp's
    /// <see cref="IGeneticAlgorithm"/>. Differences from the stock GeneticAlgorithm:
    /// every evolution stage is routed through the <see cref="Metaheuristic"/>; fitness
    /// evaluation is scoped to offspring; and chromosomes are never implicitly sorted
    /// by fitness (metaheuristics rely on stable indices — elitism is the reinsertion
    /// operator's explicit job).
    /// </summary>
    public class MetaGeneticAlgorithm : IGeneticAlgorithm
    {
        public const float DefaultCrossoverProbability = 0.75f;

        public const float DefaultMutationProbability = 0.1f;

        private bool m_stopRequested;
        private readonly object m_lock = new object();
        private GeneticAlgorithmState m_state;
        private Stopwatch m_stopwatch;

        public MetaGeneticAlgorithm(
            IPopulation population,
            IFitness fitness,
            ISelection selection,
            ICrossover crossover,
            IMutation mutation)
            : this(population, fitness, selection, crossover, mutation, new DefaultMetaHeuristic())
        {
        }

        public MetaGeneticAlgorithm(
            IPopulation population,
            IFitness fitness,
            ISelection selection,
            ICrossover crossover,
            IMutation mutation,
            IMetaHeuristic metaHeuristic)
        {
            Population = population ?? throw new ArgumentNullException(nameof(population));
            Fitness = fitness ?? throw new ArgumentNullException(nameof(fitness));
            Selection = selection ?? throw new ArgumentNullException(nameof(selection));
            Crossover = crossover ?? throw new ArgumentNullException(nameof(crossover));
            Mutation = mutation ?? throw new ArgumentNullException(nameof(mutation));
            Metaheuristic = metaHeuristic ?? throw new ArgumentNullException(nameof(metaHeuristic));

            Reinsertion = new FitnessBasedElitistReinsertion();
            Termination = new GenerationNumberTermination(1);

            CrossoverProbability = DefaultCrossoverProbability;
            MutationProbability = DefaultMutationProbability;
            TimeEvolving = TimeSpan.Zero;
            State = GeneticAlgorithmState.NotStarted;
            TaskExecutor = new LinearTaskExecutor();
            OperatorsStrategy = new LinearMetaOperatorsStrategy();

            TerminationReached += OnTerminationReached;
        }

        public event EventHandler GenerationRan;

        public event EventHandler TerminationReached;

        public event EventHandler Stopped;

        public IPopulation Population { get; private set; }

        public IFitness Fitness { get; }

        public ISelection Selection { get; set; }

        public ICrossover Crossover { get; set; }

        public IMutation Mutation { get; set; }

        public IReinsertion Reinsertion { get; set; }

        public ITermination Termination { get; set; }

        public float CrossoverProbability { get; set; }

        public float MutationProbability { get; set; }

        public IMetaHeuristic Metaheuristic { get; set; }

        public IMetaOperatorsStrategy OperatorsStrategy { get; set; }

        public ITaskExecutor TaskExecutor { get; set; }

        /// <summary>
        /// When false (the default), the cached evolution context is removed from the
        /// population's parameter store once termination is reached.
        /// </summary>
        public bool KeepContextInPopulation { get; set; }

        public int GenerationsNumber => Population.GenerationsNumber;

        public IChromosome BestChromosome => Population.BestChromosome;

        public TimeSpan TimeEvolving { get; private set; }

        public GeneticAlgorithmState State
        {
            get => m_state;

            private set
            {
                var shouldStop = Stopped != null && m_state != value && value == GeneticAlgorithmState.Stopped;

                m_state = value;

                if (shouldStop)
                {
                    Stopped(this, EventArgs.Empty);
                }
            }
        }

        public bool IsRunning => State == GeneticAlgorithmState.Started || State == GeneticAlgorithmState.Resumed;

        /// <summary>
        /// Creates and evaluates the initial generation without evolving it.
        /// </summary>
        public void Initialise()
        {
            try
            {
                lock (m_lock)
                {
                    State = GeneticAlgorithmState.Started;
                    m_stopwatch = Stopwatch.StartNew();
                    Population.CreateInitialGeneration();
                    EvaluateFitness(Population.CurrentGeneration.Chromosomes);
                    m_stopwatch.Stop();
                    TimeEvolving = m_stopwatch.Elapsed;
                }
            }
            catch
            {
                State = GeneticAlgorithmState.Stopped;
                throw;
            }
        }

        public void Start()
        {
            Initialise();
            Resume();
        }

        public void Resume()
        {
            try
            {
                lock (m_lock)
                {
                    m_stopRequested = false;
                }

                if (Population.GenerationsNumber == 0)
                {
                    throw new InvalidOperationException("Attempt to resume a genetic algorithm which was not yet started.");
                }

                if (Population.GenerationsNumber > 1)
                {
                    if (Termination.HasReached(this))
                    {
                        throw new InvalidOperationException($"Attempt to resume a genetic algorithm with a termination ({Termination}) already reached. Please, specify a new termination or extend the current one.");
                    }

                    State = GeneticAlgorithmState.Resumed;
                }

                if (EndCurrentGeneration())
                {
                    return;
                }

                bool terminationConditionReached;

                do
                {
                    if (m_stopRequested)
                    {
                        break;
                    }

                    terminationConditionReached = Step();
                }
                while (!terminationConditionReached);
            }
            catch
            {
                State = GeneticAlgorithmState.Stopped;
                throw;
            }
        }

        /// <summary>
        /// Evolves one generation and ends it. Returns true if termination was reached.
        /// </summary>
        public bool Step()
        {
            m_stopwatch.Restart();
            EvolveOneGeneration();
            m_stopwatch.Stop();
            TimeEvolving += m_stopwatch.Elapsed;

            return EndCurrentGeneration();
        }

        public void Stop()
        {
            if (Population.GenerationsNumber == 0)
            {
                throw new InvalidOperationException("Attempt to stop a genetic algorithm which was not yet started.");
            }

            lock (m_lock)
            {
                m_stopRequested = true;
            }
        }

        public void Reset(IPopulation newPopulation)
        {
            if (m_state == GeneticAlgorithmState.Started || m_state == GeneticAlgorithmState.Resumed)
            {
                throw new InvalidOperationException($"Cannot reset a genetic algorithm with state {m_state}.");
            }

            lock (m_lock)
            {
                TimeEvolving = TimeSpan.Zero;
                State = GeneticAlgorithmState.NotStarted;
                Population = newPopulation ?? throw new ArgumentNullException(nameof(newPopulation));
            }
        }

        protected virtual void EvolveOneGeneration()
        {
            var ctx = Metaheuristic.GetContext(this, Population);

            ctx.CurrentStage = EvolutionStage.Selection;
            var parents = SelectParents(ctx);

            ctx.CurrentStage = EvolutionStage.Crossover;
            ctx.SelectedParents = parents;
            var offspring = Cross(ctx, parents);

            ctx.CurrentStage = EvolutionStage.Mutation;
            ctx.GeneratedOffsprings = offspring;
            Mutate(ctx, offspring);

            EvaluateFitness(offspring);

            ctx.CurrentStage = EvolutionStage.Reinsertion;
            var newGenerationChromosomes = Reinsert(ctx, offspring, parents);
            Population.CreateNewGeneration(newGenerationChromosomes);
        }

        private bool EndCurrentGeneration()
        {
            Population.EndCurrentGeneration();
            GenerationRan?.Invoke(this, EventArgs.Empty);

            if (Termination.HasReached(this))
            {
                State = GeneticAlgorithmState.TerminationReached;
                TerminationReached?.Invoke(this, EventArgs.Empty);
                return true;
            }

            if (m_stopRequested)
            {
                TaskExecutor.Stop();
                State = GeneticAlgorithmState.Stopped;
            }

            return false;
        }

        public void EvaluateFitness(IList<IChromosome> chromosomes)
        {
            try
            {
                var withoutFitness = chromosomes.Where(c => !c.Fitness.HasValue);

                foreach (var chromosome in withoutFitness)
                {
                    TaskExecutor.Add(() => RunEvaluateFitness(chromosome));
                }

                if (!TaskExecutor.Start())
                {
                    throw new TimeoutException($"The fitness evaluation reached the {TaskExecutor.Timeout} timeout.");
                }
            }
            finally
            {
                TaskExecutor.Stop();
                TaskExecutor.Clear();
            }

            // No implicit fitness sorting here: metaheuristics rely on stable chromosome indices.
        }

        private void RunEvaluateFitness(IChromosome chromosome)
        {
            try
            {
                chromosome.Fitness = Fitness.Evaluate(chromosome);
            }
            catch (Exception ex)
            {
                throw new FitnessException(Fitness, $"Error executing Fitness.Evaluate for chromosome: {ex.Message}", ex);
            }
        }

        private IList<IChromosome> SelectParents(IEvolutionContext ctx)
        {
            return Metaheuristic.SelectParentPopulation(ctx, Selection);
        }

        private IList<IChromosome> Cross(IEvolutionContext ctx, IList<IChromosome> parents)
        {
            return OperatorsStrategy.Cross(Metaheuristic, ctx, Crossover, CrossoverProbability, parents);
        }

        private void Mutate(IEvolutionContext ctx, IList<IChromosome> offspring)
        {
            OperatorsStrategy.Mutate(Metaheuristic, ctx, Mutation, MutationProbability, offspring);
        }

        private IList<IChromosome> Reinsert(IEvolutionContext ctx, IList<IChromosome> offspring, IList<IChromosome> parents)
        {
            return Metaheuristic.Reinsert(ctx, Reinsertion, offspring, parents);
        }

        private void OnTerminationReached(object sender, EventArgs e)
        {
            if (!KeepContextInPopulation && Population is IMetaPopulation metaPopulation)
            {
                metaPopulation.Parameters.Remove(nameof(IEvolutionContext));
            }
        }
    }
}
