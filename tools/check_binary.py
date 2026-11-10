"""Check an extracted runtime package and launch its apphost directly, never dotnet."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import subprocess
import sys

def sha(path):return hashlib.sha256(path.read_bytes()).hexdigest()

def check(args):
    folder=Path(args.package).resolve()
    manifest=json.loads((folder/"BUILD-MANIFEST.json").read_text(encoding="utf-8"))
    assert manifest["sourceCommit"]==args.commit
    assert manifest["version"]=="0.6.1-preview.1" and manifest["bundledRuntime"]=="10.0.15"
    assert manifest["sdk"]=="10.0.404" and manifest["selfContained"] is True
    expected={row["path"] for row in manifest["files"]}|{"BUILD-MANIFEST.json"}
    assert {p.relative_to(folder).as_posix() for p in folder.rglob("*") if p.is_file()}==expected
    for row in manifest["files"]:
        path=folder/row["path"]
        assert path.resolve().is_relative_to(folder)
        assert path.stat().st_size==row["bytes"] and sha(path)==row["sha256"]
    for name in ("LICENSE.TXT","THIRD-PARTY-NOTICES.TXT"):assert (folder/"licenses/dotnet"/name).stat().st_size>0
    exe=folder/manifest["entrypoint"]
    if manifest["rid"]=="linux-x64":assert os.access(exe,os.X_OK)
    root=Path(args.work).resolve()
    if root.exists():raise RuntimeError("Fresh smoke work required")
    root.mkdir(parents=True)
    env=os.environ.copy()
    env["DOTNET_ROOT"]=str(root/"no installed dotnet")
    env["DOTNET_ROOT_X64"]=env["DOTNET_ROOT"]
    env["DOTNET_MULTILEVEL_LOOKUP"]="0"
    env["DOTNET_SYSTEM_IO_DISABLEFILELOCKING"]="0"
    env["PATH"]=""  # Direct apphost, no interpreter/runtime executable lookup.
    observations=[]
    def run(argv,exit_code):
        result=subprocess.run([str(exe),*argv],cwd=root,env=env,capture_output=True,timeout=30)
        result.stdout=result.stdout.decode("utf-8")
        result.stderr=result.stderr.decode("utf-8")
        assert result.returncode==exit_code,(argv,result.returncode,result.stderr)
        observations.append(dict(arguments=argv,exit=result.returncode,stdout=result.stdout,stderr=result.stderr))
        return result.stdout
    assert "Integrity 0.6.1-preview.1" in run(["--version"],0)
    assert ".NET 10.0.15" in observations[-1]["stdout"]
    out=root/"demo output with spaces"
    run(["demo",str(out),"unexpected"],1)
    assert not out.exists()
    text=run(["demo",str(out)],0)
    assert "NOT a crash demonstration" in text and "2 unattributed (UNKNOWN)" in text
    report=json.loads((out/"demo-summary.json").read_text(encoding="utf-8"))
    assert report["status"]=="expected_demo_outcomes_match" and report["crashDemonstrated"] is False
    before={p.relative_to(out).as_posix():sha(p) for p in out.rglob("*") if p.is_file()}
    run(["demo",str(out)],1)
    assert before=={p.relative_to(out).as_posix():sha(p) for p in out.rglob("*") if p.is_file()}
    for name in ("accepted.jsonl","quarantine.jsonl","summary.json"):
        assert (out/"sequence-result"/name).read_bytes()==(out/"recovery-result"/name).read_bytes()
    run(["validate",str(out/"a1-input/manifest.json"),str(out/"a1-input/input.jsonl"),str(root/"validation output")],2)
    # A healthy one-record synthetic validation still returns0.
    run(["generate",str(root/"clean input"),"1"],0)
    run(["validate",str(root/"clean input/manifest.json"),str(root/"clean input/input.jsonl"),str(root/"clean output")],0)
    csv_root=root/"csv generated demo"
    assert "6 rows; 2 accepted; 4 quarantined" in run(["csv-demo",str(csv_root)],0)
    generated=json.loads((csv_root/"result/summary.json").read_text(encoding="utf-8"))
    assert generated["policy"]["sourceKind"]=="GENERATED_EXAMPLE_DECLARED"
    csv_result=root/"csv user result"
    run(["validate-csv",str(csv_root/"example.csv"),str(csv_result),"2","0"],2)
    csv_report=json.loads((csv_result/"summary.json").read_text(encoding="utf-8"))
    assert (csv_report["records"],csv_report["accepted"],csv_report["quarantined"])==(6,2,4)
    assert csv_report["marketProvenance"]=="UNVERIFIED" and csv_report["ordering"]=="NOT_CHECKED"
    assert csv_report["policy"]["sourceKind"]=="USER_SUPPLIED_UNVERIFIED"
    before={p.name:sha(p) for p in csv_result.iterdir() if p.is_file()}
    run(["validate-csv",str(csv_root/"example.csv"),str(csv_result),"2","0"],1)
    assert before=={p.name:sha(p) for p in csv_result.iterdir() if p.is_file()}
    clean=root/"clean.csv"
    header="timestamp,open,high,low,close,volume\n"
    row="2026-01-01T00:00:00Z,-2.00,-1.00,-3.00,-2.50,0\n"
    clean.write_bytes((header+row).encode("utf-8"))
    run(["validate-csv",str(clean),str(root/"csv clean result"),"2","0"],0)
    decision=json.loads((root/"csv clean result/accepted.jsonl").read_text())
    assert decision["record"]["closeScaled"]=="-250"
    bad_args=root/"bad csv args result"
    run(["validate-csv",str(clean),str(bad_args),"9","0"],1)
    assert not bad_args.exists()
    # Preserve actual OS newline bytes when decoding subprocess output above.
    syntax="usage: Integrity validate-csv <input.csv> <new-output> <price-decimals:0..8> <volume-decimals:0..8>"
    def usage(argv, first):
        assert run(argv,1)==""
        assert observations[-1]["stderr"]==first+os.linesep+syntax+os.linesep
        assert not bad_args.exists()
    count="usage:CSV_ARGUMENT_COUNT; expected 4 arguments after validate-csv"
    price="usage:CSV_PRICE_SCALE_INVALID; price-decimals must be an unsigned integer in 0..8"
    volume="usage:CSV_VOLUME_SCALE_INVALID; volume-decimals must be an unsigned integer in 0..8"
    missing=root/"private marker missing input.csv"
    for argv in (["validate-csv"], ["validate-csv",str(missing)], ["validate-csv",str(missing),str(bad_args),"bad"], ["validate-csv",str(missing),str(bad_args),"bad","bad","extra"]):
        usage(argv,count)
    for token in ("9","-1","1.0","x","","+2"," 2","2 ","99999999999999999999999999999","\u06f2"):
        usage(["validate-csv",str(missing),str(bad_args),token,"0"],price)
        usage(["validate-csv",str(missing),str(bad_args),"2",token],volume)
        usage(["validate-csv",str(missing),str(bad_args),token,token],price)
    sentinel=root/"existing usage output";sentinel.mkdir()
    (sentinel/"keep.bin").write_bytes(bytes([0,255,10,13]))
    usage(["validate-csv",str(missing),str(sentinel),"x","0"],price)
    assert {p.name:p.read_bytes() for p in sentinel.iterdir()}=={"keep.bin":bytes([0,255,10,13])}
    run(["validate-csv",str(clean),str(root/"leading zero result"),"02","00"],0)
    for scales,expected in ((("0","8"),"-2"),(("8","0"),"-200000000")):
        boundary=root/("boundary "+scales[0]+".csv")
        boundary.write_bytes((header+"2026-01-01T00:00:00Z,-2,-1,-3,-2,0\n").encode("utf-8"))
        target=root/("boundary result "+scales[0])
        run(["validate-csv",str(boundary),str(target),*scales],0)
        assert json.loads((target/"accepted.jsonl").read_text())["record"]["closeScaled"]==expected
    precision=root/"precision.csv"
    precision.write_bytes((header+"2026-01-01T00:01:00Z,1.230,2,0,1,1\n").encode("utf-8"))
    precision_out=root/"precision result"
    run(["validate-csv",str(precision),str(precision_out),"2","0"],2)
    rejection=json.loads((precision_out/"quarantine.jsonl").read_text())
    assert (rejection["reason"],rejection["field"],rejection["record"])==("NUMBER_PRECISION_EXCEEDED","open",None)
    assert json.loads((precision_out/"summary.json").read_text())["quarantined"]==1
    global_usage=None
    for argv in ([],["Validate-csv"],["validate-csv-extra"],["validate-csv "],["--help"]):
        assert run(argv,1)==""
        current=observations[-1]["stderr"]
        assert current.startswith(syntax+" | csv-demo") and current.endswith(os.linesep)
        if global_usage is None: global_usage=current
        else: assert current==global_usage
    bad_header=root/"bad header.csv";bad_header.write_bytes(b"time,open,high,low,close,volume\n")
    bad_out=root/"bad header output"
    run(["validate-csv",str(bad_header),str(bad_out),"2","0"],1)
    assert "CSV_HEADER_INVALID" in observations[-1]["stderr"] and not bad_out.exists()
    empty=root/"header only.csv";empty.write_bytes(header.encode("utf-8"))
    empty_out=root/"no data output"
    run(["validate-csv",str(empty),str(empty_out),"2","0"],1)
    assert "CSV_NO_DATA" in observations[-1]["stderr"] and not empty_out.exists()
    quote=root/"unclosed quote.csv";quote.write_bytes((header+row+'2026-01-01T00:01:00Z,"1\n'+row).encode("utf-8"))
    quote_out=root/"partial csv output"
    run(["validate-csv",str(quote),str(quote_out),"2","0"],1)
    assert "CSV_UNCLOSED_QUOTE" in observations[-1]["stderr"]
    assert not (quote_out/"summary.json").exists()
    assert observations[-1]["stdout"]==""
    assert observations[-1]["stderr"]=="fatal:CSV_UNCLOSED_QUOTE line=3; no completed result may be inferred from partial files"+os.linesep
    assert len((quote_out/"accepted.jsonl").read_text(encoding="utf-8").splitlines())==1
    bom=root/"embedded bom.csv"
    bom.write_bytes((header+row+"2026-01-01T00:01:00Z,1,2,0,1,\ufeff1\n").encode("utf-8"))
    bom_out=root/"embedded bom output"
    run(["validate-csv",str(bom),str(bom_out),"2","0"],1)
    assert "CSV_UNEXPECTED_BOM" in observations[-1]["stderr"]
    assert not (bom_out/"summary.json").exists()
    assert len((bom_out/"accepted.jsonl").read_text(encoding="utf-8").splitlines())==1
    result=dict(status="EXTRACTED_APPHOST_DEMO_AND_CONTRACT_PASS",commit=args.commit,rid=manifest["rid"],
        sdkInstalledHostNotExcluded=True,proof="Inspected self-contained config/runtime and direct apphost launch with empty PATH and nonexistent DOTNET_ROOT; host itself may have SDK installed.",
        packagePathHasSpaces=" " in str(folder),cwdHasSpaces=" " in str(root),
        observations=observations,canonicalSha256=report["canonicalSha256"])
    assert result["packagePathHasSpaces"] and result["cwdHasSpaces"]
    Path(args.receipt).write_text(json.dumps(result,indent=2)+"\n",encoding="utf-8",newline="\n")
    print(json.dumps(dict(status=result["status"],rid=result["rid"],children=len(observations))))

if __name__=="__main__":
    p=argparse.ArgumentParser(description=__doc__)
    for arg in ("package","work","commit","receipt"):p.add_argument("--"+arg,required=True)
    check(p.parse_args())
