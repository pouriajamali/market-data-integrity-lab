using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using MarketDataIntegrity;
using Xunit;

namespace Integrity.Tests;

public sealed class SequenceTests
{
    private static Candle Row(long seq, string instrument = "S1", string epoch = "a",
        bool bad = false, long close = 101) =>
        new("synthetic", instrument, epoch, seq, "2026-01-01T00:00:00Z",
            bad ? 103 : 100, 102, 98, close, 0);

    private static SequenceManifest Policy(byte[]? bytes = null, int window = 2, int k = 1,
        params StreamBaseline[] baselines) =>
        new(2, "synthetic-a2-test", true, 4096, k, Contract.Hash(bytes ?? []), window,
            baselines.Length == 0 ? [new("synthetic", "S1", "a", 10)] : baselines);

    private sealed record Step(Candle Row, string DataReason, string Reason,
        long? Last, long? Next, int Retained, string? Blocked = null);

    [Fact]
    public void HandwrittenTransitionOracleChecksWindowConsumptionAndPersistentConflict()
    {
        var engine = new SequenceEngine(Policy());
        Step[] steps =
        [
            new(Row(9), "VALID_RECORD", "BEFORE_BASELINE", null, 10, 0),
            new(Row(10), "VALID_RECORD", "VALID_RECORD", 10, 11, 1),
            new(Row(10), "VALID_RECORD", "DUPLICATE_IN_WINDOW", 10, 11, 1),
            new(Row(11, bad: true), "OHLC_INCONSISTENT", "OHLC_INCONSISTENT", 11, 12, 2),
            new(Row(10), "VALID_RECORD", "DUPLICATE_IN_WINDOW", 11, 12, 2),
            new(Row(11, bad: true), "OHLC_INCONSISTENT", "DUPLICATE_IN_WINDOW", 11, 12, 2),
            new(Row(10), "VALID_RECORD", "DUPLICATE_IN_WINDOW", 11, 12, 2),
            new(Row(12), "VALID_RECORD", "VALID_RECORD", 12, 13, 2),
            new(Row(10), "VALID_RECORD", "STALE_OUTSIDE_WINDOW", 12, 13, 2),
            new(Row(9), "VALID_RECORD", "BEFORE_BASELINE", 12, 13, 2),
            new(Row(11, bad: true), "OHLC_INCONSISTENT", "DUPLICATE_IN_WINDOW", 12, 13, 2),
            new(Row(11), "VALID_RECORD", "CONFLICT_IN_WINDOW", 12, 13, 2, "CONFLICT_IN_WINDOW"),
            new(Row(13), "VALID_RECORD", "STREAM_BLOCKED", 12, 13, 2, "CONFLICT_IN_WINDOW"),
            new(Row(11, bad: true), "OHLC_INCONSISTENT", "STREAM_BLOCKED", 12, 13, 2, "CONFLICT_IN_WINDOW")
        ];
        foreach (var step in steps)
        {
            Assert.Equal(step.Reason, engine.Apply(step.Row, step.DataReason));
            var snapshot = Assert.Single(engine.Snapshot());
            Assert.Equal(step.Last, snapshot.LastConsumed);
            Assert.Equal(step.Next, snapshot.NextExpected);
            Assert.Equal(step.Retained, snapshot.RetainedCount);
            Assert.Equal(step.Blocked, snapshot.BlockedReason);
            Assert.False(snapshot.RangeExhausted);
        }
    }

    [Fact]
    public void GapDoesNotHealAndOtherDeclaredStreamsAndEpochsContinue()
    {
        var policy = Policy(k: 3, baselines:
        [
            new("synthetic", "S1", "a", 10), new("synthetic", "S2", "a", 1),
            new("synthetic", "S1", "b", 1)
        ]);
        var engine = new SequenceEngine(policy);
        Assert.Equal("SEQUENCE_GAP", engine.Apply(Row(11), "VALID_RECORD"));
        Assert.Equal("VALID_RECORD", engine.Apply(Row(1, "S2"), "VALID_RECORD"));
        Assert.Equal("STREAM_BLOCKED", engine.Apply(Row(10), "VALID_RECORD"));
        Assert.Equal("VALID_RECORD", engine.Apply(Row(1, epoch: "b"), "VALID_RECORD"));
        for (var i = 0; i < 100; i++)
            Assert.Equal("UNDECLARED_STREAM", engine.Apply(Row(1, "U" + i), "VALID_RECORD"));
        Assert.Equal(3, engine.DeclaredCount);
        Assert.Equal(2, engine.RetainedCount);
        var blocked = engine.Snapshot().Single(s => s.Instrument == "S1" && s.Epoch == "a");
        Assert.Null(blocked.LastConsumed);
        Assert.Equal(10, blocked.NextExpected);
        Assert.Equal("SEQUENCE_GAP", blocked.BlockedReason);
    }

