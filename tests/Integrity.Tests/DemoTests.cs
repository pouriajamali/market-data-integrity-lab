using System.Diagnostics;
using System.Text.Json;
using MarketDataIntegrity;
using Xunit;

namespace Integrity.Tests;

public sealed class DemoTests
{
    [Fact]
    public void DemoMatchesKnownCanonicalOutputsAndRefusesExistingRootWithoutMutation()
    {
        var root = Path.Combine(Path.GetTempPath(), "integrity demo " + Guid.NewGuid().ToString("N"));
        try
        {
            var output = new StringWriter();
            SyntheticDemo.Run(root, output);
            Assert.Contains("NOT a crash demonstration", output.ToString());
            Assert.Contains("2 unattributed (UNKNOWN)", output.ToString());
            var pins = new Dictionary<string,string>
            {
                ["accepted.jsonl"] = "5dc6e030b0c09c737a7fa32782773f86711406f0302fb117e3470a665fa2b7ec",
                ["quarantine.jsonl"] = "d9fbe7674dd85125264a794bd8254c9977f91b70f94da5267bca9b23dabe673b",
                ["summary.json"] = "39b086a2c9f3e4003f7c4b1e17d24b255af71fcad7e4bbea9de35633da08f9c1"
            };
            foreach (var (name, pin) in pins)
            {
                using var source = File.OpenRead(Path.Combine(root, "recovery-result", name));
                Assert.Equal(pin, Contract.Hash(source));
                Assert.Equal(File.ReadAllBytes(Path.Combine(root,"sequence-result",name)),
                    File.ReadAllBytes(Path.Combine(root,"recovery-result",name)));
            }
            using var report = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(root,"demo-summary.json")));
            Assert.False(report.RootElement.GetProperty("crashDemonstrated").GetBoolean());
            Assert.True(report.RootElement.GetProperty("completedResume").GetBoolean());
            var before = Directory.GetFiles(root, "*", SearchOption.AllDirectories)
                .ToDictionary(p=>p,p=>File.ReadAllBytes(p));
            Assert.Throws<IOException>(()=>SyntheticDemo.Run(root, new StringWriter()));
            Assert.Equal(before.Keys.Order(),Directory.GetFiles(root,"*",SearchOption.AllDirectories).Order());
            foreach(var (name,bytes) in before) Assert.Equal(bytes,File.ReadAllBytes(name));
        }
        finally { if(Directory.Exists(root)) Directory.Delete(root,true); }
    }

    [Fact]
    public async Task CliDemoExitMeaningBadArgumentsAndVersionAreExplicit()
    {
        var parent=Path.Combine(Path.GetTempPath(),"integrity cli demo "+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(parent);
        try
        {
            var destination=Path.Combine(parent,"result");
            var bad=await Cli("demo",destination,"extra");
            Assert.Equal(1,bad.Exit);
            Assert.False(Directory.Exists(destination));
            var good=await Cli("demo",destination);
            Assert.Equal(0,good.Exit);
            Assert.Equal("",good.Error);
            Assert.Contains("input contains quarantined records",good.Output);
            var existing=await Cli("demo",destination);
            Assert.Equal(1,existing.Exit);
            Assert.Equal("",existing.Output);
            var version=await Cli("--version");
            Assert.Equal(0,version.Exit);
            Assert.StartsWith("Integrity 0.6.1-preview.1",version.Output);
        }
        finally { Directory.Delete(parent,true); }
    }

    [Fact]
    public void InterruptedDemoPresentationRetainsPartialFilesAndNeverClaimsCompletion()
    {
        var root=Path.Combine(Path.GetTempPath(),"integrity partial demo "+Guid.NewGuid().ToString("N"));
        try
        {
            Assert.Throws<IOException>(()=>SyntheticDemo.Run(root,new FailedOutput()));
            Assert.True(File.Exists(Path.Combine(root,"a1-result","summary.json")));
            Assert.False(File.Exists(Path.Combine(root,"demo-summary.json")));
            var before=File.ReadAllBytes(Path.Combine(root,"a1-result","summary.json"));
            Assert.Throws<IOException>(()=>SyntheticDemo.Run(root,new StringWriter()));
            Assert.Equal(before,File.ReadAllBytes(Path.Combine(root,"a1-result","summary.json")));
        }
        finally {if(Directory.Exists(root))Directory.Delete(root,true);}
    }

    private sealed class FailedOutput : StringWriter
    {
        public override void WriteLine(string? value)=>throw new IOException("presentation failed");
    }

    private static async Task<(int Exit,string Output,string Error)> Cli(params string[] args)
    {
        var start=new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH")??"dotnet")
        {RedirectStandardOutput=true,RedirectStandardError=true,UseShellExecute=false};
        foreach(var arg in new[]{"exec","--runtimeconfig",Path.ChangeExtension(typeof(DemoTests).Assembly.Location,".runtimeconfig.json"),typeof(Validator).Assembly.Location}.Concat(args))
            start.ArgumentList.Add(arg);
        using var child=Process.Start(start)!;
        try
        {
            var output=child.StandardOutput.ReadToEndAsync();
            var error=child.StandardError.ReadToEndAsync();
            using var limit=new CancellationTokenSource(TimeSpan.FromSeconds(20));
            await child.WaitForExitAsync(limit.Token);
            return(child.ExitCode,await output,await error);
        }
        finally { if(!child.HasExited){child.Kill(entireProcessTree:true);await child.WaitForExitAsync();} }
    }
}
