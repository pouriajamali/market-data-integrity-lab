# 0.6.2-preview.1 - technology refresh (unreleased candidate)

Targets: SDK11.0.100, net11.0, stable C#15 and runtime11.0.0. No preview-language
features, AOT, ReadyToRun, trimming, single-file publishing or application/parser
behavior changes are introduced. Fixtures, integer scaling, fatal versus
quarantine behavior and the four vendor pins remain unchanged. No new benchmark
or performance claim is made.

## Verified Linux evidence before the owner CI stop

- [Input CI44096474043](https://github.com/pouriajamali/market-data-integrity-lab/actions/runs/44096474043),
  source `e30aae57bc0f6cf7ba3625d9584db1c35372567f`: actual NuGet require-mode
  signature verification of17 unchanged test packages; four actual net11 source
  locks and a separate Linux RID lock. SDK implicit native downloads were
  AspNetRuntime and CoreRuntime11.0.0; the qualified SDK supplies the apphost pack.
  This is native NuGet evidence, not a separate per-certificate current-CRL
  supplement for every test-package chain.
- [Product CI44098116245](https://github.com/pouriajamali/market-data-integrity-lab/actions/runs/44098116245),
  source `f9f904fbfa0e0dfe86196af8b110b0eff49aed0e`:165 units passed with0 errors,
  failures, skipped or not-run tests. Measurement oracles, frozen-summary
  recomputation, tiny worker correctness and A1/A2 scenarios passed. The complete
  run failed before publishing because the consumer guard compared NuGet's raw
  signed-archive SHA512 sidecar with its normalized lock contentHash.
- [Packaging CI44100029409](https://github.com/pouriajamali/market-data-integrity-lab/actions/runs/44100029409),
  source `8c48e6ad827fcfc492b29a754e21361bcca71f0a`: the corrected guard checked
  raw SHA512 and normalized metadata separately. Exact archive-to-cache native
  member/notices checks, self-contained publishing/packaging and60 extracted
  apphost invocations passed. A source-identity gate reused the preceding165
  units/scenarios; those tests were not rerun in this packaging call.

The Linux candidate has205 payload files, is36,614,199 bytes and has SHA256
`742dcbd0e911f0e747169df8026a39580134e9daa4e9a7060acaef1c5c5cbffd`.
The actual CI artifact was downloaded, its GitHub artifact digest checked, and its
archive SHA256/source/runtime manifest matched. This is a retained CI candidate,
not a public release or evidence that Windows passed. Each completed own worker
has retained-handle terminal/population-zero evidence and its lane lease was
released. Detailed receipts remain in the existing project checkpoint/outputs.

## Current owner boundary and incomplete gates

On2026-11-17T14:26:39Z the owner stopped GitHub Actions because the quota was
exhausted. Subsequent CI was **not run per owner instruction**. The documentation
HEAD uses `[skip ci]` on the existing feature branch after inspecting both local
and remote workflow triggers. There is no `pull_request_target` or other
unsupported skip event. No workflow/security/billing setting or required check
was changed; no new tag/release/PR/merge was created.

Windows SDK host qualification is an existing producer fact. Windows native
runtime-package acceptance, actual Windows RID lock generation and the165-unit /
60-apphost Windows product gate are still incomplete. No admitted local Windows
route closes those gates. The established preview publication pipeline requires
Actions plus the exact final source/tag checks, actual tested archives, known-key
checksum signature and public download/landing-page verification. Publication
therefore remains blocked under the owner's stop; no local bypass is used.

The public0.6.1-preview.1 CLI and independent0.6.0-preview.1 CSV action retain
their existing assets, tags and signatures. Existing clean0/quarantine2/fatal1
behavior, offline boundaries, licenses and historical performance evidence remain
unchanged. SDK11 x64 candidates require x86-64-v2 hardware.
