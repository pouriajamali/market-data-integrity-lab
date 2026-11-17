# 0.6.2-preview.1 - .NET 11 technology refresh

Self-contained Windows x64 and Linux glibc x64 previews use SDK11.0.100,
net11.0, stable C#15 and bundled runtime11.0.0. Both archives identify the
exact runtime source `8c48e6ad827fcfc492b29a754e21361bcca71f0a`. No parser/application behavior, fixtures,
fatal/quarantine semantics, integer scaling or four vendor pins changed.
No AOT, trimming, single-file, ReadyToRun or new performance claim is introduced.

## Verification

- Linux:165 units, measurement oracles, frozen-summary arithmetic, tiny workers
  and A1/A2 passed in [run44098116245](https://github.com/pouriajamali/market-data-integrity-lab/actions/runs/44098116245).
  That run failed before publication on a raw-versus-normalized NuGet cache-hash
  comparison. The corrected [packaging run44100029409](https://github.com/pouriajamali/market-data-integrity-lab/actions/runs/44100029409)
  verified unchanged product/input/oracle source, native archive/cache identities,
  packaged the exact source above and passed60 extracted-apphost invocations.
  Healthy Linux tests and packages were reused without replay.
- Windows: the existing official SDK tree was copied privately, all5480 members
  matched and all eight fixed SDK qualification commands passed.165 units with
  zero errors/failures/skips/not-run, measurement oracles, frozen arithmetic,
  tiny workers and A1/A2 passed locally at the exact runtime source above.
  Qualified SDK verification of the two original native runtime packages passed;
  normalized hashes were taken from its actual reports. The actual RID graph,
  locked restore, native cache/member/notices comparison, self-contained publish
  and60 extracted-apphost checks passed. No Actions were used for these stages.
- Both direct-apphost checks used paths/cwd containing spaces, empty PATH and
  nonexistent DOTNET_ROOT. This is not a claim that the machines had no SDK.
  All own workers reached observed active-process zero and released their leases.
  Checksums authenticate the actual tested archives; packaging did not rebuild Linux.

Windows was tested on the actual local Windows26H2 client build26300.9974 under
4GiB/128-process/2-CPU ceilings, with unchanged held-worker/deadline guards.
This is project compatibility evidence, not GitHub Server2025 execution or a
claim of explicit Microsoft .NET11/26H2 support-matrix certification.
Linux was tested on Ubuntu24.04 x64. Both require x86-64-v2 hardware;
native OS prerequisites apply. Binaries have SSH provenance, not OS code signing.

## Direct delivery without Actions

After the quota stop, the owner authorized local testing/signing and direct
GitHub publication. Exact release-tag CI was **not run per owner instruction**.
The signed annotated `cli-v0.6.2-preview.1` tag points to the exact tested runtime source;
its prefix matches neither existing Actions tag filter. Both workflows have
no release/create trigger. Main/feature delivery commits use `[skip ci]`.
Workflow, branch protection, security, trust stores and billing remain unchanged.

The plain console Windows publish uses the documented SDK property
`DisableTransitiveFrameworkReferenceDownloads=true`; its actual graph requires
CoreRuntime11.0.0. This omits unreferenced framework predownloads and retains
failure for missing required packs. The original attempt reported unused Desktop
runtime NU1101 before publishing; it is retained as failed evidence.

The source ZIP preserves the immutable tested commit. Current delivery notes,
the actual Windows RID lock and portable result summary are provided separately
in `RELEASE-EVIDENCE.zip` and in the repository's current documentation.
Use [PROVENANCE.md](../PROVENANCE.md) to verify the known-key checksum signature
before extracting either complete runtime directory.

Existing CLI releases and the independent0.6.0-preview.1 CSV action, its signed
assets and exact pins remain unchanged. No new benchmark or production claim.
