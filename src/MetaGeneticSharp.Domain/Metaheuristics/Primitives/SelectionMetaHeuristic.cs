#nullable disable

using System.ComponentModel;
using GeneticSharp;

namespace MetaGeneticSharp
{
    /// <summary>
    /// Substitutes a specific selection operator.
    /// </summary>
    [DisplayName("Selection")]
    public class SelectionMetaHeuristic : OperatorMetaHeuristic<ISelection>
    {
        public override IList<IChromosome> SelectParentPopulation(IEvolutionContext ctx, ISelection selection)
        {
            return base.SelectParentPopulation(ctx, GetOperator(ctx));
        }
    }
}
