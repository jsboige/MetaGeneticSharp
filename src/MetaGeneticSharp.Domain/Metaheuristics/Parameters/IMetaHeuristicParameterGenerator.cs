#nullable disable

namespace MetaGeneticSharp
{
    /// <summary>
    /// A parameter whose value is produced by a typed generator delegate.
    /// </summary>
    public interface IMetaHeuristicParameterGenerator<out TParamType> : IMetaHeuristicParameter
    {
        ParameterGenerator<TParamType> GetGenerator(IEvolutionContext ctx);
    }
}
