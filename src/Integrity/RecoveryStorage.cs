using System.Security.Cryptography;
using System.Text.Json;

namespace MarketDataIntegrity;

internal sealed class RecoverySink : IDisposable
{
    public FileStream File { get; }
    private IncrementalHash digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
    public long WrittenLength { get; private set; }

    public RecoverySink(string path, bool create) =>
        File = new FileStream(path, create ? FileMode.CreateNew : FileMode.Open,
            FileAccess.ReadWrite, FileShare.None);

    public void Verify(SinkPrefix prefix, long expectedRows)
    {
        if (prefix.Length < 0 || prefix.Length > File.Length)
            throw new InvalidDataException("short sink prefix");
        digest.Dispose();
        digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        File.Position = 0;
        long remaining = prefix.Length, rows = 0;
        var buffer = new byte[8192];
        byte last = 0;
        while (remaining > 0)
        {
            var read = File.Read(buffer, 0, (int)Math.Min(buffer.Length, remaining));
            if (read == 0) throw new InvalidDataException("short sink read");
            digest.AppendData(buffer.AsSpan(0, read));
            for (var i = 0; i < read; i++) if (buffer[i] == 10) rows = checked(rows + 1);
            last = buffer[read - 1];
            remaining -= read;
        }
        if (Convert.ToHexStringLower(digest.GetCurrentHash()) != prefix.Sha256 ||
            rows != expectedRows || (prefix.Length != 0 && last != 10))
            throw new InvalidDataException("sink prefix mismatch");
        WrittenLength = prefix.Length;
    }

    public void Trim(SinkPrefix prefix)
    {
        File.SetLength(prefix.Length);
        File.Position = prefix.Length;
        WrittenLength = prefix.Length;
    }

    public void Write(SequenceDecision decision, RecoveryHooks hooks, long completedRecords)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(decision, Contract.Json);
        if (bytes.Length + 1 > RecoveryRules.DecisionCap)
            throw new InvalidDataException("decision byte budget");
        var line = new byte[bytes.Length + 1];
        bytes.CopyTo(line, 0);
        line[^1] = 10;
        if (hooks.Enabled)
        {
            var half = line.Length / 2;
            File.Write(line.AsSpan(0, half));
            File.Flush(); // Test probe exposes a real partial physical write before kill.
            hooks.Hit("DecisionMid", completedRecords);
            File.Write(line.AsSpan(half));
        }
        else File.Write(line);
        digest.AppendData(line);
        WrittenLength = checked(WrittenLength + line.Length);
    }

    public void Flush() => File.Flush(true);
    public SinkPrefix Prefix() => new(WrittenLength, Convert.ToHexStringLower(digest.GetCurrentHash()));
    public void Dispose() { digest.Dispose(); File.Dispose(); }
}

internal static class RecoveryEvidence
{
    internal sealed record EvidenceObject(string Kind, string Blob, long Length, string Sha256);
    private sealed record Receipt(long RestoredGeneration, int RejectedCandidates, EvidenceObject[] Objects);

