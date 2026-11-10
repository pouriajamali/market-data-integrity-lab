using System.Globalization;
using System.Text;
using System.Text.Json;
using MarketDataIntegrity;
using Xunit;

namespace Integrity.Tests;

// Console redirection and culture changes must not overlap other test collections.
[CollectionDefinition("CSV CLI serial", DisableParallelization = true)]
public sealed class CsvCliCollection { }

[Collection("CSV CLI serial")]
public sealed class CsvCliTests
{
    private const string Syntax = "usage: Integrity validate-csv <input.csv> <new-output> <price-decimals:0..8> <volume-decimals:0..8>";
    private const string Count = "usage:CSV_ARGUMENT_COUNT; expected 4 arguments after validate-csv";
    private const string Price = "usage:CSV_PRICE_SCALE_INVALID; price-decimals must be an unsigned integer in 0..8";
    private const string Volume = "usage:CSV_VOLUME_SCALE_INVALID; volume-decimals must be an unsigned integer in 0..8";
    private const string Global = "usage: Integrity validate-csv <input.csv> <new-output> <price-decimals:0..8> <volume-decimals:0..8> | csv-demo <new-directory> | --version | demo <new-directory> | generate <new-directory> <count:1..1000000> | validate <manifest.json> <input.jsonl> <new-output-directory> | generate-sequence <new-directory> | validate-sequence <v2-manifest.json> <input.jsonl> <new-output-directory> | start-resumable|resume <v2-manifest.json> <input.jsonl> <workdir> <interval:1..10000>";
    private const string Header = "timestamp,open,high,low,close,volume\n";
    private const string Good = "2026-01-01T00:00:00Z,-2.00,-1.00,-3.00,-2.50,0\n";

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(5)]
    public void CountWinsBeforeInvalidScalesAndDoesNotTouchTarget(int count)
    {
        using var t = new Temp();
        var args = new[] { "validate-csv" }.Concat(new[] { t.At("missing input.csv"), t.At("new output"), "bad-price", "bad-volume", "extra" }.Take(count)).ToArray();
        Usage(Run(args), Count);
        Assert.False(Directory.Exists(t.At("new output")));
    }

    [Theory]
    [InlineData("9")]
    [InlineData("-1")]
    [InlineData("1.0")]
    [InlineData("x")]
    [InlineData("")]
    [InlineData("+2")]
    [InlineData(" 2")]
    [InlineData("2 ")]
    [InlineData("99999999999999999999999999999")]
    [InlineData("\u06f2")]
    public void InvalidScaleIdentifiesFieldBeforeFileErrorsWithoutEcho(string token)
    {
        using var t = new Temp();
        var input = t.At("secret source missing.csv");
        var output = t.At("new output");
        Usage(Run("validate-csv", input, output, token, "0"), Price);
        Usage(Run("validate-csv", input, output, "2", token), Volume);
        Usage(Run("validate-csv", input, output, token, token), Price);
        Assert.False(Directory.Exists(output));
        Assert.False(File.Exists(output));
    }

    [Theory]
    [InlineData("en-US")]
    [InlineData("fa-IR")]
    public void CulturesKeepFixedDiagnosticsSentinelAndLeadingZeroGrammar(string culture)
    {
        using var t = new Temp();
        var oldCulture = CultureInfo.CurrentCulture;
        var oldUi = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
            var existing = t.At("existing output");
            Directory.CreateDirectory(existing);
            var sentinel = Path.Combine(existing, "keep.bin");
            byte[] bytes = [0, 255, 10, 13];
            File.WriteAllBytes(sentinel, bytes);
            Usage(Run("validate-csv", t.At("missing.csv"), existing, "x", "0"), Price);
            Usage(Run("validate-csv", t.At("missing.csv"), existing, "2", "x"), Volume);
            Assert.Equal(bytes, File.ReadAllBytes(sentinel));
            Assert.Single(Directory.EnumerateFileSystemEntries(existing));
            var csv = t.At("positive.csv");
            File.WriteAllText(csv, Header + Good, new UTF8Encoding(false));
            var result = Run("validate-csv", csv, t.At("valid result"), "02", "00");
            Assert.Equal(0, result.Exit);
            Assert.Equal("", result.Stderr);
            using var row = JsonDocument.Parse(File.ReadAllText(t.At("valid result/accepted.jsonl")));
            Assert.Equal("-250", row.RootElement.GetProperty("record").GetProperty("closeScaled").GetString());
        }
        finally { CultureInfo.CurrentCulture = oldCulture; CultureInfo.CurrentUICulture = oldUi; }
    }

    [Theory]
    [InlineData("0", "8", "-2", "0")]
    [InlineData("8", "0", "-200000000", "0")]
    public void BoundaryScalesReachExactValidation(string price, string volume, string close, string lots)
    {
        using var t = new Temp();
        File.WriteAllText(t.At("input.csv"), Header + "2026-01-01T00:00:00Z,-2,-1,-3,-2,0\n", new UTF8Encoding(false));
        var result = Run("validate-csv", t.At("input.csv"), t.At("out"), price, volume);
        Assert.Equal(0, result.Exit);
        Assert.Equal("", result.Stderr);
        using var row = JsonDocument.Parse(File.ReadAllText(t.At("out/accepted.jsonl")));
        var record = row.RootElement.GetProperty("record");
        Assert.Equal(close, record.GetProperty("closeScaled").GetString());
        Assert.Equal(lots, record.GetProperty("volumeScaled").GetString());
    }

    [Fact]
    public void CleanPrecisionAndFatalFramingHaveIndependentCompletionOracles()
    {
        using var t = new Temp();
        File.WriteAllText(t.At("clean.csv"), Header + Good, new UTF8Encoding(false));
        var clean = Run("validate-csv", t.At("clean.csv"), t.At("clean"), "2", "0");
        Assert.Equal(0, clean.Exit);
        Assert.Equal("completed rows=1 accepted=1 quarantined=0; LOCAL_ROW_CHECKS_ONLY; market provenance UNVERIFIED" + Environment.NewLine, clean.Stdout);
        Assert.Equal("", clean.Stderr);
        using var accepted = JsonDocument.Parse(File.ReadAllText(t.At("clean/accepted.jsonl")));
        Assert.Equal("-250", accepted.RootElement.GetProperty("record").GetProperty("closeScaled").GetString());

        File.WriteAllText(t.At("precision.csv"), Header + "2026-01-01T00:01:00Z,1.230,2,0,1,1\n", new UTF8Encoding(false));
        var precision = Run("validate-csv", t.At("precision.csv"), t.At("precision"), "2", "0");
        Assert.Equal(2, precision.Exit);
        Assert.Equal("", precision.Stderr);
        using var rejected = JsonDocument.Parse(File.ReadAllText(t.At("precision/quarantine.jsonl")));
        Assert.Equal("NUMBER_PRECISION_EXCEEDED", rejected.RootElement.GetProperty("reason").GetString());
        Assert.Equal("open", rejected.RootElement.GetProperty("field").GetString());
        Assert.Equal(JsonValueKind.Null, rejected.RootElement.GetProperty("record").ValueKind);
        using var summary = JsonDocument.Parse(File.ReadAllText(t.At("precision/summary.json")));
        Assert.Equal("completed", summary.RootElement.GetProperty("status").GetString());
        Assert.Equal(1, summary.RootElement.GetProperty("quarantined").GetInt64());

        File.WriteAllText(t.At("fatal.csv"), Header + Good + "2026-01-01T00:02:00Z,\"1\n" + Good, new UTF8Encoding(false));
        var fatal = Run("validate-csv", t.At("fatal.csv"), t.At("fatal"), "2", "0");
        Assert.Equal(1, fatal.Exit);
        Assert.Equal("", fatal.Stdout);
        Assert.Equal("fatal:CSV_UNCLOSED_QUOTE line=3; no completed result may be inferred from partial files" + Environment.NewLine, fatal.Stderr);
        Assert.False(File.Exists(t.At("fatal/summary.json")));
        Assert.Single(File.ReadAllLines(t.At("fatal/accepted.jsonl")));
    }

    [Theory]
    [InlineData("")]
    [InlineData("Validate-csv")]
    [InlineData("validate-csv-extra")]
    [InlineData("validate-csv ")]
    [InlineData("--help")]
    public void ZeroAndLookalikeCommandsKeepExactGlobalUsage(string command)
    {
        var result = command.Length == 0 ? Run() : Run(command);
        Assert.Equal(1, result.Exit);
        Assert.Equal("", result.Stdout);
        Assert.Equal(Global + Environment.NewLine, result.Stderr);
    }

    private static void Usage(Result result, string first)
    {
        Assert.Equal(1, result.Exit);
        Assert.Equal("", result.Stdout);
        Assert.Equal(first + Environment.NewLine + Syntax + Environment.NewLine, result.Stderr);
    }

    // Invoke the real synchronous entry point, not a duplicate argument parser.
    // The disabled-parallel collection makes Console/culture ownership explicit.
    private static Result Run(params string[] args)
    {
        var originalOut = Console.Out;
        var originalError = Console.Error;
        using var stdout = new StringWriter(CultureInfo.InvariantCulture);
        using var stderr = new StringWriter(CultureInfo.InvariantCulture);
        try
        {
            Console.SetOut(stdout);
            Console.SetError(stderr);
            var exit = Assert.IsType<int>(typeof(Validator).Assembly.EntryPoint!.Invoke(null, [args]));
            return new(exit, stdout.ToString(), stderr.ToString());
        }
        finally { Console.SetOut(originalOut); Console.SetError(originalError); }
    }

    private sealed record Result(int Exit, string Stdout, string Stderr);
    private sealed class Temp : IDisposable
    {
        private readonly string root = Path.Combine(Path.GetTempPath(), "csv cli " + Guid.NewGuid().ToString("N"));
        public Temp() => Directory.CreateDirectory(root);
        public string At(string name) => Path.Combine(root, name);
        public void Dispose() => Directory.Delete(root, true);
    }
}
