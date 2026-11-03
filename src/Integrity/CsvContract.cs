using System.Globalization;
using System.Text;

namespace MarketDataIntegrity;

internal sealed class CsvFatalException(string code,long? line=null) : IOException
{
    public string Code { get; } = code;
    public long? Line { get; } = line;
}

public sealed record CsvBar(string Timestamp, string OpenScaled, string HighScaled,
    string LowScaled, string CloseScaled, string VolumeScaled);
public sealed record CsvDecision(long Line, long Offset, long ByteLength, string RawSha256,
    string Reason, string? Field, CsvBar? Record);
public sealed record CsvPolicy(string Contract, int PriceDecimals, int VolumeDecimals,
    int MaxRecordBytes, long MaxInputBytes, long MaxDataRecords, string SourceKind);
public sealed record CsvSummary(string Status, CsvPolicy Policy, string PolicySha256,
    string InputSha256, long InputBytes, string HeaderSha256, long HeaderByteLength,
    long Records, long Accepted, long Quarantined, string Assessment, string MarketProvenance,
    string Ordering, string Deduplication, string Completeness, SortedDictionary<string,long> Reasons);

internal static class CsvContract
{
    internal const int MaxRecordBytes=4096;
    internal const long MaxInputBytes=256L*1024*1024;
    internal const long MaxDataRecords=1_000_000;
    private static readonly UTF8Encoding Utf8=new(false,true);
    internal static readonly string[] Header=["timestamp","open","high","low","close","volume"];

    internal static CsvPolicy Policy(int price,int volume,string kind="USER_SUPPLIED_UNVERIFIED")
    {
        if(price is <0 or >8 || volume is <0 or >8)throw new ArgumentOutOfRangeException(nameof(price));
        if(kind is not ("USER_SUPPLIED_UNVERIFIED" or "GENERATED_EXAMPLE_DECLARED"))throw new ArgumentOutOfRangeException(nameof(kind));
        return new("ohlc-csv-v1",price,volume,MaxRecordBytes,MaxInputBytes,MaxDataRecords,kind);
    }

    internal static string Decode(RawRecord raw,bool header,bool terminatedByLf)
    {
        if(raw.TooLong)throw new CsvFatalException("CSV_RECORD_TOO_LONG");
        var data=raw.Data.Span;
        if(data.Length>0 && data[^1]==13)
        {
            if(!terminatedByLf)throw new CsvFatalException("CSV_CONTROL_CHARACTER");
            data=data[..^1];
        }
        if(header && data.StartsWith(new byte[]{239,187,191}))data=data[3..];
        return Utf8.GetString(data);
    }

    // Restricted physical-line CSV, not RFC4180 multiline CSV.
    internal static string[]? Fields(string text,out string? reason)
    {
        reason=null;
        if(text.Any(c=>c<32))throw new CsvFatalException("CSV_CONTROL_CHARACTER");
        var fields=new List<string>(6);var i=0;
        while(true)
        {
            var field=new StringBuilder();
            if(i<text.Length && text[i]=='"')
            {
                i++;var closed=false;
                while(i<text.Length)
                {
                    if(text[i]!='"'){field.Append(text[i++]);continue;}
                    if(i+1<text.Length && text[i+1]=='"'){field.Append('"');i+=2;continue;}
                    i++;closed=true;break;
                }
                if(!closed)throw new CsvFatalException("CSV_UNCLOSED_QUOTE");
                if(i<text.Length && text[i]!=',')throw new CsvFatalException("CSV_SYNTAX_INVALID");
            }
            else
            {
                while(i<text.Length && text[i]!=',')
                {
                    if(text[i]=='"')throw new CsvFatalException("CSV_SYNTAX_INVALID");
                    field.Append(text[i++]);
                }
            }
            if(fields.Count<7)fields.Add(field.ToString());
            if(i==text.Length)break;
            i++; // delimiter; a trailing comma creates an empty final field
        }
        if(fields.Count!=6){reason="CSV_COLUMN_COUNT_INVALID";return null;}
        return fields.ToArray();
    }