    public static void Protect(string directory, long generation, string[] rejected,
        RecoverySink accepted, SinkPrefix acceptedPrefix, RecoverySink quarantine,
        SinkPrefix quarantinePrefix, RecoveryHooks hooks, long records)
    {
        var objects = new List<EvidenceObject>();
        var evidence = Path.Combine(directory, "evidence");
        foreach (var path in rejected.Concat(new[]
        {
            Path.Combine(directory, "checkpoint.pending"), Path.Combine(directory, "summary.pending")
        }).Distinct(StringComparer.Ordinal))
        {
            if (!System.IO.File.Exists(path)) continue;
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            objects.Add(Copy(evidence, Path.GetFileName(path), stream, 0, stream.Length, hooks, records));
        }
        if (accepted.File.Length > acceptedPrefix.Length)
            objects.Add(Copy(evidence, "accepted-tail", accepted.File, acceptedPrefix.Length,
                accepted.File.Length - acceptedPrefix.Length, hooks, records));
        if (quarantine.File.Length > quarantinePrefix.Length)
            objects.Add(Copy(evidence, "quarantine-tail", quarantine.File, quarantinePrefix.Length,
                quarantine.File.Length - quarantinePrefix.Length, hooks, records));
        if (objects.Count == 0) return;
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new Receipt(generation, rejected.Length, objects.ToArray()), Contract.Json);
        using var receipt = new MemoryStream(bytes, writable: false);
        _ = Copy(evidence, "receipt", receipt, 0, bytes.Length, hooks, records);
        // All required evidence objects and their receipt now exist and were verified.
    }

    internal static EvidenceObject Copy(string directory, string kind, Stream source,
        long offset, long length, RecoveryHooks hooks, long records)
    {
        if (offset < 0 || length < 0 || offset > source.Length || length > source.Length - offset ||
            length > RecoveryRules.EvidenceCap) throw new InvalidDataException("evidence range");
        Directory.CreateDirectory(directory);
        var expected = HashRange(source, offset, length);
        var target = Path.Combine(directory, "blob-" + expected + ".bin");
        var pending = Path.Combine(directory, "pending-" + expected + ".bin");
        CheckBudget(directory, 0, 0);
        if (System.IO.File.Exists(target))
        {
            VerifyObject(target, length, expected);
            return new(kind, Path.GetFileName(target), length, expected);
        }
        if (System.IO.File.Exists(pending))
        {
            VerifyObject(pending, length, expected); // Incomplete pending evidence is never overwritten.
            System.IO.File.Move(pending, target);
            return new(kind, Path.GetFileName(target), length, expected);
        }
        CheckBudget(directory, length, 1);
        using (var output = new FileStream(pending, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            var original = source.Position;
            try
            {
                source.Position = offset;
                var buffer = new byte[8192];
                long remaining = length;
                using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                var first = true;
                while (remaining > 0)
                {
                    var read = source.Read(buffer, 0, (int)Math.Min(buffer.Length, remaining));
                    if (read == 0) throw new EndOfStreamException("evidence read");
                    output.Write(buffer, 0, read);
                    digest.AppendData(buffer.AsSpan(0, read));
                    remaining -= read;
                    if (first)
                    {
                        output.Flush();
                        hooks.Hit("EvidenceCopyMid", records);
                        first = false;
                    }
                }
                if (Convert.ToHexStringLower(digest.GetHashAndReset()) != expected)
                    throw new InvalidDataException("evidence source changed");
                output.Flush(true);
            }
            finally { source.Position = original; }
        }
        VerifyObject(pending, length, expected);
        System.IO.File.Move(pending, target);
        return new(kind, Path.GetFileName(target), length, expected);
    }

    private static void CheckBudget(string directory, long extraBytes, int extraFiles)
    {
        long bytes = 0;
        var files = 0;
        foreach (var path in Directory.EnumerateFileSystemEntries(directory))
        {
            var info = new FileInfo(path);
            if ((info.Attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0)
                throw new InvalidDataException("unexpected evidence entry");
            files = checked(files + 1);
            bytes = checked(bytes + info.Length);
        }
        if (checked(files + extraFiles) > RecoveryRules.EvidenceFiles ||
            checked(bytes + extraBytes) > RecoveryRules.EvidenceCap)
            throw new InvalidDataException("evidence budget exhausted; manual archive required");
    }

    private static void VerifyObject(string path, long length, string expected)
    {
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (file.Length != length || HashRange(file, 0, length) != expected)
            throw new InvalidDataException("evidence object conflict");
    }

    internal static string HashRange(Stream source, long offset, long length)
    {
        var original = source.Position;
        try
        {
            source.Position = offset;
            var buffer = new byte[8192];
            using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            long remaining = length;
            while (remaining > 0)
            {
                var read = source.Read(buffer, 0, (int)Math.Min(buffer.Length, remaining));
                if (read == 0) throw new EndOfStreamException("range read");
                digest.AppendData(buffer.AsSpan(0, read));
                remaining -= read;
            }
            return Convert.ToHexStringLower(digest.GetHashAndReset());
        }
        finally { source.Position = original; }
    }
}
