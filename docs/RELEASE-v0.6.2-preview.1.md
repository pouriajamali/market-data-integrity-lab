# 0.6.2-preview.1 — technology refresh (unreleased source preparation)

This candidate targets SDK11.0.100, net11.0, stable C#15 and runtime11.0.0.
No preview-language features, AOT, ReadyToRun, trimming or single-file publishing
are introduced. No behavior, feature, wire/JSON/checkpoint schema, fixture or
benchmark change is intended. Version-only changes establish no quality or speed
gain. All application/parser source and the Admission vendor pins remain frozen.

The published0.6.1-preview.1 CLI, its historical165 tests/60 apphost invocations
per native OS, and the independent0.6.0-preview.1 CSV action remain unchanged.
Those receipts are not evidence of this candidate.

## Source-preparation status

Framework/runtime/version and declared launcher/package-check literals are edited.
Source locks still contain net10.0 and are intentionally not regenerated before
SDK admission. No SDK/native/helper execution, restore, build, test, package,
remote push, CI, tag, signature or publication has occurred in this stage.
No acquisition, dependency hash, native result or release URL is fabricated.

## Required qualification before delivery

- One reviewed immutable shared SDK11 Win/Linux handoff, then exact per-consumer
  invocation identity; aligned/audited source and per-RID locks with unchanged
  third-party pins and actual11.0.0 runtime notices.
- Reviewed project03 guard/control identities, real Windows Job and Linux
  cgroup-v2 containment in the single admitted resource lane. New WSL shell
  success does not establish user-systemd/delegation or hardcap readiness.
- Unchanged independent golden/negative oracles, full existing native suite and
  actual extracted-apphost checks for both win-x64 and linux-x64/glibc.
- Native OS qualification and the raised x86-64-v2 CPU prerequisite.
- Actual protection/workflow route and finite zero-additional-cost CI headroom,
  including repeated old adapter CI on main. Do not bypass the adapter's gate.
- Consolidated exact-source/tag/asset approval; signed annotated new tag, exact
  native tag CI, same-commit tested archives, all six full public downloads and
  known-key checksum signature, manifests/licenses and live landing-page checks.

The old releases/tags/assets/signatures and historical performance evidence stay
immutable. Existing behavior retains clean0, completed-quarantine2 and fatal1,
exact integer scaling, fixed safe diagnostics and no live feed/account/trading.

Microsoft references: [C#15](https://learn.microsoft.com/en-us/dotnet/csharp/whats-new/csharp-15),
[.NET11 breaking changes](https://learn.microsoft.com/en-us/dotnet/core/compatibility/11),
[x64 hardware minimum](https://learn.microsoft.com/en-us/dotnet/core/compatibility/jit/11/minimum-hardware-requirements).
