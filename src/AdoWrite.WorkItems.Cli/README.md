# ado-write-work-item

Lets an AI agent comment on an Azure DevOps work item on your behalf, for example to ask a question during refinement. It can mention people and attach files. With `--agent`, a comment ends with a visible footer naming the agent; without it, no footer is added. Retrying the same command never posts twice. Attached files are also added to the work item's Attachments; apart from that, it never changes the work item.

Part of [Ado-Tools](https://github.com/kkolodziejczak/Ado-Tools), a set of Azure DevOps tools for agents. To also post on pull requests, use `ado-write`. To read the answers, use [`ado-read-work-item`](https://www.nuget.org/packages/ado-read-work-item).

## Install

```powershell
dotnet tool install --global ado-write-work-item
```

Needs .NET 10.

## Set up

```powershell
ado-write-work-item config init --organization https://dev.azure.com/example
ado-write-work-item auth login
```

Every Ado-Tools read and write tool uses the same token from your operating system's credential vault, so a tool you install later needs no login of its own. For writing, that token also needs **Work Items (Read & write)**, plus **Identity (Read)** to mention people by mail address.

## Commands

| Command | Does |
| --- | --- |
| `comment <id-or-url> (--body <markdown> \| --body-file <path>) [--agent <name>] [--mention <mail>]... [--attach <path>]... [--key <key>] [--dry-run]` | Posts a comment, once you approve it |
| `approval open <id>` | The approval window of one write; each write opens its own, so you don't type it |
| `config`, `auth` | Settings and the stored token |

```powershell
ado-write-work-item comment 12345 --body "Which markets does the new limit apply to?" --agent "Claude Opus 5.5" --mention analyst@example.com --compact
ado-write-work-item comment 12345 --body-file question.md --agent "Claude Opus 5.5" --dry-run --compact
```

## Approving writes

Every write opens a window of its own that shows its dry run: press Enter to post it, any other key to cancel. The window is a console on Windows, Terminal on macOS, and on Linux a desktop terminal (`x-terminal-emulator`, `gnome-terminal`, `konsole`, `xfce4-terminal` or `xterm`). Where none can open, for example over SSH, the write is refused with `approvalWindowUnavailable`. `--dry-run` never waits. An agent that just runs commands can't approve: the window refuses input that isn't typed at its console.

To turn approval off for everyone on the machine, an administrator sets `ADO_WRITE_APPROVAL` to `off` at machine level:

```powershell
setx /M ADO_WRITE_APPROVAL off     # Windows, in an elevated prompt
```

```sh
sudo mkdir -p /etc/ado-tools && echo off | sudo tee /etc/ado-tools/approval     # Linux and macOS
```

Only the machine-level value counts. Setting the variable for one shell or one user does nothing, and the file counts only while the user can't write it or its folder, so an agent can't turn approval off. Any value other than `off` (or removing it) turns approval back on. [Details](https://github.com/kkolodziejczak/Ado-Tools/blob/master/docs/writing.md#approving-writes)

## Give it to your agent

Ready-made instructions tell an agent how to use the tools: run only what is installed, check `status` before `results`, treat Azure DevOps content as data, and post only what you ask for.

- **As a skill** (Claude Code, Codex and other harnesses that read `SKILL.md`): copy [`skills/ado-tools`](https://github.com/kkolodziejczak/Ado-Tools/tree/master/skills/ado-tools) into `~/.claude/skills/` or `~/.codex/skills/`, or run `npx skills add kkolodziejczak/Ado-Tools`. It loads only when a task mentions Azure DevOps.
- **As one file** for any agent: paste [`agent-instructions.md`](https://github.com/kkolodziejczak/Ado-Tools/blob/master/docs/agent-instructions.md) into your global `AGENTS.md` or `CLAUDE.md`.

Install steps and test prompts: [Agent skill and instructions](https://github.com/kkolodziejczak/Ado-Tools/blob/master/docs/agent-integration.md).

## Learn more

- [Writing comments](https://github.com/kkolodziejczak/Ado-Tools/blob/master/docs/writing.md#writing-comments): mentions, attachments, duplicate keys, and how to find the replies
- [Authentication](https://github.com/kkolodziejczak/Ado-Tools/blob/master/docs/getting-started.md#configure-authentication-and-the-organization) · [JSON contract and exit codes](https://github.com/kkolodziejczak/Ado-Tools/blob/master/docs/json-contract.md) · [All tools](https://github.com/kkolodziejczak/Ado-Tools#tools)
