using System.Globalization;
using System.Text;
using System.Text.Json;
using MarketDataIntegrity;
using Xunit;

namespace Integrity.Tests;

public sealed class CsvTests
{
    private const string Header="timestamp,open,high,low,close,volume\n";
    private const string Good="2026-01-01T00:00:00Z,1.20,2.00,0,1.50,3\n";
    [Theory]
    [InlineData("1",2,"100",null)]
    [InlineData("1.2",2,"120",null)]
    [InlineData("-0.00",2,"0",null)]
    [InlineData("-2.50",2,"-250",null)]
    [InlineData("0001.20",2,"120",null)]
    [InlineData("9223372036854775807",0,"9223372036854775807",null)]
    [InlineData("-9223372036854775808",0,"-9223372036854775808",null)]
    [InlineData("92233720368547758.07",2,"9223372036854775807",null)]
    [InlineData("-92233720368547758.08",2,"-9223372036854775808",null)]
    [InlineData("92233720368547758.08",2,null,"NUMBER_OUT_OF_RANGE")]
    [InlineData("-92233720368547758.09",2,null,"NUMBER_OUT_OF_RANGE")]
    [InlineData("922337203685477581",2,null,"NUMBER_OUT_OF_RANGE")]
    [InlineData("1.230",2,null,"NUMBER_PRECISION_EXCEEDED")]
    [InlineData("1.0",0,null,"NUMBER_PRECISION_EXCEEDED")]
    [InlineData("0.000000001",8,null,"NUMBER_PRECISION_EXCEEDED")]
    [InlineData("92233720368.54775807",8,"9223372036854775807",null)]
    [InlineData("-92233720368.54775808",8,"-9223372036854775808",null)]
    [InlineData("92233720368.54775808",8,null,"NUMBER_OUT_OF_RANGE")]
    [InlineData("0.00000000",8,"0",null)]
    [InlineData("0.000000000",8,null,"NUMBER_PRECISION_EXCEEDED")]
    [InlineData("1e2",2,null,"NUMBER_FORMAT_INVALID")]
    [InlineData("1,000",2,null,"NUMBER_FORMAT_INVALID")]
    [InlineData("+1",2,null,"NUMBER_FORMAT_INVALID")]
    [InlineData(" 1",2,null,"NUMBER_FORMAT_INVALID")]
    [InlineData("1 ",2,null,"NUMBER_FORMAT_INVALID")]
    [InlineData(".1",2,null,"NUMBER_FORMAT_INVALID")]
    [InlineData("1.",2,null,"NUMBER_FORMAT_INVALID")]
    [InlineData("1..0",2,null,"NUMBER_FORMAT_INVALID")]
    [InlineData("",2,null,"NUMBER_FORMAT_INVALID")]
    public void ExactIntegerConversionNeverRounds(string text,int scale,string? expected,string? reason)
    {
        var actual=CsvContract.Number(text,scale,out var number);
        Assert.Equal(reason,actual);
        if(reason is null)Assert.Equal(expected,number.ToString(CultureInfo.InvariantCulture));
    }

    [Fact]
    public void HandwrittenOracleKeepsSignedPricesAndReasonsWithExactStrings()
    {
        using var t=new Temp();
        File.WriteAllText(t.At("input.csv"),CsvDemo.Example,new UTF8Encoding(false));
        var result=CsvValidator.Run(t.At("input.csv"),t.At("out"),2,0);
        Assert.Equal(6,result.Records);Assert.Equal(2,result.Accepted);Assert.Equal(4,result.Quarantined);
        Assert.Equal("USER_SUPPLIED_UNVERIFIED",result.Policy.SourceKind);
        Assert.Equal("UNVERIFIED",result.MarketProvenance);
        Assert.Equal("NOT_CHECKED",result.Ordering);Assert.Equal("NOT_CHECKED",result.Deduplication);
        Assert.Equal("NOT_CHECKED",result.Completeness);
        Assert.Equal(new Dictionary<string,long>{{"VALID_ROW",2},{"OHLC_INCONSISTENT",1},
            {"NUMBER_PRECISION_EXCEEDED",1},{"NEGATIVE_VOLUME",1},{"NUMBER_FORMAT_INVALID",1}}.OrderBy(x=>x.Key),
            result.Reasons.OrderBy(x=>x.Key));
        var rows=File.ReadAllLines(t.At("out/accepted.jsonl")).Select(x=>JsonDocument.Parse(x)).ToArray();
        try
        {
            Assert.Equal("10010",rows[0].RootElement.GetProperty("record").GetProperty("openScaled").GetString());
            Assert.Equal("-250",rows[1].RootElement.GetProperty("record").GetProperty("closeScaled").GetString());
            Assert.Equal(2,rows[0].RootElement.GetProperty("line").GetInt64());
            Assert.Equal(Header.Length,rows[0].RootElement.GetProperty("offset").GetInt64());
        }
        finally {foreach(var row in rows)row.Dispose();}
        var rejected=File.ReadAllLines(t.At("out/quarantine.jsonl")).Select(x=>JsonDocument.Parse(x)).ToArray();
        try
        {
            Assert.Equal(new[]{"ohlc","open","volume","open"},rejected.Select(r=>r.RootElement.GetProperty("field").GetString()));
        }
        finally {foreach(var row in rejected)row.Dispose();}
    }