    [Fact]
    public void MaximumSequenceEndsTheRangeWithoutOverflowAndStillComparesHistory()
    {
        var policy = Policy(window: 1, baselines: [new("synthetic", "S1", "a", long.MaxValue - 1)]);
        var engine = new SequenceEngine(policy);
        Assert.Equal("VALID_RECORD", engine.Apply(Row(long.MaxValue - 1), "VALID_RECORD"));
        Assert.Equal(long.MaxValue, Assert.Single(engine.Snapshot()).NextExpected);
        Assert.Equal("VALID_RECORD", engine.Apply(Row(long.MaxValue), "VALID_RECORD"));
        var final = Assert.Single(engine.Snapshot());
        Assert.True(final.RangeExhausted);
        Assert.Null(final.NextExpected);
        Assert.Equal(long.MaxValue, final.LastConsumed);
        Assert.Equal("DUPLICATE_IN_WINDOW", engine.Apply(Row(long.MaxValue), "VALID_RECORD"));
        Assert.Equal("STALE_OUTSIDE_WINDOW", engine.Apply(Row(long.MaxValue - 1), "VALID_RECORD"));
        Assert.Equal("CONFLICT_IN_WINDOW", engine.Apply(Row(long.MaxValue, close: 100), "VALID_RECORD"));
        Assert.Equal("STREAM_BLOCKED", engine.Apply(Row(long.MaxValue), "VALID_RECORD"));
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(4097, 1)]
    [InlineData(1, 0)]
    [InlineData(1, 4097)]
    [InlineData(4096, 17)]
    public void ResourceBoundsRejectBeforeWindowAllocation(int window, int maximumStreams)
    {
        Assert.Throws<InvalidDataException>(() => new SequenceEngine(Policy(window: window, k: maximumStreams)));
    }

    [Fact]
    public void ProductBoundaryAndFullWindowRetentionStayBounded()
    {
        var engine = new SequenceEngine(Policy(window: 4096, k: 16,
            baselines: [new("synthetic", "S1", "a", 1)]));
        for (var seq = 1; seq <= 4097; seq++)
            Assert.Equal("VALID_RECORD", engine.Apply(Row(seq), "VALID_RECORD"));
        Assert.Equal(4096, engine.RetainedCount);
        Assert.Equal("STALE_OUTSIDE_WINDOW", engine.Apply(Row(1), "VALID_RECORD"));
        Assert.Equal("DUPLICATE_IN_WINDOW", engine.Apply(Row(2), "VALID_RECORD"));
        Assert.Equal(1, engine.DeclaredCount);
    }

    [Fact]
    public void StrictVersionedManifestRejectsAmbiguousOrMalformedBaselines()
    {
        var valid = JsonSerializer.Serialize(Policy(), Contract.Json);
        foreach (var invalid in new[]
        {
            valid.Replace("\"formatVersion\":2", "\"formatVersion\":1"),
            valid.Replace("\"firstSequence\":10", "\"firstSequence\":0"),
            valid.Replace("\"firstSequence\":10", "\"firstSequence\":10,\"firstSequence\":10"),
            valid.Replace("\"synthetic\"", "\"\\uD800\""),
            valid.Replace("\"source\"", "\"\\uD800\""),
            valid.Replace("\"baselines\":[", "\"baselines\":null,\"ignored\":["),
            valid.Replace("\"sequenceWindow\":2", "\"sequenceWindow\":2,\"sequenceWindow\":2")
        })
            Assert.Throws<InvalidDataException>(() => SequenceContract.Read(Encoding.UTF8.GetBytes(invalid)));
        var duplicate = Policy(k: 2, baselines:
            [new("synthetic", "S1", "a", 10), new("synthetic", "S1", "a", 10)]);
        Assert.Throws<InvalidDataException>(() => new SequenceEngine(duplicate));
        Assert.Throws<InvalidDataException>(() => SequenceContract.Read(new byte[65_537]));
        Assert.Throws<InvalidDataException>(() => Contract.ReadManifest(Encoding.UTF8.GetBytes(valid)));
    }

