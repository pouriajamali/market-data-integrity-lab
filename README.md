# Market Data Integrity Lab — A2 preview

[Windows/Linux CI](https://github.com/pouriaquant91/market-data-integrity-lab/actions/workflows/verify.yml) · [Source preview v0.2.0-preview.1](https://github.com/pouriaquant91/market-data-integrity-lab/releases/tag/v0.2.0-preview.1)

An offline C#/.NET validator for explicitly synthetic OHLC records. A1 demonstrates
bounded byte framing, strict input contracts, record provenance and deterministic
accepted/quarantine output. A2 adds an opt-in bounded sequence window with declared
stream baselines, duplicate/conflict comparison and persistent per-stream gap gates.
It remains a source preview, not the completed engine.

## Review the behavior

- A line longer than the configured byte limit is streamed to a hash without being
  retained in full; the next line can still be inspected.
- Malformed UTF-8/JSON, schema violations, numeric bounds and inconsistent OHLC
  receive a stable primary reason. Every decision retains physical line, byte
  offset/length and SHA-256 of its raw record.
- The source hash is checked before and after processing. A completed summary is
  published only after both sinks flush and the final source check passes.
- Record buffers and the remembered stream set have explicit limits. This is a
  structural retention bound, not a measured process-memory ceiling or throughput result.

[Exact input/output contract](docs/CONTRACT.md) ·
[A2 sequence contract](docs/SEQUENCE.md) · [Design and limits](docs/DESIGN.md) · [Version history](CHANGELOG.md)

## Run

Requires the **.NET 10 SDK, version 10.0.403** (pinned in global.json).
The application uses only the .NET base library; tests restore xUnit v3 4.0.2
from NuGet. No account, network service, database or market feed is used at runtime.

```sh
dotnet restore tests/Integrity.Tests/Integrity.Tests.csproj --locked-mode
dotnet build tests/Integrity.Tests/Integrity.Tests.csproj -c Release --no-restore
dotnet run --project tests/Integrity.Tests -c Release --no-build
dotnet run --project src/Integrity -c Release --no-build -- generate artifacts/demo 34
dotnet run --project src/Integrity -c Release --no-build -- validate artifacts/demo/manifest.json artifacts/demo/input.jsonl artifacts/result
```

The demo deliberately quarantines records 17 and 34. The final command returns
**2**, with 32 accepted and 2 quarantined records. Exit 0 means completed without
quarantine, 2 means completed with quarantine, and 1 means fatal/usage failure.
Use new directories for generation and validation. Existing outputs are never overwritten.

## Opt-in A2 sequence scenario

~~~sh
dotnet run --project src/Integrity -c Release --no-build -- generate-sequence artifacts/sequence-demo
dotnet run --project src/Integrity -c Release --no-build -- validate-sequence artifacts/sequence-demo/manifest.json artifacts/sequence-demo/input.jsonl artifacts/sequence-result
~~~

This separate version-2 manifest declares every stream/epoch baseline and W.
The fixed 14-record synthetic scenario has **4 accepted / 10 quarantined** (exit 2),
with gap/conflict blocks and an explicit UNKNOWN assessment for unattributable input.
Its final malformed record is intentional.

The window retains the last W consumed sequence numbers. Duplicates never advance
it; equal normalized payloads are distinguished from conflicts only inside that
window. Evicted records are labelled STALE_OUTSIDE_WINDOW. A gap/conflict permanently
blocks that stream; other declared streams/epochs can continue. No state is created
for undeclared identifiers. Expected bad-OHLC records consume sequence and remain
quarantined. Reaching Int64.MaxValue ends the sequence domain without increment.

R, K, W and K*W have explicit limits; retained application state is O(R + K*W),
plus bounded metadata/output buffers. This is not a measured process-memory ceiling.
The summary's stream snapshots and UNKNOWN/PARTIAL/prefix assessments never assert
complete feed coverage. See the contract for the primary/data reason order.

The A1 version-1 path remains available and its original synthetic input, manifest
and three canonical output hashes are pinned by a compatibility regression.
[Original A1 source release](https://github.com/pouriaquant91/market-data-integrity-lab/releases/tag/v0.1.0-preview.1).

## Implemented and deferred

A1 continues to parse sequence without interpreting it. Only the A2 opt-in command
interprets explicit sequence within declared baselines/windows. Neither path
reorders events, infers gaps from dates, authenticates a source, resumes interrupted
runs or implements checkpoints. Crash recovery and reproducible performance
measurements remain future work. A completed run does not establish feed completeness,
production readiness, venue latency, order execution or profitability.

The release channel is source-only preview: no installer, hosted application or
compiled platform package. Windows and Linux CI exercise the documented behavior;
assess a particular result using its exact commit and run.

## Provenance

Fresh independent synthetic portfolio code created with AI assistance for Pouria
Jamali. Inputs are generated by the included fixed integer formula; no private
project source, fixtures, data, schemas or business rules are included. This
sample supports discussion of its own source and executed checks, not historical
employer results or an independently verified claim about individual code share.
