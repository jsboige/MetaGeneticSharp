using System.Drawing;
using GeneticSharp.Extensions.Mathematic.Functions;
using MetaGeneticSharp;

namespace MetaGeneticSharp.Extensions.Tests;

/// <summary>
/// Tests for the H2 helper (<see cref="LandscapeMaps.CreateFunctionFromImage"/> /
/// <see cref="LandscapeMaps.CreateFunctionFromFile"/>): the <see cref="LandscapeMode.CustomImage"/>
/// source becomes first-class and symmetric with <see cref="LandscapeMaps.CreateFunction(KnownHeightMap)"/>.
/// An arbitrary bitmap (in memory or on disk) reads as an elevation field via the verbatim
/// <see cref="ImageHeightMapFunction"/> and renders as a GRAPHIC PNG heatmap through the verbatim
/// <see cref="LandscapeRenderer"/>. The keystone is
/// <see cref="CreateFunctionFromImage_RendersGraphicHeatmapPng"/> (the three-mode symmetry: a
/// custom image drives the same renderer as the shipped maps).
/// </summary>
[TestFixture]
public class CustomImageLandscapeTests
{
    /// <summary>A small synthetic bitmap: a left-to-right brightness gradient (a simple ridge).</summary>
    private static Bitmap GradientBitmap(int width = 16, int height = 12)
    {
        var bitmap = new Bitmap(width, height);
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int v = (int)(255.0 * x / (width - 1));
                bitmap.SetPixel(x, y, Color.FromArgb(v, v, v));
            }
        }

        return bitmap;
    }

    [Test]
    public void CreateFunctionFromImage_RangesMatchImageDimensions()
    {
        using Bitmap source = GradientBitmap(16, 12);
        using ImageHeightMapFunction function = LandscapeMaps.CreateFunctionFromImage(source, "grad");

        System.Collections.Generic.IList<(double min, double max)> ranges = function.Ranges(2);
        Assert.That(ranges, Has.Count.EqualTo(2));
        Assert.That(ranges[0], Is.EqualTo((0.0, 15.0))); // x in [0, W-1]
        Assert.That(ranges[1], Is.EqualTo((0.0, 11.0))); // y in [0, H-1]
        Assert.That(function.Name, Is.EqualTo("grad"));
    }

    [Test]
    public void CreateFunctionFromImage_EvaluatesPixelBrightnessInByteRange()
    {
        using Bitmap source = GradientBitmap();
        using ImageHeightMapFunction function = LandscapeMaps.CreateFunctionFromImage(source);

        // Default label and a sample inside the image: brightness (R channel) 0..255.
        Assert.That(function.Name, Is.EqualTo("CustomImage"));
        double value = function.Function(new[] { 8.0, 6.0 });
        Assert.That(value, Is.InRange(0.0, 255.0));
    }

    [Test]
    public void CreateFunctionFromImage_DoesNotDisposeCallerImage()
    {
        using Bitmap source = GradientBitmap();
        using (ImageHeightMapFunction function = LandscapeMaps.CreateFunctionFromImage(source))
        {
            _ = function.Function(new[] { 1.0, 1.0 });
        }

        // The setter copied to grayscale; the caller's bitmap is untouched and still usable.
        Assert.DoesNotThrow(() => source.GetPixel(0, 0));
        Assert.That(source.Width, Is.EqualTo(16));
    }

    [Test]
    public void CreateFunctionFromImage_RendersGraphicHeatmapPng()
    {
        // KEYSTONE: a custom image drives the verbatim renderer just like the shipped maps.
        using Bitmap source = GradientBitmap(32, 24);
        using ImageHeightMapFunction function = LandscapeMaps.CreateFunctionFromImage(source);
        using LandscapeHeatmap heatmap = LandscapeRenderer.RenderHeatmap(function, width: 120, height: 90);

        byte[] png = heatmap.ToPng();
        // PNG magic number: 89 50 4E 47.
        Assert.That(png.Length, Is.GreaterThan(100));
        Assert.That(png[0], Is.EqualTo(0x89));
        Assert.That(png[1], Is.EqualTo(0x50)); // 'P'
        Assert.That(png[2], Is.EqualTo(0x4E)); // 'N'
        Assert.That(png[3], Is.EqualTo(0x47)); // 'G'
        Assert.That(heatmap.Width, Is.EqualTo(120));
        Assert.That(heatmap.Height, Is.EqualTo(90));
    }

    [Test]
    public void CreateFunctionFromFile_LoadsImageAndDefaultsNameToFileStem()
    {
        string path = Path.Combine(Path.GetTempPath(), $"mgs_h2_{Guid.NewGuid():N}.png");
        try
        {
            using (Bitmap source = GradientBitmap(20, 14))
            {
                source.Save(path, System.Drawing.Imaging.ImageFormat.Png);
            }

            using ImageHeightMapFunction function = LandscapeMaps.CreateFunctionFromFile(path);
            System.Collections.Generic.IList<(double min, double max)> ranges = function.Ranges(2);
            Assert.That(ranges[0], Is.EqualTo((0.0, 19.0)));
            Assert.That(ranges[1], Is.EqualTo((0.0, 13.0)));
            Assert.That(function.Name, Is.EqualTo(Path.GetFileNameWithoutExtension(path)));

            // The file is fully released (loaded image disposed after the grayscale copy).
            Assert.DoesNotThrow(() => File.Delete(path));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Test]
    public void CreateFunctionFromFile_ExplicitNameOverridesStem()
    {
        string path = Path.Combine(Path.GetTempPath(), $"mgs_h2_{Guid.NewGuid():N}.png");
        try
        {
            using (Bitmap source = GradientBitmap()) source.Save(path, System.Drawing.Imaging.ImageFormat.Png);
            using ImageHeightMapFunction function = LandscapeMaps.CreateFunctionFromFile(path, "MyLandscape");
            Assert.That(function.Name, Is.EqualTo("MyLandscape"));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Test]
    public void CreateFunctionFromImage_NullImage_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => LandscapeMaps.CreateFunctionFromImage(null!));
    }

    [Test]
    public void CreateFunctionFromFile_NullOrWhitespacePath_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => LandscapeMaps.CreateFunctionFromFile(null!));
        Assert.Throws<ArgumentException>(() => LandscapeMaps.CreateFunctionFromFile("   "));
    }
}
