#nullable disable

using System.ComponentModel;
using GeneticSharp;

namespace MetaGeneticSharp
{
    /// <summary>
    /// Substitutes a specific reinsertion operator.
    /// </summary>
    [DisplayName("Reinsertion")]
    public class ReinsertionMetaHeuristic : OperatorMetaHeuristic<IReinsertion>
    {
        public override IList<IChromosome> Reinsert(IEvolutionContext ctx, IReinsertion reinsertion, IList<IChromosome> offspring, IList<IChromosome> parents)
        {
            return base.Reinsert(ctx, GetOperator(ctx), offspring, parents);
        }
    }
}
