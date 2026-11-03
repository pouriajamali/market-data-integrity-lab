using System.Buffers.Binary;
using MarketDataIntegrity;
using Xunit;

namespace Integrity.Tests;

public sealed class RecoveryTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EofFinalizationTwiceThenOlderCorruptionKeepsLatestGenerationUsable(bool empty)
    {
        var fixture = new RecoveryFixture("eof-red-green",empty);
        var records=empty?0:22;
        await RecoveryChild.KillAt(fixture,false,"SummaryFlushed",records,"first-eof",interval:1);
        await RecoveryChild.KillAt(fixture,true,"SummaryFlushed",records,"second-eof",interval:1);
        var slots=Directory.EnumerateFiles(fixture.Work,"checkpoint.?")
            .Select(path=>(Path:path,Generation:BinaryPrimitives.ReadInt64LittleEndian(File.ReadAllBytes(path).AsSpan(8,8))))
            .OrderBy(p=>p.Generation).ToArray();
        var latest=slots[^1];
        if(slots.Length==2)
        {
            var older=slots[0];
            var bytes=File.ReadAllBytes(older.Path);
            File.WriteAllBytes(fixture.PathFor("older-original.bin"),bytes);
            bytes[^1]^=1;
            File.WriteAllBytes(older.Path,bytes);
        }
        var latestBytes=File.ReadAllBytes(latest.Path);
        var result=await RecoveryChild.Run(fixture,"terminal-resume","resume",
            fixture.ManifestPath,fixture.InputPath,fixture.Work,"1");
        Assert.Equal(empty?0:2,result.Exit);
        Assert.Equal("",result.Error);
        fixture.AssertCanonical(fixture.Work);
        Assert.Equal(latestBytes,File.ReadAllBytes(latest.Path));
    }
    [Theory]
    [InlineData("DecisionMid",4)]
    [InlineData("DecisionMid",14)]
    [InlineData("DecisionWritten",20)]
    [InlineData("AcceptedFlushed",20)]
    [InlineData("CheckpointPayloadMid",20)]
    [InlineData("CheckpointFlushed",20)]
    [InlineData("CheckpointCommitted",20)]
    [InlineData("SummaryFlushed",22)]
    [InlineData("SummaryCommitted",22)]
    public async Task FiniteRealKillMatrixResumesToExactA2Bytes(string point,long records)
    {
        var fixture=new RecoveryFixture("kill-"+point);
        await RecoveryChild.KillAt(fixture,false,point,records,"owned-kill");
        if(point=="CheckpointPayloadMid")
        {
            var pending=File.ReadAllBytes(Path.Combine(fixture.Work,"checkpoint.pending"));
            Assert.True(pending.Length>RecoveryCheckpoint.HeaderBytes);
            var declared=BinaryPrimitives.ReadInt32LittleEndian(pending.AsSpan(16,4));
            Assert.Equal(RecoveryCheckpoint.HeaderBytes+declared/2,pending.Length);
            Assert.True(pending.Length<RecoveryCheckpoint.HeaderBytes+declared+32);
        }
        if(point=="CheckpointFlushed")
        {
            var pending=RecoveryCheckpoint.Read(Path.Combine(fixture.Work,"checkpoint.pending"),
                fixture.Manifest,fixture.ManifestHash,fixture.PolicyHash,new FileInfo(fixture.InputPath).Length);
            Assert.Equal(10,pending.Generation);
            Assert.Equal(20,pending.Records);
        }
        var result=await RecoveryChild.Run(fixture,"resume","resume",
            fixture.ManifestPath,fixture.InputPath,fixture.Work,"2");
        Assert.Equal(2,result.Exit);Assert.Equal("",result.Error);
        if(point is "CheckpointPayloadMid" or "CheckpointFlushed")
            Assert.Contains("resumed_generation=9",result.Out);
        if(point=="CheckpointCommitted")Assert.Contains("resumed_generation=10",result.Out);
        if(point=="SummaryCommitted")Assert.Contains("already_completed=True",result.Out);
        fixture.AssertCanonical(fixture.Work);
        using var summary=System.Text.Json.JsonDocument.Parse(File.ReadAllBytes(Path.Combine(fixture.Work,"summary.json")));
        Assert.Equal(4,summary.RootElement.GetProperty("unattributedRecords").GetInt64());
        Assert.Equal("UNKNOWN_UNATTRIBUTABLE_RECORDS",summary.RootElement.GetProperty("assessment").GetString());
        var maximum=summary.RootElement.GetProperty("streamStates").EnumerateArray()
            .Single(s=>s.GetProperty("instrument").GetString()=="MAX");
        Assert.True(maximum.GetProperty("rangeExhausted").GetBoolean());
        Assert.Equal(System.Text.Json.JsonValueKind.Null,maximum.GetProperty("nextExpected").ValueKind);
        Assert.Equal("CONFLICT_IN_WINDOW",maximum.GetProperty("blockedReason").GetString());
    }

    [Fact]
    public async Task TwoConsecutiveInterruptedRestartsPreserveWindowBlocksAndUnknown()
    {
        var fixture=new RecoveryFixture("two-interruptions");
        await RecoveryChild.KillAt(fixture,false,"DecisionMid",14,"first-kill");
        await RecoveryChild.KillAt(fixture,true,"CheckpointCommitted",20,"second-kill");
        var result=await RecoveryChild.Run(fixture,"final","resume",
            fixture.ManifestPath,fixture.InputPath,fixture.Work,"2");
        Assert.Equal(2,result.Exit);fixture.AssertCanonical(fixture.Work);
    }

    [Theory]
    [InlineData("checksum")]
    [InlineData("header-length")]
    [InlineData("short-prefix")]
    [InlineData("corrupt-prefix")]
    [InlineData("invalid-window")]
    [InlineData("invalid-counters")]
    public async Task InvalidLatestCandidateFallsBackOnlyToFullyCompatiblePrevious(string damage)
    {
        var fixture=new RecoveryFixture("fallback-"+damage);
        await RecoveryChild.KillAt(fixture,false,"DecisionWritten",8,"prepare");
        PreserveOriginals(fixture);
        var latestPath=Path.Combine(fixture.Work,"checkpoint.1"); // generation3 / six records
        var latestBytes=File.ReadAllBytes(latestPath);
        if(damage is "invalid-window" or "invalid-counters")
        {
            var data=fixture.ReadCheckpoint(1);
            if(damage=="invalid-counters")data=data with{UnattributedRecords=1};
            else
            {
                var states=data.Streams.ToArray();
                var index=Array.FindIndex(states,state=>state.Window.Length>0);
                states[index]=states[index] with{Window=Enumerable.Repeat(states[index].Window[0],3).ToArray()};
                data=data with{Streams=states};
            }
            RecoveryCheckpoint.Publish(fixture.Work,data,new RecoveryHooks());
            latestBytes=File.ReadAllBytes(latestPath);
        }
        var older=fixture.ReadCheckpoint(0); // generation2 / four records
        if(damage=="checksum"){latestBytes[^1]^=1;File.WriteAllBytes(latestPath,latestBytes);}
        if(damage=="header-length")
        {
            BinaryPrimitives.WriteInt32LittleEndian(latestBytes.AsSpan(16,4),int.MaxValue);
            File.WriteAllBytes(latestPath,latestBytes);
        }
        var accepted=Path.Combine(fixture.Work,"accepted.jsonl");
        if(damage=="short-prefix")
        {using var file=new FileStream(accepted,FileMode.Open,FileAccess.Write);file.SetLength(older.AcceptedPrefix.Length);}
        if(damage=="corrupt-prefix")
        {
            var bytes=File.ReadAllBytes(accepted);
            bytes[checked((int)older.AcceptedPrefix.Length+1)]^=1;
            File.WriteAllBytes(accepted,bytes);
        }
        var result=await RecoveryChild.Run(fixture,"fallback","resume",
            fixture.ManifestPath,fixture.InputPath,fixture.Work,"2");
        Assert.Equal(2,result.Exit);
        Assert.Contains("resumed_generation=2",result.Out);
        Assert.Contains("rejected_checkpoints=1",result.Out);
        fixture.AssertCanonical(fixture.Work);
        Assert.Contains(Directory.EnumerateFiles(Path.Combine(fixture.Work,"evidence")),
            p=>File.ReadAllBytes(p).AsSpan().SequenceEqual(latestBytes));
    }

    [Theory]
    [InlineData("both-checkpoints")]
    [InlineData("both-prefixes-too-short")]
    [InlineData("missing-checkpoints")]
    public async Task NoEligibleCandidateFailsWithoutMutationOrFreshInitialization(string damage)
    {
        var fixture=new RecoveryFixture("no-candidate-"+damage);
        await RecoveryChild.KillAt(fixture,false,"DecisionWritten",8,"prepare");
        PreserveOriginals(fixture);
        foreach(var file in Directory.EnumerateFiles(fixture.Work,"checkpoint.?"))
        {
            if(damage=="missing-checkpoints")File.Delete(file);
            else if(damage=="both-checkpoints"){var bytes=File.ReadAllBytes(file);bytes[^1]^=1;File.WriteAllBytes(file,bytes);}
        }
        if(damage=="both-prefixes-too-short")
        {using var file=new FileStream(Path.Combine(fixture.Work,"accepted.jsonl"),FileMode.Open,FileAccess.Write);file.SetLength(0);}
        var before=RecoveryFixture.ActiveHashes(fixture.Work);
        var result=await RecoveryChild.Run(fixture,"refused","resume",
            fixture.ManifestPath,fixture.InputPath,fixture.Work,"2");
        Assert.Equal(1,result.Exit);
        Assert.Equal(before,RecoveryFixture.ActiveHashes(fixture.Work));
        Assert.False(File.Exists(Path.Combine(fixture.Work,"summary.json")));
    }

    [Theory]
    [InlineData("input")]
    [InlineData("manifest")]
    [InlineData("interval")]
    [InlineData("future-header")]
    [InlineData("foreign-policy-invalid-state")]
    [InlineData("ambiguous-policy")]
    public async Task ChangedIdentityOrFutureVersionCannotAuthorizeFallback(string change)
    {
        var fixture=new RecoveryFixture("identity-"+change);
        await RecoveryChild.KillAt(fixture,false,"DecisionWritten",8,"prepare");
        PreserveOriginals(fixture);
        if(change=="input")File.AppendAllText(fixture.InputPath," ");
        if(change=="manifest")File.AppendAllText(fixture.ManifestPath," ");
        if(change=="future-header")
        {var p=Path.Combine(fixture.Work,"checkpoint.1");var b=File.ReadAllBytes(p);b[7]=(byte)'2';File.WriteAllBytes(p,b);}
        if(change=="foreign-policy-invalid-state")
        {
            var checkpoint=fixture.ReadCheckpoint(1) with{PolicySha256=new string('0',64),Streams=[]};
            RecoveryCheckpoint.Publish(fixture.Work,checkpoint,new RecoveryHooks());
        }
        if(change=="ambiguous-policy")
        {
            var path=Path.Combine(fixture.Work,"checkpoint.1");
            var original=File.ReadAllBytes(path);
            var size=BinaryPrimitives.ReadInt32LittleEndian(original.AsSpan(16,4));
            var json=System.Text.Encoding.UTF8.GetString(original,20,size);
            var field="\"policySha256\":\""+fixture.PolicyHash+"\"";
            Assert.Equal(1,json.Split(field,StringSplitOptions.None).Length-1);
            json=json.Replace(field,field+","+field);
            Assert.Equal(2,json.Split(field,StringSplitOptions.None).Length-1);
            var payload=System.Text.Encoding.UTF8.GetBytes(json);
            var changed=new byte[20+payload.Length+32];
            original.AsSpan(0,20).CopyTo(changed);
            BinaryPrimitives.WriteInt32LittleEndian(changed.AsSpan(16,4),payload.Length);
            payload.CopyTo(changed,20);
            System.Security.Cryptography.SHA256.HashData(payload).CopyTo(changed,20+payload.Length);
            File.WriteAllBytes(path,changed);
        }
        var before=RecoveryFixture.ActiveHashes(fixture.Work);
        var result=await RecoveryChild.Run(fixture,"identity-refused","resume",
            fixture.ManifestPath,fixture.InputPath,fixture.Work,change=="interval"?"3":"2");
        Assert.Equal(1,result.Exit);
        Assert.Equal(before,RecoveryFixture.ActiveHashes(fixture.Work));
        Assert.False(File.Exists(Path.Combine(fixture.Work,"summary.json")));
    }

    [Fact]
    public async Task LiveWriterExcludedStaleLockReusableAndTwoStartRaceSafe()
    {
        var fixture=new RecoveryFixture("live-writer");
        await RecoveryChild.KillAt(fixture,false,"CheckpointCommitted",2,"lock-owner",async()=>
        {
            var before=Directory.EnumerateFiles(fixture.Work).ToDictionary(
                path=>Path.GetFileName(path)!,path=>new FileInfo(path).Length,StringComparer.Ordinal);
            var checkpoints=Directory.EnumerateFiles(fixture.Work,"checkpoint.?").ToDictionary(
                path=>Path.GetFileName(path)!,path=>Contract.Hash(File.ReadAllBytes(path)),StringComparer.Ordinal);
            Assert.Throws<IOException>(()=>new FileStream(Path.Combine(fixture.Work,"accepted.jsonl"),
                FileMode.Open,FileAccess.ReadWrite,FileShare.ReadWrite));
            var blocked=await RecoveryChild.Run(fixture,"second-writer","resume",
                fixture.ManifestPath,fixture.InputPath,fixture.Work,"2");
            Assert.Equal(1,blocked.Exit);
            var after=Directory.EnumerateFiles(fixture.Work).ToDictionary(
                path=>Path.GetFileName(path)!,path=>new FileInfo(path).Length,StringComparer.Ordinal);
            Assert.Equal(before,after);
            Assert.Equal(checkpoints,Directory.EnumerateFiles(fixture.Work,"checkpoint.?").ToDictionary(
                path=>Path.GetFileName(path)!,path=>Contract.Hash(File.ReadAllBytes(path)),StringComparer.Ordinal));
        });
        Assert.True(File.Exists(Path.Combine(fixture.Work,"run.lock")));
        var resumed=await RecoveryChild.Run(fixture,"stale-handle-released","resume",
            fixture.ManifestPath,fixture.InputPath,fixture.Work,"2");
        Assert.Equal(2,resumed.Exit);fixture.AssertCanonical(fixture.Work);
        var race=new RecoveryFixture("two-start-race");
        await RecoveryChild.RaceStarts(race);
        race.AssertCanonical(race.Work);
    }

    [Fact]
    public async Task InitializationAndExistingWorkdirNeverBecomeImplicitFreshResume()
    {
        var fixture=new RecoveryFixture("initialization-kill");
        await RecoveryChild.KillAt(fixture,false,"Owned",0,"initialization");
        var before=RecoveryFixture.ActiveHashes(fixture.Work);
        var resumed=await RecoveryChild.Run(fixture,"no-initial-state","resume",
            fixture.ManifestPath,fixture.InputPath,fixture.Work,"2");
        Assert.Equal(1,resumed.Exit);Assert.Equal(before,RecoveryFixture.ActiveHashes(fixture.Work));
        var started=await RecoveryChild.Run(fixture,"no-overwrite","start-resumable",
            fixture.ManifestPath,fixture.InputPath,fixture.Work,"2");
        Assert.Equal(1,started.Exit);Assert.Equal(before,RecoveryFixture.ActiveHashes(fixture.Work));
        File.Copy(Path.Combine(fixture.Work,"run.lock"),fixture.PathFor("original-lock.bin"));
        File.Delete(Path.Combine(fixture.Work,"run.lock"));
        var missing=await RecoveryChild.Run(fixture,"missing-lock","resume",
            fixture.ManifestPath,fixture.InputPath,fixture.Work,"2");
        Assert.Equal(1,missing.Exit);Assert.False(File.Exists(Path.Combine(fixture.Work,"run.lock")));
    }

    [Fact]
    public async Task PartialEvidenceFailureCannotTruncateAndRequiresManualArchive()
    {
        var fixture=new RecoveryFixture("evidence-copy-failure");
        await RecoveryChild.KillAt(fixture,false,"DecisionWritten",8,"prepare");
        using(var file=new FileStream(Path.Combine(fixture.Work,"accepted.jsonl"),FileMode.Append,FileAccess.Write))
            file.Write(new byte[16384]);
        var before=RecoveryFixture.ActiveHashes(fixture.Work);
        Assert.Throws<IOException>(()=>RecoveryRunner.Execute(fixture.ManifestPath,fixture.InputPath,
            fixture.Work,2,true,new RecoveryHooks((point,_) =>
            {if(point=="EvidenceCopyMid")throw new IOException("injected copy failure");})));
        Assert.Equal(before,RecoveryFixture.ActiveHashes(fixture.Work));
        var partial=Assert.Single(Directory.EnumerateFiles(Path.Combine(fixture.Work,"evidence"),"pending-*"));
        Assert.Equal(8192,new FileInfo(partial).Length);
        var refused=await RecoveryChild.Run(fixture,"partial-not-overwritten","resume",
            fixture.ManifestPath,fixture.InputPath,fixture.Work,"2");
        Assert.Equal(1,refused.Exit);
        Assert.Equal(before,RecoveryFixture.ActiveHashes(fixture.Work));
        Assert.Equal(8192,new FileInfo(partial).Length);
        File.Move(partial,fixture.PathFor("manually-archived-partial.bin"));
        var recovered=await RecoveryChild.Run(fixture,"after-explicit-manual-archive","resume",
            fixture.ManifestPath,fixture.InputPath,fixture.Work,"2");
        Assert.Equal(2,recovered.Exit);fixture.AssertCanonical(fixture.Work);
    }

    [Fact]
    public async Task EvidenceBudgetIncludesPendingFilesAndCannotMutateActiveData()
    {
        var fixture=new RecoveryFixture("evidence-budget");
        await RecoveryChild.KillAt(fixture,false,"DecisionWritten",8,"prepare");
        var evidence=Path.Combine(fixture.Work,"evidence");Directory.CreateDirectory(evidence);
        for(var i=0;i<RecoveryRules.EvidenceFiles;i++)File.WriteAllBytes(Path.Combine(evidence,$"retained-{i}.partial"),[1]);
        var before=RecoveryFixture.ActiveHashes(fixture.Work);
        var result=await RecoveryChild.Run(fixture,"budget-exhausted","resume",
            fixture.ManifestPath,fixture.InputPath,fixture.Work,"2");
        Assert.Equal(1,result.Exit);
        Assert.Equal(before,RecoveryFixture.ActiveHashes(fixture.Work));
        Assert.Equal(RecoveryRules.EvidenceFiles,Directory.EnumerateFiles(evidence).Count());
    }

    [Fact]
    public void ExistingContentAddressedEvidenceIsVerifiedReusedAndNeverOverwritten()
    {
        var fixture=new RecoveryFixture("evidence-reuse");
        var directory=fixture.PathFor("evidence-unit");
        byte[] data=[1,2,3,4,5];
        using var source=new MemoryStream(data);
        var first=RecoveryEvidence.Copy(directory,"test",source,0,data.Length,new RecoveryHooks(),0);
        var second=RecoveryEvidence.Copy(directory,"test",source,0,data.Length,new RecoveryHooks(),0);
        Assert.Equal(first,second);Assert.Single(Directory.EnumerateFiles(directory));
        var target=Path.Combine(directory,first.Blob);File.WriteAllBytes(target,[9,9,9,9,9]);
        Assert.Throws<InvalidDataException>(()=>RecoveryEvidence.Copy(directory,"test",source,0,data.Length,new RecoveryHooks(),0));
        Assert.Equal(new byte[]{9,9,9,9,9},File.ReadAllBytes(target));
    }

    private static void PreserveOriginals(RecoveryFixture fixture)
    {
        var destination=fixture.PathFor("damage-originals");Directory.CreateDirectory(destination);
        foreach(var path in Directory.EnumerateFiles(fixture.Work))
            File.Copy(path,Path.Combine(destination,Path.GetFileName(path)));
    }

    [Fact]
    public async Task ProductionCliStartAndCompletedResumeHaveExactCanonicalA2Output()
    {
        var fixture=new RecoveryFixture("production-cli");
        var started=await RecoveryChild.Run(fixture,"start","start-resumable",
            fixture.ManifestPath,fixture.InputPath,fixture.Work,"2");
        Assert.Equal(2,started.Exit);Assert.Equal("",started.Error);
        Assert.Contains("resumed_generation=-1",started.Out);
        fixture.AssertCanonical(fixture.Work);
        var resumed=await RecoveryChild.Run(fixture,"already-completed","resume",
            fixture.ManifestPath,fixture.InputPath,fixture.Work,"2");
        Assert.Equal(2,resumed.Exit);Assert.Equal("",resumed.Error);
        Assert.Contains("already_completed=True",resumed.Out);
        fixture.AssertCanonical(fixture.Work);
    }

    [Fact]
    public async Task EvidenceByteBudgetRefusesNewCopiesBeforeCanonicalMutation()
    {
        var fixture=new RecoveryFixture("evidence-byte-budget");
        await RecoveryChild.KillAt(fixture,false,"DecisionWritten",8,"prepare");
        var directory=Path.Combine(fixture.Work,"evidence");Directory.CreateDirectory(directory);
        var filler=Path.Combine(directory,"budget-control.partial");
        using(var file=new FileStream(filler,FileMode.CreateNew,FileAccess.Write,FileShare.Read))
            file.SetLength(RecoveryRules.EvidenceCap);
        var observed=new FileInfo(filler).Length;
        File.WriteAllText(fixture.PathFor("byte-budget-control.json"),
            System.Text.Json.JsonSerializer.Serialize(new{observed,limit=RecoveryRules.EvidenceCap,
                note="Synthetic size-only control; removed only after all expected refusal assertions pass"}));
        var before=RecoveryFixture.ActiveHashes(fixture.Work);
        var result=await RecoveryChild.Run(fixture,"byte-limit-refused","resume",
            fixture.ManifestPath,fixture.InputPath,fixture.Work,"2");
        Assert.Equal(1,result.Exit);
        Assert.Equal(before,RecoveryFixture.ActiveHashes(fixture.Work));
        Assert.Equal(RecoveryRules.EvidenceCap,new FileInfo(filler).Length);
        File.Delete(filler); // Only the successful test's manufactured zero-filled size control.
    }

}
