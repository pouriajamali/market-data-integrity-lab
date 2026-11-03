# Finite measurement protocol A4-FINITE-V1

This measures two end-to-end modes on one identified local Windows host: A2 sequence validation and A3 fresh resumable validation, interval 1000. Both use the same clean Release build and public APIs. Their writers, flush paths and checkpoint work differ. A difference is **mode cost**, not isolated causal checkpoint overhead. No fault hooks, resume timings, live feeds, latency SLA or profitability claims.

Build with .NET SDK 10.0.403 before the study:
```sh
dotnet build tools/Measurements/Measurements.csproj -c Release
python tools/test_measurements.py
```
Set `DOTNET_SYSTEM_IO_DISABLEFILELOCKING=0`. Then, from a clean pinned commit:
```sh
python tools/run_measurements.py --dotnet dotnet --output <new-private-directory>
```
The driver will not overwrite an existing directory or accept dirty source. Outputs include private resolved paths in `private-*.json`; publish only the reviewed portable `results.json`, never the entire artifact directory.

## Fixed recipe and schedule

The pinned JSON protocol supplies all parameters. `seed_label=20261103` identifies a formula; it is not a random seed. UTF8/LF, R=4096, K=16, W=64. Every physical row divisible by 37 exactly repeats its predecessor. All other rows consume the next per-stream sequence; every 17th consumed record has invalid OHLC. There are no gap/conflict blocks. Expected duplicate count is floor(N/37), consumed=N-duplicate, bad=floor(consumed/17), accepted=consumed-bad, quarantine=duplicate+bad.

Four fresh-process warmups (10k A2/A3 then 100k A2/A3), twelve regular samples (each size three rounds AB, BA, AB), two stress observations (250k A2/A3), then three separate SHA256-only references, one per size. Every completed validation has independent formula counters, exact reason counts, three canonical hashes and streaming comparison with its size's first A2 output. Warmups are retained and labelled separately.

## Metric windows

The worker creates process statistics and reads initial CPU, then allocation0, then Stopwatch t0. It calls the complete public validation method; then t1, allocation1, refreshed CPU and peak working set. Metric JSON serialization follows. Operation wall includes manifest parse, pre/post source hashing, parsing, output, flush and checkpoint work. Process startup and some JIT before t0 are excluded from operation wall, but included in parent launch-to-exit wall. JIT triggered inside validation is included. Every sample is a fresh process: warmup can warm file caches but cannot carry JIT state. Generation and output checking also warm caches. No eviction, affinity or host isolation.

Managed allocation is the cumulative whole-process delta from precise GC.GetTotalAllocatedBytes, including runtime-thread allocation and counter overhead; it excludes native allocation and is neither retained heap nor peak memory. CPU uses a slightly wider window including statistics/counter calls; zero CPU can reflect clock granularity. PeakWorkingSet64 is the OS/runtime high-water mark for the process lifetime through collection (including startup/JIT), not sampled heap or an operation-only peak. Unsupported/zero peak is UNKNOWN. The small tail after peak collection is not observed.

Throughput uses **all physical rows** divided by operation wall, with source bytes/second separately. Three regular samples support min/median/max, not tail percentiles. Stress has one observation per mode. SHA256-only references are separate operations, never subtracted or described as actual in-run hash-phase timings.

Primary API definitions: [allocation](https://learn.microsoft.com/en-us/dotnet/api/system.gc.gettotalallocatedbytes?view=net-10.0), [peak working set](https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.process.peakworkingset64?view=net-10.0), [CPU](https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.process.totalprocessortime?view=net-10.0).

## Finite resources and stop rule

600 seconds and 2 GiB for the entire study: generation, all retained inputs, warmup/reference/measurement outputs, logs, receipts and verification. Build is before this budget. One directly owned child at a time. Before launch, reserve N*2048 + 5 MiB, plus 3*(64 MiB+52) for A3 slots/pending, against already retained bytes. Before generation reserve N*512+64KiB. The driver checks time/resources during generation and child waiting and after each step. Remaining global time is the child deadline; expiry kills/waits only that owned process. A final small failure receipt may exceed an exhausted budget; it records the incomplete run.

First unexpected exit, timeout, resource limit, wrong metric/count/byte, or source/workload/binary mutation ends the schedule, retaining failed artifacts. No retry, tuning or smaller replacement. Source and built binaries are pinned before/after; input/manifest hashes are verified again after. The measured commit remains an explicit ancestor if later documentation/results are committed. Final release metadata must not pretend that later commit was the timed binary.

Public host fields omit hostname/user/IP/serial. Public commands are portable templates with executable identity/version/hash. Literal executable/cwd/argv are private. Linux correctness CI is separate from this Windows measurement; no cross-OS performance ranking.

## Published observation receipt

The single approved run is in [raw results](evidence/measurements/results.json)
and the [case study](CASE-STUDY.md). It completed21 children without retry.
The retained manifest JSON used Windows default CRLF while physical JSONL was
explicit UTF8/LF; raw manifest hashes identify those exact bytes. Generating a
new manifest on another OS may change manifest identity and summary bytes;
determinism applies to the same input/manifest bytes.

The JSON protocol status reflects its preflight preparation point. Coordinator
approval and actual execution are recorded in [execution receipt](evidence/measurements/execution.json).

Recorded binary hashes identify the actual measured local build. They do not
claim byte-identical rebuilding across directories/machines; PDB/build paths and
runtime/toolchain details can affect rebuilt binary identity. Source and contract
reproduction are separate from that binary-identity receipt.
