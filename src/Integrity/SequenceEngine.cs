namespace MarketDataIntegrity;

// One state owner; no I/O and no state admission from records.
internal sealed class SequenceEngine
{
    private sealed class State(StreamBaseline baseline, int window)
    {
        public readonly StreamBaseline Baseline = baseline;
        public Candle?[]? Slots;
        public int WriteIndex;
        public int Count;
        public long? Last;
        public string? Blocked;
        public long? Expected => Last is null ? Baseline.FirstSequence :
            Last == long.MaxValue ? null : checked(Last.Value + 1);

        public void Consume(Candle candle)
        {
            Slots ??= new Candle?[window];
            Slots[WriteIndex] = candle;
            WriteIndex = (WriteIndex + 1) % window;
            Count = Math.Min(Count + 1, window);
            Last = candle.Sequence;
        }

        public Candle? Find(long sequence)
        {
            if (Slots is not null)
                foreach (var candle in Slots)
                    if (candle?.Sequence == sequence) return candle;
            return null;
        }
    }

    private readonly Dictionary<(string, string, string), State> states;
    public int DeclaredCount => states.Count;
    public int RetainedCount => states.Values.Sum(s => s.Count);

    public SequenceEngine(SequenceManifest manifest)
    {
        SequenceContract.Validate(manifest); // Must precede dictionary/window allocations.
        states = manifest.Baselines.ToDictionary(
            b => (b.Source, b.Instrument, b.Epoch), b => new State(b, manifest.SequenceWindow));
    }

    public string Apply(Candle candle, string dataReason)
    {
        if (dataReason is not ("VALID_RECORD" or "OHLC_INCONSISTENT"))
            throw new ArgumentException("usable envelope required", nameof(dataReason));
        if (!states.TryGetValue((candle.Source, candle.Instrument, candle.Epoch), out var state))
            return "UNDECLARED_STREAM";
        if (state.Blocked is not null) return "STREAM_BLOCKED";
        if (candle.Sequence < state.Baseline.FirstSequence) return "BEFORE_BASELINE";
        if (state.Expected == candle.Sequence)
        {
            state.Consume(candle);
            return dataReason;
        }
        if (state.Expected is long expected && candle.Sequence > expected)
        {
            state.Blocked = "SEQUENCE_GAP";
            return state.Blocked;
        }
        var retained = state.Find(candle.Sequence);
        if (retained is null) return "STALE_OUTSIDE_WINDOW";
        if (retained == candle) return "DUPLICATE_IN_WINDOW";
        state.Blocked = "CONFLICT_IN_WINDOW";
        return state.Blocked;
    }

    public StreamSnapshot[] Snapshot() => states.Values
        .OrderBy(s => s.Baseline.Source, StringComparer.Ordinal)
        .ThenBy(s => s.Baseline.Instrument, StringComparer.Ordinal)
        .ThenBy(s => s.Baseline.Epoch, StringComparer.Ordinal)
        .Select(s => new StreamSnapshot(s.Baseline.Source, s.Baseline.Instrument,
            s.Baseline.Epoch, s.Baseline.FirstSequence, s.Last, s.Expected,
            s.Last == long.MaxValue, s.Blocked, s.Count)).ToArray();
}
