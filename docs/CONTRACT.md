# A1 contract (formatVersion 1)

## Local input

Only an explicitly synthetic manifest is accepted. It is a UTF-8 JSON object,
at most 65,536 bytes, with exactly these unique case-sensitive fields:

```json
{"formatVersion":1,"datasetId":"synthetic-a1","synthetic":true,"maxRecordBytes":4096,"maxStreams":16,"inputSha256":"<64 lowercase hexadecimal characters>"}
```

datasetId and source/instrument/epoch identifiers contain 1..32 ASCII letters,
digits, underscores or hyphens. maxRecordBytes (R) is 64..1,048,576 inclusive;
maxStreams (K) is 1..4096 inclusive. A1 retains at most K distinct fully parsed,
in-range (source,instrument,epoch) keys, including OHLC-invalid records.
An additional key is fatal; no silent eviction occurs.

Each nonempty physical record must be one JSON object with exactly these
unique case-sensitive fields. Unknown/missing/duplicate properties are invalid:

```json
{"source":"synthetic","instrument":"SYNTH1","epoch":"demo","sequence":1,"timestamp":"2026-01-01T00:00:00Z","open":100,"high":102,"low":98,"close":101,"volume":0}
```

sequence is an integer 1..Int64.MaxValue. OHLC are integer ticks 1..1,000,000,000,000;
volume is integer lots 0..1,000,000,000,000. Floating/exponent representations not
accepted by TryGetInt64 are schema-invalid, even if mathematically integral.
The timestamp is exactly a real UTC calendar date/time in yyyy-MM-ddTHH:mm:ssZ
(seconds only). Its ordering is not assessed. low <= open,close <= high is required.

No input field proves real-world provenance. Synthetic=true is a required
declaration, not an automatic detector of whether data actually came from a market.

## Physical bytes and primary reasons

The reader splits only on LF (0A). A trailing CR (0D) is included in raw length
and hash, and is removed only from the JSON parse view. Hence R includes CR for
CRLF records. Other JSON whitespace follows System.Text.Json. UTF-8 BOM is
rejected at the beginning of any physical record. Invalid UTF-8 is quarantined.
An unterminated final record is processed. An ending LF does not create a phantom
record; two consecutive LFs create an empty record. Empty files have zero records.

Exactly one primary reason is selected in this precedence:
RECORD_TOO_LONG, UTF8_BOM, UTF8_INVALID, EMPTY_RECORD, JSON_INVALID,
SCHEMA_INVALID, VALUE_OUT_OF_RANGE, OHLC_INCONSISTENT, VALID_RECORD.
The parser stops at the first applicable category; this is not an exhaustive
listing of every defect. JSON nesting deeper than 4 is JSON_INVALID.
A whitespace-only line may be JSON_INVALID rather than EMPTY_RECORD.
Unpaired escaped UTF-16 surrogates in JSON values/property names are JSON_INVALID;
their raw ASCII bytes alone would pass strict UTF-8 decoding.

For an oversized line the prefix retention never exceeds R bytes. All its raw
bytes are hashed until LF/EOF; the record is not parsed. The next record is
processed normally. Provenance length/hash exclude LF and include terminal CR.
Offsets and lengths count bytes from the file start, not Unicode characters.

## Outputs and failure

accepted.jsonl and quarantine.jsonl contain decisions with line (1-based),
offset (0-based), byteLength, rawSha256, reason and normalized record, or null
when no valid bounded record could be formed. Original malformed text is not
copied into output. Rejected input remains traceable through the original
hashed source and offset. Source and manifest hashes are in summary.json.

Output UTF-8 has no BOM and uses LF. Property order, reason order and row order
are stable for the same input/manifest bytes. Paths, wall-clock timestamps,
culture and performance measurements are excluded from canonical results.
The summary records counts and sorted reason totals, never all decisions in RAM.

Validation requires a seekable local source. The CLI opens it with FileShare.Read
and compares full input SHA-256 before and after validation. Windows cooperating
write/delete opens are excluded while the handle is held. On other filesystems
this may not prevent writes. Pre/post hashes are consistency checks, not proof
against adversarial change-and-restore or an authenticated source.

The output directory must be new. A CreateNew run.lock file excludes a concurrent
owner even if directory creation races. The lock file remains as an inert
ownership receipt after the handle closes. No retry or resume protocol exists.
Data sinks flush before the final hash check. summary.pending is flushed and
renamed to summary.json last; only a valid completed summary marks completion.
Partial sinks/pending files without that summary must not be consumed as a
completed run. Exit 0/2 follows completion; fatal failures return 1, leaving
partial files for inspection without overwriting or cleanup. Power-loss durability
and atomic rename semantics across all filesystems are not guaranteed.

A completed result may have quarantine and says nothing about sequence/market
completeness. A1 never interprets sequence or claims duplicate detection.
