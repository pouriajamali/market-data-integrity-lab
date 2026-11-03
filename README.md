# Market Data Integrity Lab

See **which synthetic market-data records are rejected, why a stream stops, and
what a compatible checkpoint can restore**. This offline C#/.NET lab is for
developers studying validation and process-crash recovery. It accepts explicitly
synthetic input only.

[Windows/Linux CI](https://github.com/pouriajamali/market-data-integrity-lab/actions/workflows/verify.yml) ·
[Download v0.5.0-preview.1](https://github.com/pouriajamali/market-data-integrity-lab/releases/tag/v0.5.0-preview.1) ·
[Source and developer commands](docs/DEVELOPMENT.md)

## Decisions, observable behavior, proof

| Engineering decision | What you can observe | Exact proof |
| --- | --- | --- |
| Quarantine inconsistent OHLC and preserve physical provenance | The demo's A1 example accepts 32 and quarantines 2; decisions retain line/offset/raw hash/reason | [FreshSyntheticGeneratorAndCliReturnDegradedRatherThanSuccess](tests/Integrity.Tests/BehaviorTests.cs#L222), [DemoMatchesKnownCanonicalOutputsAndRefusesExistingRootWithoutMutation](tests/Integrity.Tests/DemoTests.cs#L11) |
| Use explicit sequence baselines and a bounded comparison window | The A2 example accepts 4/quarantines 10; gaps/conflicts block that stream; stale-outside-window is not a proven duplicate | [HandwrittenTransitionOracleChecksWindowConsumptionAndPersistentConflict](tests/Integrity.Tests/SequenceTests.cs), [GapDoesNotHealAndOtherDeclaredStreamsAndEpochsContinue](tests/Integrity.Tests/SequenceTests.cs) |
| Resume only a compatible committed state; verify sink prefixes before trimming | Real owned-child interruptions resume to the exact uninterrupted A2 files; foreign/no-eligible state is refused | [FiniteRealKillMatrixResumesToExactA2Bytes](tests/Integrity.Tests/RecoveryTests.cs), [ChangedIdentityOrFutureVersionCannotAuthorizeFallback](tests/Integrity.Tests/RecoveryTests.cs) |
| Preserve rejected state/tails before mutation, within a finite evidence budget | Evidence-copy failure/exhaustion refuses recovery without truncating active data | [PartialEvidenceFailureCannotTruncateAndRequiresManualArchive](tests/Integrity.Tests/RecoveryTests.cs), [EvidenceByteBudgetRefusesNewCopiesBeforeCanonicalMutation](tests/Integrity.Tests/RecoveryTests.cs) |

**Measured tradeoff:** in the historical one-host A4 study, A3 was slower and
allocated more managed bytes than A2. The 100k operation median was 2.720s versus
1.625s (3 samples per mode). The modes use different writer/flush/checkpoint paths;
the study did **not** isolate their causal shares. Allocation is not peak memory.
These measurements describe [d9ece502/v0.4](docs/CASE-STUDY.md), **not these B1
binaries**. [Raw samples](docs/evidence/measurements/results.json) and
[local red/green development evidence](docs/evidence/development/index.json)
remain inspectable. No speed or production-performance claim follows.

## Download and run

Choose your x64 platform, verify the archive against
[SHA256SUMS.txt](https://github.com/pouriajamali/market-data-integrity-lab/releases/download/v0.5.0-preview.1/SHA256SUMS.txt),
then extract **the whole directory**. Keep the app beside its bundled runtime.

- [Windows x64 ZIP](https://github.com/pouriajamali/market-data-integrity-lab/releases/download/v0.5.0-preview.1/market-data-integrity-lab-v0.5.0-preview.1-win-x64.zip)
- [Linux glibc x64 tar.gz](https://github.com/pouriajamali/market-data-integrity-lab/releases/download/v0.5.0-preview.1/market-data-integrity-lab-v0.5.0-preview.1-linux-x64.tar.gz)

Developers can also download the [source ZIP](https://github.com/pouriajamali/market-data-integrity-lab/releases/download/v0.5.0-preview.1/market-data-integrity-lab-v0.5.0-preview.1.zip).

Windows PowerShell, from the extracted directory:

~~~powershell
.\Integrity.exe --version
.\Integrity.exe demo "demo output"
~~~

Linux shell, from the extracted directory:

~~~sh
./Integrity --version
./Integrity demo "demo output"
~~~

The app requires no installed .NET SDK/runtime or Python. These are self-contained
directory bundles, not installers. Native OS dependencies still apply. The Linux
target is glibc x64, tested on Ubuntu 24.04; Alpine/musl, ARM and macOS are not
included. Windows is x64; actual tested OS versions are recorded in build/CI
receipts. These are unsigned developer previews. See
[.NET native prerequisites](https://github.com/dotnet/core/blob/main/release-notes/10.0/dotnet-dependencies.md).

Each package includes `BUILD-MANIFEST.json` (version, exact source commit, RID,
SDK, bundled runtime and payload hashes), licenses and a short offline quickstart.
CI extracts the archives and directly runs the apphosts from paths/cwd containing
spaces, with empty PATH and nonexistent DOTNET_ROOT. The hosts may themselves
have SDKs installed; that is **not** a claim of an empirically SDK-free host test.

## What the small demo does

Expected highlights:

~~~text
A1: 34 records; 32 accepted; 2 quarantined (expected bad OHLC).
A2: 14 records; 4 accepted; 10 quarantined; 2 unattributed (UNKNOWN).
A3: completed-run resume verified; generation=7; all 3 canonical files equal A2.
This is NOT a crash demonstration. Owned-child interruption proof is in RecoveryTests.
~~~

The demo also prints actual reason counts. Inspect `a1-result/`,
`sequence-result/`, `recovery-result/` and `demo-summary.json`.
`accepted.jsonl` and `quarantine.jsonl` explain each physical decision.

**Demo exit 0 means its expected outcomes matched; it does not mean all records
were valid.** Existing validation commands keep exit 0 clean, exit 2 completed with
quarantine, exit 1 usage/fatal. A demo requires a new directory. Bad arguments or
an existing destination are refused before writes; failures retain partial files.
Do not infer whole-demo completion without its final `demo-summary.json`.

The ready demo restarts an already completed run. It kills no process and has
no fault hook. Real process-interruption proof is the separate
[finite regression matrix](tests/Integrity.Tests/RecoveryTests.cs), including
partial decisions/checkpoints, fallback and repeated interruptions.

## Limits and deeper evidence

- Only declared synthetic JSONL is supported; no real CSV/feed adapter, calendar
  inference, reordering, silent repair, broker/account connection or trading.
- Retained framing/state is bounded by R/K/W. Output/disk usage still grows.
  Unknown/partial/prefix assessments do not certify feed completeness.
- Recovery needs enabled .NET locking, cooperating writers and supported local
  filesystem behavior. Unix locks are advisory and may be disabled by runtime
  settings. This is process-crash recovery, not power-loss safety, hostile-FS
  protection or distributed exactly-once delivery.
- B1 adds usability/packaging. It has not been performance-measured. Historical
  tags, archives and raw measurement bytes remain unchanged.

[Input contract](docs/CONTRACT.md) · [Sequence contract](docs/SEQUENCE.md) ·
[Recovery contract](docs/RECOVERY.md) · [Case study](docs/CASE-STUDY.md) ·
[Claim-to-evidence map](docs/EVIDENCE.md) · [Measurement definitions](docs/MEASUREMENTS.md) ·
[Design](docs/DESIGN.md) · [Changes](CHANGELOG.md)

## Attribution

Independent synthetic portfolio sample created with AI assistance for Pouria
Jamali. No private implementation, fixtures, market/account data or product rules
are included. This does not establish employer results or an independently
assessed individual code share. [MIT license](LICENSE) covers original sample
material; bundled .NET runtime and third-party notices retain their own terms.
See [NOTICE](NOTICE.md). CV/profile/shared projects are outside this repository.
