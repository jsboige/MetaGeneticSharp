#nullable disable

using GeneticSharp;

namespace MetaGeneticSharp
{
    /// <summary>
    /// A population exposing a parameter store, used by metaheuristics to persist
    /// their evolution context across generations. Upstream <see cref="IPopulation"/>
    /// has no such store, so this is the extension point MetaGeneticSharp relies on.
    /// </summary>
    public interface IMetaPopulation : IPopulation
    {
        IDictionary<string, object> Parameters { get; }
    }
}
