# Market Data Integrity Lab - synthetic validation and recovery

[Windows/Linux CI](https://github.com/pouriaquant91/market-data-integrity-lab/actions/workflows/verify.yml) · [Source preview v0.4.0-preview.1](https://github.com/pouriaquant91/market-data-integrity-lab/releases/tag/v0.4.0-preview.1)

Offline C#/.NET validation for explicitly synthetic OHLC records. A1 provides
bounded framing, strict schema/OHLC checks and provenance. A2 adds opt-in
explicit-sequence windows. A3 adds a separate process-crash checkpoint/resume
path with validated fallback and preserved failure evidence.

[Input contract](docs/CONTRACT.md) · [Sequence contract](docs/SEQUENCE.md) ·
[Recovery contract](docs/RECOVERY.md) · [Design](docs/DESIGN.md) · [History](CHANGELOG.md)

## Build and inspect

Requires .NET SDK **10.0.403**, pinned in global.json. Application runtime uses only
the base library. Tests restore pinned xUnit v3 4.0.2; no market/account/network
service is used by validation.

~~~sh
dotnet restore tests/Integrity.Tests/Integrity.Tests.csproj --locked-mode
dotnet build tests/Integrity.Tests/Integrity.Tests.csproj -c Release --no-restore
dotnet run --project tests/Integrity.Tests -c Release --no-build
~~~

The bounded byte reader drains/hashes lines beyond R without retaining them whole.
Each decision has physical line/offset/byte length/raw hash and a stable reason.
Pre/post source hashes guard consistency. Canonical output excludes paths, clocks
and measurements. Only final summary publication marks a completed run.

## Existing A1 and A2 paths

~~~sh
dotnet run --project src/Integrity -c Release --no-build -- generate artifacts/demo 34
dotnet run --project src/Integrity -c Release --no-build -- validate artifacts/demo/manifest.json artifacts/demo/input.jsonl artifacts/result
dotnet run --project src/Integrity -c Release --no-build -- generate-sequence artifacts/sequence-demo
dotnet run --project src/Integrity -c Release --no-build -- validate-sequence artifacts/sequence-demo/manifest.json artifacts/sequence-demo/input.jsonl artifacts/sequence-result
~~~

A1 intentionally gives32 accepted/2 quarantined; A2 gives4 accepted/10 quarantined
from14 records with UNKNOWN for unattributable input. Exit2 means completed with
quarantine, exit0 completed without quarantine, exit1 fatal/usage failure.
Use new directories; these commands never implicitly resume.

A2 retains last W consumed sequence numbers. Identical normalized payload is a
duplicate only inside the window; conflict/gap blocks that declared stream.
Old evicted records are STALE_OUTSIDE_WINDOW, not proven duplicates. Baselines/
epochs come only from manifest. Expected bad-OHLC records consume sequence.
MAX ends the range without increment. UNKNOWN/partial/prefix assessments never
assert feed completeness. A1/A2 sample bytes remain pinned by regressions.
[Original A1](https://github.com/pouriaquant91/market-data-integrity-lab/releases/tag/v0.1.0-preview.1) ·
[Original A2](https://github.com/pouriaquant91/market-data-integrity-lab/releases/tag/v0.2.0-preview.1).

## Opt-in A3 recovery

Reuse sequence-demo generated above; recovery-result must be new for start.

~~~sh
dotnet run --project src/Integrity -c Release --no-build -- start-resumable artifacts/sequence-demo/manifest.json artifacts/sequence-demo/input.jsonl artifacts/recovery-result 2
dotnet run --project src/Integrity -c Release --no-build -- resume artifacts/sequence-demo/manifest.json artifacts/sequence-demo/input.jsonl artifacts/recovery-result 2
~~~

Both commands return2 for this deliberately quarantined sample. The second
verifies already completed output; **this alone is not a crash demonstration**.
Finite real-child cut cases are separate:

~~~sh
dotnet run --project tests/Integrity.Tests -c Release --no-build -- -class Integrity.Tests.RecoveryTests
~~~

A3 checks source/manifest/policy, bounded windows/counters and both sink prefixes
before any tail truncate. Two committed slots preserve the previous generation.
Rejected/pending checkpoints and discarded tails must have verified evidence
copies first. Budget192MiB/32 files includes partial copies; exhaustion/conflict
requires manual archival, never automatic deletion. EOF finalization is idempotent.

Single-writer ownership requires enabled .NET locks, cooperating writers and
tested local filesystem behavior. Unix locks are advisory/best-effort and can be
disabled by runtime settings. The recovery contract gives the exact policy key,
commit/cursor/fallback rules and limits. No PID-based lock deletion occurs.

Tests retain synthetic child/failure artifacts under ignored artifacts/recovery-tests/.
CI retains generated evidence briefly; exact-commit logs and reproducible source
tests remain the primary references. This is a source-only preview, not an
installer, hosted application or production engine.

## Guarantees still outside scope

No calendar-gap inference, stream repair, reordering, market adapter, authenticated
feed, broker connection, trade or private account exists. Recovery is for process
crashes under the stated local conditions, not power loss, hostile filesystem or
distributed exactly-once. R/K/W, checkpoint and evidence caps bound retained
application structures; they do not impose a whole-process memory guarantee.
Finite one-host observations and their limitations are linked below.
Tests/CI are not venue latency, feed-completeness, production or profit evidence.

## Provenance

Independent synthetic portfolio code created with AI assistance for Pouria Jamali.
No private project source, fixtures, schemas, market/account data or business
rules were copied. Source and executed checks support this sample's own behavior,
not historical employer outcomes or an independently assessed individual code share.

## Finite measurement evidence

[Case study](docs/CASE-STUDY.md) explains the design, real development failures,
tradeoffs and finite observations. [Claim-to-evidence map](docs/EVIDENCE.md) links
behavior to code/tests/CI. [Metric definitions and protocol](docs/MEASUREMENTS.md)
keep operation timing, process timing, allocation and lifetime peak memory separate.

The one-shot study completed 21 children in 33.969s below both resource caps.
A3 was slower and allocated more managed bytes in the tested workload; all
canonical decisions remained equal to A2. See exact observations and scope above. One local Windows host; Linux is correctness
CI, not a second performance experiment. No isolated checkpoint overhead, tail
latency, HFT, production or profit claim follows.

[Source ZIP](https://github.com/pouriaquant91/market-data-integrity-lab/releases/download/v0.4.0-preview.1/market-data-integrity-lab-v0.4.0-preview.1.zip)
and [SHA256SUMS](https://github.com/pouriaquant91/market-data-integrity-lab/releases/download/v0.4.0-preview.1/SHA256SUMS.txt)
are source artifacts requiring the SDK, not application installers.

[MIT license](LICENSE) applies to the independent sample. [Attribution](NOTICE.md)
states AI assistance and the limits of individual/employer claims.
