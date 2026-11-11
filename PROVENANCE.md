# Verify release provenance

This repository publishes the portfolio's public SSH signing key in
[SIGNING-PUBLIC-KEY.pub](SIGNING-PUBLIC-KEY.pub). The expected ED25519 fingerprint is:

```text
SHA256:AgU5bZlJSjUu0KsWMmVEEr+BfXpcMVNroTA33bHcQdU
```

Confirm that fingerprint through an independently trusted maintainer channel or
a previously trusted checkout before accepting a new download. A key and a
fingerprint obtained together from the same untrusted release are not independent
identity proof. The key is public; never share or request the private signing key.

## What is signed

The commands below verify [**v0.6.1-preview.1**](https://github.com/pouriajamali/market-data-integrity-lab/releases/tag/v0.6.1-preview.1). Its
`SHA256SUMS.txt.sig` is an SSH signature over the exact `SHA256SUMS.txt` bytes in
the `file` namespace. Verify that signature first, then the chosen archive's hash.
A matching unsigned hash alone is not authenticated provenance.

The legacy v0.6.0-preview.1 tag is **lightweight, not a signed annotated tag**.
Its existing checksum signature remains separate legacy evidence; this is not a
claim that the old tag is signed. Current signed commits are separate evidence. The legacy tag,
archives and signatures remain unchanged; new release tags must be annotated
and signed. No replacement release or retroactive tag rewrite is implied.

New release policy: sign commits and annotated release tags with the known key,
sign checksums of the actual distributed assets, and retain the public key,
independent fingerprint, verification instructions, license and attribution.
Documentation-only commits do not require a new release. Existing tags, assets
and signatures are not replaced just to add this guide. An older archive may not
contain this new guide/key file; the independently pinned public key here can
verify its existing checksum signature.

SSH provenance and GitHub's Verified status do not mean Windows Authenticode or
another OS binary signature. Application binaries remain unsigned. A valid
signature establishes integrity under the trusted key; it does not prove runtime
safety, production suitability or exclusive ownership, and does not prevent copying.
The [MIT license](LICENSE), copyright and attribution remain unchanged.

## POSIX shell

Use a fresh directory, OpenSSH with `ssh-keygen -Y`, curl, awk and GNU
`sha256sum`. This example checks the small source archive; use the exact filename
from the signed checksum file to verify a different OS archive.

```sh
repo='pouriajamali/market-data-integrity-lab'
tag='v0.6.1-preview.1'
archive='market-data-integrity-lab-v0.6.1-preview.1.zip'
for name in "$archive" SHA256SUMS.txt SHA256SUMS.txt.sig SIGNING-PUBLIC-KEY.pub; do
  curl --fail --location --output "$name" "https://github.com/$repo/releases/download/$tag/$name" || exit 1
done
```

```sh
expected='SHA256:AgU5bZlJSjUu0KsWMmVEEr+BfXpcMVNroTA33bHcQdU'
actual=$(ssh-keygen -lf SIGNING-PUBLIC-KEY.pub | awk '{print $2}')
test "$actual" = "$expected" || { echo 'Signing-key fingerprint mismatch' >&2; exit 1; }
printf 'pouriajamali %s\n' "$(cat SIGNING-PUBLIC-KEY.pub)" > allowed-signers
ssh-keygen -Y verify -f allowed-signers -I pouriajamali -n file -s SHA256SUMS.txt.sig < SHA256SUMS.txt || exit 1
entries=$(awk -v name="$archive" '$2 == name { count++ } END { print count + 0 }' SHA256SUMS.txt)
test "$entries" -eq 1 || { echo 'Chosen archive missing or duplicated in signed checksums' >&2; exit 1; }
test -f "$archive" || exit 1
sha256sum --check --strict --ignore-missing SHA256SUMS.txt || exit 1
```

The signature command should report a good `file` signature. The selected
archive must report `OK`. Other archives absent from this fresh directory are
ignored; every archive that is present must match. Stop on any failure.

## PowerShell 7 on Windows

Use a fresh directory and an existing OpenSSH `ssh-keygen` supporting `-Y`.
These commands do not install tools or change security settings.

```powershell
$ErrorActionPreference = 'Stop'
$repo = 'pouriajamali/market-data-integrity-lab'
$tag = 'v0.6.1-preview.1'
$archive = 'market-data-integrity-lab-v0.6.1-preview.1.zip'
foreach ($name in @($archive, 'SHA256SUMS.txt', 'SHA256SUMS.txt.sig', 'SIGNING-PUBLIC-KEY.pub')) {
    Invoke-WebRequest -Uri "https://github.com/$repo/releases/download/$tag/$name" -OutFile $name
}
```

```powershell
$expectedFingerprint = 'SHA256:AgU5bZlJSjUu0KsWMmVEEr+BfXpcMVNroTA33bHcQdU'
$keyInfo = @(& ssh-keygen -lf ./SIGNING-PUBLIC-KEY.pub)
if ($LASTEXITCODE -ne 0 -or $keyInfo.Count -ne 1) { throw 'Signing-key fingerprint mismatch' }
$fields = $keyInfo[0].Trim() -split '\s+'
if ($fields.Count -lt 2 -or $fields[1] -cne $expectedFingerprint) { throw 'Signing-key fingerprint mismatch' }
$key = (Get-Content -LiteralPath ./SIGNING-PUBLIC-KEY.pub -Raw).Trim()
[IO.File]::WriteAllText((Join-Path $PWD 'allowed-signers'), "pouriajamali $key`n", [Text.UTF8Encoding]::new($false))

# Feed exact file bytes to OpenSSH; a text pipeline can change newline/encoding.
$start = [Diagnostics.ProcessStartInfo]::new('ssh-keygen')
$start.UseShellExecute = $false
$start.CreateNoWindow = $true
$start.RedirectStandardInput = $true
foreach ($arg in @('-Y','verify','-f','allowed-signers','-I','pouriajamali','-n','file','-s','SHA256SUMS.txt.sig')) { $start.ArgumentList.Add($arg) }
$check = [Diagnostics.Process]::Start($start)
$bytes = [IO.File]::ReadAllBytes((Join-Path $PWD 'SHA256SUMS.txt'))
$check.StandardInput.BaseStream.Write($bytes, 0, $bytes.Length)
$check.StandardInput.Close()
$check.WaitForExit()
$exitCode = $check.ExitCode
$check.Dispose()
if ($exitCode -ne 0) { throw 'Checksum signature verification failed' }

$pattern = '^([0-9a-f]{64}) [ *]' + [regex]::Escape($archive) + '$'
$entry = @(Get-Content -LiteralPath ./SHA256SUMS.txt | Where-Object { $_ -match $pattern })
if ($entry.Count -ne 1) { throw 'Chosen archive missing or duplicated in signed checksums' }
$actualHash = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant()
if ($actualHash -ne $entry[0].Substring(0,64)) { throw 'Archive checksum mismatch' }
Write-Output "Verified signature and SHA256: $archive"
```

Do not normalize/edit the downloaded checksum file before verification. A valid
signature over different bytes or a matching hash for another filename does not
verify the chosen archive. Preserve failures and obtain a trusted copy rather
than skipping a failed signature or fingerprint check.

## Current tagged release and unchanged legacy pins

The annotated signed `v0.6.1-preview.1` tag object is
`a39b0e9843d73ec4328a39afc5bb96bc5b1d0cd0` and peels to
[commit 06f3b6104446fa7b781c70cc759d0a8eed4e3fa8](https://github.com/pouriajamali/market-data-integrity-lab/commit/06f3b6104446fa7b781c70cc759d0a8eed4e3fa8).
[Matching-OS release CI](https://github.com/pouriajamali/market-data-integrity-lab/actions/runs/42882215681) passed 165 tests and 60 extracted-apphost
invocations on each OS with runtime10.0.15. Build manifests identify SDK10.0.404,
bundled runtime10.0.15, exact source commit and RID.

Verify the exact downloaded checksum bytes with the commands above; a tag
signature alone does not authenticate an arbitrary downloaded archive. The
signature namespace is `file` and the known principal is `pouriajamali`.
No raw private key is required for verification. These SSH signatures do not
constitute Windows Authenticode or market-data authenticity.

Release archives preserve the documentation and quickstarts packaged at tag
`v0.6.1-preview.1` / commit `06f3b6104446fa7b781c70cc759d0a8eed4e3fa8`.
The source archive therefore includes the prepublication candidate wording
captured in that snapshot. The [current main guide](https://github.com/pouriajamali/market-data-integrity-lab/blob/main/PROVENANCE.md)
provides publication updates. Refreshing documentation on main must not rebuild
or replace frozen archives, tags, checksums or signatures.

The old v0.6.0-preview.1 release/tag, archives and checksum signature remain
unchanged. The separately versioned CSV action still pins those original
binaries/hashes; current CLI verification does not silently repin the action.
