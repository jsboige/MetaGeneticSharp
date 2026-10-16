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
/// The <c>Gdi_*</c> test compares the managed conversion with the original GDI+ one on every opaque
/// color, on Windows: identical except on exact half-level ties, where they differ by one level.
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

    [Test]
    [Platform("Win", Reason = GdiReason)]
    public void Gdi_EveryOpaqueColor_MatchesGdiExceptOnExactHalfLevelTies()
    {
        // KEYSTONE: on all 2^24 opaque colors (a 4096 x 4096 image whose pixel i is the color
        // 0xFF000000 | i), the managed conversion equals the GDI+ ColorMatrix of the original, except
        // where the exact luma (30r + 59g + 11b) / 100 ends in .5. There the two round the tie their
        // own way, and differ by one level. Every gray converts identically (no tie: luma = level).
        const int side = 4096;
        var bits = new int[side * side];
        for (int i = 0; i < bits.Length; i++)
        {
            bits[i] = unchecked((int)0xFF000000) | i;
        }

        using var source = new DirectBitmap(side, side, bits);
        using DirectBitmap gdi = source.Bitmap.MakeGrayscaleImage();
        using DirectBitmap managed = source.MakeGrayscaleImage();

        int tieDifferences = 0, otherDifferences = 0;
        var report = new System.Text.StringBuilder();
        for (int i = 0; i < bits.Length; i++)
        {
            if (managed.Bits[i] == gdi.Bits[i])
            {
                continue;
            }

            int r = (i >> 16) & 0xFF, g = (i >> 8) & 0xFF, b = i & 0xFF;
            bool tie = (30 * r + 59 * g + 11 * b) % 100 == 50;
            int levelGap = Math.Abs((managed.Bits[i] & 0xFF) - (gdi.Bits[i] & 0xFF));
            if (tie && levelGap == 1 && (managed.Bits[i] >> 24 & 0xFF) == 255)
            {
                tieDifferences++;
                continue;
            }

            if (otherDifferences < 20)
            {
                report.Append($"\n  rgb({r}, {g}, {b}): GDI+ {gdi.Bits[i]:X8}, managed {managed.Bits[i]:X8}");
            }

            otherDifferences++;
        }

        TestContext.Out.WriteLine($"{tieDifferences} of {bits.Length} colors differ from GDI+, all on exact half-level ties, by one level.");
        Assert.That(otherDifferences, Is.Zero, $"{otherDifferences} colors differ outside exact half-level ties:{report}");
        for (int v = 0; v < 256; v++)
        {
            int i = (v << 16) | (v << 8) | v;
            Assert.That(managed.Bits[i], Is.EqualTo(gdi.Bits[i]), $"gray level {v}");
        }
    }
}
