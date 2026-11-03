using System.Text.Json;
using MarketDataIntegrity;
using Xunit;

namespace Integrity.Tests;

public sealed class RecoveryBoundsTests
{
    [Fact]
    public void NormalizedNumericPayloadRoundTripAndRestoreReplacementAreExact()
    {
        var source=new string('S',32);var instrument=new string('I',32);var epoch=new string('E',32);
        var manifest=new SequenceManifest(2,"synthetic-roundtrip",true,512,1,new string('0',64),2,
            [new(source,instrument,epoch,long.MaxValue-1)]);
        var bad=new Candle(source,instrument,epoch,long.MaxValue-1,"9999-12-31T23:59:59Z",
            Contract.MaximumNumber,1,1,1,Contract.MaximumNumber);
        var good=bad with{Sequence=long.MaxValue,High=Contract.MaximumNumber,
            Low=Contract.MaximumNumber,Close=Contract.MaximumNumber};
        var engine=new SequenceEngine(manifest);
        Assert.Equal("OHLC_INCONSISTENT",engine.Apply(bad,"OHLC_INCONSISTENT"));
        Assert.Equal("VALID_RECORD",engine.Apply(good,"VALID_RECORD"));
        var bytes=JsonSerializer.SerializeToUtf8Bytes(engine.ExportRecovery(),Contract.Json);
        var restored=JsonSerializer.Deserialize<RecoveryStreamState[]>(bytes,Contract.Json)!;
        var clone=new SequenceEngine(manifest);
        clone.Apply(bad,"OHLC_INCONSISTENT");
        clone.RestoreRecovery(manifest,restored);
        Assert.Equal(new[]{bad,good},Assert.Single(clone.ExportRecovery()).Window);
        Assert.Equal("DUPLICATE_IN_WINDOW",clone.Apply(bad,"OHLC_INCONSISTENT"));
        Assert.Equal("DUPLICATE_IN_WINDOW",clone.Apply(good,"VALID_RECORD"));
        Assert.True(Assert.Single(clone.Snapshot()).RangeExhausted);
        Assert.Equal("CONFLICT_IN_WINDOW",clone.Apply(bad with{Volume=Contract.MaximumNumber-1},"OHLC_INCONSISTENT"));
    }

