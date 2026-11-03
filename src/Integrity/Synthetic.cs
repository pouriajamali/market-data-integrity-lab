using System.Text;
using System.Text.Json;

namespace MarketDataIntegrity;

public static class Synthetic
{
    // Integer formula-v1 is fixed independently of framework PRNG behavior.
    public static void Generate(string directory, int count)
    {
        if (count is < 1 or > 1_000_000) throw new ArgumentOutOfRangeException(nameof(count));
        if (Directory.Exists(directory) || File.Exists(directory)) throw new IOException("directory exists");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "input.jsonl");
        using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write))
        using (var writer = new StreamWriter(stream, new UTF8Encoding(false)) { NewLine = "\n" })
        {
            for (var i = 1; i <= count; i++)
            {
                long price = 100_000 + i % 100;
                var row = new Candle("synthetic", "SYNTH1", "demo", i,
                    "2026-01-01T00:00:00Z", i % 17 == 0 ? price + 3 : price,
                    price + 2, price - 2, price + 1, i % 300);
                writer.WriteLine(JsonSerializer.Serialize(row, Contract.Json));
            }
        }
        using var source = File.OpenRead(path);
        var manifest = new Manifest(1, "synthetic-a1", true, 4096, 16, Contract.Hash(source));
        using var manifestStream = new FileStream(Path.Combine(directory, "manifest.json"),
            FileMode.CreateNew, FileAccess.Write);
        using var manifestWriter = new StreamWriter(manifestStream, new UTF8Encoding(false)) { NewLine = "\n" };
        manifestWriter.WriteLine(JsonSerializer.Serialize(manifest, Contract.Json));
    }
}
