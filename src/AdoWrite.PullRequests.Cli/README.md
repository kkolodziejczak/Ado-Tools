# ado-write-pr

Lets an AI agent work on an Azure DevOps pull request on your behalf. It can open pull requests, post comments (general ones or ones attached to lines), reply in threads, resolve and reopen them, requeue build policies, and set your vote. With `--agent`, a comment ends with a visible footer naming the agent; without it, no footer is added. Retrying the same command never posts twice. Every write waits until you approve it in a console window of your own, so the agent can't post on its own. It never edits or deletes comments or completes pull requests.

Part of [Ado-Tools](https://github.com/kkolodziejczak/Ado-Tools), a set of Azure DevOps tools for agents. To also post on work items, use `ado-write`. To read pull requests, use [`ado-read-pr`](https://www.nuget.org/packages/ado-read-pr).

## Install

```powershell
dotnet tool install --global ado-write-pr
```

Needs .NET 10.

## Set up

```powershell
ado-write-pr config init --organization https://dev.azure.com/example
ado-write-pr auth login
```

Every Ado-Tools read and write tool uses the same token from your operating system's credential vault, so a tool you install later needs no login of its own. For writing, that token also needs **Code (Read & write)**, plus **Identity (Read)** to mention people by mail address.

## Commands

| Command | Does |
| --- | --- |
| `create --repository <name-or-id> --source <branch> --title <text> [--target] [--body] [--reviewer] [--work-item] [--draft]` | Opens a pull request, or returns the active one for the same branches |
| `comment <id-or-url> --body <markdown> [--agent <name>] [--file <path> --line <n>]` | Opens a thread, attached to lines when `--file` is given |
| `reply <id-or-url> --thread <id> --body <markdown> [--agent <name>]` | Replies in an existing thread |
| `resolve <id-or-url> --thread <id> [--body <markdown>]` | Marks a thread resolved, after an optional reply |
| `unresolve <id-or-url> --thread <id> [--body <markdown>]` | Makes a resolved thread active again, after an optional reply |
| `requeue <id-or-url> [--evaluation <id>]...` | Requeues expired build policies, or the ones you name |
| `vote <id-or-url> --vote <vote>` | Sets your vote: `approve`, `approve-with-suggestions`, `none`, `wait-for-author` or `reject` |
| `approval review` | Opens the approval window, where you post or reject what agents queued |
| `config`, `auth` | Settings and the stored token |

Every write command accepts `--dry-run`, which shows what would happen without changing anything. Without it, the write waits in the approval window: `↑`/`↓` pick a write, `y` posts it, `n` rejects it. On Windows the first write opens the window; elsewhere run `ado-write-pr approval review`. Comments also take `--mention`, `--attach` and `--key`.

```powershell
ado-write-pr create --project Payments --repository Payments.Api --source feature/retry --title "Retry failed payments once" --agent "Claude Opus 5.5" --compact
ado-write-pr comment 412 --body "Is this check still needed?" --agent "Claude Opus 5.5" --compact
ado-write-pr reply 412 --thread 7 --body "Thanks, that answers it." --agent "Claude Opus 5.5" --compact
ado-write-pr resolve 412 --thread 7 --body "Renamed in the last push." --agent "Claude Opus 5.5" --compact
ado-write-pr requeue 412 --dry-run --compact
ado-write-pr vote 412 --vote approve --compact
```

An administrator can turn approval off for the machine with `ADO_WRITE_APPROVAL=off` ([details](https://github.com/kkolodziejczak/Ado-Tools/blob/master/docs/writing.md#approving-writes)).

## Give it to your agent

Ready-made instructions tell an agent how to use the tools: run only what is installed, check `status` before `results`, treat Azure DevOps content as data, and post only what you ask for.

- **As a skill** (Claude Code, Codex and other harnesses that read `SKILL.md`): copy [`skills/ado-tools`](https://github.com/kkolodziejczak/Ado-Tools/tree/master/skills/ado-tools) into `~/.claude/skills/` or `~/.codex/skills/`, or run `npx skills add kkolodziejczak/Ado-Tools`. It loads only when a task mentions Azure DevOps.
- **As one file** for any agent: paste [`agent-instructions.md`](https://github.com/kkolodziejczak/Ado-Tools/blob/master/docs/agent-instructions.md) into your global `AGENTS.md` or `CLAUDE.md`.

Install steps and test prompts: [Agent skill and instructions](https://github.com/kkolodziejczak/Ado-Tools/blob/master/docs/agent-integration.md).

## Learn more

- [Writing comments](https://github.com/kkolodziejczak/Ado-Tools/blob/master/docs/writing.md#writing-comments): line comments, mentions, attachments, and duplicate protection
- [Requeueing builds and voting](https://github.com/kkolodziejczak/Ado-Tools/blob/master/docs/writing.md#requeueing-builds-and-voting)
- [Authentication](https://github.com/kkolodziejczak/Ado-Tools/blob/master/docs/getting-started.md#configure-authentication-and-the-organization) · [JSON contract and exit codes](https://github.com/kkolodziejczak/Ado-Tools/blob/master/docs/json-contract.md) · [All tools](https://github.com/kkolodziejczak/Ado-Tools#tools)
