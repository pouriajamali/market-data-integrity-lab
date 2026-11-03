using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;

namespace MarketDataIntegrity;

internal static class RecoveryCheckpoint
{
    private static ReadOnlySpan<byte> Magic => "MDILCP01"u8;
    internal const int HeaderBytes = 20;
    private static readonly string[] RootFields =
    [
        "version", "generation", "policySha256", "manifestSha256", "inputSha256", "inputLength",
        "cursor", "nextLine", "endOfInput", "records", "accepted", "quarantined",
        "unattributedRecords", "uncomparedRecords", "reasons", "streams", "acceptedPrefix",
        "quarantinePrefix"
    ];
    private static readonly string[] CandleFields =
        ["source", "instrument", "epoch", "sequence", "timestamp", "open", "high", "low", "close", "volume"];

    public static void Publish(string directory, RecoveryCheckpointData data, RecoveryHooks hooks)
    {
        using var bounded = new BoundedBuffer(RecoveryRules.CheckpointCap);
        JsonSerializer.Serialize(bounded, data, Contract.Json);
        var payload = bounded.Bytes();
        var pending = Path.Combine(directory, "checkpoint.pending");
        using (var file = new FileStream(pending, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            Span<byte> header = stackalloc byte[HeaderBytes];
            Magic.CopyTo(header);
            BinaryPrimitives.WriteInt64LittleEndian(header[8..16], data.Generation);
            BinaryPrimitives.WriteInt32LittleEndian(header[16..20], payload.Length);
            file.Write(header);
            var half = payload.Length / 2;
            file.Write(payload.AsSpan(0, half));
            if (hooks.Enabled) file.Flush(); // Probe-only visibility of a physical partial payload.
            hooks.Hit("CheckpointPayloadMid", data.Records);
            file.Write(payload.AsSpan(half));
            file.Write(SHA256.HashData(payload));
            file.Flush(true);
            hooks.Hit("CheckpointFlushed", data.Records);
        }
        File.Move(pending, Path.Combine(directory, "checkpoint." + data.Generation % 2), overwrite: true);
        hooks.Hit("CheckpointCommitted", data.Records);
    }

    public static RecoveryCheckpointData Read(string path, SequenceManifest manifest,
        string manifestHash, string policyHash, long inputLength)
    {
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (file.Length < HeaderBytes + 32 ||
            file.Length > (long)HeaderBytes + RecoveryRules.CheckpointCap + 32)
            throw new InvalidDataException("checkpoint file size");
        Span<byte> header = stackalloc byte[HeaderBytes];
        file.ReadExactly(header);
        if (!header[..8].SequenceEqual(Magic))
        {
            if (header[..6].SequenceEqual("MDILCP"u8)) throw new RecoveryIdentityException();
            throw new InvalidDataException("checkpoint magic");
        }
        var generation = BinaryPrimitives.ReadInt64LittleEndian(header[8..16]);
        var length = BinaryPrimitives.ReadInt32LittleEndian(header[16..20]);
        if (generation < 0 || length < 1 || length > RecoveryRules.CheckpointCap ||
            file.Length != (long)HeaderBytes + length + 32)
            throw new InvalidDataException("checkpoint header");
        var payload = new byte[length];
        file.ReadExactly(payload);
        Span<byte> digest = stackalloc byte[32];
        file.ReadExactly(digest);
        if (!CryptographicOperations.FixedTimeEquals(SHA256.HashData(payload), digest))
            throw new InvalidDataException("checkpoint checksum");
        try
        {
            using var doc = JsonDocument.Parse(payload,
                new JsonDocumentOptions { MaxDepth = RecoveryRules.CheckpointDepth });
            Identity(doc.RootElement, manifest, manifestHash, policyHash, inputLength);
            Shape(doc.RootElement, RootFields);
            foreach (var name in new[] { "acceptedPrefix", "quarantinePrefix" })
                Shape(doc.RootElement.GetProperty(name), ["length", "sha256"]);
            var reasons = doc.RootElement.GetProperty("reasons");
            if (reasons.ValueKind != JsonValueKind.Object) throw new InvalidDataException("reason object");
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in reasons.EnumerateObject())
                if (!RecoveryRules.Reasons.Contains(property.Name, StringComparer.Ordinal) ||
                    !seen.Add(property.Name)) throw new InvalidDataException("checkpoint reasons");
            var streams = doc.RootElement.GetProperty("streams");
            if (streams.ValueKind != JsonValueKind.Array ||
                streams.GetArrayLength() != manifest.Baselines.Length)
                throw new InvalidDataException("checkpoint stream array");
            foreach (var state in streams.EnumerateArray())
            {
                Shape(state, ["baseline", "lastConsumed", "blockedReason", "window"]);
                Shape(state.GetProperty("baseline"), ["source", "instrument", "epoch", "firstSequence"]);
                var window = state.GetProperty("window");
                if (window.ValueKind != JsonValueKind.Array ||
                    window.GetArrayLength() > manifest.SequenceWindow)
                    throw new InvalidDataException("checkpoint window array");
                foreach (var row in window.EnumerateArray()) Shape(row, CandleFields);
            }
            var data = JsonSerializer.Deserialize<RecoveryCheckpointData>(payload, Contract.Json)
                ?? throw new InvalidDataException("checkpoint absent");
            if (data.Generation != generation) throw new InvalidDataException("checkpoint generation");
            return data with { Reasons = new SortedDictionary<string, long>(data.Reasons, StringComparer.Ordinal) };
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or
            ArgumentException or OverflowException)
        { throw new InvalidDataException("checkpoint decode", error); }
    }

