# Offline OHLC CSV profile: ohlc-csv-v1

B2 checks one local file against an explicit physical-line profile. It accepts
user-supplied data without calling it synthetic or authentic. It does not connect
to a venue/account, place orders, infer venue sequence numbers, or convert CSV
into the synthetic A3 recovery contract.

~~~text
Integrity validate-csv <input.csv> <new-output> <price-decimals:0..8> <volume-decimals:0..8>
Integrity csv-demo <new-directory>
~~~

Use the apphost prefix for your OS. Both scales are mandatory. The generated
CSV demo uses2/0 and has6 rows:2 accepted/4 quarantined, with deliberately
invalid values. Its source kind is GENERATED_EXAMPLE_DECLARED. Normal CLI input
is always USER_SUPPLIED_UNVERIFIED; declaration is not authenticated provenance.

## Exact file dialect

- UTF8; LF or CRLF separators. The last row may omit its LF. A final LF adds no
  phantom record. A blank physical data row is an EMPTY_ROW quarantine.
- A UTF8 BOM is allowed only before the first header byte. Header BOM and CR
  remain included in raw header length/hash. Unexpected U+FEFF anywhere in a data row, including quoted cells, is fatal.
- The decoded header must be exactly these6 names, in this order and case:
  `timestamp,open,high,low,close,volume`. Header fields may be quoted.
  Duplicate/missing/extra/misordered/case-changed names fail before output writes.
- Fields may be unquoted or double-quoted. Double quotes inside a quoted field
  must be escaped by another double quote. No whitespace is stripped.
- A quote must close on its physical line. An unclosed quote is whole-file
  CSV_UNCLOSED_QUOTE, never a guessed continuation. Invalid quote grammar,
  encoding or control characters are also whole-file failures. Lone CR
  is a control error, including at EOF; terminal CR is stripped only when an LF
  terminator is physically present.
