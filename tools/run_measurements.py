"""Finite A4 study. Python standard library only; build beforehand. See docs/MEASUREMENTS.md."""
import argparse
import ctypes
import hashlib
import json
import os
from pathlib import Path
import platform
import shutil
import subprocess
import sys
import time

LIMIT_BYTES = 2 * 1024**3
LIMIT_SECONDS = 600
CANONICAL = ("accepted.jsonl", "quarantine.jsonl", "summary.json")
COUNTS = (10000, 100000, 250000)

def dump(path, value):
    path.write_text(json.dumps(value, indent=2, sort_keys=True) + "\n", encoding="utf-8")

def digest(path):
    value = hashlib.sha256()
    with path.open("rb") as source:
        for data in iter(lambda: source.read(65536), b""):
            value.update(data)
    return value.hexdigest()

def expected(n):
    duplicate = n // 37
    consumed = n - duplicate
    bad = consumed // 17
    return dict(records=n, accepted=consumed-bad, quarantined=duplicate+bad,
                unattributed=0, uncompared=0)

def recipe(n):
    sequences = [0] * 16
    previous = None
    consumed = 0
    for physical in range(1, n+1):
        if physical % 37 == 0:
            yield previous
            continue
        consumed += 1
        stream = (consumed-1) % 16
        sequences[stream] += 1
        price = 100000 + consumed % 100
        previous = dict(source="synthetic", instrument=f"S{stream:02d}", epoch="bench",
                        sequence=sequences[stream], timestamp="2026-01-01T00:00:00Z",
                        open=price+3 if consumed % 17 == 0 else price,
                        high=price+2, low=price-2, close=price+1, volume=consumed % 300)
        yield previous

class Guard:
    def __init__(self, root, seconds=LIMIT_SECONDS, budget=LIMIT_BYTES):
        self.root, self.deadline, self.budget = root, time.monotonic()+seconds, budget
    def remaining(self):
        value = self.deadline-time.monotonic()
        if value <= 0: raise RuntimeError("GLOBAL_DEADLINE")
        return value
    def size(self):
        return sum(p.stat().st_size for p in self.root.rglob("*") if p.is_file())
    def check(self, reserve=0):
        self.remaining()
        if self.size()+reserve > self.budget: raise RuntimeError("ARTIFACT_BUDGET")
    def child(self, argv, cwd, stdout, stderr):
        self.check()
        start = time.perf_counter_ns()
        # Direct dotnet worker: no child shell, grandchild process or background server.
        with stdout.open("xb") as out, stderr.open("xb") as err:
            child = subprocess.Popen(argv, cwd=cwd, stdout=out, stderr=err, env=os.environ.copy())
            try:
                while True:
                    self.check()
                    try:
                        child.wait(timeout=min(0.1, self.remaining()))
                        break
                    except subprocess.TimeoutExpired:
                        continue
                end = time.perf_counter_ns()
                self.check()
                if child.returncode != 0: raise RuntimeError("UNEXPECTED_CHILD_EXIT")
                return end-start
            finally:
                if child.poll() is None:
                    child.kill()
                    child.wait(timeout=10)

def generate(root, n, guard):
    guard.check(n*512 + 65536)
    folder = root / str(n)
    folder.mkdir()
    source = folder/"input.jsonl"
    with source.open("xb") as output:
        for i, row in enumerate(recipe(n)):
            output.write((json.dumps(row, separators=(",", ":"))+"\n").encode("utf-8"))
            if i % 1024 == 0: guard.check()
    manifest = dict(formatVersion=2, datasetId=f"a4-mixture-{n}", synthetic=True,
                    maxRecordBytes=4096, maxStreams=16, inputSha256=digest(source),
                    sequenceWindow=64, baselines=[dict(source="synthetic",
                    instrument=f"S{i:02d}", epoch="bench", firstSequence=1) for i in range(16)])
    dump(folder/"manifest.json", manifest)
    guard.check()
    return dict(records=n, bytes=source.stat().st_size, inputSha256=digest(source),
                manifestSha256=digest(folder/"manifest.json"), expected=expected(n))

def schedule():
    values = [(n, mode, "warmup", 0) for n in COUNTS[:2] for mode in ("A2", "A3")]
    for n in COUNTS[:2]:
        for round_no, order in enumerate((("A2","A3"),("A3","A2"),("A2","A3")), 1):
            values.extend((n, mode, "measured", round_no) for mode in order)
    values.extend((COUNTS[2], mode, "stress", 1) for mode in ("A2", "A3"))
    values.extend((n, "HASH", "hash_reference", 1) for n in COUNTS)
    return values

