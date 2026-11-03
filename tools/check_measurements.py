"""Small correctness check, not a performance study. Build Measurements first."""
import argparse
import json
import tempfile
from pathlib import Path
import run_measurements as m

def run(dotnet):
    repo = Path(__file__).resolve().parents[1]
    worker = repo/"tools/Measurements/bin/Release/net10.0/Measurements.dll"
    with tempfile.TemporaryDirectory() as tmp:
        root=Path(tmp);guard=m.Guard(root,seconds=60,budget=64*1024**2)
        for n in (74,1037):
            data=m.generate(root,n,guard)
            reference=None
            for mode in ("A2","A3","HASH"):
                folder=root/f"{n}-{mode}";folder.mkdir()
                guard.child([dotnet,str(worker),mode,str(root/str(n)/"manifest.json"),
                             str(root/str(n)/"input.jsonl"),str(folder/"output")],
                            repo,folder/"metrics.json",folder/"stderr.txt")
                value=json.loads((folder/"metrics.json").read_text())
                m.metrics_check(value,mode,n,data["inputSha256"])
                if mode=="A2":reference=folder/"output"
                if mode!="HASH":
                    for name in m.CANONICAL:m.equal_files(folder/"output"/name,reference/name,guard)
        print("Tiny worker correctness: 6 children, recipe counters and all canonical bytes PASS; not study samples.")

if __name__=="__main__":
    parser=argparse.ArgumentParser(description=__doc__);parser.add_argument("--dotnet",required=True)
    run(parser.parse_args().dotnet)
