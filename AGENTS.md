# Ado-Tools agent instructions

## Naming tools and commands

Every new tool, command and option follows this rule. Check a name against it before you write the test for it, and add new commands to the tool tables in `docs/getting-started.md` and to the page of their resource in `docs/`. Keep `README.md` a short front page: change it only when a tool is added or the pitch changes. When a command an agent uses changes, update the skill `skills/ado-tools/SKILL.md` and the paste-in `docs/agent-instructions.md` too; `AgentInstructionsTests` fails when they name a command, option or JSON field that doesn't exist.

1. **Tool name** = `ado-<read|write>[-<resource>]`, lower kebab case. It is both the NuGet package ID (`PackageId`) and the command (`ToolCommandName`). Without a resource it is the umbrella tool.
2. **Command** = `<resource> [<child>] <verb> [<id-or-url>] [--options]`. A slice tool is the umbrella path with the first space turned into a hyphen: `ado-read pr show 412` = `ado-read-pr show 412`. A module gets this for free: `ToolComposition` mounts it under its name in the umbrella and on the root of its slice.
3. **Nouns** are singular kebab case: `pr`, `work-item`, `wiki`, `sprint`, `pipeline`, `project`, `team`, plus the setup groups `config`, `auth`, `privacy`. The noun path names the resource the command takes or returns. Add a child noun only for such a resource (`wiki page`, `pipeline run`, `pipeline test`).
4. **Verbs** come from a closed list, and every command has one.
   - Read: `show` (one item), `list` (many, filtered by options), `search` (by text).
   - Write: `create`, `comment`, `reply`, `resolve`, `unresolve`, `vote`, `requeue`.
   - Setup: `init`, `show`, `set`, `reset`, `login`, `logout`, `status`.
   - Approval window: `open` (`approval open <id>`, the window where the person posts or cancels one write).
   A new verb needs the owner's approval. Don't use compound verbs such as `resolve-thread` or `list-for-pr`.
5. **The positional argument** is `<id-or-url>` of the resource the noun path names (`<url>` where only a URL works). Things inside it, and references to other resources, are options named after them: `--thread`, `--run`, `--pr`, `--wiki`, `--sprint`. `search <text>` and `privacy set <level>` keep their operand.
6. **Options** reuse the short domain word the README already uses: `--author`, `--reviewer`, `--vote`, `--status`, `--repository`, `--project`, `--assignee`, `--state`, `--tag`, `--area`, `--type`. Use `me` for the token owner. An option that already exists on another command keeps its name and values. A write verb that also posts a comment takes `--body` / `--body-file`, like `reply`.
7. **Output**: `list` returns compact entries by default, and `--full` returns an array of full `show` payloads. `--compact` only changes JSON indentation.

Never rename these, because users or stored data depend on them: `~/.ado-read/config.json`, the comment marker `<!-- ado-write:<key> -->`, vault account names, `ADO_READ_*` variables, JSON `kind` values, and project, assembly and namespace names (`AdoRead.PullRequests.Cli`, …; signing selects `AdoRead.*.dll;AdoWrite.*.dll`).

## Before every push

1. Run `./scripts/Invoke-CiChecks.ps1`. It is the CI workflow itself: restore, format, build, test, pack, package validation, package health and the installed-package smoke test of all nine tools. `ci.yml` only calls it. Push only when it ends with "All CI checks passed".
2. When a check fails, find out which side is wrong. If the code is wrong, fix the code. If the code is right and a test, smoke script or expected list describes the old behaviour, update it in the same commit and say so in the message. Never delete or skip a check to get green.
3. Create a feature branch with `git switch -c <name> --no-track origin/master` and push it with `git push -u origin <name>:<name>`. Git here may have `push.default=upstream`, so a branch that tracks `origin/master` pushes straight to `master`.
4. After the push, follow the CI run (`gh run list --branch <name> --limit 1`) and fix a red run before starting other work.

## Releasing

The packages go to nuget.org in two ways. Both build, test and check every tool with `.github/workflows/ci.yml` first.

### "Release a prerelease"

Signs a prerelease of all nine tools and pushes it to nuget.org. It runs only from `master`.

1. Run `git fetch origin` and check that `master` has no unpushed commits (`git status -sb` shows no `ahead`). If it has, push them first. The workflow builds what is on origin.
2. Start it: `gh workflow run prerelease.yml --ref master`.
3. Right away, start the approval for me (see [Starting the approval for the owner](#starting-the-approval-for-the-owner)) with `gh release-signing approve -Workflow prerelease.yml`.
4. Follow the run (`gh run list --workflow prerelease.yml --limit 1`, then `gh run watch <id> --exit-status`) and report its result, the version from the "Calculate the prerelease version" step, and the install command:
   `dotnet tool update -g <PackageId> --version <version>`.

The version is `<next>-preview.<run>`: `<next>` is the patch after the latest stable tag (or the `Version` in `Directory.Build.props` before the first one).

### "Release a stable version"

Only when I ask for a stable release and name the version. Tag a commit of `master` with `v<major>.<minor>.<patch>` and push the tag, then right away start the approval for me with `gh release-signing approve -Tag v<major>.<minor>.<patch>` (see below). After signing it waits for my approval of the `release` environment before it stages the packages on nuget.org, where I publish them. Never approve either deployment yourself.

### Signing

nuget.org accepts only packages signed with my code-signing certificate. Both workflows call `.github/workflows/sign-packages.yml`, which signs the Ado-Tools assemblies inside each package and then the package through [gh-release-signing](https://github.com/kkolodziejczak/gh-release-signing), after my approval of the `code-signing` environment and a SimplySign phone code. `scripts/Test-SignedPackages.ps1` then checks every signature before anything is pushed. The environment accepts only `v*.*.*` tags and `master`.

### Starting the approval for the owner

As soon as you start a release run (push the tag or start the workflow), also start the approval
script for the owner; don't wait for the build, and don't ask them to run it. It follows the run
and prompts the owner when signing needs them. Open it in its own window, from this repository's
folder:

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
