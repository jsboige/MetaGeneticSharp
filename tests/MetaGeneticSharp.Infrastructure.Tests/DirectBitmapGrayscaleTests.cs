using System;
using System.Drawing;
using GeneticSharp.Infrastructure.Framework.Images;

namespace MetaGeneticSharp.Infrastructure.Tests;

/// <summary>
/// Tests for the platform-independent image layer: <see cref="DirectBitmap"/> builds its GDI+
/// view only when <see cref="DirectBitmap.Bitmap"/> is read, and
/// <see cref="ImageExtensions.MakeGrayscaleImage(DirectBitmap)"/> applies jsboige's grayscale
/// matrix (@ d05826fd) in managed code. Every test except the <c>Gdi_*</c> ones runs on Linux and
/// macOS, where the type initializer of GDI+ throws: a test that touched GDI+ would fail there.
/// The <c>Gdi_*</c> tests pin the managed conversion against the original GDI+ one, pixel for
/// pixel, on Windows.
/// </summary>
[TestFixture]
public class DirectBitmapGrayscaleTests
{
    private const string GdiReason = "GDI+ (System.Drawing.Common) is Windows-only since .NET 7";

    [Test]
    public void FillAndRead_WithoutGdiView_Works()
    {
        using var bitmap = new DirectBitmap(3, 2);
        bitmap.SetPixel(2, 1, Color.FromArgb(255, 10, 20, 30));

        Assert.That(bitmap.GetPixel(2, 1).ToArgb(), Is.EqualTo(Color.FromArgb(255, 10, 20, 30).ToArgb()));
        Assert.That(bitmap.Bits[2 + 1 * 3], Is.EqualTo(Color.FromArgb(255, 10, 20, 30).ToArgb()));
    }

    [Test]
    public void Dispose_WithoutGdiView_DoesNotThrow()
    {
        var bitmap = new DirectBitmap(2, 2);
        Assert.DoesNotThrow(bitmap.Dispose);
        Assert.DoesNotThrow(bitmap.Dispose);
    }

    [Test]
    public void Grayscale_GrayPixel_KeepsItsLevel_ForAll256Levels()
    {
        using var source = new DirectBitmap(256, 1);
        for (int v = 0; v < 256; v++)
        {
            source.SetPixel(v, 0, Color.FromArgb(255, v, v, v));
        }

        using DirectBitmap gray = source.MakeGrayscaleImage();

        for (int v = 0; v < 256; v++)
        {
            Color c = gray.GetPixel(v, 0);
            Assert.That((c.A, c.R, c.G, c.B), Is.EqualTo(((byte)255, (byte)v, (byte)v, (byte)v)), $"level {v}");
        }
    }

    [Test]
    public void Grayscale_ColorPixel_UsesLumaWeights()
    {
        using var source = new DirectBitmap(3, 1);
        source.SetPixel(0, 0, Color.FromArgb(255, 255, 0, 0));
        source.SetPixel(1, 0, Color.FromArgb(255, 0, 255, 0));
        source.SetPixel(2, 0, Color.FromArgb(255, 0, 0, 255));

        using DirectBitmap gray = source.MakeGrayscaleImage();

        // round(255 * .3) = 77, round(255 * .59) = 150, round(255 * .11) = 28.
        Assert.That(gray.GetPixel(0, 0).R, Is.EqualTo(77));
        Assert.That(gray.GetPixel(1, 0).R, Is.EqualTo(150));
        Assert.That(gray.GetPixel(2, 0).R, Is.EqualTo(28));
        for (int x = 0; x < 3; x++)
        {
            Color c = gray.GetPixel(x, 0);
            Assert.That(c.G, Is.EqualTo(c.R));
            Assert.That(c.B, Is.EqualTo(c.R));
            Assert.That(c.A, Is.EqualTo(255));
        }
    }

    [Test]
    public void Grayscale_TransparentPixel_StaysTransparent()
    {
        using var source = new DirectBitmap(1, 1);
        using DirectBitmap gray = source.MakeGrayscaleImage();
        Assert.That(gray.Bits[0], Is.EqualTo(0));
    }

    [Test]
    public void Grayscale_TranslucentPixel_StaysPremultiplied()
    {
        // Premultiplied storage: a 50 % gray at alpha 128 is stored as (128, 64, 64, 64).
        using var source = new DirectBitmap(1, 1, new[] { (128 << 24) | (64 << 16) | (64 << 8) | 64 });
        using DirectBitmap gray = source.MakeGrayscaleImage();

        int argb = gray.Bits[0];
        Assert.That((argb >> 24) & 0xFF, Is.EqualTo(128), "alpha is unchanged");
        Assert.That((argb >> 16) & 0xFF, Is.EqualTo(64).Within(1), "a gray stays the same premultiplied gray");
        Assert.That(argb & 0xFF, Is.EqualTo((argb >> 16) & 0xFF));
    }

    [Test]
    public void Grayscale_ReturnsIndependentCopy_SourceUntouched()
    {
        using var source = new DirectBitmap(1, 1);
        source.SetPixel(0, 0, Color.FromArgb(255, 200, 10, 10));
        int before = source.Bits[0];

        using DirectBitmap gray = source.MakeGrayscaleImage();
        gray.SetPixel(0, 0, Color.Black);

        Assert.That(gray.Bits, Is.Not.SameAs(source.Bits));
        Assert.That(source.Bits[0], Is.EqualTo(before));
    }

    [Test]
    public void Grayscale_NullSource_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => ((DirectBitmap)null!).MakeGrayscaleImage());
    }

    /// <summary>
    /// A deterministic opaque palette: every (r, g, b) on a 17-step lattice (0, 16, ..., 240, 255),
    /// 4913 colors, plus the 256 pure grays.
    /// </summary>
    private static DirectBitmap OpaquePalette()
    {
        int[] steps = new int[17];
        for (int i = 0; i < 16; i++) steps[i] = i * 16;
        steps[16] = 255;

        var bits = new System.Collections.Generic.List<int>();
        foreach (int r in steps)
            foreach (int g in steps)
                foreach (int b in steps)
                    bits.Add(Color.FromArgb(255, r, g, b).ToArgb());
        for (int v = 0; v < 256; v++) bits.Add(Color.FromArgb(255, v, v, v).ToArgb());

        return new DirectBitmap(bits.Count, 1, bits.ToArray());
    }

    [Test]
    [Platform("Win", Reason = GdiReason)]
    public void Gdi_OpaquePixels_ManagedGrayscaleMatchesGdiColorMatrix()
    {
        // KEYSTONE: the managed conversion is the GDI+ conversion of the original, pixel for pixel.
        using DirectBitmap source = OpaquePalette();
        using DirectBitmap gdi = source.Bitmap.MakeGrayscaleImage();
        using DirectBitmap managed = source.MakeGrayscaleImage();

        for (int i = 0; i < source.Bits.Length; i++)
        {
            Assert.That(managed.Bits[i], Is.EqualTo(gdi.Bits[i]),
                $"pixel {i}: source {source.Bits[i]:X8}, GDI+ {gdi.Bits[i]:X8}, managed {managed.Bits[i]:X8}");
        }
    }
}