    [Fact]
    public void MalformedAndInvalidEnvelopeRecordsDoNotConsumeOrHideUnknownAssessment()
    {
        using var temp = new Temporary();
        var valid10 = JsonSerializer.Serialize(Row(10), Contract.Json);
        var valid11 = JsonSerializer.Serialize(Row(11), Contract.Json);
        var invalid11 = valid11.Replace("\"volume\":0", "\"volume\":-1");
        var bytes = Encoding.UTF8.GetBytes("{\r\n" + valid10 + "\n" + invalid11 + "\n" + valid11);
        using var source = new MemoryStream(bytes);
        var summary = SequenceValidator.Run(Policy(bytes), "policy", source, temp.PathFor("out"));
        Assert.Equal(4, summary.Records);
        Assert.Equal(2, summary.Accepted);
        Assert.Equal(2, summary.UnattributedRecords);
        Assert.Equal("UNKNOWN_UNATTRIBUTABLE_RECORDS", summary.Assessment);
        Assert.Equal(11, Assert.Single(summary.StreamStates).LastConsumed);
        Assert.False(summary.Reasons.ContainsKey("SEQUENCE_GAP"));
        var lines = File.ReadAllLines(temp.PathFor("out/accepted.jsonl"));
        using var first = JsonDocument.Parse(lines[0]);
        Assert.Equal(3, first.RootElement.GetProperty("offset").GetInt64());
        Assert.Equal(2, first.RootElement.GetProperty("line").GetInt64());
        Assert.Equal(Contract.Hash(Encoding.UTF8.GetBytes(valid10)), first.RootElement.GetProperty("rawSha256").GetString());
    }

    [Fact]
    public void OversizedRecordRemainsUnattributedAndDoesNotConsumeTheDeclaredBaseline()
    {
        using var temp = new Temporary();
        var bytes = Encoding.UTF8.GetBytes(new string('x', 4097) + "\n" + JsonSerializer.Serialize(Row(10), Contract.Json));
        using var source = new MemoryStream(bytes);
        var summary = SequenceValidator.Run(Policy(bytes), "policy", source, temp.PathFor("out"));
        Assert.Equal(1, summary.UnattributedRecords);
        Assert.Equal(1, summary.Accepted);
        Assert.Equal(10, Assert.Single(summary.StreamStates).LastConsumed);
        Assert.Equal("UNKNOWN_UNATTRIBUTABLE_RECORDS", summary.Assessment);
        Assert.Equal(1, summary.Reasons["RECORD_TOO_LONG"]);
    }