    [Fact]
    public void ConservativeSerializerBoundsFitVersionedDecisionCheckpointAndSummaryCaps()
    {
        var fixture=new RecoveryFixture("serializer-bounds",true);
        var source=new string('S',32);var epoch=new string('E',32);var hash=new string('0',64);
        string Instrument(int i)=>i.ToString(System.Globalization.CultureInfo.InvariantCulture).PadLeft(32,'I');
        var worstRow=new Candle(source,Instrument(0),epoch,long.MaxValue,"9999-12-31T23:59:59Z",
            Contract.MaximumNumber,1,1,1,Contract.MaximumNumber);
        var decision=new SequenceDecision(long.MaxValue,long.MaxValue,long.MaxValue,hash,
            "CONFLICT_IN_WINDOW","OHLC_INCONSISTENT",worstRow);
        var decisionBytes=JsonSerializer.SerializeToUtf8Bytes(decision,Contract.Json).Length+1;
        Assert.True(decisionBytes<=RecoveryRules.DecisionCap);

        var baselines=Enumerable.Range(0,256).Select(i=>new StreamBaseline(
            source,Instrument(i),epoch,long.MaxValue-255)).ToArray();
        var manifest=new SequenceManifest(2,new string('D',32),true,1_048_576,256,hash,256,baselines);
        var manifestBytes=JsonSerializer.SerializeToUtf8Bytes(manifest,Contract.Json);
        _=SequenceContract.Read(manifestBytes); // Actual public manifest-byte limit also applies.
        var streams=baselines.Select(b=>new RecoveryStreamState(b,long.MaxValue,null,
            Enumerable.Range(0,256).Select(i=>new Candle(b.Source,b.Instrument,b.Epoch,
                b.FirstSequence+i,"9999-12-31T23:59:59Z",Contract.MaximumNumber,
                Contract.MaximumNumber,Contract.MaximumNumber,Contract.MaximumNumber,
                Contract.MaximumNumber)).ToArray())).ToArray();
        var manifestHash=Contract.Hash(manifestBytes);var policy=RecoveryRules.PolicyHash(manifestHash,1);
        var checkpoint=new RecoveryCheckpointData(1,1,policy,manifestHash,hash,long.MaxValue,long.MaxValue,
            65537,true,65536,65536,0,0,0,new(StringComparer.Ordinal){{"VALID_RECORD",65536}},streams,
            new(long.MaxValue,hash),new(0,Contract.Hash([])));
        RecoveryRules.Validate(checkpoint,manifest,manifestHash,policy,long.MaxValue);
        var directory=fixture.PathFor("maximum-window-codec");Directory.CreateDirectory(directory);
        RecoveryCheckpoint.Publish(directory,checkpoint,new RecoveryHooks());
        var decoded=RecoveryCheckpoint.Read(Path.Combine(directory,"checkpoint.1"),manifest,
            manifestHash,policy,long.MaxValue);
        RecoveryRules.Validate(decoded,manifest,manifestHash,policy,long.MaxValue);
        Assert.Equal(65536,decoded.Streams.Sum(s=>s.Window.Length));
        var checkpointBytes=new FileInfo(Path.Combine(directory,"checkpoint.1")).Length;
        Assert.True(checkpointBytes<=RecoveryRules.CheckpointCap+RecoveryCheckpoint.HeaderBytes+32L);

        // Conservative K/counter shapes over-approximate physical-input constraints;
        // they are byte-budget proofs, not a claim that this huge input was executed.
        var snapshots=Enumerable.Range(0,4096).Select(i=>new StreamSnapshot(
            source,Instrument(i),epoch,long.MaxValue-15,long.MaxValue,null,true,
            "CONFLICT_IN_WINDOW",16)).ToArray();
        var wide=new SequenceSummary(2,"completed",new string('D',32),hash,hash,
            69632,65536,4096,1_048_576,4096,16,"OBSERVED_PREFIX_ONLY",0,0,snapshots,
            new(StringComparer.Ordinal){{"CONFLICT_IN_WINDOW",4096},{"VALID_RECORD",65536}});
        var wideBytes=RecoveryRunner.SummaryBytes(wide).Length;
        var counters=wide with{Records=long.MaxValue-1,Accepted=long.MaxValue-1,Quarantined=0,
            UnattributedRecords=long.MaxValue-1,UncomparedRecords=long.MaxValue-1};
        var counterBytes=RecoveryRunner.SummaryBytes(counters).Length;
        Assert.True(Math.Max(wideBytes,counterBytes)<=RecoveryRules.SummaryCap);
        File.WriteAllText(fixture.PathFor("serializer-cap-proof.json"),JsonSerializer.Serialize(new
        {
            manifestBytes=manifestBytes.Length,decisionBytes,decisionCap=RecoveryRules.DecisionCap,
            retainedWindowRecords=65536,checkpointBytes,checkpointCap=RecoveryRules.CheckpointCap,
            summary4096StreamsBytes=wideBytes,maximumCounterShapeBytes=counterBytes,
            summaryCap=RecoveryRules.SummaryCap,
            scope="Serialization bounds only; conservative field shapes, not a historical data/performance run"
        },new JsonSerializerOptions{WriteIndented=true}));
    }
    [Fact]
    public void PublishedA2SyntheticCanonicalBytesRemainPinned()
    {
        var fixture=new RecoveryFixture("a2-byte-pins",true);
        var data=fixture.PathFor("a2-data");
        SequenceSynthetic.Generate(data);
        var output=fixture.PathFor("a2-output");
        SequenceValidator.Run(Path.Combine(data,"manifest.json"),Path.Combine(data,"input.jsonl"),output);
        var expected=new Dictionary<string,string>
        {
            [Path.Combine(data,"input.jsonl")]="6b0fd78d56c62d16ff1351b1efd49441662923304d57b3750336bc6a54f6d311",
            [Path.Combine(data,"manifest.json")]="88d56ccebae70778845082f654b50ad7c4520791cad540a437e9594d2756f6df",
            [Path.Combine(output,"accepted.jsonl")]="5dc6e030b0c09c737a7fa32782773f86711406f0302fb117e3470a665fa2b7ec",
            [Path.Combine(output,"quarantine.jsonl")]="d9fbe7674dd85125264a794bd8254c9977f91b70f94da5267bca9b23dabe673b",
            [Path.Combine(output,"summary.json")]="39b086a2c9f3e4003f7c4b1e17d24b255af71fcad7e4bbea9de35633da08f9c1"
        };
        foreach(var item in expected)Assert.Equal(item.Value,Contract.Hash(File.ReadAllBytes(item.Key)));
    }

}
