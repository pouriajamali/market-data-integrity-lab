# Changes

## Unreleased — A4 harness

Separate fresh-process A2/A3 measurement worker, fixed synthetic recipe, finite
driver, independent counter/byte guards and explicit timing/memory definitions.
No performance result is available at this preparation commit.

## 0.3.0-preview.1 â€” A3

Opt-in local process-crash checkpoint/resume with strict identity/policy checks,
bounded state/window restore, two committed slots, verified sink-prefix fallback
and preserved bounded failure evidence. EOF finalization is idempotent; owned
child cuts and A1/A2 golden bytes are checked. Existing commands remain available.
No power-loss, hostile-FS, distributed exactly-once or performance guarantee.

## 0.2.0-preview.1 â€” A2

Opt-in manifest version 2 with explicit stream/epoch baselines, bounded consumed-
sequence windows, normalized duplicate/conflict comparison, persistent per-stream
sequence-gap/conflict blocks, bounded stream snapshots and uncertainty reporting.
New fixed synthetic scenario and handwritten state-transition/CLI evidence.
A1 parser/reader/validator/generator and canonical version-1 outputs remain pinned.
No crash recovery, reordering, market adapter or performance measurements.

## 0.1.0-preview.1 â€” A1

Initial synthetic-only offline vertical slice: bounded byte reader, strict
manifest/record parsing, OHLC checks, per-record provenance, deterministic sinks
and explicit completion/failure semantics. New generated data and failure tests.

Sequence-window processing, durable recovery and performance measurements are
unimplemented. This is a source-only preview, not the full planned engine.
