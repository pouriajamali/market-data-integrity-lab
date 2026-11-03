using System.Text;
using System.Text.Json;

namespace MarketDataIntegrity;

public static class Validator
{
    public static Summary Run(string manifestPath, string inputPath, string outputDirectory)
    {
        var manifestBytes = Contract.ReadManifestBytes(manifestPath);
        var manifest = Contract.ReadManifest(manifestBytes);
        using var input = new FileStream(inputPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Run(manifest, Contract.Hash(manifestBytes), input, outputDirectory);
    }

    // Seekable source required. Hash checks are consistency guards, not adversarial immutability.
    internal static Summary Run(Manifest manifest, string manifestHash, Stream input, string outputDirectory)
    {
        if (!input.CanRead || !input.CanSeek) throw new InvalidDataException("seekable input required");
        // Validate callers of this overload against the same bounded contract.
        manifest = Contract.ReadManifest(JsonSerializer.SerializeToUtf8Bytes(manifest, Contract.Json));
        if (Contract.Hash(input) != manifest.InputSha256) throw new InvalidDataException("source identity");
        if (Directory.Exists(outputDirectory) || File.Exists(outputDirectory))
            throw new IOException("output already exists");
        Directory.CreateDirectory(outputDirectory);
        // CreateNew excludes a concurrent owner even when directory creation races.
        using var ownership = new FileStream(Path.Combine(outputDirectory, "run.lock"),
            FileMode.CreateNew, FileAccess.Write, FileShare.None);
        var streams = new HashSet<(string Source, string Instrument, string Epoch)>();
        var reasons = new SortedDictionary<string, long>(StringComparer.Ordinal);
        long records = 0, accepted = 0, quarantined = 0;
        using (var goodFile = NewFile("accepted.jsonl"))
        using (var badFile = NewFile("quarantine.jsonl"))
        {
            using var good = Writer(goodFile);
            using var bad = Writer(badFile);
            BoundedReader.Visit(input, manifest.MaxRecordBytes, raw =>
            {
                var parsed = raw.TooLong ? ("RECORD_TOO_LONG", (Candle?)null) : Contract.Parse(raw.Data);
                var (reason, candle) = parsed;
                if (candle is not null)
                {
                    var key = (candle.Source, candle.Instrument, candle.Epoch);
                    if (!streams.Contains(key) && streams.Count == manifest.MaxStreams)
                        throw new InvalidDataException("stream capacity exceeded");
                    streams.Add(key);
                }
                records = checked(records + 1);
                reasons[reason] = checked(reasons.GetValueOrDefault(reason) + 1);
                var decision = new Decision(raw.Line, raw.Offset, raw.ByteLength, raw.Sha256, reason, candle);
                if (reason == "VALID_RECORD")
                {
                    accepted = checked(accepted + 1);
                    good.WriteLine(JsonSerializer.Serialize(decision, Contract.Json));
                }
                else
                {
                    quarantined = checked(quarantined + 1);
                    bad.WriteLine(JsonSerializer.Serialize(decision, Contract.Json));
                }
            });
            good.Flush();
            bad.Flush();
            goodFile.Flush(true);
            badFile.Flush(true);
        }
        if (Contract.Hash(input) != manifest.InputSha256)
            throw new InvalidDataException("source changed");
        var report = new Summary("completed", manifest.DatasetId, manifest.InputSha256,
            manifestHash, records, accepted, quarantined, streams.Count,
            manifest.MaxRecordBytes, manifest.MaxStreams, reasons);
        using (var reportFile = NewFile("summary.pending"))
        {
            using var writer = Writer(reportFile);
            writer.WriteLine(JsonSerializer.Serialize(report, Contract.Json));
            writer.Flush();
            reportFile.Flush(true);
        }
        // Only this final rename publishes completion. Partial sinks are never a completed run.
        File.Move(Path.Combine(outputDirectory, "summary.pending"),
            Path.Combine(outputDirectory, "summary.json"));
        return report;

        FileStream NewFile(string name) => new(Path.Combine(outputDirectory, name),
            FileMode.CreateNew, FileAccess.Write, FileShare.None);
    }

    private static StreamWriter Writer(Stream stream) =>
        new(stream, new UTF8Encoding(false), 4096, leaveOpen: true) { NewLine = "\n" };
}
