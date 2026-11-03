# Bounded retention and publication boundary

## A1 retained state and I/O

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

The A1 path remains a bounded validation preview. The separate A2 sequence path
is described below. A3 now adds the separate checkpoint/resume contract linked
below; feature claims remain bounded by executed evidence. No CV/profile edits are part of
this repository task.

## A2 additive sequence path

A2 reuses the unchanged A1 byte reader and Candle parser but has a separate manifest,
state owner and versioned output runner. This keeps the A1 contract and golden
canonical output hashes intact. It deliberately avoids an unrelated A1 I/O refactor.

Every key is admitted from the bounded manifest only. A state's lazily allocated
W-slot array stores bounded normalized Candle values. Each successful expected
consumption advances one slot; duplicate, stale, before-baseline and blocked input
cannot evict entries. A linear scan on historical input costs at most W comparisons;
no performance advantage is claimed. Total retained slots cannot exceed K*W<=65,536.
Int64.MaxValue is an explicit end state, not an overflowing next cursor.

Primary sequence decisions preserve a separate parser/OHLC dataReason. Parse
failures cannot be attributed to a stream; undeclared keys do not enter the state
dictionary. Unknown/partial assessment and per-stream snapshots make these limits
inspectable without asserting feed completeness. [The A2 contract](SEQUENCE.md)
defines decision order and the exact limits.

Evidence includes a handwritten transition table with cursor/window/block snapshots,
two-stream and declared-epoch isolation, both sides of the retention boundary,
bad-OHLC consumption/repetition, numeric end-of-domain handling, bounded admission,
canonical output/provenance and a real opt-in CLI process with a bounded deadline.
Original A1 behavior tests remain unchanged. There are no new measurements or
durable recovery guarantees.

## A3 recovery path

[Recovery contract](RECOVERY.md) defines same-handle source checks, fixed policy
identity, two-slot publication, complete window export/restore, prefix validation
and evidence protection before mutation. A1/A2 canonical formats are unchanged.
Committed EOF is finalized without inventing another checkpoint generation.
Ownership assumes enabled local .NET locking and cooperating writers; Unix locks
are advisory/best-effort. No power-loss, hostile-FS, distributed exactly-once or
performance guarantee follows.
