#nullable disable

using GeneticSharp;

namespace MetaGeneticSharp
{
    /// <summary>
    /// A child chromosome sliced from a parent individual. It keeps track of the parent
    /// chromosome and gene offset, so a complete individual can be built back from a
    /// karyotype of EukaryoteChromosomes.
    /// </summary>
    public class EukaryoteChromosome : ChromosomeBase
    {
        /// <summary>
        /// The parent chromosome this child chromosome belongs to.
        /// </summary>
        public IChromosome ParentIndividual { get; set; }

        /// <summary>
        /// The gene start index of the child chromosome in its parent.
        /// </summary>
        public int StartGeneIndex { get; set; }

        public EukaryoteChromosome(IChromosome parent, int startGeneIdx, int length) : base(length)
        {
            ParentIndividual = parent;
            StartGeneIndex = startGeneIdx;
            var parentGenes = new ArraySegment<Gene>(ParentIndividual.GetGenes(), startGeneIdx, length);

            ReplaceGenes(0, parentGenes.ToArray());

            if (parent.Fitness.HasValue)
            {
                Fitness = parent.Fitness;
            }
        }

        public override Gene GenerateGene(int geneIndex)
        {
            if (geneIndex >= Length)
            {
                throw new ArgumentOutOfRangeException(nameof(geneIndex), $"Eukaryote chromosome size overflow: gene index {geneIndex} with Length {Length}");
            }
            return ParentIndividual.GenerateGene(StartGeneIndex + geneIndex);
        }

        public override IChromosome CreateNew()
        {
            return new EukaryoteChromosome(ParentIndividual, StartGeneIndex, Length);
        }

        /// <summary>
        /// Extracts the child chromosomes from a parent individual, given the lengths of
        /// the child chromosomes.
        /// </summary>
        public static IList<IChromosome> GetKaryotype(IChromosome parentIndividual, IList<int> subChromosomeLengths)
        {
            var toReturn = new List<IChromosome>(subChromosomeLengths.Count);
            var currentGeneIdx = 0;
            foreach (var subChromosomeLength in subChromosomeLengths)
            {
                toReturn.Add(new EukaryoteChromosome(parentIndividual, currentGeneIdx, subChromosomeLength));
                currentGeneIdx += subChromosomeLength;
            }

            return toReturn;
        }

        /// <summary>
        /// Splits a parent population into karyotypes and groups the child chromosomes by
        /// sub-chromosome position, returning one child population per position.
        /// </summary>
        public static List<List<IChromosome>> GetSubPopulations(IEnumerable<IChromosome> parents, IList<int> subChromosomeLengths)
        {
            var karyotypes = parents.Select(parent => GetKaryotype(parent, subChromosomeLengths)).ToList();
            var subPopulations = Enumerable.Range(0, subChromosomeLengths.Count)
                .Select(i => karyotypes.Select(p => p[i]).ToList()).ToList();
            return subPopulations;
        }

        /// <summary>
        /// Updates the parent genes from the current child.
        /// </summary>
        public void UpdateParent()
        {
            ParentIndividual.ReplaceGenes(StartGeneIndex, GetGenes());
        }

        /// <summary>
        /// Updates the parent genes from a collection of Eukaryote children.
        /// </summary>
        public static void UpdateParent(IList<IChromosome> children)
        {
            foreach (var objEukaryoteChromosome in children)
            {
                ((EukaryoteChromosome)objEukaryoteChromosome).UpdateParent();
            }
        }

        /// <summary>
        /// Builds a complete individual back from its karyotype.
        /// </summary>
        public static IChromosome GetNewIndividual(IList<EukaryoteChromosome> karyotype)
        {
            var newParent = karyotype[0].ParentIndividual.CreateNew();
            foreach (var subChromosome in karyotype)
            {
                newParent.ReplaceGenes(subChromosome.StartGeneIndex, subChromosome.GetGenes());
            }

            return newParent;
        }

        /// <summary>
        /// Builds a population of parent individuals back from per-position child populations.
        /// </summary>
        public static IList<IChromosome> GetNewIndividuals(IList<IList<IChromosome>> subPopulations)
        {
            var toReturn = new List<IChromosome>();
            var karyotypes = Enumerable.Range(0, subPopulations[0].Count)
                .Select(i => subPopulations.Select(subPopulation => subPopulation[i]).Cast<EukaryoteChromosome>().ToList());
            foreach (var karyotype in karyotypes)
            {
                var newParent = GetNewIndividual(karyotype);
                toReturn.Add(newParent);
            }

            return toReturn;
        }
    }
}
