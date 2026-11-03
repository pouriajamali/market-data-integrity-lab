using MarketDataIntegrity;

try
{
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
    Console.Error.WriteLine("usage: Integrity generate <new-directory> <count:1..1000000> | validate <manifest.json> <input.jsonl> <new-output-directory>");
    return 1;
}
catch (Exception error) when (error is IOException or UnauthorizedAccessException or
    InvalidDataException or System.Text.Json.JsonException or ArgumentException or OverflowException)
{
    // Do not emit source data, paths, exception messages or stack traces.
    Console.Error.WriteLine($"fatal:{error.GetType().Name}; no completed result may be inferred from partial files");
    return 1;
}
