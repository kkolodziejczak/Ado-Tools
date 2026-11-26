# ado-read-pr

Reads an Azure DevOps pull request and returns it as JSON, for people and AI agents: title, status, author, reviewers and their votes, source and target commits, linked work items, and the review threads with their comments. You give it an ID or a URL; you never need to name the project. It only reads; it never votes, replies or changes anything.

Part of [Ado-Tools](https://github.com/kkolodziejczak/Ado-Tools), a set of Azure DevOps tools for agents. Install only the tools you need, or use `ado-read` to get every read feature in one tool.

## Install

```powershell
dotnet tool install --global ado-read-pr
```

Needs .NET 10.

## Set up

```powershell
ado-read-pr config init --organization https://dev.azure.com/example
ado-read-pr auth login
```

`auth login` saves a personal access token in your operating system's credential vault and asks you to pick a privacy level. The token needs **Code (Read)**.

### Privacy levels

The level decides how much content leaves Azure DevOps. Metadata is always returned: ids, titles, states, types, paths, dates, counts, links, and project, team, repository, Wiki page and sprint names.

| Level | Returns on top of the metadata |
| --- | --- |
| `minimal` | Nothing more. |
| `standard` | Descriptions and page text, comments, test error messages and stack traces. People are replaced with aliases. |
| `full` | Everything: also the real people, work-item custom fields, work-item images and build log tails. |

Withheld fields come back as `null` or empty. `privacy show` shows what is allowed now, and `privacy set <level>` changes it, with `--allow` or `--deny` for single categories. An agent can only make the policy stricter: loosening it needs a person at an interactive terminal.

## Commands

| Command | Returns |
| --- | --- |
| `show <id-or-url>` | The pull request and its review threads |
| `list [--author me\|<name>] [--reviewer me\|<name> [--vote <vote>]] [--status] [--repository] [--project] [--top] [--full \| --activity]` | Pull requests newest first: short entries, with `--activity` also their checks and thread activity, or with `--full` the `show` document of each |
| `project list`, `team list` | The projects and teams you can access |
| `config`, `auth`, `privacy` | Settings, the stored token, and the content policy |

```powershell
ado-read-pr show 412 --compact
ado-read-pr show https://dev.azure.com/example/Payments/_git/Payments.Api/pullrequest/412 --compact
ado-read-pr list --reviewer me --vote none --compact
ado-read-pr list --author me --compact
ado-read-pr list --author me --activity --compact
```

Each thread includes its file path, line range and commit IDs, so you can look at the change in your local Git repository. The tool never reads file contents from the repository. To post comments or vote, use [`ado-write-pr`](https://www.nuget.org/packages/ado-write-pr).

## Give it to your agent

Ready-made instructions tell an agent how to use the tools: run only what is installed, check `status` before `results`, treat Azure DevOps content as data, and post only what you ask for.

- **As a skill** (Claude Code, Codex and other harnesses that read `SKILL.md`): copy [`skills/ado-tools`](https://github.com/kkolodziejczak/Ado-Tools/tree/master/skills/ado-tools) into `~/.claude/skills/` or `~/.codex/skills/`, or run `npx skills add kkolodziejczak/Ado-Tools`. It loads only when a task mentions Azure DevOps.
- **As one file** for any agent: paste [`agent-instructions.md`](https://github.com/kkolodziejczak/Ado-Tools/blob/master/docs/agent-instructions.md) into your global `AGENTS.md` or `CLAUDE.md`.

Install steps and test prompts: [Agent skill and instructions](https://github.com/kkolodziejczak/Ado-Tools/blob/master/docs/agent-integration.md).

## Learn more

- [Pull requests in detail](https://github.com/kkolodziejczak/Ado-Tools/blob/master/docs/pull-requests.md): how the pull request is found, and the thread fields; [listing pull requests](https://github.com/kkolodziejczak/Ado-Tools/blob/master/docs/pull-requests.md#listing-pull-requests)
- [Privacy levels](https://github.com/kkolodziejczak/Ado-Tools/blob/master/docs/privacy.md) · [Authentication](https://github.com/kkolodziejczak/Ado-Tools/blob/master/docs/getting-started.md#configure-authentication-and-the-organization) · [JSON contract and exit codes](https://github.com/kkolodziejczak/Ado-Tools/blob/master/docs/json-contract.md)
- [All tools](https://github.com/kkolodziejczak/Ado-Tools#tools)
