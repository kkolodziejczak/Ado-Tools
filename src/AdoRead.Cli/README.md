# ado-read

Reads Azure DevOps pull requests, work items, pipelines, Wiki pages and sprints and returns them as JSON, for people and AI agents. It is the all-in-one read tool: it contains every read feature of [Ado-Tools](https://github.com/kkolodziejczak/Ado-Tools). It never changes anything in Azure DevOps, and it never stores your token anywhere but your operating system's credential vault.

## Install

```powershell
dotnet tool install --global ado-read
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
| `pr show <id-or-url>`, `pr list [--author] [--reviewer] [--vote] [--status] [--full]` | A pull request with its review threads, or the pull requests matching filters | [`ado-read-pr`](https://www.nuget.org/packages/ado-read-pr) |
| `work-item show <id-or-url> [--download-images <dir>]`, `work-item list [filters \| --wiql <query>] [--full]` | A work item, and optionally its images; or the work items matching filters or a WIQL query | [`ado-read-work-item`](https://www.nuget.org/packages/ado-read-work-item) |
| `pipeline run list --pr <id-or-url>`, `pipeline run show <id-or-url>`, `pipeline test list --run <id-or-url>` | Pipeline diagnosis for a pull request or a build | [`ado-read-pipeline`](https://www.nuget.org/packages/ado-read-pipeline) |
| `wiki list`, `wiki page list\|show\|search` | Wiki discovery, search and page reads | [`ado-read-wiki`](https://www.nuget.org/packages/ado-read-wiki) |
| `sprint list\|show` | A team's sprints and their work items | [`ado-read-sprint`](https://www.nuget.org/packages/ado-read-sprint) |
| `project list`, `team list` | The projects and teams you can access | every read tool |
| `config`, `auth`, `privacy` | Settings, the stored token, and the content policy | every read tool |

```powershell
ado-read pr show 412 --compact
ado-read pr list --reviewer me --vote none --compact
ado-read work-item show 12345 --compact
ado-read work-item list --tag needs-refinement --compact
ado-read pipeline run list --pr 412 --project Payments --compact
ado-read sprint show --compact
```

The single-feature tools have the same commands without the feature name (`ado-read-pr show 412`), so an agent or a team can install only what it needs. To post comments, requeue builds or vote, install [`ado-write`](https://www.nuget.org/packages/ado-write). It is a separate package, so installing a read tool never lets an agent post anything.

## Learn more

- [Full documentation](https://github.com/kkolodziejczak/Ado-Tools#readme): every command, its limits and its output
- [Privacy levels](https://github.com/kkolodziejczak/Ado-Tools/blob/master/docs/privacy.md) · [Authentication](https://github.com/kkolodziejczak/Ado-Tools/blob/master/docs/getting-started.md#configure-authentication-and-the-organization) · [JSON contract and exit codes](https://github.com/kkolodziejczak/Ado-Tools/blob/master/docs/json-contract.md)
