using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace MarketDataIntegrity;

public static class CsvValidator
{
    public static CsvSummary Run(string inputPath,string outputDirectory,int priceDecimals,int volumeDecimals)
    {
        var policy=CsvContract.Policy(priceDecimals,volumeDecimals);
        using var input=new FileStream(inputPath,FileMode.Open,FileAccess.Read,FileShare.Read);
        return Run(input,outputDirectory,policy);
    }

    internal static CsvSummary Run(Stream input,string directory,CsvPolicy policy)
    {
        _=CsvContract.Policy(policy.PriceDecimals,policy.VolumeDecimals,policy.SourceKind);
        if(policy.Contract!="ohlc-csv-v1")throw new ArgumentOutOfRangeException(nameof(policy));
        if(policy.MaxInputBytes is <1 or >CsvContract.MaxInputBytes ||
            policy.MaxDataRecords is <1 or >CsvContract.MaxDataRecords ||
            policy.MaxRecordBytes is <64 or >CsvContract.MaxRecordBytes)
            throw new ArgumentOutOfRangeException(nameof(policy));
        if(!input.CanRead || !input.CanSeek)throw new InvalidDataException("seekable input required");
        if(Directory.Exists(directory)||File.Exists(directory))throw new IOException("output exists");
        var guarded=new GuardedInput(input,policy.MaxInputBytes);
        var originalLength=guarded.Length;
        var initial=Hash(guarded);
        if(initial.Bytes!=originalLength || guarded.Length!=originalLength)
            throw new CsvFatalException("CSV_SOURCE_CHANGED");
        var policyHash=Contract.Hash(JsonSerializer.SerializeToUtf8Bytes(policy,Contract.Json));
        FileStream? owner=null,goodFile=null,badFile=null;
        StreamWriter? good=null,bad=null;
        long records=0,accepted=0,quarantined=0,headerLength=0;
        string? headerHash=null;
        var reasons=new SortedDictionary<string,long>(StringComparer.Ordinal);
        try
        {
            BoundedReader.Visit(guarded,policy.MaxRecordBytes,raw=>
            {
                var terminatedByLf=checked(raw.Offset+raw.ByteLength)<originalLength;
                if(raw.Line==1)
                {
                    CsvContract.CheckHeader(raw,terminatedByLf);headerHash=raw.Sha256;headerLength=raw.ByteLength;return;
                }
                if(records==policy.MaxDataRecords)throw new CsvFatalException("CSV_ROW_LIMIT",raw.Line);
                var parsed=CsvContract.Parse(raw,policy.PriceDecimals,policy.VolumeDecimals,terminatedByLf);
                if(owner is null)
                {
                    Directory.CreateDirectory(directory);
                    owner=New("run.lock");
                    if(Directory.EnumerateFileSystemEntries(directory).Count()!=1)
                        throw new IOException("output changed");
                    goodFile=New("accepted.jsonl");badFile=New("quarantine.jsonl");
                    good=Writer(goodFile);bad=Writer(badFile);
                }
                records=checked(records+1);
                reasons[parsed.Reason]=checked(reasons.GetValueOrDefault(parsed.Reason)+1);
                var decision=new CsvDecision(raw.Line,raw.Offset,raw.ByteLength,raw.Sha256,
                    parsed.Reason,parsed.Field,parsed.Bar);
                var target=parsed.Reason=="VALID_ROW"?good!:bad!;
                target.WriteLine(JsonSerializer.Serialize(decision,Contract.Json));
                if(parsed.Reason=="VALID_ROW")accepted++;else quarantined++;
            });
            if(headerHash is null || records==0)throw new CsvFatalException("CSV_NO_DATA");
            good!.Flush();bad!.Flush();goodFile!.Flush(true);badFile!.Flush(true);
            good.Dispose();good=null;bad.Dispose();bad=null;
            goodFile.Dispose();goodFile=null;badFile.Dispose();badFile=null;
            var final=Hash(guarded);
            if(final.Hash!=initial.Hash || final.Bytes!=originalLength || guarded.Length!=originalLength)
                throw new CsvFatalException("CSV_SOURCE_CHANGED");
            var summary=new CsvSummary("completed",policy,policyHash,initial.Hash,originalLength,
                headerHash,headerLength,records,accepted,quarantined,"LOCAL_ROW_CHECKS_ONLY",
                "UNVERIFIED","NOT_CHECKED","NOT_CHECKED","NOT_CHECKED",reasons);
            using(var pending=New("summary.pending"))
            using(var writer=Writer(pending))
            {
                writer.WriteLine(JsonSerializer.Serialize(summary,Contract.Json));
                writer.Flush();pending.Flush(true);
            }
            File.Move(Path.Combine(directory,"summary.pending"),Path.Combine(directory,"summary.json"));
            return summary;
        }
        finally {good?.Dispose();bad?.Dispose();goodFile?.Dispose();badFile?.Dispose();owner?.Dispose();}
        FileStream New(string name)=>new(Path.Combine(directory,name),FileMode.CreateNew,FileAccess.Write,FileShare.None);
    }

    private static StreamWriter Writer(Stream file)=>new(file,new UTF8Encoding(false),4096,leaveOpen:true){NewLine="\n"};
    private static (string Hash,long Bytes) Hash(GuardedInput input)
    {
        input.Position=0;
        using var hash=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer=new byte[8192];int count;
        while((count=input.Read(buffer))!=0)hash.AppendData(buffer.AsSpan(0,count));
        var bytes=input.ReadBytes;
        var result=Convert.ToHexStringLower(hash.GetHashAndReset());
        input.Position=0;
        return(result,bytes);
    }

    private sealed class GuardedInput(Stream inner,long limit) : Stream
    {
        internal long ReadBytes {get;private set;}
        public override bool CanRead=>inner.CanRead;
        public override bool CanSeek=>inner.CanSeek;
        public override bool CanWrite=>false;
        public override long Length
        {
            get {var length=inner.Length;if(length>limit)throw new CsvFatalException("CSV_INPUT_LIMIT");return length;}
        }
        public override long Position
        {
            get=>inner.Position;
            set {if(value!=0)throw new NotSupportedException();inner.Position=0;ReadBytes=0;}
        }
        public override int Read(byte[] buffer,int offset,int count)=>Read(buffer.AsSpan(offset,count));
        public override int Read(Span<byte> buffer)
        {
            var allowed=(int)Math.Min(buffer.Length,limit-ReadBytes+1);
            var count=inner.Read(buffer[..allowed]);
            ReadBytes=checked(ReadBytes+count);
            if(ReadBytes>limit)throw new CsvFatalException("CSV_INPUT_LIMIT");
            return count;
        }
        public override long Seek(long offset,SeekOrigin origin)
        {if(offset!=0 || origin!=SeekOrigin.Begin)throw new NotSupportedException();Position=0;return 0;}
        public override void Flush()=>throw new NotSupportedException();
        public override void SetLength(long value)=>throw new NotSupportedException();
        public override void Write(byte[] buffer,int offset,int count)=>throw new NotSupportedException();
    }
}
