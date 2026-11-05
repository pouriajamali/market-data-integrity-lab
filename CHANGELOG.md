# Changes

## Unreleased - documentation

- Clarify the source quick start and expected result, and link focused contribution guidance. Runtime behavior, release assets and recorded measurements are unchanged.


## CSV action 0.1.0-preview.1

Add a composite GitHub Action for caller-owned offline OHLC CSV files. The
adapter verifies the existing signed v0.6.0-preview.1 manifest and pinned archive
hash before extracting and running the apphost. Explicit scales, workspace path
bounds, a 64 MiB input cap, bounded downloads/extraction and a process timeout
keep the invocation finite. Clean/quarantined/fatal fixtures exercise exits
0/2/1 on Windows and Ubuntu. Reports stay in the runner workspace. CLI source,
historical binary packages, signatures and 141-test release evidence are unchanged.

## 0.6.0-preview.1 - B2 offline OHLC CSV

Separate explicit-scale local CSV profile with lossless signed64-bit integer
strings, per-row provenance/reasons and whole-file framing/resource/source guards.
Generated CSV demo; matching-OS package checks cover clean/quarantined/fatal
CSV outcomes. Ordering/dedup/completeness are NOT_CHECKED; source is UNVERIFIED.
No live connection, trading, CSV recovery, silent conversion or A4 rerun.

## 0.5.0-preview.1 - B1 runnable synthetic demo

Self-contained win-x64/Linux glibc x64 directory bundles, per-payload build
manifests/licenses and extracted-apphost verification. New bounded fresh-only
demo and version commands; existing canonical validation/recovery contracts stay
unchanged. README puts engineering decisions, observable outcomes and exact
proof tests first. Completed-run resume is not a crash demonstration. A4 adverse
measurements remain historical, not a B1 performance claim. No B2/real-data path.

## Unreleased - documentation maintenance

Update current GitHub links after the owner account rename. Existing preview
artifacts, tags, measured code and historical receipts are unchanged; no new
software release or measurement run is introduced.

## 0.4.0-preview.1 - A4/A5 portfolio source preview

Separate fresh-process A2/A3 measurement worker, fixed synthetic recipe, finite
driver, independent counter/byte guards and explicit timing/memory definitions.
Finite one-host observations, inspectable raw receipts, design/failure case study
and claim-to-evidence map. Measured ancestor code is distinguished from this
results/documentation release. No new application contract or production claim.

## 0.3.0-preview.1 — A3

Opt-in local process-crash checkpoint/resume with strict identity/policy checks,
bounded state/window restore, two committed slots, verified sink-prefix fallback
and preserved bounded failure evidence. EOF finalization is idempotent; owned
child cuts and A1/A2 golden bytes are checked. Existing commands remain available.
No power-loss, hostile-FS, distributed exactly-once or performance guarantee.

## 0.2.0-preview.1 — A2

Opt-in manifest version 2 with explicit stream/epoch baselines, bounded consumed-
sequence windows, normalized duplicate/conflict comparison, persistent per-stream
sequence-gap/conflict blocks, bounded stream snapshots and uncertainty reporting.
New fixed synthetic scenario and handwritten state-transition/CLI evidence.
A1 parser/reader/validator/generator and canonical version-1 outputs remain pinned.
No crash recovery, reordering, market adapter or performance measurements.

## 0.1.0-preview.1 — A1

Initial synthetic-only offline vertical slice: bounded byte reader, strict
manifest/record parsing, OHLC checks, per-record provenance, deterministic sinks
and explicit completion/failure semantics. New generated data and failure tests.

Sequence-window processing, durable recovery and performance measurements are
unimplemented. This is a source-only preview, not the full planned engine.
