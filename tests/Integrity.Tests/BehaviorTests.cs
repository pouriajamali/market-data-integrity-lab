using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using MarketDataIntegrity;
using Xunit;


namespace Integrity.Tests;

public sealed class BehaviorTests
{
    private static readonly byte[] Valid = Encoding.UTF8.GetBytes(Row());
    private static string Row(string instrument = "SYNTH1") =>
        "{\"source\":\"synthetic\",\"instrument\":\"" + instrument +
        "\",\"epoch\":\"demo\",\"sequence\":1,\"timestamp\":\"2026-01-01T00:00:00Z\"," +
        "\"open\":100,\"high\":102,\"low\":98,\"close\":101,\"volume\":0}";

    [Fact]
    public void PhysicalProvenanceIncludesCrAndEofRecordWithoutInventingTrailingLine()
    {
        var first = Encoding.UTF8.GetBytes(Row() + "\r");
        var second = Encoding.UTF8.GetBytes("{\"x\":\"é\"}");
        var input = first.Concat(new byte[] { 10 }).Concat(second).Concat(new byte[] { 10 })
            .Concat(Valid).ToArray();
        var observed = new List<(long Line, long Offset, long Length, string Hash)>();
        using var source = new ChunkStream(input, chunkSize: 1);
        BoundedReader.Visit(source, 4096, r => observed.Add((r.Line, r.Offset, r.ByteLength, r.Sha256)));
        Assert.Equal(new[]
        {
            (1L, 0L, (long)first.Length, Contract.Hash(first)),
            (2L, first.Length + 1L, (long)second.Length, Contract.Hash(second)),
            (3L, first.Length + second.Length + 2L, (long)Valid.Length, Contract.Hash(Valid))
        }, observed);
        Assert.Equal("VALID_RECORD", Contract.Parse(first).Reason);
        using var finalLf = new MemoryStream(Valid.Concat(new byte[] { 10 }).ToArray());
        var lines = 0;
        BoundedReader.Visit(finalLf, 4096, _ => lines++);
        Assert.Equal(1, lines);
    }

    [Theory]
    [InlineData("UTF8_BOM")]
    [InlineData("UTF8_INVALID")]
    [InlineData("EMPTY_RECORD")]
    [InlineData("JSON_INVALID")]
    public void ByteAndJsonFailuresAreClassified(string expected)
    {
        byte[] bytes = expected switch
        {
            "UTF8_BOM" => new byte[] { 0xef, 0xbb, 0xbf }.Concat(Valid).ToArray(),
            "UTF8_INVALID" => [0xc3, 0x28],
            "EMPTY_RECORD" => [],
            _ => Encoding.UTF8.GetBytes("{")
        };
        Assert.Equal(expected, Contract.Parse(bytes).Reason);
    }

    [Theory]
    [InlineData("\"volume\":0", "\"volume\":0,\"volume\":1", "SCHEMA_INVALID")]
    [InlineData("\"volume\":0", "\"extra\":1,\"volume\":0", "SCHEMA_INVALID")]
    [InlineData("\"volume\":0", "\"volume\":0.5", "SCHEMA_INVALID")]
    [InlineData("\"sequence\":1", "\"sequence\":0", "VALUE_OUT_OF_RANGE")]
    [InlineData("\"high\":102", "\"high\":1000000000001", "VALUE_OUT_OF_RANGE")]
    [InlineData("\"volume\":0", "\"volume\":-1", "VALUE_OUT_OF_RANGE")]
    [InlineData("\"open\":100", "\"open\":103", "OHLC_INCONSISTENT")]
    [InlineData("\"close\":101", "\"close\":97", "OHLC_INCONSISTENT")]
    [InlineData("2026-01-01T00:00:00Z", "2026-02-30T00:00:00Z", "SCHEMA_INVALID")]
    public void SchemaAndOhlcHaveExplicitIndependentBoundaries(string old, string replacement, string reason)
    {
        Assert.Equal(reason, Contract.Parse(Encoding.UTF8.GetBytes(Row().Replace(old, replacement))).Reason);
    }

    [Theory]
    [InlineData("source")]
    [InlineData("timestamp")]
    [InlineData("property")]
    public void UnpairedEscapedSurrogateIsQuarantinedAndNextRecordContinues(string location)
    {
        var input = location switch
        {
            "source" => Row().Replace("\"synthetic\"", "\"\\uD800\""),
            "timestamp" => Row().Replace("\"2026-01-01T00:00:00Z\"", "\"\\uD800\""),
            _ => Row().Replace("\"source\"", "\"\\uD800\"")
        };
        using var temp = new Temporary();
        var bytes = Encoding.UTF8.GetBytes(input + "\n" + Row());
        using var source = new MemoryStream(bytes);
        var summary = Validator.Run(ManifestFor(bytes), "policy", source, temp.PathFor("out"));
        Assert.Equal(2, summary.Records);
        Assert.Equal(1, summary.Accepted);
        Assert.Equal(1, summary.Quarantined);
        Assert.Equal(1, summary.Reasons["JSON_INVALID"]);
        Assert.True(File.Exists(temp.PathFor("out/summary.json")));
    }

