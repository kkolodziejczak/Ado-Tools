# Ado-Tools agent instructions

## Releasing

The packages go to nuget.org in two ways. Both build, test and check every tool with `.github/workflows/ci.yml` first.

### "Release a prerelease"

Pushes a prerelease of all nine tools to nuget.org right away, with no approval.

1. Use the branch you are working on in this checkout. Use another branch only when I name it.
2. Run `git fetch origin` and check that the branch exists on origin and has no unpushed commits (`git status -sb` shows no `ahead`). If it has unpushed commits, push them first. The workflow builds what is on origin.
3. Start it: `gh workflow run prerelease.yml --ref <branch>`. A branch created before `prerelease.yml` existed must first merge `master`.
4. Follow the run (`gh run list --workflow prerelease.yml --limit 1`, then `gh run watch <id> --exit-status`) and report its result, the version from the "Calculate the prerelease version" step, and the install command:
   `dotnet tool update -g <PackageId> --version <version>`.

The version is `<next>-<label>.<run>`: `<next>` is the patch after the latest stable tag (or the `Version` in `Directory.Build.props` before the first one), `<label>` is `preview` on `master` and otherwise a short form of the branch name.

### "Release a stable version"

Only when I ask for a stable release and name the version. Tag a commit of `master` with `v<major>.<minor>.<patch>` and push the tag. `.github/workflows/release.yml` then waits for my approval of the `release` environment before it stages the packages on nuget.org, where I publish them. Never approve that deployment yourself.