    private static void Identity(JsonElement root, SequenceManifest manifest,
        string manifestHash, string policyHash, long inputLength)
    {
        if (root.ValueKind != JsonValueKind.Object) throw new InvalidDataException("checkpoint object");
        string[] fields = ["version", "policySha256", "manifestSha256", "inputSha256", "inputLength"];
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in root.EnumerateObject())
            if (fields.Contains(property.Name, StringComparer.Ordinal) && !seen.Add(property.Name))
                throw new RecoveryIdentityException();
        if (seen.Count != fields.Length) throw new RecoveryIdentityException();
        var version = root.GetProperty("version");
        var length = root.GetProperty("inputLength");
        if (version.ValueKind != JsonValueKind.Number || !version.TryGetInt32(out var v) || v != 1 ||
            length.ValueKind != JsonValueKind.Number || !length.TryGetInt64(out var bytes) || bytes != inputLength)
            throw new RecoveryIdentityException();
        foreach (var pair in new[] { ("policySha256", policyHash), ("manifestSha256", manifestHash),
            ("inputSha256", manifest.InputSha256) })
        {
            var value = root.GetProperty(pair.Item1);
            if (value.ValueKind != JsonValueKind.String || value.GetString() != pair.Item2)
                throw new RecoveryIdentityException();
        }
    }

    private static void Shape(JsonElement element, string[] required)
    {
        if (element.ValueKind != JsonValueKind.Object) throw new InvalidDataException("checkpoint object");
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
            if (!required.Contains(property.Name, StringComparer.Ordinal) || !seen.Add(property.Name))
                throw new InvalidDataException("checkpoint property");
        if (seen.Count != required.Length) throw new InvalidDataException("checkpoint property missing");
    }

    private sealed class BoundedBuffer(int maximum) : Stream
    {
        private readonly MemoryStream inner = new();
        public byte[] Bytes() => inner.ToArray();
        public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));
        public override void Write(ReadOnlySpan<byte> buffer)
        {
            if (inner.Length + buffer.Length > maximum) throw new InvalidDataException("checkpoint budget");
            inner.Write(buffer);
        }
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => inner.Length;
        public override long Position { get => inner.Position; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        protected override void Dispose(bool disposing) { if (disposing) inner.Dispose(); base.Dispose(disposing); }
    }
}