    [Fact]
    public void InclusiveOhlcAndNumericUpperBoundsAreAccepted()
    {
        var row = Row().Replace("\"open\":100", "\"open\":98")
            .Replace("\"close\":101", "\"close\":102")
            .Replace("\"volume\":0", "\"volume\":1000000000000");
        Assert.Equal("VALID_RECORD", Contract.Parse(Encoding.UTF8.GetBytes(row)).Reason);
    }

    [Fact]
    public void OversizedRecordIsDrainedAndHashedWithoutRetainingIt()
    {
        var giant = Enumerable.Repeat((byte)'x', 1_048_577).ToArray();
        using var source = new ChunkStream(giant.Concat(new byte[] { 10 }).Concat(Valid).ToArray());
        var visits = 0;
        BoundedReader.Visit(source, 256, raw =>
        {
            visits++;
            if (visits == 1)
            {
                Assert.True(raw.TooLong);
                Assert.Equal(giant.LongLength, raw.ByteLength);
                Assert.Equal(256, raw.Data.Length);
                Assert.Equal(Contract.Hash(giant), raw.Sha256);
            }
            else
            {
                Assert.False(raw.TooLong);
                Assert.Equal(giant.LongLength + 1, raw.Offset);
                Assert.Equal("VALID_RECORD", Contract.Parse(raw.Data).Reason);
            }
        });
        Assert.Equal(2, visits);
        Assert.True(source.LargestReadRequest <= BoundedReader.ReadBufferBytes);
    }

    [Fact]
    public void ByteLimitIsInclusiveAndCountsCr()
    {
        using var source = new MemoryStream(Enumerable.Repeat((byte)' ', 64)
            .Concat(new byte[] { 10 }).Concat(Enumerable.Repeat((byte)' ', 64))
            .Concat(new byte[] { 13, 10 }).ToArray());
        var states = new List<(long, bool)>();
        BoundedReader.Visit(source, 64, row => states.Add((row.ByteLength, row.TooLong)));
        Assert.Equal(new[] { (64L, false), (65L, true) }, states);
    }

    [Fact]
    public void StreamCapacityIsFatalAndPublishesNoSummary()
    {
        using var temp = new Temporary();
        var bytes = Encoding.UTF8.GetBytes(Row() + "\n" + Row("SYNTH2"));
        using var source = new MemoryStream(bytes);
        Assert.Throws<InvalidDataException>(() => Validator.Run(ManifestFor(bytes, k: 1), "policy",
            source, temp.PathFor("result")));
        Assert.False(File.Exists(temp.PathFor("result/summary.json")));
    }

