# ado-read

Reads Azure DevOps pull requests, work items, pipelines, Wiki pages and sprints and returns them as JSON, for people and AI agents. It is the all-in-one read tool: it contains every read feature of [Ado-Tools](https://github.com/kkolodziejczak/Ado-Tools). It never changes anything in Azure DevOps, and it never stores your token anywhere but your operating system's credential vault.

## Install

```powershell
dotnet tool install --global AdoRead.Tool
```

Needs .NET 10.

## Set up

```powershell
ado-read config init --organization https://dev.azure.com/example
ado-read auth login
```

`auth login` saves a personal access token and asks you to pick a privacy level (`minimal`, `standard` or `full`), which decides how much content the commands return. Give the token only the read scopes for the commands you use: **Code**, **Work Items**, **Wiki**, **Build**, **Test Management** and **Project and Team** (all Read).

### Privacy levels

The level decides how much content leaves Azure DevOps. Metadata is always returned: ids, titles, states, types, paths, dates, counts, links, and project, team, repository, Wiki page and sprint names.

| Level | Returns on top of the metadata |
| --- | --- |
| `minimal` | Nothing more. |
| `standard` | Descriptions and page text, comments, test error messages and stack traces. People are replaced with aliases. |
| `full` | Everything: also the real people, work-item custom fields, work-item images and build log tails. |

Withheld fields come back as `null` or empty. `privacy show` shows what is allowed now, and `privacy set <level>` changes it, with `--allow` or `--deny` for single categories. An agent can only make the policy stricter: loosening it needs a person at an interactive terminal.

## Commands

| Command | Returns | Also available on its own |
| --- | --- | --- |
| `pr <id-or-url>` | A pull request with its review threads | [`AdoRead.PullRequests.Tool`](https://www.nuget.org/packages/AdoRead.PullRequests.Tool) (`ado-read-pr`) |
| `work-item <id-or-url> [--download-images <dir>]` | A work item, and optionally its images | [`AdoRead.WorkItems.Tool`](https://www.nuget.org/packages/AdoRead.WorkItems.Tool) (`ado-read-work-item`) |
| `pipelines pr\|run\|tests` | Pipeline diagnosis for a pull request or a build | [`AdoRead.Pipelines.Tool`](https://www.nuget.org/packages/AdoRead.Pipelines.Tool) (`ado-read-pipelines`) |
| `wiki list\|pages\|page\|search` | Wiki discovery, search and page reads | [`AdoRead.Wiki.Tool`](https://www.nuget.org/packages/AdoRead.Wiki.Tool) (`ado-read-wiki`) |
| `sprints list\|show` | A team's sprints and their work items | [`AdoRead.Sprints.Tool`](https://www.nuget.org/packages/AdoRead.Sprints.Tool) (`ado-read-sprints`) |
| `projects`, `teams` | The projects and teams you can access | every read tool |
| `config`, `auth`, `privacy` | Settings, the stored token, and the content policy | every read tool |

```powershell
ado-read pr 412 --compact
ado-read work-item 12345 --compact
ado-read pipelines pr 412 --project Payments --compact
ado-read sprints show --compact
```

The single-feature tools have the same commands without the feature name (`ado-read-pr 412`), so an agent or a team can install only what it needs. To post comments, requeue builds or vote, install [`AdoWrite.Tool`](https://www.nuget.org/packages/AdoWrite.Tool) (`ado-write`). It is a separate package, so installing a read tool never lets an agent post anything.

## Learn more

- [Full documentation](https://github.com/kkolodziejczak/Ado-Tools#readme): every command, its limits and its output
- [Privacy levels](https://github.com/kkolodziejczak/Ado-Tools#privacy) · [Authentication](https://github.com/kkolodziejczak/Ado-Tools#configure-authentication-and-the-organization) · [JSON contract and exit codes](https://github.com/kkolodziejczak/Ado-Tools#json-contract-and-exit-codes)
