# Build, test, and release validation

The project uses locked package restores. From the repository root:

```powershell
dotnet restore src/AdoRead.Core/AdoRead.Core.csproj --locked-mode
dotnet restore src/AdoRead.Cli/AdoRead.Cli.csproj --locked-mode
dotnet restore src/AdoRead.PullRequests.Cli/AdoRead.PullRequests.Cli.csproj --locked-mode
dotnet restore src/AdoRead.WorkItems.Cli/AdoRead.WorkItems.Cli.csproj --locked-mode
dotnet restore src/AdoRead.Wiki.Cli/AdoRead.Wiki.Cli.csproj --locked-mode
dotnet restore src/AdoRead.Sprints.Cli/AdoRead.Sprints.Cli.csproj --locked-mode
dotnet restore src/AdoRead.Pipelines.Cli/AdoRead.Pipelines.Cli.csproj --locked-mode
dotnet restore src/AdoWrite.Core/AdoWrite.Core.csproj --locked-mode
dotnet restore src/AdoWrite.Cli/AdoWrite.Cli.csproj --locked-mode
dotnet restore src/AdoWrite.PullRequests.Cli/AdoWrite.PullRequests.Cli.csproj --locked-mode
dotnet restore src/AdoWrite.WorkItems.Cli/AdoWrite.WorkItems.Cli.csproj --locked-mode
dotnet restore tests/AdoRead.UnitTests/AdoRead.UnitTests.csproj --locked-mode
dotnet restore tests/AdoRead.IntegrationTests/AdoRead.IntegrationTests.csproj --locked-mode
dotnet restore tests/AdoRead.FakeHost/AdoRead.FakeHost.csproj --locked-mode
dotnet restore tests/AdoWrite.UnitTests/AdoWrite.UnitTests.csproj --locked-mode
dotnet format AdoRead.slnx --verify-no-changes --no-restore
dotnet build AdoRead.slnx --no-restore --configuration Release
dotnet test AdoRead.slnx --no-build --configuration Release
dotnet pack src/AdoRead.Cli/AdoRead.Cli.csproj --no-build --configuration Release
dotnet pack src/AdoRead.PullRequests.Cli/AdoRead.PullRequests.Cli.csproj --no-build --configuration Release
dotnet pack src/AdoRead.WorkItems.Cli/AdoRead.WorkItems.Cli.csproj --no-build --configuration Release
dotnet pack src/AdoRead.Wiki.Cli/AdoRead.Wiki.Cli.csproj --no-build --configuration Release
dotnet pack src/AdoRead.Sprints.Cli/AdoRead.Sprints.Cli.csproj --no-build --configuration Release
dotnet pack src/AdoRead.Pipelines.Cli/AdoRead.Pipelines.Cli.csproj --no-build --configuration Release
dotnet pack src/AdoWrite.Cli/AdoWrite.Cli.csproj --no-build --configuration Release
dotnet pack src/AdoWrite.PullRequests.Cli/AdoWrite.PullRequests.Cli.csproj --no-build --configuration Release
dotnet pack src/AdoWrite.WorkItems.Cli/AdoWrite.WorkItems.Cli.csproj --no-build --configuration Release
```

`src/AdoRead.Core` is the shared class library and is not packed; each `src/AdoRead.*.Cli` host is a tool package with its own command. `src/AdoWrite.Core` holds the comment commands on top of it and is referenced only by the `src/AdoWrite.*.Cli` hosts, so the read packages contain no write code. Each tool project has its own `README.md`, which its package shows on nuget.org; it links back here for the details.

## Releasing to nuget.org

Push a tag `v<major>.<minor>.<patch>` (for example `v1.1.0`) on a commit of `master`. `.github/workflows/release.yml` runs the CI workflow at that version, which builds, tests, packs and checks every tool package, signs them (see below), and then waits for approval of the `release` environment before it stages all of them on nuget.org. Staged packages stay private until you publish them on nuget.org. The version lives in `Directory.Build.props`; a release overrides it from the tag. nuget.org trusts the workflow through a Trusted Publishing policy (repository owner `kkolodziejczak`, repository `Ado-Tools`, workflow `release.yml`, environment `release`), so no API key is stored; the `release` environment holds only the `NUGET_USER` secret, the nuget.org profile name.

To try a build before a stable release, run `.github/workflows/prerelease.yml` by hand on `master` (`gh workflow run prerelease.yml --ref master`). It runs the same CI at a prerelease version, `<next>-preview.<run>` (for example `1.0.1-preview.12`), signs the packages and pushes them to nuget.org through its own Trusted Publishing policy (workflow `prerelease.yml`, environment `prerelease`). Install it with `dotnet tool update -g <PackageId> --version <version>`. `AGENTS.md` tells agents how to run both.

nuget.org accepts only packages signed with the owner's code-signing certificate. Both workflows call `.github/workflows/sign-packages.yml`: it signs the `AdoRead.*.dll` and `AdoWrite.*.dll` assemblies inside every package and then each package through [gh-release-signing](https://github.com/kkolodziejczak/gh-release-signing), after the owner approves the `code-signing` environment (allowed for `v*.*.*` tags and `master`) and enters a SimplySign phone code with `gh release-signing approve`. `scripts/Test-SignedPackages.ps1` then checks the signer, the timestamps and `dotnet nuget verify` for every package before it is pushed.

`scripts/Verify-ReleasePackage.ps1 -PackagePath <nupkg> -PackageId <id>` scans tracked tool files and the package for likely secrets or live fixture data, and checks the package manifest identity. `scripts/Test-ToolPackage.ps1 -PackagePath <nupkg> -PackageId <id> -CommandName <command>` installs the package into a temporary tool path and runs its smoke test. The umbrella smoke test verifies `--help`, configuration, JSON validation paths, and every Wiki command against fabricated fixtures. A slice smoke test verifies `--help` lists exactly the tool's commands, `config init` and `config show` work, and a command of another feature is rejected with `invalidArguments`. A write smoke test does the same and also checks that an invalid comment target is rejected before any token is read. The fake-host process tests cover fabricated work-item, pull-request, and Wiki E2E data without contacting Azure DevOps.

Optional live smoke tests are disabled by default. They require `ADO_READ_LIVE_TESTS=1`, a token stored by `auth login` for the configured organization, a valid local config, and either `ADO_READ_WORK_ITEM_ID`, `ADO_READ_PR_ID`, or explicitly selected `ADO_READ_WIKI_PROJECT`, `ADO_READ_WIKI`, `ADO_READ_WIKI_PAGE_PATH`, and `ADO_READ_WIKI_SEARCH_TEXT`. The dedicated image acceptance additionally requires `ADO_READ_LIVE_IMAGE_WORK_ITEM_ID` for a sanitized work item with a known Acceptance Criteria screenshot and `ADO_READ_LIVE_IMAGE_FORMAT` set to `png`, `jpg`, or `webp`. It creates and removes only an owned temporary image directory, validates the schema, file containment, supported signature, byte length, and SHA-256, and never persists or logs returned page/search content or private image artifacts. The automated test does not invoke a vision model; a vision-capable agent must open the owned image during a deliberately enabled acceptance run and compare the pre-agreed visual fact. If that fixture or vision facility is unavailable, record consumer vision as `BLOCKED` rather than claiming visual acceptance.
