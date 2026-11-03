using System.Text;
using System.Text.Json;

namespace MarketDataIntegrity;

public static class RecoveryRunner
{
    public static RecoveryOutcome Start(string manifestPath, string inputPath, string directory, int interval) =>
        Execute(manifestPath, inputPath, directory, interval, resume: false, new RecoveryHooks());

    public static RecoveryOutcome Resume(string manifestPath, string inputPath, string directory, int interval) =>
        Execute(manifestPath, inputPath, directory, interval, resume: true, new RecoveryHooks());

    internal static RecoveryOutcome Execute(string manifestPath, string inputPath, string directory,
        int interval, bool resume, RecoveryHooks hooks)
    {
        var manifestBytes = Contract.ReadManifestBytes(manifestPath);
        var manifest = SequenceContract.Read(manifestBytes);
        var manifestHash = Contract.Hash(manifestBytes);
        var policyHash = RecoveryRules.PolicyHash(manifestHash, interval);
        using var source = new FileStream(inputPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        var inputLength = source.Length;
        VerifySource();
        var lockPath = Path.Combine(directory, "run.lock");
        if (!resume)
        {
            if (Directory.Exists(directory) || File.Exists(directory)) throw new IOException("workdir exists");
            hooks.Hit("BeforeDirectoryCreate", 0);
            Directory.CreateDirectory(directory);
        }
        using var ownership = new FileStream(lockPath, resume ? FileMode.Open : FileMode.CreateNew,
            FileAccess.ReadWrite, FileShare.None);
        var marker = "MDIL-A3-v1\n"u8.ToArray();
        if (resume)
        {
            if (ownership.Length != marker.Length) throw new InvalidDataException("missing initialization");
            var actual = new byte[marker.Length];
            ownership.ReadExactly(actual);
            if (!actual.AsSpan().SequenceEqual(marker)) throw new RecoveryIdentityException();
        }
        else
        {
            if (Directory.EnumerateFileSystemEntries(directory).Count() != 1)
                throw new IOException("workdir race");
            ownership.Write(marker);
            ownership.Flush(true);
        }
        hooks.Hit("Owned", 0);
        using var accepted = new RecoverySink(Path.Combine(directory, "accepted.jsonl"), !resume);
        using var quarantine = new RecoverySink(Path.Combine(directory, "quarantine.jsonl"), !resume);
        var engine = new SequenceEngine(manifest);
        RecoveryCheckpointData current;
        var rejected = new List<string>();
        long restored = -1;
        if (resume)
        {
            var candidates = new List<RecoveryCheckpointData>();
            for (var slot = 0; slot < 2; slot++)
            {
                var path = Path.Combine(directory, "checkpoint." + slot);
                if (!File.Exists(path)) continue;
                try
                {
                    var data = RecoveryCheckpoint.Read(path, manifest, manifestHash, policyHash, inputLength);
                    RecoveryRules.Validate(data, manifest, manifestHash, policyHash, inputLength);
                    if (data.Generation % 2 != slot || data.Generation > data.Records)
                        throw new InvalidDataException("checkpoint generation slot");
                    candidates.Add(data);
                }
                catch (Exception error) when (error is InvalidDataException or OverflowException)
                { rejected.Add(path); }
            }
            if (candidates.Count == 2 &&
                candidates.Max(c => c.Generation) - candidates.Min(c => c.Generation) != 1)
                throw new RecoveryIdentityException();
            var eligible = new List<RecoveryCheckpointData>();
            foreach (var candidate in candidates.OrderByDescending(c => c.Generation))
            {
                try
                {
                    VerifyCursor(candidate);
                    accepted.Verify(candidate.AcceptedPrefix, candidate.Accepted);
                    quarantine.Verify(candidate.QuarantinePrefix, candidate.Quarantined);
                    eligible.Add(candidate);
                }
                catch (InvalidDataException)
                { rejected.Add(Path.Combine(directory, "checkpoint." + candidate.Generation % 2)); }
            }
            if (eligible.Count == 0) throw new InvalidDataException("no valid checkpoint");
            current = eligible.MaxBy(c => c.Generation)!;
            // Rebuild both digest contexts for the selected candidate, after all candidates were inspected.
            accepted.Verify(current.AcceptedPrefix, current.Accepted);
            quarantine.Verify(current.QuarantinePrefix, current.Quarantined);
            engine.RestoreRecovery(manifest, current.Streams);
            restored = current.Generation;
            var summaryPath = Path.Combine(directory, "summary.json");
            if (File.Exists(summaryPath))
            {
                var expected = SummaryBytes(RecoveryRules.Summary(current, manifest, engine));
                if (!current.EndOfInput || accepted.File.Length != current.AcceptedPrefix.Length ||
                    quarantine.File.Length != current.QuarantinePrefix.Length ||
                    !ReadBounded(summaryPath, RecoveryRules.SummaryCap).AsSpan().SequenceEqual(expected))
                    throw new InvalidDataException("completed summary mismatch");
                return new(RecoveryRules.Summary(current, manifest, engine), restored, rejected.Count, true);
            }
            // No active bytes are removed until every required failure artifact has a verified copy.
            RecoveryEvidence.Protect(directory, restored, rejected.Distinct(StringComparer.Ordinal).ToArray(),
                accepted, current.AcceptedPrefix, quarantine, current.QuarantinePrefix, hooks, current.Records);
            foreach (var pending in new[] { "checkpoint.pending", "summary.pending" })
            {
                var path = Path.Combine(directory, pending);
                if (File.Exists(path)) File.Delete(path);
            }
            accepted.Trim(current.AcceptedPrefix);
            hooks.Hit("FirstSinkTruncated", current.Records);
            quarantine.Trim(current.QuarantinePrefix);
        }
        else
        {
            current = new(1, 0, policyHash, manifestHash, manifest.InputSha256, inputLength, 0, 1,
                inputLength == 0, 0, 0, 0, 0, 0, new(StringComparer.Ordinal), engine.ExportRecovery(),
                accepted.Prefix(), quarantine.Prefix());
            accepted.Flush();
            quarantine.Flush();
            RecoveryCheckpoint.Publish(directory, current, hooks);
        }

        var dirty = false;
        var baseOffset = current.Cursor;
        var baseRecords = current.Records;
        source.Position = current.Cursor;
        BoundedReader.Visit(source, manifest.MaxRecordBytes, raw =>
        {
            var (dataReason, candle) = raw.TooLong ? ("RECORD_TOO_LONG", (Candle?)null) : Contract.Parse(raw.Data);
            var reason = candle is null ? dataReason : engine.Apply(candle, dataReason);
            var absoluteOffset = checked(baseOffset + raw.Offset);
            var line = checked(baseRecords + raw.Line);
            var end = checked(absoluteOffset + raw.ByteLength);
            if (end > inputLength) throw new InvalidDataException("source grew");
            var cursor = end < inputLength ? checked(end + 1) : end;
            var decision = new SequenceDecision(line, absoluteOffset, raw.ByteLength,
                raw.Sha256, reason, dataReason, candle);
            (reason == "VALID_RECORD" ? accepted : quarantine).Write(decision, hooks, current.Records);
            var records = checked(current.Records + 1);
            current.Reasons[reason] = checked(current.Reasons.GetValueOrDefault(reason) + 1);
            current = current with
            {
                Records = records, NextLine = checked(records + 1), Cursor = cursor,
                EndOfInput = cursor == inputLength,
                Accepted = checked(current.Accepted + (reason == "VALID_RECORD" ? 1 : 0)),
                Quarantined = checked(current.Quarantined + (reason == "VALID_RECORD" ? 0 : 1)),
                UnattributedRecords = checked(current.UnattributedRecords +
                    (candle is null || reason == "UNDECLARED_STREAM" ? 1 : 0)),
                UncomparedRecords = checked(current.UncomparedRecords +
                    (reason is "BEFORE_BASELINE" or "STALE_OUTSIDE_WINDOW" or "STREAM_BLOCKED" ? 1 : 0))
            };
            dirty = true;
            hooks.Hit("DecisionWritten", current.Records);
            if (current.Records % interval == 0) Commit();
        });
        VerifySource();
        if (current.Cursor != inputLength) throw new InvalidDataException("cursor did not reach EOF");
        if (dirty) Commit(); // Re-finalizing a committed EOF never invents a new generation.
        var summary = RecoveryRules.Summary(current, manifest, engine);
        var pendingSummary = Path.Combine(directory, "summary.pending");
        using (var file = new FileStream(pendingSummary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            file.Write(SummaryBytes(summary));
            file.Flush(true);
        }
        hooks.Hit("SummaryFlushed", current.Records);
        File.Move(pendingSummary, Path.Combine(directory, "summary.json"));
        hooks.Hit("SummaryCommitted", current.Records);
        return new(summary, restored, rejected.Count, false);

        void Commit()
        {
            accepted.Flush();
            hooks.Hit("AcceptedFlushed", current.Records);
            quarantine.Flush();
            hooks.Hit("SinksFlushed", current.Records);
            var checkpoint = current with
            {
                Generation = checked(current.Generation + 1),
                Streams = engine.ExportRecovery(), AcceptedPrefix = accepted.Prefix(),
                QuarantinePrefix = quarantine.Prefix()
            };
            RecoveryRules.Validate(checkpoint, manifest, manifestHash, policyHash, inputLength);
            RecoveryCheckpoint.Publish(directory, checkpoint, hooks);
            current = checkpoint;
            dirty = false;
        }

        void VerifySource()
        {
            if (source.Length != inputLength || Contract.Hash(source) != manifest.InputSha256 ||
                source.Length != inputLength) throw new InvalidDataException("source identity changed");
        }

        void VerifyCursor(RecoveryCheckpointData checkpoint)
        {
            source.Position = 0;
            long remaining = checkpoint.Cursor, lines = 0;
            byte last = 0;
            var buffer = new byte[8192];
            while (remaining > 0)
            {
                var read = source.Read(buffer, 0, (int)Math.Min(buffer.Length, remaining));
                if (read == 0) throw new InvalidDataException("short source prefix");
                for (var i = 0; i < read; i++) if (buffer[i] == 10) lines = checked(lines + 1);
                last = buffer[read - 1];
                remaining -= read;
            }
            if (checkpoint.Cursor == inputLength && inputLength != 0 && last != 10) lines = checked(lines + 1);
            if (lines != checkpoint.Records || (checkpoint.Cursor > 0 &&
                checkpoint.Cursor < inputLength && last != 10))
                throw new InvalidDataException("cursor not a complete record boundary");
        }
    }

    internal static byte[] SummaryBytes(SequenceSummary summary)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(summary, Contract.Json);
        if (bytes.Length + 1 > RecoveryRules.SummaryCap) throw new InvalidDataException("summary budget");
        var line = new byte[bytes.Length + 1];
        bytes.CopyTo(line, 0);
        line[^1] = 10;
        return line;
    }

    private static byte[] ReadBounded(string path, int maximum)
    {
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (file.Length > maximum) throw new InvalidDataException("summary size");
        var bytes = new byte[(int)file.Length];
        file.ReadExactly(bytes);
        return bytes;
    }
}
