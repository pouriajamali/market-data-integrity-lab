# Changes

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
