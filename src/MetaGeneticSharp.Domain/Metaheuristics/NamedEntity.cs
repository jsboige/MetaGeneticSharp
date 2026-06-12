#nullable disable

using System.Diagnostics;

namespace MetaGeneticSharp
{
    /// <summary>
    /// Base implementation of <see cref="INamedEntity"/>.
    /// </summary>
    [DebuggerDisplay("{Guid} - {Name} - {Description}")]
    public abstract class NamedEntity : INamedEntity
    {
        public Guid Guid { get; set; } = Guid.NewGuid();

        public string Name { get; set; }

        public string Description { get; set; }
    }
}
