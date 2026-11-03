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
    assert manifest["version"]=="0.5.0-preview.1" and manifest["bundledRuntime"]=="10.0.14"
    assert manifest["sdk"]=="10.0.403" and manifest["selfContained"] is True
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
        result=subprocess.run([str(exe),*argv],cwd=root,env=env,capture_output=True,text=True,timeout=30)
        assert result.returncode==exit_code,(argv,result.returncode,result.stderr)
        observations.append(dict(arguments=argv,exit=result.returncode,stdout=result.stdout,stderr=result.stderr))
        return result.stdout
    assert "Integrity 0.5.0-preview.1" in run(["--version"],0)
    assert ".NET 10.0.14" in observations[-1]["stdout"]
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
