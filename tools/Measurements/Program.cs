using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using MarketDataIntegrity;

// Separate process per observation. No friend assembly or fault-hook access.
if (args.Length != 4 || args[0] is not ("A2" or "A3" or "HASH")) return 1;
try
{
    var mode = args[0];
    using var process = Process.GetCurrentProcess();
    process.Refresh();
    var cpu0 = process.TotalProcessorTime.Ticks;
    var allocated0 = GC.GetTotalAllocatedBytes(precise: true);
    var t0 = Stopwatch.GetTimestamp();
    SequenceSummary? summary = null;
    string? digest = null;
    if (mode == "A2") summary = SequenceValidator.Run(args[1], args[2], args[3]);
    else if (mode == "A3") summary = RecoveryRunner.Start(args[1], args[2], args[3], 1000).Summary;
    else
    {
        using var source = File.OpenRead(args[2]);
        digest = Convert.ToHexStringLower(SHA256.HashData(source));
    }
    var t1 = Stopwatch.GetTimestamp();
    var allocated1 = GC.GetTotalAllocatedBytes(precise: true);
    process.Refresh();
    var cpu1 = process.TotalProcessorTime.Ticks;
    long? peak = null;
    try { var value = process.PeakWorkingSet64; if (value > 0) peak = value; }
    catch (Exception error) when (error is PlatformNotSupportedException or System.ComponentModel.Win32Exception) { }
    Console.WriteLine(JsonSerializer.Serialize(new
    {
        mode, operationTicks = t1 - t0, clockFrequency = Stopwatch.Frequency,
        allocatedBytes = allocated1 - allocated0, cpuTicks = cpu1 - cpu0,
        cpuTickFrequency = TimeSpan.TicksPerSecond, lifetimePeakWorkingSetBytes = peak,
        peakStatus = peak is null ? "UNKNOWN" : "OBSERVED",
        runtime = RuntimeInformation.FrameworkDescription,
        architecture = RuntimeInformation.ProcessArchitecture.ToString(),
        records = summary?.Records, accepted = summary?.Accepted, quarantined = summary?.Quarantined,
        unattributed = summary?.UnattributedRecords, uncompared = summary?.UncomparedRecords, digest
    }));
    return 0;
}
catch (Exception error)
{
    // Never print source records or machine-specific exception messages.
    Console.Error.WriteLine("measurement_failed:" + error.GetType().Name);
    return 1;
}
