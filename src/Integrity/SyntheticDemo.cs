using System.Text;
using System.Text.Json;

namespace MarketDataIntegrity;

internal static class SyntheticDemo
{
    public static void Run(string directory, TextWriter output)
    {
        // Refuse an existing destination before creating any file in it.
        if (Directory.Exists(directory) || File.Exists(directory))
            throw new IOException("new demo directory required");
        Directory.CreateDirectory(directory);
        using var owner = new FileStream(Path.Combine(directory, "demo.lock"),
            FileMode.CreateNew, FileAccess.Write, FileShare.None);
        if (Directory.EnumerateFileSystemEntries(directory).Count() != 1)
            throw new IOException("demo destination changed");
        string At(string name) => Path.Combine(directory, name);
        Synthetic.Generate(At("a1-input"), 34);
        var a1 = Validator.Run(At("a1-input/manifest.json"), At("a1-input/input.jsonl"), At("a1-result"));
        if (a1.Records != 34 || a1.Accepted != 32 || a1.Quarantined != 2)
            throw new InvalidDataException("unexpected A1 demo result");
        output.WriteLine("A1: 34 records; 32 accepted; 2 quarantined (expected bad OHLC).");
        Reasons("A1", a1.Reasons);
        SequenceSynthetic.Generate(At("sequence-input"));
        var manifest = At("sequence-input/manifest.json");
        var input = At("sequence-input/input.jsonl");
        var a2 = SequenceValidator.Run(manifest, input, At("sequence-result"));
        if (a2.Records != 14 || a2.Accepted != 4 || a2.Quarantined != 10 || a2.UnattributedRecords != 2)
            throw new InvalidDataException("unexpected sequence demo result");
        output.WriteLine("A2: 14 records; 4 accepted; 10 quarantined; 2 unattributed (UNKNOWN).");
        Reasons("A2", a2.Reasons);
        var start = RecoveryRunner.Start(manifest, input, At("recovery-result"), 2);
        var resume = RecoveryRunner.Resume(manifest, input, At("recovery-result"), 2);
        if (start.AlreadyCompleted || !resume.AlreadyCompleted || resume.RestoredGeneration != 7)
            throw new InvalidDataException("unexpected completed resume");
        var hashes = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var name in new[] { "accepted.jsonl", "quarantine.jsonl", "summary.json" })
        {
            var reference = File.ReadAllBytes(At("sequence-result/" + name));
            var recovered = File.ReadAllBytes(At("recovery-result/" + name));
            if (!reference.AsSpan().SequenceEqual(recovered))
                throw new InvalidDataException("canonical output mismatch");
            using var file = File.OpenRead(At("recovery-result/" + name));
            hashes[name] = Contract.Hash(file);
        }
        output.WriteLine("A3: completed-run resume verified; generation=7; all 3 canonical files equal A2.");
        output.WriteLine("This is NOT a crash demonstration. Owned-child interruption proof is in RecoveryTests.");
        using (var report = new FileStream(At("demo-summary.json"), FileMode.CreateNew, FileAccess.Write, FileShare.None))
        using (var writer = new StreamWriter(report, new UTF8Encoding(false)) { NewLine = "\n" })
        {
            writer.WriteLine(JsonSerializer.Serialize(new
            {
                formatVersion = 1, status = "expected_demo_outcomes_match", synthetic = true,
                a1 = new { a1.Records, a1.Accepted, a1.Quarantined },
                sequence = new { a2.Records, a2.Accepted, a2.Quarantined, a2.UnattributedRecords, a2.Assessment },
                completedResume = true, restoredGeneration = resume.RestoredGeneration,
                crashDemonstrated = false, canonicalSha256 = hashes
            }, Contract.Json));
        }
        output.WriteLine("Demo complete. Exit 0 means expected outcomes matched; input contains quarantined records.");
        output.WriteLine("Inspect a1-result/, sequence-result/, recovery-result/ and demo-summary.json in your chosen directory.");

        void Reasons(string label, SortedDictionary<string, long> reasons)
        {
            foreach (var (reason, count) in reasons) output.WriteLine($"  {label} {reason}: {count}");
        }
    }
}