    [Fact]
    public void QuotedHeaderBomCrLfEofAndRawPhysicalHashesAreExact()
    {
        using var t=new Temp();
        var header="\"timestamp\",\"open\",\"high\",\"low\",\"close\",\"volume\"\r\n";
        var row="\"2026-01-01T00:00:00Z\",\"1.20\",\"2\",\"0\",\"1.50\",\"3\"";
        var data=new byte[]{239,187,191}.Concat(Encoding.UTF8.GetBytes(header+row)).ToArray();
        File.WriteAllBytes(t.At("input"),data);
        var r=CsvValidator.Run(t.At("input"),t.At("out"),2,0);
        Assert.Equal(1,r.Records);Assert.Equal(1,r.Accepted);
        var rawHeader=new byte[]{239,187,191}.Concat(Encoding.UTF8.GetBytes(header[..^1])).ToArray();
        Assert.Equal(Contract.Hash(rawHeader),r.HeaderSha256);
        using var decision=JsonDocument.Parse(File.ReadAllText(t.At("out/accepted.jsonl")));
        Assert.Equal(3+Encoding.UTF8.GetByteCount(header),decision.RootElement.GetProperty("offset").GetInt64());
        Assert.Equal(Encoding.UTF8.GetByteCount(row),decision.RootElement.GetProperty("byteLength").GetInt64());
        Assert.Equal(Contract.Hash(Encoding.UTF8.GetBytes(row)),decision.RootElement.GetProperty("rawSha256").GetString());
    }

    [Theory]
    [InlineData("2026-02-29T00:00:00Z,1,2,0,1,1\n","TIMESTAMP_INVALID")]
    [InlineData("2026-01-01T00:00:00+00:00,1,2,0,1,1\n","TIMESTAMP_INVALID")]
    [InlineData("2026-01-01T00:00:00Z,1,2,0,1\n","CSV_COLUMN_COUNT_INVALID")]
    [InlineData("2026-01-01T00:00:00Z,1,2,0,1,1,\n","CSV_COLUMN_COUNT_INVALID")]
    [InlineData("2026-01-01T00:00:00Z,\"1\"\"2\",2,0,1,1\n","NUMBER_FORMAT_INVALID")]
    [InlineData("\n","EMPTY_ROW")]
    [InlineData("\r\n","EMPTY_ROW")]
    public void WellFramedBadRowsQuarantineWithoutInventingExtraTrailingRows(string row,string reason)
    {
        using var t=new Temp();File.WriteAllText(t.At("in"),Header+row,new UTF8Encoding(false));
        var r=CsvValidator.Run(t.At("in"),t.At("out"),2,0);
        Assert.Equal(1,r.Records);Assert.Equal(1,r.Quarantined);Assert.Equal(1,r.Reasons[reason]);
    }

    [Theory]
    [InlineData("","CSV_NO_DATA")]
    [InlineData("timestamp,open,high,low,close,volume\n","CSV_NO_DATA")]
    [InlineData("timestamp,open,high,low,close,volume","CSV_NO_DATA")]
    [InlineData("Timestamp,open,high,low,close,volume\n"+Good,"CSV_HEADER_INVALID")]
    [InlineData("timestamp,open,high,low,close,close\n"+Good,"CSV_HEADER_INVALID")]
    [InlineData("timestamp;open;high;low;close;volume\n"+Good,"CSV_HEADER_INVALID")]
    [InlineData("timestamp,open,high,low,close,volume,extra\n"+Good,"CSV_HEADER_INVALID")]
    public void HeaderAndNoDataFailuresNeverCreateOutput(string data,string code)
    {
        using var t=new Temp();File.WriteAllText(t.At("in"),data,new UTF8Encoding(false));
        Assert.Equal(code,Assert.Throws<CsvFatalException>(()=>CsvValidator.Run(t.At("in"),t.At("out"),2,0)).Code);
        Assert.False(Directory.Exists(t.At("out")));
    }

