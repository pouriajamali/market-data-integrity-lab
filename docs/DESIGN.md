# Bounded retention and publication boundary

One synchronous pull reader owns the stream. Its byte arrays are R and 8,192
bytes; a SHA-256 state hashes each physical record incrementally. Its visitor
borrows a buffer only for the duration of the callback. Parsing is bounded by R
and JSON depth 4. Output is serialized immediately to fixed-size writer buffers.

The set of stream keys stops at K, each key with three identifiers of at most
32 ASCII characters. Reason counters have a fixed finite vocabulary. Retained
application state is O(R + K), independent of the number of records or of a
single oversized line. Temporary parse/serialization allocations and GC/runtime
working-set behavior still exist. The tests check structural buffer/read limits,
not a numeric whole-process memory budget or a zero-allocation claim.

A hash prepass identifies the whole file before output exists. A second hash
pass follows validation. This intentionally costs additional I/O; no performance
claim omits it. Output completion is a single final summary rename after flushed
sinks and source revalidation. An interruption can leave partial files and
requires a new run/output directory; it cannot be resumed in A1.

The separate accepted/quarantine sinks preserve each category's input order.
No reorder queue, task-per-record fanout, upstream sequence state, strategy,
book, matching engine, broker or database exists.

## Failure-oriented checks

Physical framing checks independently verify raw hashes/offsets for CRLF,
multibyte input, one-byte stream reads and EOF. Long-line checks verify bounded
retention and recovery of the following line. Input cases cover strict schema,
malformed bytes, OHLC and inclusive limits. Stream-capacity failures, source
mutation and injected read failure must leave no completed summary. Canonical
outputs are compared across directories/cultures. A separate CLI process checks
the generated demo's nonzero degraded exit and actual saved summary.

## Source provenance and scope

All source, tests, contracts and synthetic formulas were written fresh for this
repository. No private project or existing portfolio implementation was copied.
Only generated synthetic input is in scope. The public package allowlist is
source/configuration/tests/docs/workflow plus dependency lock files; private
planning reports, local paths/logs/account data and raw chat history stay outside.

The project is deliberately an A1 preview. Later sequence and recovery work must
add its own accepted contracts and executed evidence before any README/CV claim.
No CV/profile edits are part of this repository task.
