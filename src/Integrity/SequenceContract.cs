using System.Text;
using System.Text.Json;

namespace MarketDataIntegrity;

public sealed record StreamBaseline(string Source, string Instrument, string Epoch, long FirstSequence);
public sealed record SequenceManifest(int FormatVersion, string DatasetId, bool Synthetic,
    int MaxRecordBytes, int MaxStreams, string InputSha256, int SequenceWindow,
    StreamBaseline[] Baselines);

public sealed record SequenceDecision(long Line, long Offset, long ByteLength, string RawSha256,
    string Reason, string DataReason, Candle? Record);

public sealed record StreamSnapshot(string Source, string Instrument, string Epoch,
    long FirstSequence, long? LastConsumed, long? NextExpected, bool RangeExhausted,
    string? BlockedReason, int RetainedCount);

public sealed record SequenceSummary(int FormatVersion, string Status, string DatasetId,
    string InputSha256, string ManifestSha256, long Records, long Accepted, long Quarantined,
    int MaxRecordBytes, int MaxStreams, int SequenceWindow, string Assessment,
    long UnattributedRecords, long UncomparedRecords, StreamSnapshot[] StreamStates,
    SortedDictionary<string, long> Reasons);

public static class SequenceContract
{
    public const int MaximumRetained = 65_536;

    public static SequenceManifest Read(byte[] bytes)
    {
        if (bytes.Length > 65_536) throw new InvalidDataException("manifest size");
        try
        {
            _ = new UTF8Encoding(false, true).GetString(bytes);
            using var doc = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 4 });
            RequireFields(doc.RootElement, ["formatVersion", "datasetId", "synthetic",
                "maxRecordBytes", "maxStreams", "inputSha256", "sequenceWindow", "baselines"]);
            var k = doc.RootElement.GetProperty("maxStreams");
            if (k.ValueKind != JsonValueKind.Number || !k.TryGetInt32(out var maximum) ||
                maximum is < 1 or > 4096) throw new InvalidDataException("stream bound");
            var baselines = doc.RootElement.GetProperty("baselines");
            if (baselines.ValueKind != JsonValueKind.Array || baselines.GetArrayLength() < 1 ||
                baselines.GetArrayLength() > maximum) throw new InvalidDataException("baseline count");
            foreach (var baseline in baselines.EnumerateArray())
                RequireFields(baseline, ["source", "instrument", "epoch", "firstSequence"]);
            var manifest = JsonSerializer.Deserialize<SequenceManifest>(bytes, Contract.Json)
                ?? throw new InvalidDataException("manifest missing");
            Validate(manifest);
            return manifest;
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or DecoderFallbackException)
        { throw new InvalidDataException("sequence manifest encoding or JSON", error); }
    }

    internal static void Validate(SequenceManifest manifest)
    {
        if (manifest.FormatVersion != 2) throw new InvalidDataException("manifest version");
        // Share the existing metadata limits without changing the A1 parser.
        _ = Contract.ReadManifest(JsonSerializer.SerializeToUtf8Bytes(new Manifest(1,
            manifest.DatasetId, manifest.Synthetic, manifest.MaxRecordBytes, manifest.MaxStreams,
            manifest.InputSha256), Contract.Json));
        if (manifest.SequenceWindow is < 1 or > 4096 ||
            (long)manifest.MaxStreams * manifest.SequenceWindow > MaximumRetained ||
            manifest.Baselines is null || manifest.Baselines.Length < 1 ||
            manifest.Baselines.Length > manifest.MaxStreams)
            throw new InvalidDataException("window or baseline bound");
        var keys = new HashSet<(string, string, string)>();
        foreach (var baseline in manifest.Baselines)
        {
            if (baseline is null || !Contract.IsIdentifier(baseline.Source) ||
                !Contract.IsIdentifier(baseline.Instrument) || !Contract.IsIdentifier(baseline.Epoch) ||
                baseline.FirstSequence < 1 ||
                !keys.Add((baseline.Source, baseline.Instrument, baseline.Epoch)))
                throw new InvalidDataException("baseline identity");
        }
    }

    private static void RequireFields(JsonElement element, string[] fields)
    {
        if (element.ValueKind != JsonValueKind.Object) throw new InvalidDataException("object required");
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
            if (!fields.Contains(property.Name, StringComparer.Ordinal) || !seen.Add(property.Name))
                throw new InvalidDataException("unexpected or duplicate property");
        if (seen.Count != fields.Length) throw new InvalidDataException("missing property");
    }
}