    [Theory]
    [InlineData("2026-01-01T00:00:00Z,\"1\n","CSV_UNCLOSED_QUOTE")]
    [InlineData("2026-01-01T00:00:00Z,1\"2,2,0,1,1\n","CSV_SYNTAX_INVALID")]
    [InlineData("2026-01-01T00:00:00Z,\"1\"x,2,0,1,1\n","CSV_SYNTAX_INVALID")]
    [InlineData("2026-01-01T00:00:00Z,1,2,0,1,1,extra,\"open\n","CSV_UNCLOSED_QUOTE")]
    [InlineData("2026-01-01T00:00:00Z,1\r0,2,0,1,1\n","CSV_CONTROL_CHARACTER")]
    public void QuoteOrControlFramingFailureStopsWholeFileWithoutBlindResync(string bad,string code)
    {
        using var t=new Temp();File.WriteAllText(t.At("in"),Header+Good+bad+Good,new UTF8Encoding(false));
        Assert.Equal(code,Assert.Throws<CsvFatalException>(()=>CsvValidator.Run(t.At("in"),t.At("out"),2,0)).Code);
        Assert.Single(File.ReadAllLines(t.At("out/accepted.jsonl")));
        Assert.False(File.Exists(t.At("out/summary.json")));
    }

    [Fact]
    public void EncodingAndOversizeErrorsAreFatalAndPartialOutputsRemain()
    {
        foreach(var (tail,code) in new[]{
            (new byte[]{255,10},"CSV_UTF8_INVALID"),
            (new byte[]{239,187,191,10},"CSV_UNEXPECTED_BOM"),
            (Encoding.UTF8.GetBytes(new string('9',4097)+"\n"),"CSV_RECORD_TOO_LONG")})
        {
            using var t=new Temp();
            File.WriteAllBytes(t.At("in"),Encoding.UTF8.GetBytes(Header+Good).Concat(tail).ToArray());
            Assert.Equal(code,Assert.Throws<CsvFatalException>(()=>CsvValidator.Run(t.At("in"),t.At("out"),2,0)).Code);
            Assert.True(File.Exists(t.At("out/accepted.jsonl")));
            Assert.False(File.Exists(t.At("out/summary.json")));
        }
    }

    [Fact]
    public void DuplicatesOrderAndCultureDoNotSilentlyChangeTheContract()
    {
        using var t=new Temp();
        var old=CultureInfo.CurrentCulture;
        try
        {
            var earlier=Good.Replace("2026-01-01","2025-01-01");
            File.WriteAllText(t.At("in"),Header+Good+Good+earlier,new UTF8Encoding(false));
            CultureInfo.CurrentCulture=CultureInfo.GetCultureInfo("fa-IR");
            var a=CsvValidator.Run(t.At("in"),t.At("a"),2,0);
            CultureInfo.CurrentCulture=CultureInfo.GetCultureInfo("en-US");
            var b=CsvValidator.Run(t.At("in"),t.At("b"),2,0);
            Assert.Equal(3,a.Accepted);Assert.Equal(3,b.Accepted);
            foreach(var n in new[]{"accepted.jsonl","quarantine.jsonl","summary.json"})
            {
                Assert.Equal(File.ReadAllBytes(t.At("a/"+n)),File.ReadAllBytes(t.At("b/"+n)));
                Assert.DoesNotContain((byte)13,File.ReadAllBytes(t.At("a/"+n)));
            }
            var before=Directory.GetFiles(t.At("a")).ToDictionary(p=>p,p=>File.ReadAllBytes(p));
            Assert.Throws<IOException>(()=>CsvValidator.Run(t.At("in"),t.At("a"),2,0));
            foreach(var(p,bytes)in before)Assert.Equal(bytes,File.ReadAllBytes(p));
        }
        finally {CultureInfo.CurrentCulture=old;}
    }

    [Fact]
    public void ActualByteCapsApplyInHashReadAndPostHashEvenWhenLengthLies()
    {
        var bytes=Encoding.UTF8.GetBytes(Header+Good);
        using var t=new Temp();
        using(var input=new ChangingStream(bytes,1,bytes.Concat(new byte[100]).ToArray()))
            Assert.Equal("CSV_INPUT_LIMIT",Assert.Throws<CsvFatalException>(()=>CsvValidator.Run(input,t.At("pre"),
                CsvContract.Policy(2,0) with{MaxInputBytes=bytes.Length+1})).Code);
        Assert.False(Directory.Exists(t.At("pre")));
        foreach(var pass in new[]{2,3})
        {
            using var input=new ChangingStream(bytes,pass,bytes.Concat(new byte[100]).ToArray());
            Assert.Equal("CSV_INPUT_LIMIT",Assert.Throws<CsvFatalException>(()=>CsvValidator.Run(input,t.At("cap"+pass),
                CsvContract.Policy(2,0) with{MaxInputBytes=bytes.Length+1})).Code);
            Assert.False(File.Exists(t.At("cap"+pass+"/summary.json")));
        }
    }

