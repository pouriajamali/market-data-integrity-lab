# Check an OHLC CSV in your own CI

Use this when a checked-in CSV must pass the lab's row checks before the rest of
your workflow runs. You choose the price and volume scales. A value such as
`10.001` at price scale `2` is quarantined, rather than rounded to `10.00`.

The action downloads the existing `v0.6.0-preview.1` apphost. It verifies the
original OpenSSH-signed manifest against the pinned public key, checks a second
pinned archive SHA-256, then runs `validate-csv` on the runner. No CSV is sent to
the lab's author or an external validation service.

## Workflow

Create `.github/workflows/check-bars.yml` in your repository. Replace the CSV
path and both scales with the values for your file. Pin the lab action to the
full commit recorded on its [action release](https://github.com/pouriajamali/market-data-integrity-lab/releases/tag/csv-action-v0.1.0-preview.1)
when adopting it; the version tag below is easier to read.

```yaml
name: Check bars
on: [push, pull_request]
permissions:
  contents: read
jobs:
  check:
    runs-on: ubuntu-24.04
    timeout-minutes: 10
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-python@v5
        with:
          python-version: '3.13'
      - uses: pouriajamali/market-data-integrity-lab@csv-action-v0.1.0-preview.1
        with:
          csv: data/bars.csv
          price-decimals: '2'
          volume-decimals: '0'
          report-directory: artifacts/ohlc-check
```

Windows x64 is also supported using `windows-latest`. Linux needs glibc; the
integration test uses Ubuntu 24.04. Python 3.13 and OpenSSH `ssh-keygen` must be
available on the runner. No installed .NET SDK is needed for the downloaded app.

## Read the result

| Exit code | Meaning | Default job behavior |
| --- | --- | --- |
| `0` | Completed with no quarantined rows | Pass |
| `2` | Completed with quarantined rows | Fail |
| `1` | Fatal input, verification or execution failure; no completed result | Fail |

Completed runs write `accepted.jsonl`, `quarantine.jsonl` and `summary.json` into
the new report directory. Rows and hashes stay there; the action logs only the
outcome. It never overwrites an existing report directory. You can use
`continue-on-error` to inspect quarantines, but that changes your workflow's
acceptance policy. The action exposes the exact outcome as `exit-code`.

Reports contain input data. Upload them as CI artifacts only when their audience
and retention are appropriate for your data; this action does not upload them.

## Scope

The adapter caps input at 64 MiB, uses a 300-second process timeout and accepts
only paths within the workspace. Downloads and extraction are size-bounded;
archive traversal, links and special files are refused. The
[CSV profile](CSV.md) still applies. A passing run checks local rows; it does not
establish provenance, ordering, completeness or suitability for a backtest.

The [clean, quarantined and malformed fixtures](../examples/ci/) are synthetic.
[CI](../.github/workflows/csv-action.yml) exercises the real signed preview on
Windows and Ubuntu, including failure outcomes. The adapter is a separate
preview from the unchanged CLI binaries and their release test count.
