using System.Drawing;
using System.IO;
using GeneticSharp.Infrastructure.Framework.Images;
using MetaGeneticSharp;

namespace MetaGeneticSharp.Extensions.Tests;

/// <summary>
/// Notebook smoke tests : ces tests vérifient que chaque cellule du notebook
/// MGS-7b-LandscapeMultidim fonctionne correctement (charge la DLL, rend les
/// heatmaps N-D, écrit les PNG). Ce sont les memes operations que les cellules
/// .NET Interactive du notebook, validees en NUnit plutot que via MCP Jupyter
/// (kernel .net-csharp parfois indisponible dans le sandbox CI / notebook runner).
///
/// Le notebook reste le livrable pedagogique ; ces tests sont la preuve que
/// chaque cellule produit le bon artefact end-to-end.
/// </summary>
[TestFixture]
public class KnownFunctionLandscapeNotebookSmokeTests
{
    private const string AssetsDir = @"D:\Dev\CoursIA-c706-mgs-multidim-projection\MyIA.AI.Notebooks\Search\Part4-Metaheuristics\assets";

    [Test]
    public void Cell1_BridgeLoad_ExposesNdOverloads()
    {
        // Filtrer par nom de paramètre "dimension" (les surcharges 2-D ont int width/height
        // qui sont aussi int — sans ce filtre on aurait 4 résultats).
        var ndOverloads = typeof(KnownFunctionLandscape).GetMethods()
            .Where(m => m.Name == "RenderHeatmap" && m.GetParameters()
                .Any(p => p.Name == "dimension" && p.ParameterType == typeof(int)))
            .ToList();

        // 2 surcharges N-D : (fitness, dim, nbSamples, rng, w, h) et (fitness, xR, yR, dim, nbSamples, rng, w, h)
        Assert.That(ndOverloads.Count, Is.EqualTo(2),
            "Cell1 sanity check : le pont doit exposer 2 surcharges N-D.");
    }

    [Test]
    public void Cell2_Sphere2D_WritesPng()
    {
        using var h = KnownFunctionLandscape.RenderHeatmap(
            new SphereFitness(), dimension: 2, nbSamples: 1, width: 100, height: 100);
        var path = Path.Combine(AssetsDir, "landscape_multidim_sphere_2d.png");
        Directory.CreateDirectory(AssetsDir);
        File.WriteAllBytes(path, h.ToPng());
        Assert.That(File.Exists(path), Is.True);
        Assert.That(new FileInfo(path).Length, Is.GreaterThan(100));
    }

    [Test]
    public void Cell2_Sphere5D_WritesPngWithSpread()
    {
        var rng = new Random(2026);
        using var h = KnownFunctionLandscape.RenderHeatmap(
            new SphereFitness(), dimension: 5, nbSamples: 50, rng: rng, width: 100, height: 100);
        var path = Path.Combine(AssetsDir, "landscape_multidim_sphere_5d.png");
        File.WriteAllBytes(path, h.ToPng());

        // Le fix coordsRange=max-min doit donner un spread de couleurs > 1 (sinon = bug)
        var colors = new HashSet<int>();
        for (int x = 0; x < 100; x++)
        for (int y = 0; y < 100; y++)
            colors.Add(h.Bitmap.GetPixel(x, y).ToArgb());
        Assert.That(colors.Count, Is.GreaterThan(5),
            "Sphere 5-D 100x100 avec coordsRange OK doit avoir un spread de couleurs > 5.");
    }

    [Test]
    public void Cell3_RastriginAllDimensions_WritePngs()
    {
        var rng = new Random(42);
        foreach (int dim in new[] { 2, 5, 10, 30 })
        {
            using var h = KnownFunctionLandscape.RenderHeatmap(
                new RastriginFitness(), dimension: dim, nbSamples: 25, rng: rng,
                width: 120, height: 120);
            var path = Path.Combine(AssetsDir, $"landscape_multidim_rastrigin_d{dim}.png");
            File.WriteAllBytes(path, h.ToPng());
            Assert.That(File.Exists(path), Is.True);
        }
    }

    [Test]
    public void Cell4_SchwefelAllDimensions_WritePngs()
    {
        var rng = new Random(99);
        foreach (int dim in new[] { 5, 30 })
        {
            using var h = KnownFunctionLandscape.RenderHeatmap(
                new SchwefelFitness(), dimension: dim, nbSamples: 30, rng: rng,
                width: 120, height: 120);
            var path = Path.Combine(AssetsDir, $"landscape_multidim_schwefel_d{dim}.png");
            File.WriteAllBytes(path, h.ToPng());
            Assert.That(File.Exists(path), Is.True);
        }
    }

    [Test]
    public void Cell5_Exercise1_NbSamplesConvergence_MaxRedIsMonotone()
    {
        // La MAX projection est un sub-martingale : MAX(N+1 uniforms) >= MAX(N uniforms).
        // Donc le pixel le plus rouge (max red channel) doit être non-décroissant en nbSamples.
        var stats = new List<(int nbSamples, byte maxRed)>();
        foreach (int nb in new[] { 1, 5, 25, 100 })
        {
            using var h = KnownFunctionLandscape.RenderHeatmap(
                new RastriginFitness(), dimension: 10, nbSamples: nb,
                rng: new Random(2026), width: 80, height: 80);

            byte maxR = 0;
            for (int x = 0; x < 80; x++)
            for (int y = 0; y < 80; y++)
            {
                var r = h.Bitmap.GetPixel(x, y).R;
                if (r > maxR) maxR = r;
            }
            stats.Add((nb, maxR));
        }

        // Le pixel rouge max est non-décroissant en nbSamples (MAX-of-uniforms sub-martingale).
        for (int i = 1; i < stats.Count; i++)
        {
            Assert.That(stats[i].maxRed, Is.GreaterThanOrEqualTo(stats[i - 1].maxRed),
                $"maxRed doit être non-décroissant : nb={stats[i - 1].nbSamples}→{stats[i - 1].maxRed}, nb={stats[i].nbSamples}→{stats[i].maxRed}.");
        }
    }
}