    [Fact]
    public void DemoHasIndependentExpectedReasonsAndCanonicalCrossCultureOutputs()
    {
        using var temp = new Temporary();
        SequenceSynthetic.Generate(temp.PathFor("data"));
        var original = CultureInfo.CurrentCulture;
        try
        {
            foreach (var culture in new[] { "en-US", "fa-IR" })
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
                var report = SequenceValidator.Run(temp.PathFor("data/manifest.json"),
                    temp.PathFor("data/input.jsonl"), temp.PathFor(culture));
                Assert.Equal(14, report.Records);
                Assert.Equal(4, report.Accepted);
                Assert.Equal(10, report.Quarantined);
                Assert.Equal(2, report.UnattributedRecords);
                Assert.Equal(3, report.UncomparedRecords);
                Assert.Equal(2, report.StreamStates.Count(s => s.BlockedReason is not null));
                var expected = new SortedDictionary<string, long>(StringComparer.Ordinal)
                {
                    ["CONFLICT_IN_WINDOW"] = 1, ["DUPLICATE_IN_WINDOW"] = 2,
                    ["JSON_INVALID"] = 1, ["OHLC_INCONSISTENT"] = 1, ["SEQUENCE_GAP"] = 1,
                    ["STALE_OUTSIDE_WINDOW"] = 1, ["STREAM_BLOCKED"] = 2,
                    ["UNDECLARED_STREAM"] = 1, ["VALID_RECORD"] = 4
                };
                Assert.Equal(expected, report.Reasons);
            }
            foreach (var name in new[] { "accepted.jsonl", "quarantine.jsonl", "summary.json" })
                Assert.Equal(File.ReadAllBytes(temp.PathFor("en-US/" + name)), File.ReadAllBytes(temp.PathFor("fa-IR/" + name)));
            using var repeatedBad = JsonDocument.Parse(File.ReadAllLines(temp.PathFor("en-US/quarantine.jsonl"))[2]);
            Assert.Equal("DUPLICATE_IN_WINDOW", repeatedBad.RootElement.GetProperty("reason").GetString());
            Assert.Equal("OHLC_INCONSISTENT", repeatedBad.RootElement.GetProperty("dataReason").GetString());
        }
        finally { CultureInfo.CurrentCulture = original; }
    }

    [Fact]
    public void A1PublishedSyntheticBytesAndOutputsRemainExactlyPinned()
    {
        using var temp = new Temporary();
        Synthetic.Generate(temp.PathFor("data"), 34);
        Validator.Run(temp.PathFor("data/manifest.json"), temp.PathFor("data/input.jsonl"), temp.PathFor("out"));
        var expected = new Dictionary<string, string>
        {
            ["data/input.jsonl"] = "3fbacbe00ade881134d900a6b35446da3ad8207143da76c9ab946e8c9b6baaa4",
            ["data/manifest.json"] = "fb1ec4c7185e134ba559d74cf28e7b966ccc893d61f6a0ac2121cfe16632f39e",
            ["out/accepted.jsonl"] = "8edc352149f619e035625c939d182cfa82c520cb9402200bc00b2039ab63ea89",
            ["out/quarantine.jsonl"] = "a8dfb539278a8e74cfe102cb7a2788d130a576795b1e2ff41bd7ee1faa31428f",
            ["out/summary.json"] = "86ba9ca262dc2179d972d3d28a118123b673067879a52b64e877dc3a83742c60"
        };
        foreach (var item in expected)
            Assert.Equal(item.Value, Contract.Hash(File.ReadAllBytes(temp.PathFor(item.Key))));
    }

    [Fact]
    public async Task OptInCliExercisesSequenceResultsAndRejectsVersionMismatch()
    {
        using var temp = new Temporary();
        SequenceSynthetic.Generate(temp.PathFor("data"));
        var result = await RunCli("validate-sequence", temp.PathFor("data/manifest.json"),
            temp.PathFor("data/input.jsonl"), temp.PathFor("out"));
        Assert.Equal(2, result.Exit);
        Assert.Equal("", result.Stderr);
        Assert.Contains("records=14 quarantined=10 unattributed=2", result.Stdout);
        Assert.True(File.Exists(temp.PathFor("out/summary.json")));
        var oldMode = await RunCli("validate", temp.PathFor("data/manifest.json"),
            temp.PathFor("data/input.jsonl"), temp.PathFor("old-mode"));
        Assert.Equal(1, oldMode.Exit);
        Assert.False(Directory.Exists(temp.PathFor("old-mode")));
        Synthetic.Generate(temp.PathFor("v1"), 1);
        var newMode = await RunCli("validate-sequence", temp.PathFor("v1/manifest.json"),
            temp.PathFor("v1/input.jsonl"), temp.PathFor("new-mode"));
        Assert.Equal(1, newMode.Exit);
        Assert.False(Directory.Exists(temp.PathFor("new-mode")));
    }

    [Fact]
    public void SequenceRunnerStillRefusesChangedSourceAndNeverPublishesSummary()
    {
        using var temp = new Temporary();
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(Row(10), Contract.Json));
        using var source = new MutatingSource(bytes);
        Assert.Throws<InvalidDataException>(() => SequenceValidator.Run(Policy(bytes), "policy", source, temp.PathFor("out")));
        Assert.False(File.Exists(temp.PathFor("out/summary.json")));
    }

    private static async Task<(int Exit, string Stdout, string Stderr)> RunCli(params string[] args)
    {
        var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
        { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (var arg in new[] { "exec", "--runtimeconfig",
            Path.ChangeExtension(typeof(SequenceTests).Assembly.Location, ".runtimeconfig.json"),
            typeof(SequenceValidator).Assembly.Location }.Concat(args)) start.ArgumentList.Add(arg);
        using var child = Process.Start(start)!;
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var stdout = child.StandardOutput.ReadToEndAsync(deadline.Token);
        var stderr = child.StandardError.ReadToEndAsync(deadline.Token);
        try { await Task.WhenAll(stdout, stderr, child.WaitForExitAsync(deadline.Token)); }
        catch (OperationCanceledException)
        {
            if (!child.HasExited) child.Kill(entireProcessTree: true);
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await child.WaitForExitAsync(cleanup.Token);
            throw new TimeoutException("Owned A2 CLI child exceeded deadline.");
        }
        return (child.ExitCode, await stdout, await stderr);
    }

    private sealed class Temporary : IDisposable
    {
        private readonly string root = Path.Combine(Path.GetTempPath(), "integrity-a2-" + Guid.NewGuid().ToString("N"));
        public Temporary() => Directory.CreateDirectory(root);
        public string PathFor(string name) => Path.Combine(root, name.Replace('/', Path.DirectorySeparatorChar));
        public void Dispose() => Directory.Delete(root, true);
    }

    private sealed class MutatingSource(byte[] bytes) : Stream
    {
        private readonly MemoryStream inner = new(bytes.ToArray());
        private int passes;
        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));
        public override int Read(Span<byte> buffer)
        {
            if (inner.Position == 0 && inner.Length > 0 && ++passes == 2)
            { inner.WriteByte((byte)'['); inner.Position = 0; }
            return inner.Read(buffer);
        }
        public override bool CanRead => true;
        public override bool CanSeek => true;
        public override bool CanWrite => false;
        public override long Length => inner.Length;
        public override long Position { get => inner.Position; set => inner.Position = value; }
        public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);
        public override void Flush() { }
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing) { if (disposing) inner.Dispose(); base.Dispose(disposing); }
    }
}