- This is a restricted physical-line CSV profile, **not complete RFC4180 CSV**.
  [RFC4180](https://www.rfc-editor.org/rfc/rfc4180.html) permits quoted multiline
  fields; this implementation refuses them. No delimiter/encoding/header/unit
  detection, column mapping or locale guessing is performed.

An empty file or a valid header without data is fatal CSV_NO_DATA. No output
directory is created for these cases or for a bad header.

## Numbers and timestamps

Timestamp is exactly ASCII `yyyy-MM-ddTHH:mm:ssZ`, with a valid Gregorian date
and clock time. UTC is explicit; offsets, fractional seconds, leap seconds and
timezone inference are not supported. The timestamp is not checked for order,
duplication or expected market/calendar intervals.

Numeric syntax is optional minus followed by at least one ASCII integer digit,
optionally a decimal point with at least one fractional digit. Prices may be
zero/negative. Volume must be nonnegative. Plus signs, exponent notation,
thousands separators, whitespace, NaN/Infinity and locale digits are rejected.

Each number is converted directly to a signed64-bit integer at the declared
decimal scale, without float or decimal rounding. An extra fractional digit is
rejected even when zero (`1.230` at scale2 is not accepted). Integer/shorter
fractions are padded exactly (`1.2` at scale2 becomes120). Leading zeroes and
negative zero normalize numerically; raw hashes retain original byte identity.

Scaled values must fit Int64, including MinValue/MaxValue. All four price fields
use the same price scale; volume has its separate scale. These are decimal places,
not proof of correct tick size, currency, instrument, unit or vendor metadata.

The OHLC condition is `low <= open,close <= high`. Normalized scaled values are
**canonical integer strings** in JSON, preventing accidental binary-float loss
in downstream JSON consumers. For example, `"-2.50"` at scale2 becomes `"-250"`.

## Decisions and completion

Output is local, deterministic UTF8/LF:

- `accepted.jsonl`, `quarantine.jsonl`: physical line (header is line1), byte
  offset, raw byte length/hash, reason, known field name and a normalized record
  when available. Raw CSV text is not copied into diagnostics.
- `summary.json`: exact input/header identities, byte count, policy/scales/hash,
  bounds, counters and reason totals. No source filename, directory or clock.
- `run.lock`: cooperating-writer ownership handle; never an automatic PID lease.

Raw row length/hash exclude LF and include a terminal CR. Input hash includes
all original bytes. Outputs require a fresh destination; existing files/dirs
are refused without mutation. They are created lazily after the first data row
can be parsed; failures retain any partial files already written.

Only flush/close of both sinks, successful bounded post-hash/length verification
and final summary.pending rename publish completion. A whole-file failure never
publishes a prefix as a completed result. There is no CSV resume, repair or A3
checkpoint conversion.

Exit0: at least one data row and no quarantines. Exit2: completed with quarantines.
Exit1: usage/fatal/incomplete. Neither0 nor2 certifies backtest readiness.
CSV-demo exit0 only means its expected generated outcomes matched.

## Errors and resource limits

Whole-file fatal codes: CSV_INPUT_LIMIT, CSV_ROW_LIMIT, CSV_RECORD_TOO_LONG,
CSV_NO_DATA, CSV_HEADER_INVALID, CSV_UTF8_INVALID, CSV_UNEXPECTED_BOM,
CSV_CONTROL_CHARACTER, CSV_SYNTAX_INVALID, CSV_UNCLOSED_QUOTE,
CSV_SOURCE_CHANGED. IO failures are also fatal. Safe code/line/count diagnostics
never echo the source values or input path.

Well-framed row quarantines: EMPTY_ROW, CSV_COLUMN_COUNT_INVALID,
TIMESTAMP_INVALID, NUMBER_FORMAT_INVALID, NUMBER_PRECISION_EXCEEDED,
NUMBER_OUT_OF_RANGE, NEGATIVE_VOLUME, OHLC_INCONSISTENT. VALID_ROW is accepted.

Precedence: global byte/row caps; raw row cap; UTF8/BOM; empty row; control/quote
grammar; column count; timestamp; numbers in open/high/low/close/volume order;
negative volume; OHLC. Only one primary row reason is emitted. Framing/encoding
ambiguity is fatal so subsequent continuation fragments cannot be silently
accepted. Oversized rows are drained/hash-framed with bounded retention, then
fatal because their quote closure cannot be safely established from a prefix.

Hard caps:256MiB actual source bytes,1,000,000 physical data records and4096 raw
bytes per physical line including CR/BOM but excluding LF. They are enforced
during initial hash, record reads and post-hash, not solely by initial file size.
No dynamic limit increase or smaller substituted run occurs. Disk/output grows
with rows; disk-full fails with partial evidence retained.

One input handle spans pre-hash/read/post-hash with FileShare.Read. Length and
actual hashed byte counts must agree. These are same-handle consistency checks,
not authenticated source identity, hostile change-and-restore/ABA defense or an
atomic filesystem snapshot. Ownership assumes enabled .NET locking, cooperating
writers and supported local filesystem behavior, as in the existing lab.

## What is and is not checked

Every completed report says LOCAL_ROW_CHECKS_ONLY, market provenance UNVERIFIED,
and ordering/deduplication/completeness NOT_CHECKED. Repeated or decreasing
timestamps may pass local row checks. A well-formed row does not establish the
file's instrument, bar interval, market coverage, data rights, fill quality or
economic value. User files stay local; only newly generated examples are in the
source/tests/packages.

[CsvTests](../tests/Integrity.Tests/CsvTests.cs) contain handwritten scaled/reason
oracles, precision/min/max cases, framing/BOM/CRLF/EOF checks, culture-independent
bytes, no-data/header/no-overwrite behavior and injected byte/read/mutation/row
failures. Matching-OS extracted apphost checks exercise the ready packages.
The original synthetic contracts/golden bytes and historical A4 evidence remain
separate and unchanged. B2 has not been performance-measured.

## Development finding: terminal CR

A local pre-release regression exposed lone CR at EOF being stripped as though
it were CRLF. The parser now strips terminal CR only when raw offset/length and
the verified source length establish a physical LF. The shared legacy reader
was not changed. [LoneCrAtEofIsNotSilentlyTreatedAsCrLf](../tests/Integrity.Tests/CsvTests.cs)
reproduced the failure before the fix; the complete138-case suite passed after.

The [local red receipt](evidence/csv-eof-cr-red.txt) is pre-release development
evidence, not historical public CI or an independently rebuilt pre-fix commit.
Only UTF8/BOM/newline normalization and private-workspace-prefix substitution
were applied. Original private SHA256:97b97fc3ff71f5a8e4a5dac1471aee1e281ddc9afe2b58395ca7e952dec21e7b.
Published normalized SHA256:bad7f620bcb56d55b5df1804bb4b63e55a58a63e189670d3db819a8cfda6195e.

## Review finding: embedded BOM

Coordinator review found the first candidate checking only a data row prefix
for U+FEFF, while the contract forbids it anywhere in data. Three focused cases
(unquoted/quoted volume and timestamp) reproduced ordinary quarantine instead
of the required file-fatal refusal. The check now covers the entire decoded
data row. Existing accepted prefix files remain, no final summary is published,
and the continuation row is never processed. The complete141-case suite passed
after correction; ready-package checks also cover embedded BOM.

[Normalized local red receipt](evidence/csv-embedded-bom-red.txt), not historical
public CI: only UTF8/BOM/newline normalization and private-prefix substitution.
Original private SHA256:c8d2f2b1fe39b4108c3453d01329958bc22f4f4b7a746b45fe30e78c2449606b.
Published normalized SHA256:cdfd2fea5128e95125881194e0e44202b3435441f2aa2b9f5e7a6b2d1e9b709d.

## Command argument refusal (source M1)

The unpublished 0.6.1-preview.1 candidate adds command-specific usage diagnostics
for the exact first token `validate-csv`. Existing published0.6.0 downloads and
the separate pinned CSV action retain their original usage behavior.

Arity is checked before price scale, then volume scale. Arguments must be exactly
`<input.csv> <new-output> <price-decimals> <volume-decimals>`. Scales retain the
invariant unsigned integer grammar/range0..8, including leading zeroes; signs,
whitespace, decimals, non-ASCII digits and overflow are invalid.

| Condition | First stderr line |
| --- | --- |
| Missing/extra arguments | `usage:CSV_ARGUMENT_COUNT; expected 4 arguments after validate-csv` |
| Invalid price scale | `usage:CSV_PRICE_SCALE_INVALID; price-decimals must be an unsigned integer in 0..8` |
| Invalid volume scale | `usage:CSV_VOLUME_SCALE_INVALID; volume-decimals must be an unsigned integer in 0..8` |

Second line, followed by the native OS newline:
`usage: Integrity validate-csv <input.csv> <new-output> <price-decimals:0..8> <volume-decimals:0..8>`.
Both lines use OS newlines (CRLF on Windows, LF on Linux), stdout is empty and exit
is1. Fixed diagnostics contain no supplied token, path, raw row or exception text.
Usage refusal precedes target-file/directory I/O; process/runtime initialization
may perform unrelated I/O. Existing output and nonexistent input do not override
the argument reason. Lookalike commands and zero arguments keep global usage.

Independent invented acceptance examples, each after the exact header and with
price scale2/volume scale0:

~~~text
2026-01-01T00:00:00Z,-2.00,-1.00,-3.00,-2.50,0
~~~

This row returns0 with closeScaled string `"-250"`.

~~~text
2026-01-01T00:01:00Z,1.230,2,0,1,1
~~~

This row returns2 with completed summary and NUMBER_PRECISION_EXCEEDED, field
open, record null. The extra zero is not discarded. In a separate fixture, an
unclosed quoted physical row after a good prefix returns1/CSV_UNCLOSED_QUOTE;
no final summary or accepted continuation is published. A usage error is neither
a new row-reason code nor a new CsvFatalException code.

[CsvCliTests](../tests/Integrity.Tests/CsvCliTests.cs) exercises the real synchronous
entry point under exclusive Console/culture ownership, including en-US/fa-IR,
sentinels, exact global usage, leading-zero and boundary scales.
[Extracted package checks](../tools/check_binary.py) separately invoke actual
apphosts and preserve raw OS newline bytes. Native Linux validation remains a
later CI gate for this local Windows candidate.
