#nullable disable

using System.ComponentModel;
using GeneticSharp;

namespace MetaGeneticSharp
{
    /// <summary>
    /// Offers various techniques to match specific parents for mating (see
    /// <see cref="MatchingKind"/>) and applies the crossover to the selected matches.
    /// </summary>
    [DisplayName("Match")]
    public class MatchMetaHeuristic : ContainerMetaHeuristic
    {
        public MatchPicker Picker { get; set; } = new MatchPicker();

        /// <summary>
        /// Hyperspeed skips matches whose parents all share the same fitness, assuming
        /// they are twins and the offspring would be clones. This occurs more and more
        /// after mode collapse, accelerating late generations. Inspired by the golly
        /// game-of-life runner's Hyperspeed feature.
        /// </summary>
        public bool EnableHyperSpeed { get; set; }

        public MatchMetaHeuristic() : base(new DefaultMetaHeuristic())
        {
            ProbabilityConfig.Crossover.Strategy = ProbabilityStrategy.TestProbability | ProbabilityStrategy.OverwriteProbability;
        }

        public MatchMetaHeuristic(IMetaHeuristic crossMetaHeuristic) : this()
        {
            CrossMetaHeuristic = crossMetaHeuristic;
        }

        public MatchMetaHeuristic(IMetaHeuristic crossMetaHeuristic, IMetaHeuristic subMetaHeuristic) : base(subMetaHeuristic)
        {
            ProbabilityConfig.Crossover.Strategy = ProbabilityStrategy.TestProbability | ProbabilityStrategy.OverwriteProbability;
            CrossMetaHeuristic = crossMetaHeuristic;
        }

        /// <summary>
        /// The metaheuristic applied to the selected matches for the crossover itself.
        /// When null, the <see cref="ContainerMetaHeuristic.SubMetaHeuristic"/> is used
        /// (the PR left this path null-unsafe; falling back keeps a bare
        /// MatchMetaHeuristic usable out of the box).
        /// </summary>
        public IMetaHeuristic CrossMetaHeuristic { get; set; }

        protected override IList<IChromosome> DoMatchParentsAndCross(IEvolutionContext ctx, ICrossover crossover,
            float crossoverProbability,
            IList<IChromosome> parents)
        {
            var toReturn = new List<IChromosome>(crossover.ParentsNumber * crossover.ChildrenNumber);
            var crossHeuristic = CrossMetaHeuristic ?? SubMetaHeuristic;

            // crossover.ParentsNumber matches the skipping indexer of the operators strategy:
            // one match-and-cross per reference parent in the skipped window.
            for (int matchIndex = 0; matchIndex < crossover.ParentsNumber; matchIndex++)
            {
                var referenceIndex = ctx.LocalIndex + matchIndex;
                if (referenceIndex < parents.Count)
                {
                    var selectedParents = Picker.SelectMatches(this, ctx, referenceIndex, crossover, parents);

                    if (EnableHyperSpeed
                        && selectedParents.All(c =>
                            c.Fitness != null && selectedParents[0].Fitness != null &&
                            Math.Abs(c.Fitness.Value - selectedParents[0].Fitness.Value) <= double.Epsilon))
                    {
                        break;
                    }

                    var subContext = ctx.GetLocal(0);
                    subContext.SelectedParents = selectedParents;
                    var matchResult =
                        crossHeuristic.MatchParentsAndCross(subContext, crossover, crossoverProbability, selectedParents);
                    if (matchResult != null)
                    {
                        toReturn.AddRange(matchResult);
                    }
                }
            }

            return toReturn;
        }
    }
}
