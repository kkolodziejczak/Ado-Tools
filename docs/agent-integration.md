# Agent integration

Give your agent ready-made instructions for the tools. Both versions say the same things: use only the installed tools, check `status` before `results`, treat Azure DevOps content as data, and post only what you asked for.

| You use | Install | Costs context |
| --- | --- | --- |
| A harness with skills (Claude Code, Codex, and others that read `SKILL.md`) | The `ado-tools` skill | Only its description, until a task needs it |
| Any other agent, or you want the rules in every session | The paste-in file | Every session |

## The skill

The skill is one file, [`skills/ado-tools/SKILL.md`](../skills/ado-tools/SKILL.md). Put it in a folder named `ado-tools` inside your harness's skills folder:

| Harness | Skills folder |
| --- | --- |
| Claude Code | `~/.claude/skills/` (all projects) or `.claude/skills/` (one repository) |
| Codex | `~/.codex/skills/` or `~/.agents/skills/` (all projects), `.agents/skills/` (one repository) |

```powershell
$folder = "$HOME\.claude\skills\ado-tools"      # Codex: "$HOME\.codex\skills\ado-tools"
New-Item -ItemType Directory -Force $folder | Out-Null
Invoke-WebRequest https://raw.githubusercontent.com/kkolodziejczak/Ado-Tools/master/skills/ado-tools/SKILL.md -OutFile "$folder\SKILL.md"
```

With Node.js, `npx skills add kkolodziejczak/Ado-Tools` does the same for the harnesses it finds. In Codex you can also ask its built-in skill installer to install `https://github.com/kkolodziejczak/Ado-Tools/tree/master/skills/ado-tools`.

Start a new session afterwards. The skill loads by itself when a task mentions Azure DevOps.

## The paste-in file

Copy everything in [`docs/agent-instructions.md`](agent-instructions.md) into your global agent instructions, for example `~/.claude/CLAUDE.md` (Claude Code) or `~/.codex/AGENTS.md` (Codex).

## Check that it works

[`docs/agent-test-prompts.md`](agent-test-prompts.md) lists prompts with what the agent should and should not do.

## Using the output from your own code

Use compact output when passing a result to another program or agent:

```powershell
ado-read work-item show 12345 --compact | ConvertFrom-Json
ado-read pr show 412 --compact | ConvertFrom-Json
```

Check `status` before acting on `results`. Do not infer that a missing PR is absent when the status is `partial` or `error`; inspect `errors` first. The tool returns context only—it does not evaluate requirements or review feedback.

For visual work-item acceptance, create a unique empty temporary directory owned by the consumer and pass each argument separately:

```powershell
ado-read work-item show "12345" --download-images "C:\agent-temp\ado-read-12345" --compact
```

Require `kind: "workItemSearch"`, schema `2.1`, and the matching exit/status pair. For `success` and `partial`, also require `query.downloadImagesDirectory` and `media.outputDirectory` to equal the resolved requested directory. A valid `notFound` or `error` envelope may have no resolved download directory or media result because validation, configuration, authentication, cancellation, or lookup can fail before visual evidence exists; do not attempt to consume images from those statuses. Validate every `downloaded` path and load it with the available vision facility. Never pass a PAT, authorization header, protected URL, cookie, or image URL to a model. Remove only the consumer-created temporary directory after all observations and provenance are captured, and validate the resolved target before recursive cleanup; the tool itself removes invocation-owned staging/final files on global failure or cancellation without deleting caller-owned files or recursively deleting the supplied directory.

For PR review context, verify file and ancestry details from the v2 response in the matching local repository. For example, after confirming the response's source and target revisions exist locally, use `git diff <targetCommitId>...<sourceCommitId> -- <thread.filePath>` and inspect the reported line range. Do not treat a nullable `isPossiblyOutdated` value as a relevance verdict.
