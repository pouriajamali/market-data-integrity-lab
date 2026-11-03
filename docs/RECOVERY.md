# A3 process-crash recovery — checkpoint format 1

A3 is opt-in: start-resumable / resume use the existing version-2 synthetic
manifest and A2 sequence rules. A1/A2 commands and canonical bytes stay compatible.
A completed A3 run must have the same accepted.jsonl, quarantine.jsonl and
summary.json bytes as uninterrupted A2 on that input/manifest.

## Ownership and operating conditions

Use local files with working .NET file locking and same-directory replacement.
One cooperating .NET writer controls the work directory. Start requires a new
directory, CreateNew files and a second directory check under the exclusive
handle. Resume needs the existing A3 marker/sinks/committed checkpoint; it never
turns missing initialization into a fresh run.

run.lock is an exclusive FileShare.None handle, not a PID lease. The file remains
after exit; resume can reopen it after the OS releases a dead owner's handle.
No PID inspection, automatic lock deletion or unrelated process kill occurs.
A kill before generation0 is committed causes controlled refusal.

Unix .NET locks are advisory/best-effort. System.IO.DisableFileLocking or
DOTNET_SYSTEM_IO_DISABLEFILELOCKING can disable them. Ownership therefore requires
locking enabled and cooperating writers on a supported local filesystem.
There is no self-probe/fail-closed promise for disabled locking, defense against
tools ignoring locks, hostile mutation, network filesystems, power loss or
distributed exactly-once guarantee. The
[pinned .NET 10.0.14 implementation](https://github.com/dotnet/runtime/blob/v10.0.14/src/libraries/System.Private.CoreLib/src/Microsoft/Win32/SafeHandles/SafeFileHandle.Unix.cs)
defines this runtime boundary.

One input handle with FileShare.Read is held across initial hash, reads and final
hash; length is checked too. These are consistency checks, not authentication
or protection against adversarial change-and-restore.

## Identities and exact policy key

Manifest identity is SHA-256 of its exact raw bytes, including whitespace.
Source identity is declared SHA-256 plus actual byte length. Paths are not
identities. Different source/manifest/interval/engine contract/policy is refused.

policySha256 hashes compact UTF-8 JSON with this exact property order:
~~~json
{"checkpointFormatVersion":1,"runnerContract":"A3-process-crash-v1","sequenceContract":"A2-sequence-v1","manifestSha256":"<raw manifest SHA256>","checkpointEveryRecords":2,"maxCheckpointBytes":67108864,"generationSlots":2,"maxDecisionBytes":2048,"maxSummaryBytes":4194304,"checkpointMaxDepth":8,"maxEvidenceBytes":201326592,"maxEvidenceFiles":32}
~~~
Apart from manifest identity, the only user-controlled policy parameter is\ncheckpointEveryRecords, explicitly within 1..10000. Contract/schema/
cap changes need review/versioning; limits are never raised to fit a workload.

## Commit order, cursor and EOF

checkpoint.0 / checkpoint.1 alternate by generation parity. checkpoint.pending
is never a candidate. Binary format: 20-byte little-endian header (8-byte MDILCP01
magic, Int64 generation, Int32 payload length), canonical JSON payload and its
32-byte SHA-256. Header and payload generations must agree. Checksum is a
corruption guard, not authentication.

Payload contains identities, next physical byte cursor/line, all counters/reasons,
both sink lengths/prefix digests and every declared stream's complete bounded
normalized window plus last/blocked state. MAX is restored without overflow.

1. Finish complete decisions; a torn line never advances a committed cursor.
2. Flush accepted and force its file buffers, then quarantine likewise.
3. Capture state/cursor/counters and non-resetting hashes of both exact prefixes.
4. Write/flush pending checkpoint, then replace its same-directory slot. The other
   previously committed generation remains available.

Cursor comes from raw record offset/length/LF, never read-ahead source.Position.
CR belongs to raw bytes. Malformed/invalid-UTF8/oversized records advance the
physical cursor once the whole LF/EOF is known. Unterminated EOF is consumed once;
trailing LF adds no record. Empty input is cursor0/nextLine1.

After final source verification, checkpoint only uncommitted record state.
Committed EOF is reused: finalization without new records never invents another
generation. Each increment requires newly committed records; generation<=records
and generation0 supports empty input. summary.pending is flushed/renamed last.

## Resume, validation and fallback

Decode only the two committed slots. Header/length/version/checksum, depth/schema
and array counts are checked before allocating restored windows. Recognized future
versions and missing/duplicated/incompatible identity fields are fatal; they do
not authorize fallback to a different identity. Other corrupt candidates may be
rejected if an older candidate is independently eligible.

Validate window membership/order/count, bounded payloads, blocked/MAX state,
reason/counter totals, physical cursor boundary and line count. Verify both sink
prefix lengths, SHA-256 and complete canonical row counts. Choose the newest
fully eligible candidate; no valid candidate means refusal, not repair.

Before any truncate/slot overwrite, protect rejected checkpoint bytes, pending
checkpoint/summary and discarded sink tails. Only after every required copy is
complete/verified may protected pending names be removed, both tails truncated
and state/digest contexts restored. Gap/conflict blocks and UNKNOWN survive.
Diagnostics stay outside the three canonical outputs.

An existing summary is accepted idempotently only with an eligible EOF checkpoint,
exact sink lengths/prefixes and byte-identical canonical summary. Otherwise
completion is refused. Fallback/generation counts are reported outside canonical
output; partial sinks are never completion evidence.

## Resource and evidence bounds

Existing R/K/W and K*W<=65536 apply. Payload cap is64MiB/depth8; decision including
LF is at most2048 bytes and summary4MiB. Codec buffers are additionally bounded.
These are retention limits, not measured process memory or allocation results.

The flat content-addressed evidence area is limited to192MiB/32 files, including
pending/partial copies and receipts. Copy uses8192-byte buffers. Existing names
are not trusted: length/hash are rechecked. Every needed copy is verified before
active bytes change. Failed copying or exhausted budget leaves sinks/checkpoints
untouched. Incomplete pending evidence is never overwritten; complete verified
pending objects can be promoted. No automatic deletion/archive/retry occurs.

If manual archival is needed, ensure no live owner, retain evidence outside the
active evidence area, then retry identical inputs/policy. Do not delete run.lock
based on PID. A new work directory is an alternative. Recovery is not unlimited;
output storage still grows with input, and disk-full is a failure.

## Evidence scope

The finite probe is a separate test-only executable; production CLI has no
pause/kill option. Parent tests kill only owned children with bounded deadlines
and cleanup. Mid-record/checkpoint probe instrumentation exposes flushed partial bytes;
it is not used for performance work.

Cases cover mid/post-decision cuts, between-sink flushes, pending payload/flush/
rename, summary publication, repeated EOF finalization, consecutive restarts,
valid fallback versus no candidate, foreign/ambiguous policy/future version,
initialization/start race, live/stale handles and evidence copy/reuse/budgets.
Fixtures include CRLF, malformed/oversized/invalid-UTF8 input, unterminated EOF,
MAX and UNKNOWN. A1/A2 golden bytes and numeric/window serialization caps are
checked.

Development findings are distinct: EOF finalization reproduced a pre-release
product defect before dirty-state/idempotence fixed it. The first live-lock
observation harness tried to read exclusively held files; only the observation
changed, not the product lock. A duplicated-policy regression separately guards
identity ambiguity before generic fallback. Local failed receipts were retained.
Actual Windows/Linux acceptance belongs to the exact published commit/CI run,
not this document alone; no production or performance guarantee follows.
