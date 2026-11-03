using System.Text;
namespace MarketDataIntegrity;

internal static class CsvDemo
{
    internal const string Example="timestamp,open,high,low,close,volume\n"+
        "2026-01-01T00:00:00Z,100.10,101.20,99.00,100.50,10\n"+
        "2026-01-01T00:01:00Z,-2.00,-1.00,-3.00,-2.50,0\n"+
        "2026-01-01T00:02:00Z,5.00,4.00,3.00,3.50,1\n"+
        "2026-01-01T00:03:00Z,1.001,2,0,1,1\n"+
        "2026-01-01T00:04:00Z,1,2,0,1,-1\n"+
        "2026-01-01T00:05:00Z,\"100,10\",101,99,100,1\n";
    public static void Run(string directory,TextWriter output)
    {
        if(Directory.Exists(directory)||File.Exists(directory))throw new IOException("new demo root required");
        Directory.CreateDirectory(directory);
        using var owner=new FileStream(Path.Combine(directory,"demo.lock"),FileMode.CreateNew,FileAccess.Write,FileShare.None);
        if(Directory.EnumerateFileSystemEntries(directory).Count()!=1)throw new IOException("root changed");
        using(var file=new FileStream(Path.Combine(directory,"example.csv"),FileMode.CreateNew,FileAccess.Write))
        {
            var bytes=new UTF8Encoding(false).GetBytes(Example);file.Write(bytes);file.Flush(true);
        }
        using var input=File.OpenRead(Path.Combine(directory,"example.csv"));
        var summary=CsvValidator.Run(input,Path.Combine(directory,"result"),
            CsvContract.Policy(2,0,"GENERATED_EXAMPLE_DECLARED"));
        if(summary.Records!=6||summary.Accepted!=2||summary.Quarantined!=4)
            throw new InvalidDataException("unexpected example result");
        output.WriteLine("CSV demo: generated example; 6 rows; 2 accepted; 4 quarantined (expected).");
        foreach(var(reason,count) in summary.Reasons)output.WriteLine($"  {reason}: {count}");
        output.WriteLine("LOCAL_ROW_CHECKS_ONLY; ordering, deduplication and completeness NOT_CHECKED.");
        output.WriteLine("Demo exit0 means expected outcomes matched; see example.csv and result/ in your chosen directory.");
    }
}
