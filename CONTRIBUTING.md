# Contributing

Start with the README and the current release. A useful contribution is a small
case another developer can reproduce, with a clear expected result.

## Report a problem

Include the commit or release, OS and toolchain versions. Provide a few invented CSV rows, explicit price/volume scales, expected row/file decision, exit code and the actual reason.
Use synthetic inputs only. Remove credentials, account identifiers, personal
data and employer or proprietary source before opening an issue.

## Run the checks

Use .NET SDK10.0.403, as pinned by global.json. From the repository root:

```sh
dotnet restore tests/Integrity.Tests/Integrity.Tests.csproj --locked-mode
dotnet build tests/Integrity.Tests/Integrity.Tests.csproj -c Release --no-restore
dotnet run --project tests/Integrity.Tests -c Release --no-build
```

## Submit a focused change

Explain the behavior before and after the change. For a bug, add a regression
case that fails before the repair. Preserve the documented input and output
contracts, keep historical evidence intact, and update the relevant design or
format notes when the contract changes. Do not replace recorded measurements
with estimates or transfer their results to a different implementation.

Small improvements to setup instructions, fixtures and diagnostic messages are
welcome. Read the license and attribution in this repository before contributing.
