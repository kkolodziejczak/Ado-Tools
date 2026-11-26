# ado-write

Lets an AI agent act in Azure DevOps on your behalf: it can comment on pull requests and work items, reply in pull-request threads, requeue pull-request builds, and set your vote. With `--agent`, a comment ends with a visible footer naming the agent; without it, no footer is added. Retrying the same command never posts twice. It is the all-in-one write tool of [Ado-Tools](https://github.com/kkolodziejczak/Ado-Tools). It never edits or deletes comments, resolves threads, completes pull requests, or changes work-item fields.

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
| `pr vote` | Sets your vote (needs `--confirm`) | [`ado-write-pr`](https://www.nuget.org/packages/ado-write-pr) |
| `work-item comment` | Comments on a work item | [`ado-write-work-item`](https://www.nuget.org/packages/ado-write-work-item) |
| `config`, `auth` | Settings and the stored token | every write tool |

Every command accepts `--dry-run`, which shows what would happen without changing anything.

```powershell
ado-write pr comment 412 --body "Is this check still needed?" --agent "Claude Opus 5.5" --compact
ado-write work-item comment 12345 --body "Which markets does the new limit apply to?" --agent "Claude Opus 5.5" --compact
ado-write pr requeue 412 --dry-run --compact
ado-write pr vote 412 --vote approve --confirm --compact
```

To read pull requests and work items, and the answers to a comment, install [`ado-read`](https://www.nuget.org/packages/ado-read).

## Learn more

- [Writing comments](https://github.com/kkolodziejczak/Ado-Tools#writing-comments) · [Requeueing builds and voting](https://github.com/kkolodziejczak/Ado-Tools#requeueing-builds-and-voting)
- [Authentication](https://github.com/kkolodziejczak/Ado-Tools#configure-authentication-and-the-organization) · [JSON contract and exit codes](https://github.com/kkolodziejczak/Ado-Tools#json-contract-and-exit-codes) · [All tools](https://github.com/kkolodziejczak/Ado-Tools#tools)
