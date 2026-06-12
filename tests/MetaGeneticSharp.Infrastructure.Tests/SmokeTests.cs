using GeneticSharp;

namespace MetaGeneticSharp.Infrastructure.Tests;

/// <summary>
/// Smoke tests proving the reference chain
/// MetaGeneticSharp.Infrastructure -> GeneticSharp.Infrastructure.Framework (submodule, tag 3.1.4)
/// compiles and runs.
/// </summary>
[TestFixture]
public class SmokeTests
{
    [Test]
    public void UpstreamStringExtensions_With_FormatsString()
    {
        var result = "Hello {0}".With("MetaGeneticSharp");

        Assert.That(result, Is.EqualTo("Hello MetaGeneticSharp"));
    }
}
