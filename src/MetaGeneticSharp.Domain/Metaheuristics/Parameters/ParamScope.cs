#nullable disable

namespace MetaGeneticSharp
{
    /// <summary>
    /// The caching scope of a metaheuristic parameter: a parameter value is computed
    /// once per scope combination and reused within it.
    /// </summary>
    [Flags]
    public enum ParamScope
    {
        None = 0,
        Constant = 1,
        Evolution = 2,
        Generation = 4,
        Stage = 8,
        MetaHeuristic = 16,
        Individual = 32
    }
}
