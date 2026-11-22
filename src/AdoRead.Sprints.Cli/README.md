# ado-read-sprints

Lists a team's Azure DevOps sprints and shows what is in one, as JSON, for people and AI agents. For each sprint you get its dates, a count of work items per state, and a short entry for every work item (ID, type, title, state, assignee). Filtering by state answers questions like "what is done?" in your board's own words. It only reads.

Part of [Ado-Tools](https://github.com/kkolodziejczak/Ado-Tools), a set of Azure DevOps tools for agents. Install only the tools you need, or use `AdoRead.Tool` (`ado-read`) to get every read feature in one tool.

## Install

```powershell
dotnet tool install --global AdoRead.Sprints.Tool
```

Needs .NET 10.

## Set up

Sprints belong to a team, so set the project and the team once:

```powershell
ado-read-sprints config init --organization https://dev.azure.com/example --project Payments --team "Payments Team"
ado-read-sprints auth login
```

`auth login` saves a personal access token in your operating system's credential vault and asks you to pick a privacy level. The token needs **Work Items (Read)**, plus **Project and Team (Read)** for `projects` and `teams`. If you don't know the names, run `projects`, then `teams --project <name>`.

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
| `list [--timeframe current\|past\|future]` | The team's sprints |
| `show [--sprint <name-or-path>] [--state <state>]...` | One sprint (the current one by default) with its work items |
| `projects`, `teams [--project <name>] [--all]` | The projects and teams you can access |
| `config`, `auth`, `privacy` | Settings, the stored token, and the content policy |

```powershell
ado-read-sprints show --compact
ado-read-sprints show --sprint "Sprint 42" --state Done --state Closed --compact
```

## Learn more

- [Sprints in detail](https://github.com/kkolodziejczak/Ado-Tools#sprints)
- [Privacy levels](https://github.com/kkolodziejczak/Ado-Tools#privacy) · [Authentication](https://github.com/kkolodziejczak/Ado-Tools#configure-authentication-and-the-organization) · [JSON contract and exit codes](https://github.com/kkolodziejczak/Ado-Tools#json-contract-and-exit-codes)
- [All tools](https://github.com/kkolodziejczak/Ado-Tools#tools)
