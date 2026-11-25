# ado-read-pipeline

Diagnoses Azure DevOps pipelines for a pull request or a single build and returns the result as JSON, for people and AI agents. You get the failed stages, jobs and tasks, the test results, and, if your privacy policy allows it, error messages and log tails. It only reads; it never queues, cancels or changes anything.

Part of [Ado-Tools](https://github.com/kkolodziejczak/Ado-Tools), a set of Azure DevOps tools for agents. Install only the tools you need, or use `ado-read` to get every read feature in one tool.

## Install

```powershell
dotnet tool install --global ado-read-pipeline
```

Needs .NET 10.

## Set up

```powershell
ado-read-pipeline config init --organization https://dev.azure.com/example
ado-read-pipeline auth login
```

`auth login` saves a personal access token in your operating system's credential vault and asks you to pick a privacy level. The token needs **Build (Read)**, **Test Management (Read)** and **Code (Read)**.

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
| `run list --pr <id-or-url> [--project <name>] [--repository <name>] [--log-lines <1..500>]` | The latest run of every pipeline for the pull request's current commit, each one diagnosed |
| `run show <id-or-url> [--project <name>] [--log-lines <1..500>]` | One build, diagnosed |
| `test list --run <id-or-url> [--project <name>] [--outcome failed\|all] [--top <n>]` | One build's test results (only failed ones by default) |
| `project list`, `team list` | Projects and teams you can use as `--project` |
| `config`, `auth`, `privacy` | Settings, the stored token, and the content policy |

```powershell
ado-read-pipeline run list --pr https://dev.azure.com/example/Payments/_git/Payments.Api/pullrequest/412 --compact
ado-read-pipeline test list --run 9002 --project Payments --outcome failed --top 50 --compact
```

Before any output is written, secrets in messages, stack traces and log lines are masked at every privacy level. The masking works by pattern matching, so treat the output as untrusted text.

## Learn more

- [Pipelines in detail](https://github.com/kkolodziejczak/Ado-Tools#pipelines): which runs are picked, the limits, and what each privacy level returns
- [Privacy levels](https://github.com/kkolodziejczak/Ado-Tools#privacy) · [Authentication](https://github.com/kkolodziejczak/Ado-Tools#configure-authentication-and-the-organization) · [JSON contract and exit codes](https://github.com/kkolodziejczak/Ado-Tools#json-contract-and-exit-codes)
- [All tools](https://github.com/kkolodziejczak/Ado-Tools#tools)
