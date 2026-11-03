using System.Text.Json;
using System.Security.Cryptography;

namespace MarketDataIntegrity;

public sealed record RecoveryOutcome(SequenceSummary Summary, long RestoredGeneration,
    int RejectedCandidates, bool AlreadyCompleted);

internal sealed record RecoveryPolicy(int CheckpointFormatVersion, string RunnerContract,
    string SequenceContract, string ManifestSha256, int CheckpointEveryRecords,
    int MaxCheckpointBytes, int GenerationSlots, int MaxDecisionBytes,
    int MaxSummaryBytes, int CheckpointMaxDepth, long MaxEvidenceBytes, int MaxEvidenceFiles);

internal sealed record RecoveryStreamState(StreamBaseline Baseline, long? LastConsumed,
    string? BlockedReason, Candle[] Window);
internal sealed record SinkPrefix(long Length, string Sha256);

internal sealed record RecoveryCheckpointData(int Version, long Generation, string PolicySha256,
    string ManifestSha256, string InputSha256, long InputLength, long Cursor, long NextLine,
    bool EndOfInput, long Records, long Accepted, long Quarantined, long UnattributedRecords,
    long UncomparedRecords, SortedDictionary<string, long> Reasons, RecoveryStreamState[] Streams,
    SinkPrefix AcceptedPrefix, SinkPrefix QuarantinePrefix);

internal sealed class RecoveryIdentityException() : IOException("recovery identity or version mismatch");

internal static class RecoveryRules
{
    public const int CheckpointCap = 64 * 1024 * 1024;
    public const int DecisionCap = 2048;
    public const int SummaryCap = 4 * 1024 * 1024;
    public const int CheckpointDepth = 8;
    public const long EvidenceCap = 192L * 1024 * 1024;
    public const int EvidenceFiles = 32;
    public static readonly string[] Reasons =
    [
        "RECORD_TOO_LONG", "UTF8_BOM", "UTF8_INVALID", "EMPTY_RECORD", "JSON_INVALID",
        "SCHEMA_INVALID", "VALUE_OUT_OF_RANGE", "OHLC_INCONSISTENT", "VALID_RECORD",
        "UNDECLARED_STREAM", "STREAM_BLOCKED", "BEFORE_BASELINE", "DUPLICATE_IN_WINDOW",
        "CONFLICT_IN_WINDOW", "STALE_OUTSIDE_WINDOW", "SEQUENCE_GAP"
    ];

    public static string PolicyHash(string manifestHash, int interval)
    {
        if (interval is < 1 or > 10_000) throw new ArgumentOutOfRangeException(nameof(interval));
        var policy = new RecoveryPolicy(1, "A3-process-crash-v1", "A2-sequence-v1",
            manifestHash, interval, CheckpointCap, 2, DecisionCap, SummaryCap, CheckpointDepth, EvidenceCap, EvidenceFiles);
        return Contract.Hash(JsonSerializer.SerializeToUtf8Bytes(policy, Contract.Json));
    }

    public static bool IsHash(string? value) => value is { Length: 64 } &&
        value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');

    public static void Validate(RecoveryCheckpointData checkpoint, SequenceManifest manifest,
        string manifestHash, string policyHash, long inputLength)
    {
        if (checkpoint.Version != 1 || checkpoint.PolicySha256 != policyHash ||
            checkpoint.ManifestSha256 != manifestHash ||
            checkpoint.InputSha256 != manifest.InputSha256 || checkpoint.InputLength != inputLength)
            throw new RecoveryIdentityException();
        if (checkpoint.Generation < 0 || checkpoint.Cursor < 0 || checkpoint.Cursor > inputLength ||
            checkpoint.EndOfInput != (checkpoint.Cursor == inputLength) ||
            checkpoint.Records < 0 || checkpoint.Accepted < 0 || checkpoint.Quarantined < 0 ||
            checkpoint.UnattributedRecords < 0 || checkpoint.UncomparedRecords < 0 ||
            checkpoint.NextLine != checked(checkpoint.Records + 1) ||
            checked(checkpoint.Accepted + checkpoint.Quarantined) != checkpoint.Records ||
            checkpoint.Reasons is null || checkpoint.Reasons.Count > Reasons.Length ||
            checkpoint.Streams is null || checkpoint.Streams.Length != manifest.Baselines.Length ||
            checkpoint.AcceptedPrefix is null || checkpoint.QuarantinePrefix is null ||
            checkpoint.AcceptedPrefix.Length < 0 || checkpoint.QuarantinePrefix.Length < 0 ||
            !IsHash(checkpoint.AcceptedPrefix.Sha256) || !IsHash(checkpoint.QuarantinePrefix.Sha256))
            throw new InvalidDataException("checkpoint bounds or counters");

        long total = 0;
        foreach (var (reason, count) in checkpoint.Reasons)
        {
            if (!Reasons.Contains(reason, StringComparer.Ordinal) || count <= 0)
                throw new InvalidDataException("checkpoint reason");
            total = checked(total + count);
        }
        long Count(string reason) => checkpoint.Reasons.GetValueOrDefault(reason);
        var unattributed = Reasons[..7].Aggregate(0L, (sum, reason) => checked(sum + Count(reason)));
        unattributed = checked(unattributed + Count("UNDECLARED_STREAM"));
        if (total != checkpoint.Records || checkpoint.Accepted != Count("VALID_RECORD") ||
            checkpoint.UnattributedRecords != unattributed ||
            checkpoint.UncomparedRecords != checked(Count("BEFORE_BASELINE") +
                Count("STALE_OUTSIDE_WINDOW") + Count("STREAM_BLOCKED")))
            throw new InvalidDataException("checkpoint reason totals");

        SequenceEngine.ValidateRecoveryState(manifest, checkpoint.Streams);
        long consumed = 0;
        foreach (var state in checkpoint.Streams)
            if (state.LastConsumed is long last)
                consumed = checked(consumed + checked(last - state.Baseline.FirstSequence + 1));
        if (consumed != checked(checkpoint.Accepted + Count("OHLC_INCONSISTENT")) ||
            checkpoint.Streams.Count(s => s.BlockedReason == "SEQUENCE_GAP") != Count("SEQUENCE_GAP") ||
            checkpoint.Streams.Count(s => s.BlockedReason == "CONFLICT_IN_WINDOW") != Count("CONFLICT_IN_WINDOW") ||
            (checkpoint.Cursor == 0 && checkpoint.Records != 0))
            throw new InvalidDataException("checkpoint state totals");
    }

    public static SequenceSummary Summary(RecoveryCheckpointData checkpoint, SequenceManifest manifest,
        SequenceEngine engine) =>
        new(2, "completed", manifest.DatasetId, manifest.InputSha256, checkpoint.ManifestSha256,
            checkpoint.Records, checkpoint.Accepted, checkpoint.Quarantined,
            manifest.MaxRecordBytes, manifest.MaxStreams, manifest.SequenceWindow,
            checkpoint.UnattributedRecords > 0 ? "UNKNOWN_UNATTRIBUTABLE_RECORDS" :
                checkpoint.UncomparedRecords > 0 ? "PARTIAL_COMPARISON" : "OBSERVED_PREFIX_ONLY",
            checkpoint.UnattributedRecords, checkpoint.UncomparedRecords, engine.Snapshot(), checkpoint.Reasons);
}

internal sealed class RecoveryHooks(Action<string, long>? action = null)
{
    public bool Enabled => action is not null;
    public void Hit(string point, long records) => action?.Invoke(point, records);
}
