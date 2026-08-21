#nullable disable
using System.Collections.Generic;

namespace MetaGeneticSharp
{
    /// <summary>
    /// Stores the tabu attributes and answers membership/frequency queries (#12049 axe 4).
    /// Deliberately decoupled from <see cref="ITabuProjection"/> (what is stored) and
    /// <see cref="ITabuFilter"/> (when the interdict applies): recency (short-term, tenure) and
    /// frequency (long-term, diversification) are the two classic memories of the tabu
    /// template, and hybrids combine them.
    /// </summary>
    public interface ITabuMemory
    {
        /// <summary>Commits attributes after a transition is performed.</summary>
        void Remember(IEnumerable<string> attributes);

        /// <summary>Whether the attribute is currently interdicted (recency test).</summary>
        bool IsTabu(string attribute);

        /// <summary>
        /// How many times the attribute has ever been committed (long-term frequency; 0 when
        /// never seen). The diversification signal: heavily-frequented regions of the search
        /// space deserve a penalty or an avoidance, as opposed to the recency interdict.
        /// </summary>
        long Frequency(string attribute);

        /// <summary>
        /// Ages the memory by one unit (one improvement pass). Implementations expire entries
        /// whose tenure is exhausted.
        /// </summary>
        void Tick();
    }

    /// <summary>
    /// The short-term recency memory: a bounded FIFO of the last <c>tenure</c> committed
    /// attributes. This is the "LimitedQueue" of the ygmh DiscreteTS -- the single memory their
    /// monolith hard-wires; here it is one interchangeable implementation among others.
    /// </summary>
    public class RecencyTabuMemory : ITabuMemory
    {
        private readonly Queue<string> _recent = new();
        private readonly Dictionary<string, int> _counts = new();
        private readonly int _tenure;

        /// <param name="tenure">How many committed attribute-sets stay interdicted (classic tenure; the ygmh queue capacity).</param>
        public RecencyTabuMemory(int tenure)
        {
            _tenure = tenure;
        }

        /// <inheritdoc />
        public void Remember(IEnumerable<string> attributes)
        {
            foreach (var attribute in attributes)
            {
                if (_counts.TryGetValue(attribute, out var count))
                {
                    _counts[attribute] = count + 1;
                }
                else
                {
                    _counts[attribute] = 1;
                }

                _recent.Enqueue(attribute);
            }

            // Evict by AGE (queue depth), the classic fixed-tenure expiry.
            while (_recent.Count > _tenure)
            {
                var expired = _recent.Dequeue();
                if (--_counts[expired] <= 0)
                {
                    _counts.Remove(expired);
                }
            }
        }

        /// <inheritdoc />
        public bool IsTabu(string attribute) => _counts.ContainsKey(attribute);

        /// <inheritdoc />
        public long Frequency(string attribute) =>
            _counts.TryGetValue(attribute, out var count) ? count : 0;

        /// <inheritdoc />
        public void Tick()
        {
            // Fixed-tenure variant: expiry is purely queue-depth-driven (Remember evicts);
            // Tick is the extension point for dynamic-tenure subclasses.
        }
    }

    /// <summary>
    /// The long-term frequency memory: never interdicts, only counts. Use for diversification
    /// -- regions (attributes) with high frequency are where the search has already spent its
    /// time; a <see cref="ITabuFilter"/> or the improvement operator can use
    /// <see cref="ITabuMemory.Frequency"/> to steer away without a hard interdict.
    /// </summary>
    public class FrequencyTabuMemory : ITabuMemory
    {
        private readonly Dictionary<string, long> _counts = new();

        /// <inheritdoc />
        public void Remember(IEnumerable<string> attributes)
        {
            foreach (var attribute in attributes)
            {
                _counts.TryGetValue(attribute, out var count);
                _counts[attribute] = count + 1;
            }
        }

        /// <inheritdoc />
        public bool IsTabu(string attribute) => false;

        /// <inheritdoc />
        public long Frequency(string attribute) =>
            _counts.TryGetValue(attribute, out var count) ? count : 0;

        /// <inheritdoc />
        public void Tick()
        {
        }
    }

    /// <summary>
    /// A recency memory with tenure growing on repetition: each time an attribute is
    /// recommitted while still interdicted, its eviction is deferred -- the more a cycle
    /// replays a move, the longer that move stays forbidden (dynamic tenure, Glover's
    /// reaction strategy against persistent cycling).
    /// </summary>
    public class ReactiveTabuMemory : ITabuMemory
    {
        private readonly Dictionary<string, long> _commitCounts = new();
        private readonly Dictionary<string, int> _live = new();
        private readonly int _baseTenure;

        /// <param name="baseTenure">The tenure of an attribute committed for the first time.</param>
        public ReactiveTabuMemory(int baseTenure)
        {
            _baseTenure = baseTenure;
        }

        /// <inheritdoc />
        public void Remember(IEnumerable<string> attributes)
        {
            foreach (var attribute in attributes)
            {
                _commitCounts.TryGetValue(attribute, out var commits);
                _commitCounts[attribute] = commits + 1;

                // Repetition extends the stay: each re-commit of an attribute still under
                // interdict defers its eviction by one more base-tenure unit.
                _live.TryGetValue(attribute, out var stay);
                _live[attribute] = stay + _baseTenure;
            }
        }

        /// <inheritdoc />
        public bool IsTabu(string attribute) => _live.ContainsKey(attribute);

        /// <inheritdoc />
        public long Frequency(string attribute) =>
            _commitCounts.TryGetValue(attribute, out var count) ? count : 0;

        /// <inheritdoc />
        public void Tick()
        {
            foreach (var key in _live.Keys.ToList())
            {
                if (--_live[key] <= 0)
                {
                    _live.Remove(key);
                }
            }
        }
    }

    /// <summary>Composes two memories: interdicted when EITHER interdicts, frequency is the sum.</summary>
    public class HybridTabuMemory : ITabuMemory
    {
        private readonly ITabuMemory _recency;
        private readonly ITabuMemory _frequency;

        /// <param name="recency">The short-term component (its IsTabu carries the interdict).</param>
        /// <param name="frequency">The long-term component (carries Frequency; its IsTabu is advisory and composed by OR anyway).</param>
        public HybridTabuMemory(ITabuMemory recency, ITabuMemory frequency)
        {
            _recency = recency;
            _frequency = frequency;
        }

        /// <inheritdoc />
        public void Remember(IEnumerable<string> attributes)
        {
            _recency.Remember(attributes);
            _frequency.Remember(attributes);
        }

        /// <inheritdoc />
        public bool IsTabu(string attribute) =>
            _recency.IsTabu(attribute) || _frequency.IsTabu(attribute);

        /// <inheritdoc />
        public long Frequency(string attribute) =>
            _recency.Frequency(attribute) + _frequency.Frequency(attribute);

        /// <inheritdoc />
        public void Tick()
        {
            _recency.Tick();
            _frequency.Tick();
        }
    }
}
