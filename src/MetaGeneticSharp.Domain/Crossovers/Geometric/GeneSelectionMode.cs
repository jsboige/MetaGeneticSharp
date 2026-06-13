#nullable disable
using System;

namespace MetaGeneticSharp
{
    /// <summary>
    /// Controls how the ordered embedding walks the candidate offspring metric-space
    /// values when converting them back to gene-space swaps. Ported from
    /// GeneticSharp.Domain.Crossovers.Geometric (PR giacomelli/GeneticSharp#87).
    /// </summary>
    [Flags]
    public enum GeneSelectionMode
    {
        /// <summary>Visit every index in order.</summary>
        AllIndexed = 0,

        /// <summary>Return as soon as one swap is accepted.</summary>
        SingleFirstAllowed = 1,

        /// <summary>Visit indices in a random order.</summary>
        RandomOrder = 2,
    }
}
