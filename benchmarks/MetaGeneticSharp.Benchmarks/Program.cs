// Benchmarks BenchmarkDotNet pour l'API publique de MetaGeneticSharp (EPIC .NET 11, CoursIA#18770).
// Baseline net9.0 : dotnet run -c Release -- --filter * --job short
// Charges a seed fixe : memes operandes a chaque iteration, sans I/O ni reseau.
using BenchmarkDotNet.Running;

BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