    [Fact]
    public void MutationReadFailureAndRowLimitCannotPublishCompletion()
    {
        var bytes=Encoding.UTF8.GetBytes(Header+Good);
        using var t=new Temp();
        using(var input=new ChangingStream(bytes,3,Encoding.UTF8.GetBytes((Header+Good).Replace("1.20","1.30"))))
            Assert.Equal("CSV_SOURCE_CHANGED",Assert.Throws<CsvFatalException>(()=>CsvValidator.Run(input,t.At("mutate"),CsvContract.Policy(2,0))).Code);
        Assert.False(File.Exists(t.At("mutate/summary.json")));
        using(var input=new ChangingStream(bytes,2,null))
            Assert.Throws<IOException>(()=>CsvValidator.Run(input,t.At("readfail"),CsvContract.Policy(2,0)));
        Assert.False(File.Exists(t.At("readfail/summary.json")));
        using(var input=new MemoryStream(Encoding.UTF8.GetBytes(Header+Good+Good)))
            Assert.Equal("CSV_ROW_LIMIT",Assert.Throws<CsvFatalException>(()=>CsvValidator.Run(input,t.At("limit"),
                CsvContract.Policy(2,0) with{MaxDataRecords=1})).Code);
        Assert.False(File.Exists(t.At("limit/summary.json")));
    }

    [Fact]
    public void LoneCrAtEofIsNotSilentlyTreatedAsCrLf()
    {
        using var t=new Temp();
        File.WriteAllText(t.At("in"),Header+Good.TrimEnd('\n')+"\r",new UTF8Encoding(false));
        Assert.Equal("CSV_CONTROL_CHARACTER",
            Assert.Throws<CsvFatalException>(()=>CsvValidator.Run(t.At("in"),t.At("out"),2,0)).Code);
        Assert.False(File.Exists(t.At("out/summary.json")));
    }

    [Theory]
    [InlineData("2026-01-01T00:00:00Z,1,2,0,1,\uFEFF1\n")]
    [InlineData("2026-01-01T00:00:00Z,1,2,0,1,\"\uFEFF1\"\n")]
    [InlineData("2026-01-01T00:00:00\uFEFFZ,1,2,0,1,1\n")]
    public void EmbeddedDataBomIsFatalWithoutPublishingOrResyncing(string bad)
    {
        using var t=new Temp();
        File.WriteAllText(t.At("in"),Header+Good+bad+Good,new UTF8Encoding(false));
        Assert.Equal("CSV_UNEXPECTED_BOM",
            Assert.Throws<CsvFatalException>(()=>CsvValidator.Run(t.At("in"),t.At("out"),2,0)).Code);
        Assert.Single(File.ReadAllLines(t.At("out/accepted.jsonl")));
        Assert.False(File.Exists(t.At("out/summary.json")));
    }

    private sealed class ChangingStream(byte[] original,int changePass,byte[]? replacement) : Stream
    {
        private long pos;private int pass;
        private byte[] Data=>pass>=changePass && replacement is not null?replacement:original;
        public override bool CanRead=>true;public override bool CanSeek=>true;public override bool CanWrite=>false;
        public override long Length=>original.Length;
        public override long Position{get=>pos;set{pos=value;if(value==0)pass++;}}
        public override int Read(byte[] b,int o,int c)=>Read(b.AsSpan(o,c));
        public override int Read(Span<byte> b)
        {
            if(pass>=changePass && replacement is null && pos==original.Length)throw new IOException("injected");
            var count=(int)Math.Min(b.Length,Data.Length-pos);
            Data.AsSpan((int)pos,count).CopyTo(b);pos+=count;return count;
        }
        public override long Seek(long o,SeekOrigin s){Position=o;return pos;}
        public override void Flush()=>throw new NotSupportedException();
        public override void SetLength(long v)=>throw new NotSupportedException();
        public override void Write(byte[] b,int o,int c)=>throw new NotSupportedException();
    }

    private sealed class Temp : IDisposable
    {
        private readonly string root=Path.Combine(Path.GetTempPath(),"csv checks "+Guid.NewGuid().ToString("N"));
        public Temp()=>Directory.CreateDirectory(root);
        public string At(string name)=>Path.Combine(root,name);
        public void Dispose()=>Directory.Delete(root,true);
    }
}
