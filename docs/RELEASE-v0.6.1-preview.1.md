# 0.6.1-preview.1 — offline CSV usage diagnostics

For exact `validate-csv`, invalid arguments explain whether argument count,
price scale or volume scale failed, in that precedence. Exit 1, empty stdout and
two fixed OS-newline stderr lines precede target input/output I/O. Paths, values
and raw rows are not echoed. Invariant unsigned scale 0..8 grammar and leading
zeroes remain; other commands retain global usage.

No new CSV profile or parser behavior. Precision remains exact: -2.50 at scale 2
normalizes to the string "-250"; 1.230 is quarantined rather than rounded.
Whole-file framing failures retain a prefix without publishing a completed
summary. LOCAL_ROW_CHECKS_ONLY; provenance UNVERIFIED; ordering, deduplication and
completeness NOT_CHECKED. No live feed/account/trading, data-rights or authenticity
claim.

SDK 10.0.404 / bundled .NET 10.0.15 servicing and CLI 0.6.1-preview.1 are
synchronized. Source locks, four parser/vendor source pins, Directory.Build.props
and historical evidence remain unchanged. The separate CSV action intentionally
retains its 0.6.0-preview.1 binaries and exact archive pins. No new performance
study or production-performance claim.

## Observed source validation

The runtime/test source at
[5773d833dbf724430762df23f917e40ff7f8ef53](https://github.com/pouriajamali/market-data-integrity-lab/commit/5773d833dbf724430762df23f917e40ff7f8ef53)
passed [Windows and Linux CI](https://github.com/pouriajamali/market-data-integrity-lab/actions/runs/42879282267)
on 2026-11-10:

- Windows x64 and Ubuntu 24.04 glibc x64 each passed 165 tests, with no failures,
  errors, skips or unrun cases, under .NET 10.0.15.
- Each native job built and extracted its self-contained archive, then passed
  60 actual apphost invocations, including the argument diagnostics and separate
  clean, precision-quarantine and fatal-framing cases.
- Downloaded smoke receipts identify the exact source commit and runtime
  10.0.15 on both RIDs. Their evidence ZIP hashes match GitHub's artifact digests.
  The hosts may have SDKs installed; the checks use empty PATH and nonexistent
  DOTNET_ROOT and do not establish an empirically SDK-free host.

The independent [CSV action CI](https://github.com/pouriajamali/market-data-integrity-lab/actions/runs/42879282239)
also passed on Windows and Ubuntu, observing exits 0/2/1 using its unchanged
signed 0.6.0-preview.1 downloads. This does not repin the action to the new CLI.

## Distribution and verification

Runtime bundles target Windows x64 ZIP and Linux glibc x64 tar.gz, alongside a
source ZIP, build manifest, licenses and checksums. They are directory bundles,
not installers or OS-signed binaries. Native OS dependencies still apply.

The source-run evidence artifacts contain verification receipts; its runtime
archives are not retained by the existing main-run policy. CI log archive hashes
are distinct from independently downloaded release-archive hashes. Source CI
does not establish tag signing, checksum signing or public asset availability;
those identities and actual download verification belong to the publication
receipt.
