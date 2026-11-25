# ado-write-work-item

Lets an AI agent comment on an Azure DevOps work item on your behalf, for example to ask a question during refinement. It can mention people and attach files. With `--agent`, a comment ends with a visible footer naming the agent; without it, the text is posted as written. Retrying the same command never posts twice. Attached files are also added to the work item's Attachments; apart from that, it never changes the work item.

Part of [Ado-Tools](https://github.com/kkolodziejczak/Ado-Tools), a set of Azure DevOps tools for agents. To also post on pull requests, use `AdoWrite.Tool` (`ado-write`). To read the answers, use [`AdoRead.WorkItems.Tool`](https://www.nuget.org/packages/AdoRead.WorkItems.Tool) (`ado-read-work-item`).

## Install

```powershell
dotnet tool install --global AdoWrite.WorkItems.Tool
```

Needs .NET 10.

## Set up

```powershell
ado-write-work-item config init --organization https://dev.azure.com/example
ado-write-work-item auth login
```

Every Ado-Tools read and write tool uses the same token from your operating system's credential vault, so if you already logged in with a read tool, there is nothing more to set up. For writing, the token also needs **Work Items (Read & write)**, plus **Identity (Read)** to mention people by mail address.

## Commands

| Command | Does |
| --- | --- |
| `comment <id-or-url> (--body <markdown> \| --body-file <path>) [--agent <name>] [--mention <mail>]... [--attach <path>]... [--key <key>] [--dry-run]` | Posts a comment |
| `config`, `auth` | Settings and the stored token |

```powershell
ado-write-work-item comment 12345 --body "Which markets does the new limit apply to?" --agent "Claude Opus 5.5" --mention analyst@example.com --compact
ado-write-work-item comment 12345 --body-file question.md --agent "Claude Opus 5.5" --dry-run --compact
```

## Learn more

- [Writing comments](https://github.com/kkolodziejczak/Ado-Tools#writing-comments): mentions, attachments, duplicate keys, and how to find the replies
- [Authentication](https://github.com/kkolodziejczak/Ado-Tools#configure-authentication-and-the-organization) · [JSON contract and exit codes](https://github.com/kkolodziejczak/Ado-Tools#json-contract-and-exit-codes) · [All tools](https://github.com/kkolodziejczak/Ado-Tools#tools)
