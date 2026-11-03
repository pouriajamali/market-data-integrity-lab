using System.Text;
using System.Text.Json;

namespace MarketDataIntegrity;

public static class SequenceValidator
{
    public static SequenceSummary Run(string manifestPath, string inputPath, string outputDirectory)
    {
        var bytes = Contract.ReadManifestBytes(manifestPath);
        var manifest = SequenceContract.Read(bytes);
        using var source = new FileStream(inputPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Run(manifest, Contract.Hash(bytes), source, outputDirectory);
    }

    internal static SequenceSummary Run(SequenceManifest manifest, string manifestHash,
        Stream source, string outputDirectory)
    {
        SequenceContract.Validate(manifest);
        if (!source.CanRead || !source.CanSeek) throw new InvalidDataException("seekable input required");
        if (Contract.Hash(source) != manifest.InputSha256) throw new InvalidDataException("source identity");
        if (Directory.Exists(outputDirectory) || File.Exists(outputDirectory))
            throw new IOException("output already exists");
        Directory.CreateDirectory(outputDirectory);
        using var ownership = new FileStream(Path.Combine(outputDirectory, "run.lock"),
            FileMode.CreateNew, FileAccess.Write, FileShare.None);
        var engine = new SequenceEngine(manifest);
        var reasons = new SortedDictionary<string, long>(StringComparer.Ordinal);
        long records = 0, accepted = 0, quarantined = 0, unattributed = 0, uncompared = 0;
        using (var goodFile = NewFile("accepted.jsonl"))
        using (var badFile = NewFile("quarantine.jsonl"))
        {
            using var good = Writer(goodFile);
            using var bad = Writer(badFile);
            BoundedReader.Visit(source, manifest.MaxRecordBytes, raw =>
            {
                var (dataReason, candle) = raw.TooLong
                    ? ("RECORD_TOO_LONG", (Candle?)null) : Contract.Parse(raw.Data);
                var reason = candle is null ? dataReason : engine.Apply(candle, dataReason);
                if (candle is null || reason == "UNDECLARED_STREAM")
                    unattributed = checked(unattributed + 1);
                if (reason is "BEFORE_BASELINE" or "STALE_OUTSIDE_WINDOW" or "STREAM_BLOCKED")
                    uncompared = checked(uncompared + 1);
                records = checked(records + 1);
                reasons[reason] = checked(reasons.GetValueOrDefault(reason) + 1);
                var row = new SequenceDecision(raw.Line, raw.Offset, raw.ByteLength,
                    raw.Sha256, reason, dataReason, candle);
                if (reason == "VALID_RECORD")
                {
                    accepted = checked(accepted + 1);
                    good.WriteLine(JsonSerializer.Serialize(row, Contract.Json));
                }
                else
                {
                    quarantined = checked(quarantined + 1);
                    bad.WriteLine(JsonSerializer.Serialize(row, Contract.Json));
                }
            });
            good.Flush();
            bad.Flush();
            goodFile.Flush(true);
            badFile.Flush(true);
        }
        if (Contract.Hash(source) != manifest.InputSha256) throw new InvalidDataException("source changed");
        var assessment = unattributed > 0 ? "UNKNOWN_UNATTRIBUTABLE_RECORDS" :
            uncompared > 0 ? "PARTIAL_COMPARISON" : "OBSERVED_PREFIX_ONLY";
        var summary = new SequenceSummary(2, "completed", manifest.DatasetId, manifest.InputSha256,
            manifestHash, records, accepted, quarantined, manifest.MaxRecordBytes,
            manifest.MaxStreams, manifest.SequenceWindow, assessment, unattributed, uncompared,
            engine.Snapshot(), reasons);
        using (var reportFile = NewFile("summary.pending"))
        {
            using var writer = Writer(reportFile);
            writer.WriteLine(JsonSerializer.Serialize(summary, Contract.Json));
            writer.Flush();
            reportFile.Flush(true);
        }
        File.Move(Path.Combine(outputDirectory, "summary.pending"), Path.Combine(outputDirectory, "summary.json"));
        return summary;

        FileStream NewFile(string name) => new(Path.Combine(outputDirectory, name),
            FileMode.CreateNew, FileAccess.Write, FileShare.None);
    }

    private static StreamWriter Writer(Stream stream) =>
        new(stream, new UTF8Encoding(false), 4096, leaveOpen: true) { NewLine = "\n" };
}
