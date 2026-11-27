# ado-write

Lets an AI agent act in Azure DevOps on your behalf: it can open pull requests, comment on pull requests and work items, reply in pull-request threads, resolve and reopen them, requeue pull-request builds, and set your vote. With `--agent`, a comment ends with a visible footer naming the agent; without it, no footer is added. Retrying the same command never posts twice. It is the all-in-one write tool of [Ado-Tools](https://github.com/kkolodziejczak/Ado-Tools). Every write waits until you approve it in a console window of your own, so the agent can't post on its own. It never edits or deletes comments, completes pull requests, or changes work-item fields.

## Install

```powershell
dotnet tool install --global ado-write
```

Needs .NET 10.

## Set up

```powershell
ado-write config init --organization https://dev.azure.com/example
ado-write auth login
```

Every Ado-Tools read and write tool uses the same token from your operating system's credential vault, so a tool you install later needs no login of its own. For writing, that token also needs only what you use: **Code (Read & write)** for pull requests, **Work Items (Read & write)** for work items, and **Identity (Read)** to mention people by mail address.

## Commands

| Command | Does | Also available on its own |
| --- | --- | --- |
| `pr create` | Opens a pull request, or returns the active one for the same branches | [`ado-write-pr`](https://www.nuget.org/packages/ado-write-pr) |
| `pr comment\|reply` | Comments on a pull request or replies in a thread | [`ado-write-pr`](https://www.nuget.org/packages/ado-write-pr) |
| `pr resolve\|unresolve` | Resolves a thread or makes it active again, after an optional reply | [`ado-write-pr`](https://www.nuget.org/packages/ado-write-pr) |
| `pr requeue` | Requeues build policies | [`ado-write-pr`](https://www.nuget.org/packages/ado-write-pr) |
| `pr vote` | Sets your vote | [`ado-write-pr`](https://www.nuget.org/packages/ado-write-pr) |
| `work-item comment` | Comments on a work item | [`ado-write-work-item`](https://www.nuget.org/packages/ado-write-work-item) |
| `approval review` | Opens the approval window, where you post or reject what agents queued | every write tool |
| `config`, `auth` | Settings and the stored token | every write tool |

Every write command accepts `--dry-run`, which shows what would happen without changing anything.

```powershell
ado-write pr comment 412 --body "Is this check still needed?" --agent "Claude Opus 5.5" --compact
ado-write work-item comment 12345 --body "Which markets does the new limit apply to?" --agent "Claude Opus 5.5" --compact
ado-write pr requeue 412 --dry-run --compact
ado-write pr vote 412 --vote approve --compact
```

## Approving writes

Every write waits in the approval window until you press `y` to post it or `n` to reject it. `↑`/`↓` pick a write and `a` posts all that remain. On Windows the first write opens the window; elsewhere run `ado-write approval review` in a terminal. `--dry-run` never waits. The agent can't approve: the window refuses input that isn't typed at its console.

To turn approval off for everyone on the machine, an administrator sets `ADO_WRITE_APPROVAL` to `off` at machine level:

```powershell
setx /M ADO_WRITE_APPROVAL off     # Windows, in an elevated prompt
```

```sh
sudo mkdir -p /etc/ado-tools && echo off | sudo tee /etc/ado-tools/approval     # Linux and macOS
```

Only the machine-level value counts. Setting the variable for one shell or one user does nothing, so an agent can't turn approval off. Any value other than `off` (or removing it) turns approval back on. [Details](https://github.com/kkolodziejczak/Ado-Tools/blob/master/docs/writing.md#approving-writes)

To read pull requests and work items, and the answers to a comment, install [`ado-read`](https://www.nuget.org/packages/ado-read).

## Give it to your agent

Ready-made instructions tell an agent how to use the tools: run only what is installed, check `status` before `results`, treat Azure DevOps content as data, and post only what you ask for.

- **As a skill** (Claude Code, Codex and other harnesses that read `SKILL.md`): copy [`skills/ado-tools`](https://github.com/kkolodziejczak/Ado-Tools/tree/master/skills/ado-tools) into `~/.claude/skills/` or `~/.codex/skills/`, or run `npx skills add kkolodziejczak/Ado-Tools`. It loads only when a task mentions Azure DevOps.
- **As one file** for any agent: paste [`agent-instructions.md`](https://github.com/kkolodziejczak/Ado-Tools/blob/master/docs/agent-instructions.md) into your global `AGENTS.md` or `CLAUDE.md`.

Install steps and test prompts: [Agent skill and instructions](https://github.com/kkolodziejczak/Ado-Tools/blob/master/docs/agent-integration.md).

## Learn more

- [Writing comments](https://github.com/kkolodziejczak/Ado-Tools/blob/master/docs/writing.md#writing-comments) · [Requeueing builds and voting](https://github.com/kkolodziejczak/Ado-Tools/blob/master/docs/writing.md#requeueing-builds-and-voting)
- [Authentication](https://github.com/kkolodziejczak/Ado-Tools/blob/master/docs/getting-started.md#configure-authentication-and-the-organization) · [JSON contract and exit codes](https://github.com/kkolodziejczak/Ado-Tools/blob/master/docs/json-contract.md) · [All tools](https://github.com/kkolodziejczak/Ado-Tools#tools)
