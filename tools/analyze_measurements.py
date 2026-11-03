"""Independent receipt arithmetic; does not import the measurement driver."""
import json
import statistics
from pathlib import Path
import sys
root=Path(sys.argv[1]);out=Path(sys.argv[2])
r=json.loads((root/"results.json").read_text(encoding="utf-8"))
assert r["status"]=="COMPLETED"
assert len(r["samples"])==21
assert r["source"]==r["sourceAfter"]
assert r["elapsedSeconds"]<=600
assert r["artifactBytesBeforeFinalReceipt"]<2*1024**3
phase_counts={phase:sum(s["phase"]==phase for s in r["samples"])
              for phase in ("warmup","measured","stress","hash_reference")}
assert phase_counts==dict(warmup=4,measured=12,stress=2,hash_reference=3)
groups={}
for s in r["samples"]:
    v=s["metrics"];seconds=v["operationTicks"]/v["clockFrequency"]
    assert seconds>0
    if s["mode"]!="HASH":
        n=s["records"]
        repeats=sum(i%37==0 for i in range(1,n+1))
        bad=sum(i%17==0 for i in range(1,n-repeats+1))
        assert v["records"]==n and v["accepted"]==n-repeats-bad and v["quarantined"]==repeats+bad
        assert v["unattributed"]==v["uncompared"]==0
    if s["phase"] in ("measured","stress"):
        key=f'{s["phase"]}-{s["records"]}-{s["mode"]}'
        size=r["workloads"][str(s["records"])]["bytes"]
        groups.setdefault(key,[]).append(dict(index=s["index"],operationSeconds=seconds,
            parentSeconds=s["parentWallNanoseconds"]/1e9,recordsPerSecond=s["records"]/seconds,
            sourceMiBPerSecond=size/2**20/seconds,cpuSeconds=v["cpuTicks"]/v["cpuTickFrequency"],
            managedAllocatedBytes=v["allocatedBytes"],managedBytesPerPhysicalRecord=v["allocatedBytes"]/s["records"],
            lifetimePeakMiB=None if v["lifetimePeakWorkingSetBytes"] is None else v["lifetimePeakWorkingSetBytes"]/2**20))
for n in (10000,100000,250000):
    samples=[s for s in r["samples"] if s["records"]==n and s["mode"]!="HASH"]
    assert all(s["canonicalSha256"]==samples[0]["canonicalSha256"] for s in samples)
summary={}
for key, rows in groups.items():
    fields={name:[v[name] for v in rows if v[name] is not None] for name in rows[0] if name!="index"}
    summary[key]=dict(samples=len(rows),raw=rows,stats={name:None if not values else dict(
        min=min(values),median=statistics.median(values),max=max(values)) for name,values in fields.items()})
result=dict(status="INDEPENDENT_ARITHMETIC_PASS",phaseCounts=phase_counts,
    measuredCommit=r["source"]["commit"],groups=summary)
reference=root/"summary.json"
if reference.exists():
    assert result==json.loads(reference.read_text(encoding="utf-8")), "Published summary mismatch"
out.parent.mkdir(parents=True,exist_ok=True)
out.write_text(json.dumps(result,indent=2)+"\n",encoding="utf-8")
print(json.dumps({k:dict(n=v["samples"],seconds=v["stats"]["operationSeconds"],
    allocationBytes=v["stats"]["managedAllocatedBytes"],peakMiB=v["stats"]["lifetimePeakMiB"])
    for k,v in summary.items()},indent=2))
