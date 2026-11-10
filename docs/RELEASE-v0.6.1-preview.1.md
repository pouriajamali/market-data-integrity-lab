# 0.6.1-preview.1 — offline CSV usage diagnostics

Local source candidate; publication requires a separate review and approval.
Existing download/action links intentionally retain0.6.0-preview.1 until then.

For exact `validate-csv`, invalid arguments explain whether argument count,
price scale or volume scale failed, in that precedence. Exit1, empty stdout and
two fixed OS-newline stderr lines precede target input/output I/O. Paths, values
and raw rows are not echoed. Invariant unsigned scale0..8 grammar and leading
zeroes remain; other commands retain global usage.

No new CSV profile or parser behavior. Precision remains exact: -2.50 at scale2
normalizes to string"-250";1.230 is quarantined rather than rounded. Whole-file
framing failures retain a prefix without publishing a completed summary.
LOCAL_ROW_CHECKS_ONLY; provenance UNVERIFIED; ordering/dedup/completeness
NOT_CHECKED. No live feed/account/trading, data-rights or authenticity claim.

SDK10.0.404/bundled .NET10.0.15 servicing, CLI0.6.1-preview.1 and finite version
guards are synchronized. Source locks, four parser/vendor source pins,
Directory.Build.props, CSV action/pinned0.6.0 binaries and all historical
evidence remain unchanged. No new measurement/performance claim.

Required before publication: passing complete source suite, extracted matching-OS
Windows/Linux apphost checks bound to the exact signed commit, signed annotated
tag/checksums and actual downloadable archive verification. Windows local
evidence alone is insufficient. Runtime bundles remain Windows x64 ZIP and
Linux glibc x64 tar.gz plus source ZIP, manifest/licenses and checksums;
these are directory bundles, not installers or OS-signed binaries.