    [Fact]
    public void SourceIdentityMismatchCreatesNoOutput()
    {
        using var temp = new Temporary();
        using var source = new MemoryStream(Valid);
        var wrong = ManifestFor(Valid) with { InputSha256 = new string('0', 64) };
        Assert.Throws<InvalidDataException>(() => Validator.Run(wrong, "policy", source, temp.PathFor("result")));
        Assert.False(Directory.Exists(temp.PathFor("result")));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MutationOrReadFailureCannotPublishCompletion(bool readFailure)
    {
        using var temp = new Temporary();
        var bytes = Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat(Row() + "\n", 100)));
        using var source = new ChunkStream(bytes, failOnValidation: readFailure, mutateOnValidation: !readFailure);
        if (readFailure) Assert.Throws<IOException>(() => Validator.Run(ManifestFor(bytes), "policy", source, temp.PathFor("result")));
        else Assert.Throws<InvalidDataException>(() => Validator.Run(ManifestFor(bytes), "policy", source, temp.PathFor("result")));
        Assert.False(File.Exists(temp.PathFor("result/summary.json")));
    }

    [Fact]
    public void CanonicalOutputsAreIndependentOfRunDirectoryAndCulture()
    {
        using var temp = new Temporary();
        var data = Encoding.UTF8.GetBytes(Row() + "\r\n{\n" +
            Row().Replace("\"open\":100", "\"open\":103"));
        var previous = CultureInfo.CurrentCulture;
        try
        {
            foreach (var culture in new[] { "en-US", "fa-IR" })
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
                using var source = new MemoryStream(data);
                var summary = Validator.Run(ManifestFor(data), "fixed-manifest-hash", source, temp.PathFor(culture));
                Assert.Equal(3, summary.Records);
                Assert.Equal(1, summary.Accepted);
                Assert.Equal(2, summary.Quarantined);
            }
            foreach (var file in new[] { "accepted.jsonl", "quarantine.jsonl", "summary.json" })
                Assert.Equal(File.ReadAllBytes(temp.PathFor("en-US/" + file)),
                    File.ReadAllBytes(temp.PathFor("fa-IR/" + file)));
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    [Fact]
    public void ManifestIsStrictBoundedAndSyntheticOnly()
    {
        var valid = JsonSerializer.Serialize(ManifestFor(Valid), Contract.Json);
        foreach (var invalid in new[]
        {
            valid.Replace("\"synthetic\":true", "\"synthetic\":false"),
            valid.Replace("\"synthetic-test\"", "\"\\uD800\""),
            valid.Replace("\"maxStreams\":2", "\"maxStreams\":0"),
            valid.Replace("\"maxStreams\":2", "\"maxStreams\":2,\"maxStreams\":2"),
            valid.Replace("\"maxRecordBytes\":4096", "\"maxRecordBytes\":63")
        })
            Assert.Throws<InvalidDataException>(() => Contract.ReadManifest(Encoding.UTF8.GetBytes(invalid)));
        using var temp = new Temporary();
        File.WriteAllBytes(temp.PathFor("large.json"), new byte[65_537]);
        Assert.Throws<InvalidDataException>(() => Contract.ReadManifestBytes(temp.PathFor("large.json")));
    }

    [Fact]
    public async Task FreshSyntheticGeneratorAndCliReturnDegradedRatherThanSuccess()
    {
        using var temp = new Temporary();
        Synthetic.Generate(temp.PathFor("a"), 34);
        Synthetic.Generate(temp.PathFor("b"), 34);
        foreach (var file in new[] { "manifest.json", "input.jsonl" })
            Assert.Equal(File.ReadAllBytes(temp.PathFor("a/" + file)), File.ReadAllBytes(temp.PathFor("b/" + file)));
        var (exit, stdout, stderr) = await RunCli("validate",
            temp.PathFor("a/manifest.json"), temp.PathFor("a/input.jsonl"), temp.PathFor("out"));
        Assert.Equal("", stderr);
        Assert.Equal(2, exit);
        Assert.Contains("records=34 quarantined=2", stdout);
        using var summary = JsonDocument.Parse(File.ReadAllBytes(temp.PathFor("out/summary.json")));
        Assert.Equal(32, summary.RootElement.GetProperty("accepted").GetInt64());
        Assert.Equal(2, summary.RootElement.GetProperty("quarantined").GetInt64());

        var badManifest = File.ReadAllText(temp.PathFor("a/manifest.json"))
            .Replace("\"synthetic-a1\"", "\"\\uD800\"");
        File.WriteAllText(temp.PathFor("bad-manifest.json"), badManifest, new UTF8Encoding(false));
        var bad = await RunCli("validate", temp.PathFor("bad-manifest.json"),
            temp.PathFor("a/input.jsonl"), temp.PathFor("bad-output"));
        Assert.Equal(1, bad.Exit);
        Assert.Equal("", bad.Stdout);
        Assert.Equal("fatal:InvalidDataException; no completed result may be inferred from partial files",
            bad.Stderr.Trim());
        Assert.False(Directory.Exists(temp.PathFor("bad-output")));
    }

    private static async Task<(int Exit, string Stdout, string Stderr)> RunCli(params string[] arguments)
    {
        var dll = typeof(Validator).Assembly.Location;
        var runtime = Path.ChangeExtension(typeof(BehaviorTests).Assembly.Location, ".runtimeconfig.json");
        var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
        {
            RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false
        };
        foreach (var arg in new[] { "exec", "--runtimeconfig", runtime, dll }.Concat(arguments))
            start.ArgumentList.Add(arg);
        using var process = Process.Start(start)!;
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var stdoutTask = process.StandardOutput.ReadToEndAsync(deadline.Token);
        var stderrTask = process.StandardError.ReadToEndAsync(deadline.Token);
        try
        {
            await Task.WhenAll(stdoutTask, stderrTask, process.WaitForExitAsync(deadline.Token));
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            using var cleanupDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await process.WaitForExitAsync(cleanupDeadline.Token);
            throw new TimeoutException("Owned CLI child exceeded the 30-second deadline.");
        }
        return (process.ExitCode, await stdoutTask, await stderrTask);
    }

    private static Manifest ManifestFor(byte[] bytes, int k = 2) =>
        new(1, "synthetic-test", true, 4096, k, Contract.Hash(bytes));

    private sealed class Temporary : IDisposable
    {
        private readonly string root = Path.Combine(Path.GetTempPath(), "integrity-a1-" + Guid.NewGuid().ToString("N"));
        public Temporary() => Directory.CreateDirectory(root);
        public string PathFor(string name) => Path.Combine(root, name.Replace('/', Path.DirectorySeparatorChar));
        public void Dispose() => Directory.Delete(root, true);
    }

    private sealed class ChunkStream(byte[] bytes, int chunkSize = 8192,
        bool failOnValidation = false, bool mutateOnValidation = false) : Stream
    {
        private readonly MemoryStream inner = new(bytes.ToArray());
        private int passes;
        public int LargestReadRequest { get; private set; }
        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));
        public override int Read(Span<byte> buffer)
        {
            LargestReadRequest = Math.Max(LargestReadRequest, buffer.Length);
            if (inner.Position == 0 && inner.Length > 0)
            {
                passes++;
                if (passes == 2 && mutateOnValidation)
                {
                    inner.WriteByte((byte)'[');
                    inner.Position = 0;
                }
            }
            if (passes == 2 && failOnValidation && inner.Position >= 8192)
                throw new IOException("injected read failure");
            return inner.Read(buffer[..Math.Min(buffer.Length, chunkSize)]);
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
