# Ado-Tools agent instructions

## Releasing

The packages go to nuget.org in two ways. Both build, test and check every tool with `.github/workflows/ci.yml` first.

### "Release a prerelease"

Signs a prerelease of all nine tools and pushes it to nuget.org. It runs only from `master`.

1. Run `git fetch origin` and check that `master` has no unpushed commits (`git status -sb` shows no `ahead`). If it has, push them first. The workflow builds what is on origin.
2. Start it: `gh workflow run prerelease.yml --ref master`.
3. When the run waits for signing, start the approval for me (see [Starting the approval for the owner](#starting-the-approval-for-the-owner)) with `gh release-signing approve -Workflow prerelease.yml`.
4. Follow the run (`gh run list --workflow prerelease.yml --limit 1`, then `gh run watch <id> --exit-status`) and report its result, the version from the "Calculate the prerelease version" step, and the install command:
   `dotnet tool update -g <PackageId> --version <version>`.

The version is `<next>-preview.<run>`: `<next>` is the patch after the latest stable tag (or the `Version` in `Directory.Build.props` before the first one).

### "Release a stable version"

Only when I ask for a stable release and name the version. Tag a commit of `master` with `v<major>.<minor>.<patch>` and push the tag. `.github/workflows/release.yml` then waits for signing: start the approval for me with `gh release-signing approve -Tag v<major>.<minor>.<patch>` (see below). After signing it waits for my approval of the `release` environment before it stages the packages on nuget.org, where I publish them. Never approve either deployment yourself.

### Signing

nuget.org accepts only packages signed with my code-signing certificate. Both workflows call `.github/workflows/sign-packages.yml`, which signs the Ado-Tools assemblies inside each package and then the package through [gh-release-signing](https://github.com/kkolodziejczak/gh-release-signing), after my approval of the `code-signing` environment and a SimplySign phone code. `scripts/Test-SignedPackages.ps1` then checks every signature before anything is pushed. The environment accepts only `v*.*.*` tags and `master`.

### Starting the approval for the owner

When a run waits for signing, start the approval script for the owner instead of asking them to
run it. Open it in its own window, from this repository's folder, so the owner can answer it:

```powershell
Start-Process powershell.exe -WorkingDirectory (Get-Location) -ArgumentList '-NoExit', '-Command', 'gh release-signing approve -Tag vX.Y.Z'
```

- Pass `-RunId <id>` after a rerun, and `-Workflow <file>` when the calling workflow is not
  `release.yml`.
- If `gh extension list` does not show `release-signing`, run `Approve-ReleaseSigning.ps1` from a
  local clone of gh-release-signing instead (`-NoExit -ExecutionPolicy Bypass -File <path> …`),
  still with this repository's folder as the working directory.
- Never answer its prompts or type the code yourself. Tell the owner the window is open, then wait
  for the run to finish in the background and report its result.
