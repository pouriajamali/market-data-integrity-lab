using System.Diagnostics;
using System.Text.Json;

namespace Integrity.Tests;

internal static class RecoveryChild
{
    private static string Dotnet => Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet";
    private static string MainDll => Path.Combine(RecoveryFixture.RepoRoot, "src", "Integrity", "bin",
        new DirectoryInfo(Path.GetDirectoryName(typeof(RecoveryChild).Assembly.Location)!).Parent!.Name,
        "net10.0", "Integrity.dll");
    private static string ProbeDll => Path.Combine(RecoveryFixture.RepoRoot, "tests", "RecoveryProbe", "bin",
        new DirectoryInfo(Path.GetDirectoryName(typeof(RecoveryChild).Assembly.Location)!).Parent!.Name,
        "net10.0", "RecoveryProbe.dll");

    public static Process Spawn(string dll, IEnumerable<string> arguments)
    {
        var start = new ProcessStartInfo(Dotnet)
        {
            RedirectStandardInput = true, RedirectStandardOutput = true,
            RedirectStandardError = true, UseShellExecute = false
        };
        start.ArgumentList.Add(dll);
        foreach (var arg in arguments) start.ArgumentList.Add(arg);
        return Process.Start(start) ?? throw new InvalidOperationException("owned child failed to start");
    }

    public static async Task<(int Exit,string Out,string Error)> Run(RecoveryFixture fixture,
        string label, params string[] arguments)
    {
        using var child = Spawn(MainDll, arguments);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var stdout = child.StandardOutput.ReadToEndAsync(deadline.Token);
        var stderr = child.StandardError.ReadToEndAsync(deadline.Token);
        try
        {
            await Task.WhenAll(stdout, stderr, child.WaitForExitAsync(deadline.Token));
            var result = (child.ExitCode, await stdout, await stderr);
            Save(fixture,label,child.Id,result.ExitCode,result.Item2,result.Item3,"completed");
            return result;
        }
        finally { await Cleanup(child,fixture,label); }
    }

    public static async Task KillAt(RecoveryFixture fixture, bool resume, string point, long records,
        string label, Func<Task>? whilePaused = null, int interval = 2)
    {
        using var child = Spawn(ProbeDll, new[] { resume ? "resume" : "start", fixture.ManifestPath,
            fixture.InputPath, fixture.Work, interval.ToString(System.Globalization.CultureInfo.InvariantCulture), point, records.ToString(System.Globalization.CultureInfo.InvariantCulture) });
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var stderr = child.StandardError.ReadToEndAsync(deadline.Token);
        string ready = "";
        try
        {
            ready = await child.StandardOutput.ReadLineAsync(deadline.Token) ?? "";
            Xunit.Assert.Equal($"READY:{point}:{records}", ready);
            if (whilePaused is not null) await whilePaused();
            child.Kill(entireProcessTree:true);
            await child.WaitForExitAsync(deadline.Token);
            var rest = await child.StandardOutput.ReadToEndAsync(deadline.Token);
            Save(fixture,label,child.Id,child.ExitCode,ready+"\n"+rest,await stderr,"killed owned child");
        }
        finally { await Cleanup(child,fixture,label); }
    }

    public static async Task RaceStarts(RecoveryFixture fixture)
    {
        var args = new[] { "start", fixture.ManifestPath, fixture.InputPath, fixture.Work,
            "2", "BeforeDirectoryCreate", "0" };
        using var first = Spawn(ProbeDll,args);
        using var second = Spawn(ProbeDll,args);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var error1=first.StandardError.ReadToEndAsync(deadline.Token);
        var error2=second.StandardError.ReadToEndAsync(deadline.Token);
        try
        {
            Xunit.Assert.Equal("READY:BeforeDirectoryCreate:0",await first.StandardOutput.ReadLineAsync(deadline.Token));
            Xunit.Assert.Equal("READY:BeforeDirectoryCreate:0",await second.StandardOutput.ReadLineAsync(deadline.Token));
            await first.StandardInput.WriteLineAsync("continue");
            await first.StandardInput.FlushAsync();
            await second.StandardInput.WriteLineAsync("continue");
            await second.StandardInput.FlushAsync();
            var out1=first.StandardOutput.ReadToEndAsync(deadline.Token);
            var out2=second.StandardOutput.ReadToEndAsync(deadline.Token);
            await Task.WhenAll(out1,out2,error1,error2,first.WaitForExitAsync(deadline.Token),second.WaitForExitAsync(deadline.Token));
            Save(fixture,"race-first",first.Id,first.ExitCode,await out1,await error1,"race");
            Save(fixture,"race-second",second.Id,second.ExitCode,await out2,await error2,"race");
            Xunit.Assert.Equal(new[]{1,2},new[]{first.ExitCode,second.ExitCode}.Order().ToArray());
        }
        finally { await Cleanup(first,fixture,"race-first");await Cleanup(second,fixture,"race-second"); }
    }

    private static async Task Cleanup(Process child,RecoveryFixture fixture,string label)
    {
        if(child.HasExited)return;
        child.Kill(entireProcessTree:true);
        using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await child.WaitForExitAsync(deadline.Token);
        File.WriteAllText(fixture.PathFor(label+"-cleanup.json"),JsonSerializer.Serialize(
            new{child.Id,child.ExitCode,Reason="deadline/assertion cleanup; only owned child killed"}));
    }
    private static void Save(RecoveryFixture fixture,string label,int pid,int exit,string stdout,string stderr,string outcome)
    {
        if(stdout.Length>16384||stderr.Length>16384)throw new InvalidDataException("probe output budget");
        File.WriteAllText(fixture.PathFor(label+".json"),JsonSerializer.Serialize(
            new{pid,exit,stdout,stderr,outcome},new JsonSerializerOptions{WriteIndented=true}));
    }
}
