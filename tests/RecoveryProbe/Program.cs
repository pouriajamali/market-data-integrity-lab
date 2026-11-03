using MarketDataIntegrity;
using System.Text.Json;

try
{
    if (args.Length != 7 || args[0] is not ("start" or "resume") ||
        !int.TryParse(args[4], out var interval) || !long.TryParse(args[6], out var record))
        return 1;
    var paused = false;
    var hooks = new RecoveryHooks((point, records) =>
    {
        if (paused || point != args[5] || records != record) return;
        paused = true;
        Console.WriteLine($"READY:{point}:{records}");
        Console.Out.Flush();
        if (Console.ReadLine() != "continue") throw new InvalidDataException("probe release missing");
    });
    var result = RecoveryRunner.Execute(args[1], args[2], args[3], interval,
        args[0] == "resume", hooks);
    Console.WriteLine(JsonSerializer.Serialize(new
    {
        result.Summary.Records, result.Summary.Accepted, result.Summary.Quarantined,
        result.RestoredGeneration, result.RejectedCandidates, result.AlreadyCompleted
    }));
    return result.Summary.Quarantined == 0 ? 0 : 2;
}
catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or
    ArgumentException or OverflowException or JsonException)
{
    Console.Error.WriteLine("fatal:" + error.GetType().Name);
    return 1;
}
