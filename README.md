# Ado-Tools

[![CI](https://github.com/kkolodziejczak/Ado-Tools/actions/workflows/ci.yml/badge.svg)](https://github.com/kkolodziejczak/Ado-Tools/actions/workflows/ci.yml)
[![NuGet](https://img.shields.io/nuget/vpre/ado-read?label=ado-read)](https://www.nuget.org/packages/ado-read)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)

**Azure DevOps context for AI agents and the terminal.** Read pull requests, work items, pipelines, Wikis and sprints as stable JSON. Post comments, requeue builds and vote only if you install a write tool.

```powershell
ado-read pr list --reviewer me --vote none               # pull requests waiting for my review
ado-read pr show 412                                     # one pull request with every review thread
ado-read pipeline run list --pr 412 --project Payments   # why its build failed, with test results
ado-read sprint show                                     # my team's current sprint and its work items
ado-write pr reply 412 --thread 7 --body "Fixed in the latest push."
```

## Why

**You decide what an agent can do by choosing which tools it gets.** Each tool does one job, and an agent can only run the tools installed in its environment. You don't need hooks, prompt rules or permission checks to stop it: the environment is the limit.

| Install | The agent can |
| --- | --- |
| `ado-read-pr` | Read pull requests. Nothing else. |
| `ado-read` | Read pull requests, work items, pipelines, sprints and Wikis. Change nothing. |
| `ado-read-pr` + `ado-write-pr` | Read pull requests, and also comment, reply and vote on them. |

- **The same JSON shape every time.** Every command returns one fixed envelope (`status`, `results`, `errors`) and a documented exit code. An agent reads fields; it never parses text or guesses whether the answer is complete.
- **Nothing to learn.** No MCP server to run and no protocol to configure. Short commands such as `pr show 412` take an ID or the URL from the browser.
- **You approve every write.** A write tool doesn't post on its own: each comment, vote or new pull request waits in a console window until you press `y`. An administrator can turn this off for the machine with `setx /M ADO_WRITE_APPROVAL off` (Windows) or `off` in `/etc/ado-tools/approval` (Linux, macOS); see [Approving writes](docs/writing.md#approving-writes).
- **Your token stays in the vault.** It is stored in your operating system's credential vault and never accepted as an argument, read from a file, or written to output.
- **You control what leaves Azure DevOps.** A privacy level (`minimal`, `standard`, `full`) decides whether text, comments and real names are returned. An agent can only make it stricter.

## Quick start

You need .NET 10 and an Azure DevOps Services organization.

```powershell
dotnet tool install --global ado-read --prerelease
ado-read config init --organization https://dev.azure.com/<your-org>
ado-read auth login          # paste a personal access token; it goes to the OS vault
ado-read pr list --author me
```

## Give it to your agent

- **As a skill** (Claude Code, Codex and other harnesses that read `SKILL.md`): copy
  [`skills/ado-tools`](skills/ado-tools) into `~/.claude/skills/` or `~/.codex/skills/`,
  or run `npx skills add kkolodziejczak/Ado-Tools`. It loads only when a task mentions Azure DevOps.
- **As one file** for any agent: paste [`docs/agent-instructions.md`](docs/agent-instructions.md)
  into your global `AGENTS.md` or `CLAUDE.md`.

Install steps and test prompts: [docs/agent-integration.md](docs/agent-integration.md).

## Tools

Install the umbrella tool for everything, or only the slice you need. Each package ID is also its command.

| Tool | What it does | Downloads |
| --- | --- | --- |
| [`ado-read`](https://www.nuget.org/packages/ado-read) | Every read feature below in one tool | [![ado-read downloads](https://img.shields.io/nuget/dt/ado-read?label=)](https://www.nuget.org/packages/ado-read) |
| [`ado-read-pr`](https://www.nuget.org/packages/ado-read-pr) | Pull requests, reviewers, votes and review threads | [![ado-read-pr downloads](https://img.shields.io/nuget/dt/ado-read-pr?label=)](https://www.nuget.org/packages/ado-read-pr) |
| [`ado-read-work-item`](https://www.nuget.org/packages/ado-read-work-item) | Work items, comments, WIQL queries and images | [![ado-read-work-item downloads](https://img.shields.io/nuget/dt/ado-read-work-item?label=)](https://www.nuget.org/packages/ado-read-work-item) |
| [`ado-read-pipeline`](https://www.nuget.org/packages/ado-read-pipeline) | Pull-request builds, failed steps, log tails and test results | [![ado-read-pipeline downloads](https://img.shields.io/nuget/dt/ado-read-pipeline?label=)](https://www.nuget.org/packages/ado-read-pipeline) |
| [`ado-read-sprint`](https://www.nuget.org/packages/ado-read-sprint) | Sprints and their work items | [![ado-read-sprint downloads](https://img.shields.io/nuget/dt/ado-read-sprint?label=)](https://www.nuget.org/packages/ado-read-sprint) |
| [`ado-read-wiki`](https://www.nuget.org/packages/ado-read-wiki) | Wiki pages and full-text search | [![ado-read-wiki downloads](https://img.shields.io/nuget/dt/ado-read-wiki?label=)](https://www.nuget.org/packages/ado-read-wiki) |
| [`ado-write`](https://www.nuget.org/packages/ado-write) | Every write feature below in one tool | [![ado-write downloads](https://img.shields.io/nuget/dt/ado-write?label=)](https://www.nuget.org/packages/ado-write) |
| [`ado-write-pr`](https://www.nuget.org/packages/ado-write-pr) | Create pull requests, comment, reply, resolve threads, requeue builds, vote | [![ado-write-pr downloads](https://img.shields.io/nuget/dt/ado-write-pr?label=)](https://www.nuget.org/packages/ado-write-pr) |
| [`ado-write-work-item`](https://www.nuget.org/packages/ado-write-work-item) | Comment on work items | [![ado-write-work-item downloads](https://img.shields.io/nuget/dt/ado-write-work-item?label=)](https://www.nuget.org/packages/ado-write-work-item) |

Commands follow one pattern, `<resource> <verb> [<id-or-url>]`, and accept an ID or a browser URL. Set a default project once with `config init --project` and you rarely need to name it. In a slice tool the resource moves into the name: `ado-read pr show 412` is `ado-read-pr show 412`.

## Documentation

- [Getting started](docs/getting-started.md): install, sign-in, token scopes, configuration
- [Privacy](docs/privacy.md): levels, categories, aliases
- Commands: [pull requests](docs/pull-requests.md) · [work items](docs/work-items.md) · [pipelines](docs/pipelines.md) · [sprints](docs/sprints.md) · [Wiki](docs/wiki.md) · [write tools](docs/writing.md)
- [JSON contract and exit codes](docs/json-contract.md) · [Agent skill and instructions](docs/agent-integration.md)
- [Building and releasing](docs/development.md)

## License

[MIT](LICENSE)
