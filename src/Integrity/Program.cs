using MarketDataIntegrity;

try
{
    if (args.Length == 2 && args[0] == "csv-demo")
    {
        CsvDemo.Run(args[1], Console.Out);
        return 0;
    }
    if (args.Length == 5 && args[0] == "validate-csv" &&
        int.TryParse(args[3], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var priceDecimals) &&
        int.TryParse(args[4], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var volumeDecimals) &&
        priceDecimals is >= 0 and <= 8 && volumeDecimals is >= 0 and <= 8)
    {
        var result = CsvValidator.Run(args[1], args[2], priceDecimals, volumeDecimals);
        Console.WriteLine($"completed rows={result.Records} accepted={result.Accepted} quarantined={result.Quarantined}; LOCAL_ROW_CHECKS_ONLY; market provenance UNVERIFIED");
        return result.Quarantined == 0 ? 0 : 2;
    }
    if (args.Length == 1 && args[0] == "--version")
    {
        var version = typeof(Validator).Assembly.GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
            .Cast<System.Reflection.AssemblyInformationalVersionAttribute>().Single().InformationalVersion;
        Console.WriteLine($"Integrity {version}; {System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}; {System.Runtime.InteropServices.RuntimeInformation.RuntimeIdentifier}");
        return 0;
    }
    if (args.Length == 2 && args[0] == "demo")
    {
        SyntheticDemo.Run(args[1], Console.Out);
        return 0;
    }
    if (args.Length == 3 && args[0] == "generate" &&
        int.TryParse(args[2], System.Globalization.NumberStyles.None,
            System.Globalization.CultureInfo.InvariantCulture, out var count))
    {
        Synthetic.Generate(args[1], count);
        return 0;
    }
    if (args.Length == 4 && args[0] == "validate")
    {
        var result = Validator.Run(args[1], args[2], args[3]);
        Console.WriteLine($"completed records={result.Records} quarantined={result.Quarantined}");
        return result.Quarantined == 0 ? 0 : 2;
    }
    if (args.Length == 2 && args[0] == "generate-sequence")
    {
        SequenceSynthetic.Generate(args[1]);
        return 0;
    }
    if (args.Length == 4 && args[0] == "validate-sequence")
    {
        var result = SequenceValidator.Run(args[1], args[2], args[3]);
        Console.WriteLine($"completed records={result.Records} quarantined={result.Quarantined} unattributed={result.UnattributedRecords}");
        return result.Quarantined == 0 ? 0 : 2;
    }
    if (args.Length == 5 && args[0] is "start-resumable" or "resume" &&
        int.TryParse(args[4], System.Globalization.NumberStyles.None,
            System.Globalization.CultureInfo.InvariantCulture, out var interval))
    {
        var result = args[0] == "resume"
            ? RecoveryRunner.Resume(args[1], args[2], args[3], interval)
            : RecoveryRunner.Start(args[1], args[2], args[3], interval);
        Console.WriteLine($"completed records={result.Summary.Records} quarantined={result.Summary.Quarantined} resumed_generation={result.RestoredGeneration} rejected_checkpoints={result.RejectedCandidates} already_completed={result.AlreadyCompleted}");
        return result.Summary.Quarantined == 0 ? 0 : 2;
    }
    Console.Error.WriteLine("usage: Integrity validate-csv <input.csv> <new-output> <price-decimals:0..8> <volume-decimals:0..8> | csv-demo <new-directory> | --version | demo <new-directory> | generate <new-directory> <count:1..1000000> | validate <manifest.json> <input.jsonl> <new-output-directory> | generate-sequence <new-directory> | validate-sequence <v2-manifest.json> <input.jsonl> <new-output-directory> | start-resumable|resume <v2-manifest.json> <input.jsonl> <workdir> <interval:1..10000>");
    return 1;
}
catch (CsvFatalException error)
{
    var line = error.Line is null ? "" : $" line={error.Line.Value}";
    Console.Error.WriteLine($"fatal:{error.Code}{line}; no completed result may be inferred from partial files");
    return 1;
}
catch (Exception error) when (error is IOException or UnauthorizedAccessException or
    InvalidDataException or System.Text.Json.JsonException or ArgumentException or OverflowException)
{
    // Do not emit source data, paths, exception messages or stack traces.
    Console.Error.WriteLine($"fatal:{error.GetType().Name}; no completed result may be inferred from partial files");
    return 1;
}
