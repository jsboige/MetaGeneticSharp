using System;
using System.Drawing;
using GeneticSharp.Infrastructure.Framework.Images;

namespace MetaGeneticSharp.Infrastructure.Tests;

/// <summary>
/// Tests for the L3 micro-opt of jsboige's verbatim <see cref="DirectBitmap"/>
/// (@ d05826fd, MyIntelligenceAgency/GeneticSharp, branch Metaheuristics). The
/// <c>DirectBitmap(int, int, int[])</c> constructor previously copied the source array twice
/// (<c>new List&lt;int&gt;(existing).ToArray()</c>); L3 replaces that with a single
/// <c>Array.Clone()</c>. These tests pin the contract the change must preserve: the produced
/// <see cref="DirectBitmap.Bits"/> is a <b>byte-identical but independent</b> copy of the input
/// (no aliasing), <see cref="DirectBitmap.Clone"/> stays a deep copy, and a null array still
/// throws <see cref="ArgumentNullException"/>. The keystone is
/// <see cref="CtorFromExisting_CopiesInput_NotAliases"/>, which directly exercises the changed
/// line by mutating the source array after construction.
/// </summary>
[TestFixture]
public class DirectBitmapTests
{
    [Test]
    public void CtorFromExisting_CopiesInput_NotAliases()
    {
        // KEYSTONE: the constructor must COPY `existing` (the L3 Array.Clone), not alias it.
        // Mutating the source after construction must not bleed into the bitmap's Bits.
        int[] existing = { 11, 22, 33, 44 };
        using var bitmap = new DirectBitmap(2, 2, existing);

        existing[0] = 999;

        Assert.That(bitmap.Bits, Is.Not.SameAs(existing), "Bits must be an independent array");
        Assert.That(bitmap.Bits[0], Is.EqualTo(11), "post-construction source mutation must not leak in");
        Assert.That(bitmap.Bits, Is.EqualTo(new[] { 11, 22, 33, 44 }).AsCollection);
    }

    [Test]
    public void Clone_ProducesByteIdenticalIndependentBits()
    {
        using var original = new DirectBitmap(4, 3);
        for (int i = 0; i < original.Bits.Length; i++)
        {
            original.Bits[i] = unchecked((int)0xFF000000) | (i * 7);
        }

        using DirectBitmap clone = original.Clone();

        Assert.That(clone.Bits, Is.Not.SameAs(original.Bits), "clone must own a distinct array");
        Assert.That(clone.Bits, Is.EqualTo(original.Bits).AsCollection, "clone bits must be byte-identical");
    }

    [Test]
    public void Clone_IsDeepCopy_MutationDoesNotLeak()
    {
        using var original = new DirectBitmap(2, 2);
        original.SetPixel(0, 0, Color.Red);
        using DirectBitmap clone = original.Clone();

        clone.SetPixel(0, 0, Color.Blue);

        Assert.That(original.GetPixel(0, 0).ToArgb(), Is.EqualTo(Color.Red.ToArgb()),
            "mutating the clone must not affect the original");
        Assert.That(clone.GetPixel(0, 0).ToArgb(), Is.EqualTo(Color.Blue.ToArgb()));
    }

    [Test]
    public void Clone_PreservesDimensionsAndPixels()
    {
        using var original = new DirectBitmap(5, 3);
        original.SetPixel(4, 2, Color.FromArgb(255, 10, 20, 30));
        using DirectBitmap clone = original.Clone();

        Assert.That(clone.Width, Is.EqualTo(5));
        Assert.That(clone.Height, Is.EqualTo(3));
        Assert.That(clone.GetPixel(4, 2).ToArgb(), Is.EqualTo(Color.FromArgb(255, 10, 20, 30).ToArgb()));
    }

    [Test]
    public void ICloneableClone_ReturnsEquivalentIndependentDirectBitmap()
    {
        using var original = new DirectBitmap(3, 2);
        original.SetPixel(1, 1, Color.Lime);
        using var clone = (DirectBitmap)((ICloneable)original).Clone();

        Assert.That(clone.Bits, Is.EqualTo(original.Bits).AsCollection);
        Assert.That(clone.Bits, Is.Not.SameAs(original.Bits));
        Assert.That(clone.GetPixel(1, 1).ToArgb(), Is.EqualTo(Color.Lime.ToArgb()));
    }

    [Test]
    public void Clone_SinglePixelBitmap_Succeeds()
    {
        using var original = new DirectBitmap(1, 1);
        original.SetPixel(0, 0, Color.Magenta);
        using DirectBitmap clone = original.Clone();

        Assert.That(clone.Bits, Has.Length.EqualTo(1));
        Assert.That(clone.Bits, Is.Not.SameAs(original.Bits));
        Assert.That(clone.GetPixel(0, 0).ToArgb(), Is.EqualTo(Color.Magenta.ToArgb()));
    }

    [Test]
    public void CtorFromExisting_NullArray_ThrowsArgumentNullException()
    {
        // The original threw ArgumentNullException from the List<int> constructor on a null
        // source; the L3 explicit guard preserves that contract.
        Assert.Throws<ArgumentNullException>(() => new DirectBitmap(2, 2, null!));
    }
}
