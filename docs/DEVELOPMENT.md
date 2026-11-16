# Source build and existing CLI commands

This source-preparation branch pins SDK11.0.100, net11.0, stable C#15 and
runtime11.0.0. Runtime source uses the BCL; test package versions remain pinned.
No new-SDK restore, build or test has run. Source locks remain net10.0 pending
admitted SDK regeneration and dependency review, so this checkout is not yet a
verified source build. Ready users should use the existing0.6.1-preview.1 release.

The commands below are the intended later build route after shared SDK admission,
new lock generation/audit, exact project kernel-cap admission and the bounded
execution contract. They are not evidence of execution on this candidate.

~~~sh
dotnet restore tests/Integrity.Tests/Integrity.Tests.csproj --locked-mode
dotnet build tests/Integrity.Tests/Integrity.Tests.csproj -c Release --no-restore
dotnet run --project tests/Integrity.Tests -c Release --no-build
~~~

Optional focused interruption proof (owned test children only):
~~~sh
dotnet run --project tests/Integrity.Tests -c Release --no-build -- -class Integrity.Tests.RecoveryTests
~~~

Existing commands remain available. Replace the prefix below with
`./Integrity` or `.\\Integrity.exe` when using a ready bundle:

~~~sh
dotnet run --project src/Integrity -c Release --no-build -- generate artifacts/demo 34
dotnet run --project src/Integrity -c Release --no-build -- validate artifacts/demo/manifest.json artifacts/demo/input.jsonl artifacts/result
dotnet run --project src/Integrity -c Release --no-build -- generate-sequence artifacts/sequence-demo
dotnet run --project src/Integrity -c Release --no-build -- validate-sequence artifacts/sequence-demo/manifest.json artifacts/sequence-demo/input.jsonl artifacts/sequence-result
dotnet run --project src/Integrity -c Release --no-build -- start-resumable artifacts/sequence-demo/manifest.json artifacts/sequence-demo/input.jsonl artifacts/recovery-result 2
dotnet run --project src/Integrity -c Release --no-build -- resume artifacts/sequence-demo/manifest.json artifacts/sequence-demo/input.jsonl artifacts/recovery-result 2
~~~

These intentionally degraded examples return2. The second recovery command
verifies completed output; it is not an interruption test. Generated source,
decisions and summaries are canonical UTF8/LF. Use fresh destinations.

The separate small `demo <new-directory>` command checks expected example
outcomes and all3 A2/A3 canonical files, returning0 when those checks match.
It adds no new framing, sequence or recovery semantics.

## Binary delivery

The current [CLI preview is 0.6.1-preview.1](https://github.com/pouriajamali/market-data-integrity-lab/releases/tag/v0.6.1-preview.1) for Windows x64 and Linux
glibc x64. The separate CSV action retains its immutable 0.6.0-preview.1 binaries
and exact download/signature pins.
SelfContained=true, runtime10.0.15, single-file/trim/AOT/R2R=false. Runtime restore
uses a separate artifact lock path, preserving the original source lockfile.
Packages include original runtime LICENSE.TXT and THIRD-PARTY-NOTICES.TXT.

`tools/package_binary.py` requires a clean exact-commit checkout and inventories
the payload. `tools/check_binary.py` checks its manifest, bundled runtime and
direct apphost behavior after extraction on the matching OS. Runtime checks use
a new bounded fixture, not the A4 performance schedule. The release job retrieves
the tested archive files and verifies their manifests, without rebuilding them.
Binary archive/container identity is separate from source and historical timing.

B1 binaries are not benchmarked. [A4](CASE-STUDY.md) belongs to its immutable
measured ancestor. Do not rerun or relabel that study as B1 evidence.

## B2 local CSV

[CSV.md](CSV.md) defines the separate ohlc-csv-v1 profile. Both scales are explicit.
Use `validate-csv <input.csv> <new-output> <price-decimals> <volume-decimals>`
or `csv-demo <new-directory>`. CSV has no implicit A3 conversion/recovery.

CsvTests independently cover integer/scaled boundaries, malformed/quoted rows,
BOM/CRLF/EOF, no data/header failures, exact hashes, cultures, source mutation/read
and actual byte/row caps. Runtime package smoke adds bounded CSV commands.
No private or downloaded market data is used by development or delivery checks.

## M1 CSV usage diagnostics

The delivered0.6.1 M1 change added fixed arity/price/volume diagnostics in Program.cs;
no parser or source dependency changes. Source package locks and the four public
Admission vendor source pins remain frozen. That released CLI uses SDK10.0.404 and bundled
runtime10.0.15; its net10.0 target and Directory.Build.props were unchanged.
CsvCliTests is a disabled-parallel collection because it invokes the actual
synchronous CLI entry point while owning Console/culture, rather than duplicating
argument parsing. Actual archive checks invoke self-contained apphosts separately.

[Exact-tag CI](https://github.com/pouriajamali/market-data-integrity-lab/actions/runs/42882215681) at 06f3b6104446fa7b781c70cc759d0a8eed4e3fa8
passed 165 tests and 60 extracted-apphost invocations on each native OS, using
.NET10.0.15. Runtime BUILD-MANIFEST.json files bind that source commit, RID, SDK,
runtime and payload hashes; `PROVENANCE.md` describes independent checksum/signature
verification. Linux restore or a Windows-only run is not Linux execution. These
matching-OS CI results are functional/package checks, not a new performance study.

## Technology-refresh execution boundary

The new candidate keeps all CSV, synthetic, sequence and recovery semantics,
independent literal oracles and the separate0.6.0 CSV action pins. Only declared
framework/version literals change in existing tests/tools. Do not rerun the A4
study. .NET11 x64 requires x86-64-v2 hardware as well as native OS prerequisites.
Actual new locks, runtime notices, dependency audit, real Windows Job/Linux cgroup
containment and native results are still pending. SDK producer04 is shared once;
do not bootstrap a second copy or reuse06-specific wrappers. Before any hosted
event, reconcile protection/actual workflow route and the old adapter's repeated
main-trigger CI with the explicit zero-additional-cost budget.
