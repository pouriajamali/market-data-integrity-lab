using System.Text;
using System.Text.Json;
using MarketDataIntegrity;

namespace Integrity.Tests;

internal sealed class RecoveryFixture
{
    public string Root { get; }
    public string ManifestPath => PathFor("data/manifest.json");
    public string InputPath => PathFor("data/input.jsonl");
    public string Work => PathFor("run");
    public SequenceManifest Manifest { get; }
    public string ManifestHash { get; }
    public string PolicyHash => RecoveryRules.PolicyHash(ManifestHash, 2);
    public static string RepoRoot
    {
        get
        {
            var directory = new DirectoryInfo(Path.GetDirectoryName(typeof(RecoveryFixture).Assembly.Location)!);
            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "global.json")))
                directory = directory.Parent;
            return directory?.FullName ?? throw new InvalidOperationException("repository root not found");
        }
    }
    public RecoveryFixture(string name, bool empty = false)
    {
        Root = Path.Combine(RepoRoot, "artifacts", "recovery-tests", name + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(PathFor("data"));
        Candle Row(long seq, string id = "S1", string epoch = "a", bool bad = false, long close = 101) =>
            new("synthetic", id, epoch, seq, "2026-01-01T00:00:00Z", bad ? 103 : 100, 102, 98, close, 0);
        Candle[] rows =
        [
            Row(10), Row(1,"S2"), Row(10), Row(11,bad:true), Row(11,bad:true), Row(12),
            Row(10), Row(12,close:100), Row(13), Row(3,"S2"), Row(2,"S2"), Row(1,epoch:"b"),
            Row(long.MaxValue-1,"MAX"), Row(long.MaxValue,"MAX"), Row(long.MaxValue,"MAX"),
            Row(long.MaxValue-1,"MAX"), Row(long.MaxValue,"MAX",close:100), Row(long.MaxValue,"MAX"),
            Row(1,"UNDECLARED")
        ];
        using (var file = new FileStream(InputPath, FileMode.CreateNew, FileAccess.Write))
        {
            if (!empty)
            {
                for (var i = 0; i < rows.Length; i++)
                {
                    file.Write(JsonSerializer.SerializeToUtf8Bytes(rows[i], Contract.Json));
                    file.Write(i % 2 == 0 ? "\r\n"u8 : "\n"u8);
                }
                file.Write(new byte[] { 0xc3, 0x28, 10 });
                file.Write(Encoding.ASCII.GetBytes(new string('x', 1025)));
                file.Write("\r\n{"u8); // Long CRLF record followed by malformed EOF without LF.
            }
        }
        using var source = File.OpenRead(InputPath);
        Manifest = new(2, "synthetic-recovery", true, 512, 4, Contract.Hash(source), 2,
            [new("synthetic","S1","a",10),new("synthetic","S2","a",1),
             new("synthetic","S1","b",1),new("synthetic","MAX","a",long.MaxValue-1)]);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(Manifest, Contract.Json);
        File.WriteAllBytes(ManifestPath, bytes);
        ManifestHash = Contract.Hash(bytes);
        _ = SequenceValidator.Run(ManifestPath, InputPath, PathFor("baseline"));
    }
    public string PathFor(string name) => Path.Combine(Root, name.Replace('/', Path.DirectorySeparatorChar));
    public void AssertCanonical(string directory)
    {
        foreach (var file in new[] { "accepted.jsonl", "quarantine.jsonl", "summary.json" })
            Xunit.Assert.Equal(File.ReadAllBytes(PathFor("baseline/" + file)), File.ReadAllBytes(Path.Combine(directory, file)));
    }
    public RecoveryCheckpointData ReadCheckpoint(int slot) =>
        RecoveryCheckpoint.Read(Path.Combine(Work, "checkpoint." + slot), Manifest,
            ManifestHash, PolicyHash, new FileInfo(InputPath).Length);

    public static Dictionary<string,string> ActiveHashes(string directory) =>
        Directory.EnumerateFiles(directory).ToDictionary(p => Path.GetFileName(p)!,
            p => Contract.Hash(File.ReadAllBytes(p)), StringComparer.Ordinal)!;
}
