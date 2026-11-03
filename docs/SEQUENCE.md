# A2 sequence contract — opt-in manifest version 2

A2 adds `generate-sequence <new-directory>` and
`validate-sequence <manifest.json> <input.jsonl> <new-output-directory>`.
The A1 `generate`/`validate`, manifest version 1, record parser and output contract
remain unchanged. Version 2 is accepted only by the opt-in command.
No market connection, inferred calendar gap, reordering, repair, checkpoint,
restart recovery or performance measurement is added.

## Manifest, identity and resource bounds

The manifest is at most 65,536 UTF-8 bytes, with exactly these unique fields:
formatVersion (2), datasetId, synthetic (true), maxRecordBytes (R), maxStreams (K),
inputSha256, sequenceWindow (W), baselines.

A1 identifier/number/hash/R/K rules still apply. W is 1..4096; K * W must not
exceed 65,536. All checks occur before state-window allocation. baselines is an
array of 1..K unique normalized stream keys, each object having exactly source,
instrument, epoch and firstSequence (integer 1..Int64.MaxValue). Every identifier
has A1's 1..32 ASCII-character bound. Additional/duplicate properties and malformed
Unicode are fatal manifest errors. All manifest limits apply together.

A stream key is (source,instrument,epoch). Every key and its first expected
sequence must be explicitly declared. An input record never creates a new key.
Unknown keys are UNDECLARED_STREAM, quarantined without state growth.
Epochs are independent declared streams; timestamps, decreasing sequences and
new identifiers never reset existing state. An unused declared epoch does not
prove that any data exists for it.

Each stream retains at most the last W *consumed sequence numbers*, including
expected records with OHLC violations. Duplicates, old input, parse errors and
blocked records never advance the window. State is O(R + K*W), plus the bounded
manifest and output buffers; this is a retention argument, not a measured
process-memory budget, allocation or throughput claim.

## Decision order and state transitions

A1 byte framing and parser semantics are reused. RECORD_TOO_LONG and parser
failures (including envelope identity/numbers) are classified before sequence
processing. Only a fully parsed, numerically in-range Candle has a usable
envelope; OHLC_INCONSISTENT still has that envelope. Malformed/unattributable
records do not change any cursor and make overall assessment UNKNOWN.

For an attributable envelope, apply these ordered rules:

1. An undeclared key produces UNDECLARED_STREAM with no state allocation.
2. A stream previously blocked by a gap/conflict produces STREAM_BLOCKED.
3. A number below its declared firstSequence produces BEFORE_BASELINE.
4. A number equal to nextExpected is consumed, stored in the W-slot window and
   advances nextExpected. Its primary reason is VALID_RECORD or OHLC_INCONSISTENT.
   An OHLC-invalid record is quarantined but consumes sequence to avoid a false gap.
5. For an older number, compare its normalized payload with the retained entry.
   Equal payload produces DUPLICATE_IN_WINDOW; unequal payload produces
   CONFLICT_IN_WINDOW and permanently blocks that stream. If the number was
   evicted, produce STALE_OUTSIDE_WINDOW without claiming duplicate/conflict detection.
6. A number above nextExpected produces SEQUENCE_GAP and permanently blocks that
   stream. The triggering event is not consumed and cannot heal the gap.

Comparison uses the complete bounded Candle value (all identifiers, sequence,
timestamp and OHLCV), not raw JSON text, a probabilistic hash match, or record ID
alone. Property ordering/whitespace do not distinguish equal normalized payloads.
An identical repeated bad-OHLC payload is a duplicate; its dataReason still
records OHLC_INCONSISTENT. A changed payload at that sequence is a conflict.

A blocked stream cannot resume in A2, even if the missing sequence later appears.
Other declared streams continue independently. A separately declared epoch can
start at its own baseline, while the original epoch remains blocked.

Consuming Int64.MaxValue marks rangeExhausted=true and nextExpected=null.
No increment is attempted. Retained repeats/conflicts and evicted older numbers
are still classified by rules 1..5. No larger Int64 input sequence exists.
lastConsumed is null before the first consumption; below-baseline records cannot
be conflated with evicted records.

## Evidence and completion

Each A2 decision preserves A1 line/offset/raw-byte length/hash and adds dataReason
beside its primary reason. For sequence failures dataReason preserves the parsed
OHLC result; primary reasons describe the ordered state decision. Only primary
VALID_RECORD goes to accepted.jsonl; every other reason is quarantined.

The canonical summary contains formatVersion=2, counts, R/K/W, sorted reason
totals and declared-stream snapshots sorted ordinally by source/instrument/epoch:
firstSequence, lastConsumed, nextExpected, rangeExhausted, blockedReason and
retainedCount. Snapshots contain no unbounded history or per-record accumulation.

unattributedRecords counts malformed/oversized/invalid-envelope and undeclared-key
records. uncomparedRecords counts BEFORE_BASELINE, STALE_OUTSIDE_WINDOW and
STREAM_BLOCKED records. Assessment is UNKNOWN_UNATTRIBUTABLE_RECORDS when the first
count is nonzero, otherwise PARTIAL_COMPARISON when the second is nonzero, otherwise
OBSERVED_PREFIX_ONLY. Blocked states are explicit even in the last category.
None of these states means feed completeness: the known scope is only each
declared baseline through lastConsumed, with no authenticated source/end-of-feed
claim. completed describes completion of file processing only.

Source identity, pre/post hashes, output locking, deterministic UTF-8/LF sinks,
final summary rename and controlled fatal failures follow A1. Exit 0 means a
completed run without quarantine, 2 means completed with quarantine, and 1 means
fatal/usage failure. A2 cannot resume partial output. No runtime/source timestamps
or measurements enter canonical output.
