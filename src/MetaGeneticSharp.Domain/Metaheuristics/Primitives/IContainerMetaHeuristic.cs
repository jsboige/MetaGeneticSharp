#nullable disable

namespace MetaGeneticSharp
{
    /// <summary>
    /// A metaheuristic wrapping a sub-metaheuristic, the basic unit of composition.
    /// </summary>
    public interface IContainerMetaHeuristic : IMetaHeuristic
    {
        IMetaHeuristic SubMetaHeuristic { get; set; }

        OperatorsProbabilityConfig ProbabilityConfig { get; set; }
    }
}