def reservation(n, mode):
    return n*2048 + 5*1024**2 + (3*(64*1024**2+52) if mode == "A3" else 0)

def metrics_check(value, mode, n, input_digest):
    if value["mode"] != mode: raise RuntimeError("MODE_MISMATCH")
    for key in ("operationTicks", "clockFrequency", "cpuTickFrequency"):
        if type(value[key]) is not int or value[key] <= 0: raise RuntimeError("INVALID_METRIC")
    for key in ("allocatedBytes", "cpuTicks"):
        if type(value[key]) is not int or value[key] < 0: raise RuntimeError("INVALID_METRIC")
    peak = value["lifetimePeakWorkingSetBytes"]
    if peak is None:
        if value["peakStatus"] != "UNKNOWN": raise RuntimeError("INVALID_PEAK")
    elif type(peak) is not int or peak <= 0 or value["peakStatus"] != "OBSERVED":
        raise RuntimeError("INVALID_PEAK")
    if mode == "HASH":
        if value["digest"] != input_digest: raise RuntimeError("HASH_REFERENCE_MISMATCH")
    else:
        for key, count in expected(n).items():
            if value[key] != count: raise RuntimeError("COUNTER_MISMATCH")

def equal_files(left, right, guard):
    with left.open("rb") as a, right.open("rb") as b:
        while True:
            guard.remaining()
            x, y = a.read(65536), b.read(65536)
            if x != y: raise RuntimeError("CANONICAL_BYTE_MISMATCH")
            if not x: return

def git(repo, *args):
    return subprocess.check_output(["git", "-C", str(repo), *args], text=True,
                                   timeout=10).strip()

def fingerprint(repo):
    if git(repo, "status", "--porcelain"): raise RuntimeError("DIRTY_SOURCE")
    paths = git(repo, "ls-files").splitlines()
    return dict(commit=git(repo, "rev-parse", "HEAD"), tree=git(repo, "rev-parse", "HEAD^{tree}"),
                files={name:digest(repo/name) for name in paths})

def host():
    result = dict(os=platform.system(), osVersion=platform.version(),
                  architecture=platform.machine(), logicalCores=os.cpu_count(),
                  cpuModel="UNKNOWN", visiblePhysicalRamBytes=None,
                  pythonVersion=platform.python_version(), locking="DOTNET_SYSTEM_IO_DISABLEFILELOCKING=0")
    if sys.platform == "win32":
        import winreg
        with winreg.OpenKey(winreg.HKEY_LOCAL_MACHINE,
                           r"HARDWARE\DESCRIPTION\System\CentralProcessor\0") as key:
            result["cpuModel"] = winreg.QueryValueEx(key, "ProcessorNameString")[0].strip()
        class Memory(ctypes.Structure):
            _fields_ = [("length",ctypes.c_ulong),("load",ctypes.c_ulong)]+[(name,ctypes.c_ulonglong)
                        for name in ("total","available","pageTotal","pageAvailable","virtualTotal","virtualAvailable","extended")]
        memory = Memory()
        memory.length = ctypes.sizeof(memory)
        if ctypes.windll.kernel32.GlobalMemoryStatusEx(ctypes.byref(memory)):
            result["visiblePhysicalRamBytes"] = memory.total
    return result

