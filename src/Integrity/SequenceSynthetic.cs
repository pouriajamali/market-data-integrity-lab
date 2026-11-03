using System.Text;
using System.Text.Json;

namespace MarketDataIntegrity;

public static class SequenceSynthetic
{
    public static void Generate(string directory)
    {
        if (Directory.Exists(directory) || File.Exists(directory)) throw new IOException("directory exists");
        Directory.CreateDirectory(directory);
        Candle Row(string instrument, string epoch, long sequence, bool bad = false, long close = 101) =>
            new("synthetic", instrument, epoch, sequence, "2026-01-01T00:00:00Z",
                bad ? 103 : 100, 102, 98, close, 0);
        // A fixed, bounded scenario. No copied feed, strategy or private fixture.
        Candle[] rows =
        [
            Row("SYNTH1", "a", 10), Row("SYNTH2", "a", 1),
            Row("SYNTH1", "a", 10), Row("SYNTH1", "a", 11, bad: true),
            Row("SYNTH1", "a", 11, bad: true), Row("SYNTH1", "a", 12),
            Row("SYNTH1", "a", 10), Row("SYNTH1", "a", 12, close: 100),
            Row("SYNTH1", "a", 13), Row("SYNTH2", "a", 3),
            Row("SYNTH2", "a", 2), Row("SYNTH1", "b", 1),
            Row("UNDECLARED", "a", 1)
        ];
        var path = Path.Combine(directory, "input.jsonl");
        using (var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write))
        using (var writer = new StreamWriter(file, new UTF8Encoding(false)) { NewLine = "\n" })
        {
            foreach (var row in rows) writer.WriteLine(JsonSerializer.Serialize(row, Contract.Json));
            writer.Write("{"); // Final malformed record intentionally has no newline.
        }
        using var source = File.OpenRead(path);
        var manifest = new SequenceManifest(2, "synthetic-a2", true, 4096, 3,
            Contract.Hash(source), 2,
            [new("synthetic", "SYNTH1", "a", 10), new("synthetic", "SYNTH2", "a", 1),
                new("synthetic", "SYNTH1", "b", 1)]);
        using var output = new FileStream(Path.Combine(directory, "manifest.json"), FileMode.CreateNew, FileAccess.Write);
        using var manifestWriter = new StreamWriter(output, new UTF8Encoding(false)) { NewLine = "\n" };
        manifestWriter.WriteLine(JsonSerializer.Serialize(manifest, Contract.Json));
    }
}
