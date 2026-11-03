"""Package one self-contained directory; run only on its matching OS after publish."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import platform
import re
import shutil
import subprocess
import tarfile
import zipfile

VERSION="0.5.0-preview.1"
RUNTIME="10.0.14"

def sha(path):
    h=hashlib.sha256()
    with path.open("rb") as f:
        for chunk in iter(lambda:f.read(65536),b""):h.update(chunk)
    return h.hexdigest()

def package(args):
    repo=Path(__file__).resolve().parents[1]
    if subprocess.check_output(["git","-C",str(repo),"status","--porcelain"],text=True).strip():
        raise RuntimeError("Clean committed source required for package provenance")
    if subprocess.check_output(["git","-C",str(repo),"rev-parse","HEAD"],text=True).strip()!=args.commit:
        raise RuntimeError("Package commit must match source checkout")
    published=Path(args.publish).resolve()
    output=Path(args.output).resolve()
    expected_os="Windows" if args.rid=="win-x64" else "Linux"
    if platform.system()!=expected_os:raise RuntimeError("Package on matching OS")
    if not re.fullmatch("[0-9a-f]{40}",args.commit):raise RuntimeError("Exact source commit required")
    entry="Integrity.exe" if args.rid=="win-x64" else "Integrity"
    native=["hostfxr.dll","coreclr.dll"] if args.rid=="win-x64" else ["libhostfxr.so","libcoreclr.so"]
    for name in [entry,"Integrity.dll","System.Private.CoreLib.dll",*native]:
        if not (published/name).is_file():raise RuntimeError("Missing self-contained runtime")
    config=json.loads((published/"Integrity.runtimeconfig.json").read_text(encoding="utf-8"))
    options=config["runtimeOptions"]
    if "framework" in options or "frameworks" in options:raise RuntimeError("Framework-dependent config")
    if not any(f["name"]=="Microsoft.NETCore.App" and f["version"]==RUNTIME for f in options["includedFrameworks"]):
        raise RuntimeError("Unexpected bundled runtime")
    name=f"market-data-integrity-lab-v{VERSION}-{args.rid}"
    stage=output/name
    if stage.exists():raise RuntimeError("New package stage required")
    output.mkdir(parents=True,exist_ok=True)
    shutil.copytree(published,stage)
    # Publish BCL app/runtime only; never include test assemblies or probe.
    if any(re.search("xunit|RecoveryProbe|Integrity.Tests|Measurements",p.name,re.I) for p in stage.rglob("*")):
        raise RuntimeError("Unexpected test/measurement payload")
    if any(p.suffix.lower()==".pdb" for p in stage.rglob("*")):
        raise RuntimeError("Debug symbols must not ship in runtime bundle")
    for filename in ("LICENSE","NOTICE.md"):shutil.copyfile(repo/filename,stage/filename)
    licenses=stage/"licenses/dotnet";licenses.mkdir(parents=True)
    pack=Path(args.runtime_pack)
    for filename in ("LICENSE.TXT","THIRD-PARTY-NOTICES.TXT"):
        matches=[p for p in pack.iterdir() if p.is_file() and p.name.upper()==filename]
        if len(matches)!=1:raise RuntimeError("Runtime license/notices required")
        shutil.copyfile(matches[0],licenses/filename)
    command=".\\Integrity.exe" if args.rid=="win-x64" else "./Integrity"
    quick=f"""Market Data Integrity Lab v{VERSION} ({args.rid})
Extract the entire directory; keep the executable beside its bundled runtime.
From this directory run:
  {command} --version
  {command} demo "my demo output"

No .NET SDK/runtime or Python installation is required to run this bundle.
A1:34 records,32 accepted,2 quarantined. A2:14 records,4 accepted,10 quarantined.
A3:completed-run resume generation7,3 canonical files equal to A2.
Demo exit0 means expected outcomes matched, not all records are valid.
This is NOT a crash demonstration; no process is killed.
Existing validation commands retain exit0 clean,2 quarantined,1 fatal/usage.
Use a new output directory; existing directories/files are refused without overwrite.
On failure retain partial files; do not infer completion from them.
Only synthetic inputs are supported. No network or account service is used.
Self-contained still requires native OS prerequisites. Linux target:glibc x64,
tested on Ubuntu24.04 CI; not Alpine/musl,ARM,macOS,or every Linux distribution.
Windows target:x64,with actual tested host details in CI/delivery receipts.
Unsigned developer preview. Recovery requires enabled .NET file locking,
cooperating writers and supported local filesystem behavior; not power-loss safety.
Performance study belongs to historical d9ece502/v0.4,not these B1 binaries.
Docs:https://github.com/pouriajamali/market-data-integrity-lab
"""
    (stage/"QUICKSTART.txt").write_text(quick,encoding="utf-8",newline="\n")
    files=[]
    for p in sorted(stage.rglob("*")):
        if p.is_symlink():raise RuntimeError("No symlinks in package")
        if p.is_file():files.append(dict(path=p.relative_to(stage).as_posix(),bytes=p.stat().st_size,sha256=sha(p)))
    manifest=dict(formatVersion=1,version=VERSION,rid=args.rid,sourceCommit=args.commit,
        sdk=args.sdk,bundledRuntime=RUNTIME,selfContained=True,entrypoint=entry,
        buildHost=dict(os=platform.system(),osVersion=platform.version(),architecture=platform.machine()),
        runtimeConfig=config,files=files,inventoryScope="Every payload file except this manifest; outer release checksum covers manifest too.")
    (stage/"BUILD-MANIFEST.json").write_text(json.dumps(manifest,indent=2)+"\n",encoding="utf-8",newline="\n")
    archive=output/(name+(".zip" if args.rid=="win-x64" else ".tar.gz"))
    if archive.exists():raise RuntimeError("Archive exists")
    if args.rid=="win-x64":
        with zipfile.ZipFile(archive,"x",zipfile.ZIP_DEFLATED,compresslevel=9) as z:
            for p in sorted(stage.rglob("*")):
                if p.is_file():z.write(p,p.relative_to(output).as_posix())
    else:
        with tarfile.open(archive,"x:gz") as t:t.add(stage,arcname=name)
    print(json.dumps(dict(archive=str(archive),sha256=sha(archive),bytes=archive.stat().st_size,payloadFiles=len(files))))

if __name__=="__main__":
    parser=argparse.ArgumentParser(description=__doc__)
    for arg in ("publish","output","runtime-pack","commit","sdk"):parser.add_argument("--"+arg,required=True)
    parser.add_argument("--rid",choices=("win-x64","linux-x64"),required=True)
    package(parser.parse_args())
