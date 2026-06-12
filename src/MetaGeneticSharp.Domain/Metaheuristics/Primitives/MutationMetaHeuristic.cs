#nullable disable

using System.ComponentModel;
using GeneticSharp;

namespace MetaGeneticSharp
{
    /// <summary>
    /// Substitutes a specific mutation operator.
    /// </summary>
    [DisplayName("Mutation")]
    public class MutationMetaHeuristic : OperatorMetaHeuristic<IMutation>
    {
        protected override void DoMutateChromosome(IEvolutionContext ctx, IMutation mutation, float mutationProbability, IList<IChromosome> offSprings)
        {
            base.DoMutateChromosome(ctx, GetOperator(ctx), mutationProbability, offSprings);
        }
    }
}
