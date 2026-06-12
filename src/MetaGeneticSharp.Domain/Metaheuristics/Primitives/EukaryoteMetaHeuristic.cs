#nullable disable

using System.ComponentModel;
using GeneticSharp;

namespace MetaGeneticSharp
{
    /// <summary>
    /// Splits each individual into a karyotype of child sub-chromosomes and applies a
    /// distinct phase metaheuristic to each sub-chromosome population, before building
    /// the resulting individuals back. Reinsertion is not supported: scope it to
    /// Selection/Crossover/Mutation (the canonical PR usage is
    /// <c>Scope = EvolutionStage.Crossover | EvolutionStage.Mutation</c>) so reinsertion
    /// falls through to the sub-metaheuristic.
    /// </summary>
    [DisplayName("Eukaryote")]
    public class EukaryoteMetaHeuristic : SubPopulationMetaHeuristicBase<SubPopulation>
    {
        public EukaryoteMetaHeuristic()
        {
        }

        public EukaryoteMetaHeuristic(int subChromosomeSize, params IMetaHeuristic[] phaseHeuristics) : base(subChromosomeSize, phaseHeuristics)
        {
        }

        public EukaryoteMetaHeuristic(int phaseSize, int phaseNb, params IMetaHeuristic[] phaseHeuristics) : base(phaseSize, phaseNb, phaseHeuristics)
        {
        }

        protected override IList<SubPopulation> GenerateSubPopulations(IMetaHeuristic h, IEvolutionContext c)
        {
            var subPopulations = new List<SubPopulation>(PhaseSizes.Phases.Count);
            var subPopulationChromosomes = EukaryoteChromosome.GetSubPopulations(c.Population.CurrentGeneration.Chromosomes, PhaseSizes.Phases);
            for (int subPopulationIndex = 0; subPopulationIndex < subPopulationChromosomes.Count; subPopulationIndex++)
            {
                var subPopulation = new SubPopulation(c.Population, subPopulationChromosomes[subPopulationIndex]);
                subPopulations.Add(subPopulation);
            }
            return subPopulations;
        }

        private const string SubPopulationsKey = "eukaryoteSubPopulations";

        protected override IList<IChromosome> ScopedSelectParentPopulation(IEvolutionContext ctx, ISelection selection)
        {
            var subPopulations = DynamicSubPopulationParameter.Get(this, ctx, SubPopulationsKey);
            var selectedParents = PerformSubOperator(subPopulations, (subHeuristic, subPopulation) =>
            {
                var newCtx = subPopulation.GetContext(ctx);
                var toReturn = subHeuristic.SelectParentPopulation(newCtx, selection);
                newCtx.SelectedParents = toReturn;

                return toReturn;
            });

            return selectedParents;
        }

        protected override IList<IChromosome> ScopedMatchParentsAndCross(IEvolutionContext ctx, ICrossover crossover, float crossoverProbability, IList<IChromosome> parents)
        {
            var subPopulations = DynamicSubPopulationParameter.Get(this, ctx, SubPopulationsKey);

            SynchroniseParents(subPopulations, ctx, parents);

            var offsprings = PerformSubOperator(subPopulations, (subHeuristic, subPopulation) =>
            {
                // The PR passes the parent ctx here; the sub-population context is the
                // correct one (its Population holds sub-chromosomes — picks that read the
                // population, e.g. MatchingKind.Random/Best, would otherwise mix gene sizes).
                var newCtx = subPopulation.GetContext(ctx);
                var toReturn = subHeuristic.MatchParentsAndCross(newCtx, crossover, 1, newCtx.SelectedParents);
                newCtx.GeneratedOffsprings = toReturn;
                return toReturn;
            });

            return offsprings;
        }

        protected override void ScopedMutateChromosome(IEvolutionContext ctx, IMutation mutation, float mutationProbability, IList<IChromosome> offSprings)
        {
            var karyotype = EukaryoteChromosome.GetKaryotype(offSprings[ctx.LocalIndex], PhaseSizes.Phases);
            for (var subChromosomeIdx = 0; subChromosomeIdx < karyotype.Count; subChromosomeIdx++)
            {
                var subChromosome = karyotype[subChromosomeIdx];
                var subContext = ctx.GetLocal(0);
                PhaseHeuristics[subChromosomeIdx].MutateChromosome(subContext, mutation, mutationProbability, new List<IChromosome>(new[] { subChromosome }));
            }
            EukaryoteChromosome.UpdateParent(karyotype);
        }

        protected override IList<IChromosome> ScopedReinsert(IEvolutionContext ctx, IReinsertion reinsertion, IList<IChromosome> offspring, IList<IChromosome> parents)
        {
            throw new InvalidOperationException("Eukaryote MetaHeuristic doesn't support reinsertion");
        }

        private void SynchroniseParents(IList<SubPopulation> subPopulations, IEvolutionContext ctx, IList<IChromosome> parents)
        {
            var subContext0 = subPopulations.Last().GetContext(ctx);
            if (subContext0.SelectedParents == null || subContext0.SelectedParents.Count == 0)
            {
                lock (ctx)
                {
                    if (subContext0.SelectedParents == null || subContext0.SelectedParents.Count == 0)
                    {
                        var selectedSubParents = EukaryoteChromosome.GetSubPopulations(parents, PhaseSizes.Phases);
                        for (int subPopulationIndex = 0; subPopulationIndex < selectedSubParents.Count; subPopulationIndex++)
                        {
                            subPopulations[subPopulationIndex].GetContext(ctx).SelectedParents = selectedSubParents[subPopulationIndex];
                        }
                    }
                }
            }
        }
    }
}
