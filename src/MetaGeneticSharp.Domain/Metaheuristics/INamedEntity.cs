#nullable disable

namespace MetaGeneticSharp
{
    /// <summary>
    /// An entity with a unique identifier, a name and a description.
    /// </summary>
    public interface INamedEntity
    {
        Guid Guid { get; set; }

        string Name { get; set; }

        string Description { get; set; }
    }
}
