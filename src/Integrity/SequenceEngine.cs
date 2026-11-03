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
    public RecoveryStreamState[] ExportRecovery() => states.Values
        .OrderBy(s => s.Baseline.Source, StringComparer.Ordinal)
        .ThenBy(s => s.Baseline.Instrument, StringComparer.Ordinal)
        .ThenBy(s => s.Baseline.Epoch, StringComparer.Ordinal)
        .Select(s => new RecoveryStreamState(s.Baseline, s.Last, s.Blocked,
            s.Slots?.Where(c => c is not null).Select(c => c!).OrderBy(c => c.Sequence).ToArray() ?? []))
        .ToArray();

    public static void ValidateRecoveryState(SequenceManifest manifest, RecoveryStreamState[] restored)
    {
        if (restored.Length != manifest.Baselines.Length)
            throw new InvalidDataException("restored stream count");
        var seen = new HashSet<(string, string, string)>();
        var baselines = manifest.Baselines.ToDictionary(b => (b.Source, b.Instrument, b.Epoch));
        foreach (var state in restored)
        {
            if (state is null || state.Baseline is null || state.Window is null)
                throw new InvalidDataException("restored state missing");
            var b = state.Baseline;
            var key = (b.Source, b.Instrument, b.Epoch);
            if (!baselines.TryGetValue(key, out var declared) || b != declared || !seen.Add(key) ||
                state.BlockedReason is not (null or "SEQUENCE_GAP" or "CONFLICT_IN_WINDOW"))
                throw new InvalidDataException("restored identity");
            if (state.LastConsumed is null)
            {
                if (state.Window.Length != 0 || state.BlockedReason == "CONFLICT_IN_WINDOW")
                    throw new InvalidDataException("restored empty state");
                continue;
            }
            var last = state.LastConsumed.Value;
            if (last < b.FirstSequence || (last == long.MaxValue && state.BlockedReason == "SEQUENCE_GAP"))
                throw new InvalidDataException("restored terminal state");
            var count = (int)Math.Min(manifest.SequenceWindow, checked(last - b.FirstSequence + 1));
            if (state.Window.Length != count) throw new InvalidDataException("restored window length");
            var first = checked(last - count + 1);
            for (var i = 0; i < count; i++)
            {
                var row = state.Window[i];
                if (row is null || row.Source != b.Source || row.Instrument != b.Instrument ||
                    row.Epoch != b.Epoch || row.Sequence != checked(first + i))
                    throw new InvalidDataException("restored sequence range");
                var parsed = Contract.Parse(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(row, Contract.Json));
                if (parsed.Record != row) throw new InvalidDataException("restored payload");
            }
        }
    }

    public void RestoreRecovery(SequenceManifest manifest, RecoveryStreamState[] restored)
    {
        ValidateRecoveryState(manifest, restored); // No new windows until every stream validates.
        foreach (var saved in restored)
        {
            var b = saved.Baseline;
            var state = states[(b.Source, b.Instrument, b.Epoch)];
            state.Slots = null; state.WriteIndex = 0; state.Count = 0; state.Last = null; state.Blocked = null;
            foreach (var row in saved.Window) state.Consume(row);
            state.Last = saved.LastConsumed;
            state.Blocked = saved.BlockedReason;
        }
    }

}