    internal static void CheckHeader(RawRecord raw,bool terminatedByLf)
    {
        try
        {
            var fields=Fields(Decode(raw,true,terminatedByLf),out _);
            if(fields is null || !fields.SequenceEqual(Header))throw new CsvFatalException("CSV_HEADER_INVALID");
        }
        catch(DecoderFallbackException){throw new CsvFatalException("CSV_HEADER_INVALID",raw.Line);}
        catch(CsvFatalException error){throw new CsvFatalException(error.Code,raw.Line);}
    }

    internal static (string Reason,string? Field,CsvBar? Bar) Parse(RawRecord raw,int price,int volume,bool terminatedByLf)
    {
        try{return ParseLine(raw,price,volume,terminatedByLf);}
        catch(CsvFatalException error){throw new CsvFatalException(error.Code,raw.Line);}
    }

    private static (string Reason,string? Field,CsvBar? Bar) ParseLine(RawRecord raw,int price,int volume,bool terminatedByLf)
    {
        string text;
        try{text=Decode(raw,false,terminatedByLf);}
        catch(DecoderFallbackException){throw new CsvFatalException("CSV_UTF8_INVALID");}
        if(text.StartsWith('\uFEFF'))throw new CsvFatalException("CSV_UNEXPECTED_BOM");
        if(text.Length==0)return("EMPTY_ROW",null,null);
        var fields=Fields(text,out var syntax);
        if(fields is null)return(syntax!,null,null);
        var time=fields[0];
        const string shape="0000-00-00T00:00:00Z";
        if(time.Length!=20 || time.Where((c,i)=>shape[i]=='0' ? c<'0'||c>'9' : c!=shape[i]).Any() ||
            !DateTime.TryParseExact(time,"yyyy-MM-dd'T'HH:mm:ss'Z'",CultureInfo.InvariantCulture,DateTimeStyles.None,out _))
            return("TIMESTAMP_INVALID","timestamp",null);
        var numbers=new long[5];
        for(var i=0;i<5;i++)
        {
            var reason=Number(fields[i+1],i==4?volume:price,out numbers[i]);
            if(reason is not null)return(reason,Header[i+1],null);
        }
        if(numbers[4]<0)return("NEGATIVE_VOLUME","volume",null);
        var bar=new CsvBar(time,S(numbers[0]),S(numbers[1]),S(numbers[2]),S(numbers[3]),S(numbers[4]));
        return numbers[2]>numbers[0] || numbers[2]>numbers[3] || numbers[0]>numbers[1] || numbers[3]>numbers[1]
            ? ("OHLC_INCONSISTENT","ohlc",bar) : ("VALID_ROW",null,bar);
        static string S(long n)=>n.ToString(CultureInfo.InvariantCulture);
    }

    internal static string? Number(string text,int scale,out long value)
    {
        value=0;var negative=text.StartsWith('-');var start=negative?1:0;
        var dot=-1;
        if(start==text.Length)return "NUMBER_FORMAT_INVALID";
        for(var i=start;i<text.Length;i++)
        {
            if(text[i]=='.' && dot<0){dot=i;continue;}
            if(text[i]<'0'||text[i]>'9')return "NUMBER_FORMAT_INVALID";
        }
        var integerDigits=(dot<0?text.Length:dot)-start;
        var fractional=dot<0?0:text.Length-dot-1;
        if(integerDigits<1 || (dot>=0 && fractional==0))return "NUMBER_FORMAT_INVALID";
        if(fractional>scale)return "NUMBER_PRECISION_EXCEEDED";
        ulong limit=negative?9223372036854775808UL:9223372036854775807UL,acc=0;
        for(var i=start;i<text.Length;i++)
        {
            if(i==dot)continue;
            var digit=(ulong)(text[i]-'0');
            if(acc>(limit-digit)/10)return "NUMBER_OUT_OF_RANGE";
            acc=acc*10+digit;
        }
        for(var i=fractional;i<scale;i++)
        {
            if(acc>limit/10)return "NUMBER_OUT_OF_RANGE";
            acc*=10;
        }
        value=negative ? acc==9223372036854775808UL?long.MinValue:-(long)acc : (long)acc;
        return null;
    }
}