def execute(args):
    repo = Path(__file__).resolve().parents[1]
    root = Path(args.output).resolve()
    if root.exists(): raise RuntimeError("NEW_DIRECTORY_REQUIRED")
    root.mkdir(parents=True)
    guard = Guard(root)
    receipt = dict(status="INCOMPLETE", protocol="A4-FINITE-V1", samples=[], workloads={})
    launches = []
    try:
        if os.environ.get("DOTNET_SYSTEM_IO_DISABLEFILELOCKING") != "0":
            raise RuntimeError("LOCKING_ENV_REQUIRED")
        before = fingerprint(repo)
        worker = repo/"tools/Measurements/bin/Release/net10.0/Measurements.dll"
        executable = Path(shutil.which(args.dotnet) or args.dotnet).resolve(strict=True)
        binaries = {p.name:digest(p) for p in worker.parent.iterdir() if p.is_file()}
        receipt.update(source=before, host=host(), workerFiles=binaries,
                       executableSha256=digest(executable),
                       sdk=subprocess.check_output([str(executable), "--version"], text=True,
                                                   timeout=guard.remaining()).strip())
        datasets = root/"datasets"; datasets.mkdir()
        for n in COUNTS: receipt["workloads"][str(n)] = generate(datasets, n, guard)
        reference = {}
        for index, (n, mode, phase, round_no) in enumerate(schedule(), 1):
            guard.check(reservation(n, mode))
            folder = root/f"{index:02d}-{phase}-{n}-{mode}"
            folder.mkdir()
            manifest, source = datasets/str(n)/"manifest.json", datasets/str(n)/"input.jsonl"
            output = folder/"output"
            argv = [str(executable),str(worker),mode,str(manifest),str(source),str(output)]
            launches.append(dict(index=index, executable=str(executable), cwd=str(repo), argv=argv))
            dump(root/"private-launches.json", launches)
            parent_ns = guard.child(argv, repo, folder/"metrics.json", folder/"stderr.txt")
            value = json.loads((folder/"metrics.json").read_text(encoding="utf-8"))
            metrics_check(value, mode, n, receipt["workloads"][str(n)]["inputSha256"])
            hashes = {}
            if mode != "HASH":
                report = json.loads((output/"summary.json").read_text(encoding="utf-8"))
                for key, expected_value in expected(n).items():
                    report_key = {"unattributed":"unattributedRecords", "uncompared":"uncomparedRecords"}.get(key,key)
                    if report[report_key] != expected_value: raise RuntimeError("SUMMARY_COUNT_MISMATCH")
                reasons = report["reasons"]
                consumed = n-n//37
                if reasons != {"DUPLICATE_IN_WINDOW":n//37, "OHLC_INCONSISTENT":consumed//17,
                               "VALID_RECORD":consumed-consumed//17}:
                    raise RuntimeError("REASON_COUNT_MISMATCH")
                hashes = {name:digest(output/name) for name in CANONICAL}
                if n not in reference:
                    if mode != "A2": raise RuntimeError("MISSING_A2_REFERENCE")
                    reference[n] = output
                for name in CANONICAL: equal_files(output/name,reference[n]/name,guard)
            sample = dict(index=index,phase=phase,round=round_no,records=n,mode=mode,
                          parentWallNanoseconds=parent_ns,metrics=value,canonicalSha256=hashes,
                          portableCommand=f"dotnet tools/Measurements/bin/Release/net10.0/Measurements.dll {mode} <manifest> <input> <new-output>")
            receipt["samples"].append(sample)
            dump(root/"results.json", receipt)
            guard.check()
            print(f"{index}/21 {phase} {n} {mode} PASS", flush=True)
        after = fingerprint(repo)
        if after != before: raise RuntimeError("SOURCE_MUTATION")
        if {p.name:digest(p) for p in worker.parent.iterdir() if p.is_file()} != binaries:
            raise RuntimeError("BINARY_MUTATION")
        if digest(executable) != receipt["executableSha256"]: raise RuntimeError("EXECUTABLE_MUTATION")
        for n in COUNTS:
            if digest(datasets/str(n)/"input.jsonl") != receipt["workloads"][str(n)]["inputSha256"]:
                raise RuntimeError("INPUT_MUTATION")
            if digest(datasets/str(n)/"manifest.json") != receipt["workloads"][str(n)]["manifestSha256"]:
                raise RuntimeError("MANIFEST_MUTATION")
        receipt["status"] = "COMPLETED"
        receipt["sourceAfter"] = after
        guard.check()
    except Exception as error:
        receipt["status"] = "INCOMPLETE_INVALID"
        receipt["failureType"] = type(error).__name__
        # Our own fixed failure labels only; do not publish arbitrary exception paths.
        receipt["failureCode"] = str(error) if type(error) is RuntimeError else "SEE_PRIVATE_FAILURE"
        dump(root/"private-failure.json", dict(type=type(error).__name__,detail=str(error)))
        raise
    finally:
        receipt["elapsedSeconds"] = LIMIT_SECONDS-(guard.deadline-time.monotonic())
        receipt["artifactBytesBeforeFinalReceipt"] = guard.size()
        dump(root/"results.json", receipt)

if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--dotnet", required=True)
    parser.add_argument("--output", required=True, help="new private artifact directory, not source checkout")
    execute(parser.parse_args())
