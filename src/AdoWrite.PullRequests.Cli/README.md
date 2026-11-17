# ado-write-pr

Lets an AI agent work on an Azure DevOps pull request on your behalf. It can post comments (general ones or ones attached to lines), reply in threads, requeue build policies, and set your vote. Every comment ends with a visible footer naming the agent, and retrying the same command never posts twice. It never edits or deletes comments, resolves threads, or completes pull requests.

Part of [Ado-Tools](https://github.com/kkolodziejczak/Ado-Tools), a set of Azure DevOps tools for agents. To also post on work items, use `AdoWrite.Tool` (`ado-write`). To read pull requests, use [`AdoRead.PullRequests.Tool`](https://www.nuget.org/packages/AdoRead.PullRequests.Tool) (`ado-read-pr`).

## Install

```powershell
dotnet tool install --global AdoWrite.PullRequests.Tool
```

Needs .NET 10.

## Set up

```powershell
ado-write-pr config init --organization https://dev.azure.com/example
ado-write-pr auth login
```

The write tools keep their own token in your operating system's credential vault, separate from the read token. It needs **Code (Read & write)**, plus **Identity (Read)** to mention people by mail address.

## Commands

| Command | Does |
| --- | --- |
| `comment <id-or-url> --body <markdown> --agent <name> [--file <path> --line <n>]` | Opens a thread, attached to lines when `--file` is given |
| `reply <id-or-url> --thread <id> --body <markdown> --agent <name>` | Replies in an existing thread |
| `requeue <id-or-url> [--evaluation <id>]...` | Requeues expired build policies, or the ones you name |
| `vote <id-or-url> --vote <vote> --confirm` | Sets your vote: `approve`, `approve-with-suggestions`, `none`, `wait-for-author` or `reject` |
| `config`, `auth` | Settings and the stored write token |

Every command accepts `--dry-run`, which shows what would happen without changing anything. Comments also take `--mention`, `--attach` and `--key`.

```powershell
ado-write-pr comment 412 --body "Is this check still needed?" --agent "Claude Opus 5.5" --compact
ado-write-pr reply 412 --thread 7 --body "Thanks, that answers it." --agent "Claude Opus 5.5" --compact
ado-write-pr requeue 412 --dry-run --compact
ado-write-pr vote 412 --vote approve --confirm --compact
```

An agent should pass `--confirm` only after you agreed to the vote.

## Learn more

- [Writing comments](https://github.com/kkolodziejczak/Ado-Tools#writing-comments): line comments, mentions, attachments, and duplicate protection
- [Requeueing builds and voting](https://github.com/kkolodziejczak/Ado-Tools#requeueing-builds-and-voting)
- [Authentication](https://github.com/kkolodziejczak/Ado-Tools#configure-authentication-and-the-organization) · [JSON contract and exit codes](https://github.com/kkolodziejczak/Ado-Tools#json-contract-and-exit-codes) · [All tools](https://github.com/kkolodziejczak/Ado-Tools#tools)
