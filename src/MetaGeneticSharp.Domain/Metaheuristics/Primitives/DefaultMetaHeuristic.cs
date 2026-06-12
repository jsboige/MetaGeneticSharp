#nullable disable

using System.ComponentModel;
using GeneticSharp;

namespace MetaGeneticSharp
{
    /// <summary>
    /// Reproduces the standard GA behavior as a metaheuristic: regular selection,
    /// adjacent-parents crossover with probability test, per-index mutation, regular
    /// reinsertion. This is the default leaf of every metaheuristic composition.
    /// </summary>
    [DisplayName("Default")]
    public class DefaultMetaHeuristic : ScopedMetaHeuristic
    {
        private MatchMetaHeuristic _matchMetaHeuristic;

        public DefaultMetaHeuristic()
            : base(new NoOpMetaHeuristic())
        {
        }

        /// <summary>
        /// The default metaheuristic recycles the original operators-strategy routine
        /// (adjacent matching); touching this property at configuration time switches
        /// the crossover stage to a dedicated <see cref="MetaGeneticSharp.MatchMetaHeuristic"/>
        /// (current + random matches by default), offering the full matching flexibility.
        /// </summary>
        public MatchMetaHeuristic MatchMetaHeuristic
        {
            get
            {
                if (_matchMetaHeuristic == null)
                {
                    lock (this)
                    {
                        _matchMetaHeuristic ??= new MatchMetaHeuristic().WithMatches(MatchingKind.Current, MatchingKind.Random);
                    }
                }
                return _matchMetaHeuristic;
            }
        }

        protected override IList<IChromosome> ScopedSelectParentPopulation(IEvolutionContext ctx, ISelection selection)
        {
            return selection.SelectChromosomes(ctx.Population.MinSize, ctx.Population.CurrentGeneration);
        }

        protected override IList<IChromosome> ScopedMatchParentsAndCross(IEvolutionContext ctx, ICrossover crossover, float crossoverProbability, IList<IChromosome> parents)
        {
            if (_matchMetaHeuristic != null)
            {
                return _matchMetaHeuristic.MatchParentsAndCross(ctx, crossover, crossoverProbability, parents);
            }

            // If match the probability cross is made, otherwise no offspring is produced
            // for this index. Checks that enough parents remain at the end of the list
            // for what the crossover expects.
            if (parents.Count - ctx.LocalIndex >= crossover.ParentsNumber
                && RandomizationProvider.Current.GetDouble() <= crossoverProbability)
            {
                var selectedParents = new List<IChromosome>(crossover.ParentsNumber);

                for (int i = 0; i < crossover.ParentsNumber; i++)
                {
                    selectedParents.Add(parents[ctx.LocalIndex + i]);
                }

                return crossover.Cross(selectedParents);
            }

            return new List<IChromosome>();
        }

        protected override void ScopedMutateChromosome(IEvolutionContext ctx, IMutation mutation, float mutationProbability, IList<IChromosome> offSprings)
        {
            mutation.Mutate(offSprings[ctx.LocalIndex], mutationProbability);
        }

        protected override IList<IChromosome> ScopedReinsert(IEvolutionContext ctx, IReinsertion reinsertion, IList<IChromosome> offspring, IList<IChromosome> parents)
        {
            return reinsertion.SelectChromosomes(ctx.Population, offspring, parents);
        }
    }
}
