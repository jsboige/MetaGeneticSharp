#nullable disable

using System.ComponentModel;
using GeneticSharp;

namespace MetaGeneticSharp
{
    /// <summary>
    /// Substitutes a specific crossover operator. (The PR mislabels this one
    /// "Container" — copy-paste slip, fixed here.)
    /// </summary>
    [DisplayName("Crossover")]
    public class CrossoverMetaHeuristic : OperatorMetaHeuristic<ICrossover>
    {
        protected override IList<IChromosome> DoMatchParentsAndCross(IEvolutionContext ctx, ICrossover crossover, float crossoverProbability, IList<IChromosome> parents)
        {
            return base.DoMatchParentsAndCross(ctx, GetOperator(ctx), crossoverProbability, parents);
        }
    }
}
