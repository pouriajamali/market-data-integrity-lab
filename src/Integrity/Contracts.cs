using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace MarketDataIntegrity;

public sealed record Manifest(int FormatVersion, string DatasetId, bool Synthetic,
    int MaxRecordBytes, int MaxStreams, string InputSha256);

public sealed record Candle(string Source, string Instrument, string Epoch, long Sequence,
    string Timestamp, long Open, long High, long Low, long Close, long Volume);

public sealed record Decision(long Line, long Offset, long ByteLength, string RawSha256,
    string Reason, Candle? Record);

public sealed record Summary(string Status, string DatasetId, string InputSha256,
    string ManifestSha256, long Records, long Accepted, long Quarantined,
    int Streams, int MaxRecordBytes, int MaxStreams, SortedDictionary<string, long> Reasons);

public static class Contract
{
    public const long MaximumNumber = 1_000_000_000_000;
    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public static string Hash(ReadOnlySpan<byte> bytes) =>
        Convert.ToHexStringLower(SHA256.HashData(bytes));

    public static string Hash(Stream stream)
    {
        stream.Position = 0;
        var hash = Convert.ToHexStringLower(SHA256.HashData(stream));
        stream.Position = 0;
        return hash;
    }

    public static byte[] ReadManifestBytes(string path)
    {
        using var input = File.OpenRead(path);
        var bytes = new byte[65_537];
        var count = 0;
        while (count < bytes.Length)
        {
            var read = input.Read(bytes.AsSpan(count));
            if (read == 0) break;
            count += read;
        }
        if (count > 65_536) throw new InvalidDataException("manifest size");
        return bytes.AsSpan(0, count).ToArray();
    }

    public static bool IsIdentifier(string? text) =>
        text is { Length: >= 1 and <= 32 } &&
        text.All(c => c is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or
            >= '0' and <= '9' or '_' or '-');

    private static Dictionary<string, JsonElement>? Fields(JsonElement root, string[] names)
    {
        if (root.ValueKind != JsonValueKind.Object) return null;
        var fields = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var field in root.EnumerateObject())
            if (!names.Contains(field.Name, StringComparer.Ordinal) ||
                !fields.TryAdd(field.Name, field.Value)) return null;
        return fields.Count == names.Length ? fields : null;
    }

    private static string? Text(Dictionary<string, JsonElement> fields, string key) =>
        fields[key].ValueKind == JsonValueKind.String ? fields[key].GetString() : null;

    private static bool Integer(Dictionary<string, JsonElement> fields, string key, out long value)
    {
        value = 0;
        return fields[key].ValueKind == JsonValueKind.Number && fields[key].TryGetInt64(out value);
    }

    public static Manifest ReadManifest(byte[] bytes)
    {
        try { return ParseManifest(bytes); }
        catch (Exception error) when (error is JsonException or InvalidOperationException or DecoderFallbackException)
        { throw new InvalidDataException("manifest encoding or JSON", error); }
    }

    private static Manifest ParseManifest(byte[] bytes)
    {
        _ = StrictUtf8.GetString(bytes);
        using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 4 });
        var f = Fields(document.RootElement,
            ["formatVersion", "datasetId", "synthetic", "maxRecordBytes", "maxStreams", "inputSha256"]);
        if (f is null || !Integer(f, "formatVersion", out var version) || version != 1 ||
            !IsIdentifier(Text(f, "datasetId")) || f["synthetic"].ValueKind != JsonValueKind.True ||
            !Integer(f, "maxRecordBytes", out var r) || r is < 64 or > 1_048_576 ||
            !Integer(f, "maxStreams", out var k) || k is < 1 or > 4096)
            throw new InvalidDataException("manifest contract");
        var hash = Text(f, "inputSha256");
        if (hash is not { Length: 64 } || !hash.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f'))
            throw new InvalidDataException("manifest hash");
        return new Manifest(1, Text(f, "datasetId")!, true, (int)r, (int)k, hash);
    }

    public static (string Reason, Candle? Record) Parse(ReadOnlyMemory<byte> raw)
    {
        var span = raw.Span;
        if (span.StartsWith(new byte[] { 0xef, 0xbb, 0xbf })) return ("UTF8_BOM", null);
        try { _ = StrictUtf8.GetString(span); }
        catch (DecoderFallbackException) { return ("UTF8_INVALID", null); }
        // A terminal CR belongs to provenance but is removed from the JSON parse view.
        if (span.Length > 0 && span[^1] == 13) raw = raw[..^1];
        if (raw.Length == 0) return ("EMPTY_RECORD", null);
        try
        {
            using var document = JsonDocument.Parse(raw, new JsonDocumentOptions { MaxDepth = 4 });
            var f = Fields(document.RootElement,
                ["source", "instrument", "epoch", "sequence", "timestamp", "open", "high", "low", "close", "volume"]);
            if (f is null || !IsIdentifier(Text(f, "source")) || !IsIdentifier(Text(f, "instrument")) ||
                !IsIdentifier(Text(f, "epoch")) || !Integer(f, "sequence", out var sequence) ||
                !Integer(f, "open", out var open) || !Integer(f, "high", out var high) ||
                !Integer(f, "low", out var low) || !Integer(f, "close", out var close) ||
                !Integer(f, "volume", out var volume))
                return ("SCHEMA_INVALID", null);
            var timestamp = Text(f, "timestamp");
            if (timestamp is null || !DateTime.TryParseExact(timestamp, "yyyy-MM-dd'T'HH:mm:ss'Z'",
                CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out _))
                return ("SCHEMA_INVALID", null);
            if (sequence < 1 || open is < 1 or > MaximumNumber || high is < 1 or > MaximumNumber ||
                low is < 1 or > MaximumNumber || close is < 1 or > MaximumNumber ||
                volume is < 0 or > MaximumNumber)
                return ("VALUE_OUT_OF_RANGE", null);
            var candle = new Candle(Text(f, "source")!, Text(f, "instrument")!, Text(f, "epoch")!,
                sequence, timestamp, open, high, low, close, volume);
            return low > high || open < low || open > high || close < low || close > high
                ? ("OHLC_INCONSISTENT", candle) : ("VALID_RECORD", candle);
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException)
        { return ("JSON_INVALID", null); }
    }
}
