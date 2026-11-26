# Sprints

Work-item and pull-request IDs are organization scoped, but **iterations are team scoped**: Azure DevOps has no organization-wide "current sprint", so the sprint commands need a project and a team. Set them once and they stop being flags:

```powershell
ado-read config init --organization https://dev.azure.com/example --project Payments --team "Payments Team"
ado-read sprint show --compact
```

If you do not know the names, walk to them. Each command lists what the next one needs, so a person or an agent can follow the chain without guessing:

```powershell
ado-read project list --compact                          # pick a project
ado-read team list --project Payments --compact          # pick a team (yours only; --all for every team)
ado-read sprint list --project Payments --team "Payments Team" --compact   # pick an iteration
```

Running `ado-read sprint show` with no project or team configured is not a crash: it exits `2` with a `projectRequired` or `teamRequired` error whose message names the command to run next.

`ado-read sprint show` returns the current iteration unless `--sprint` names one, matched against iteration name then path, case-insensitively. Results hold a single sprint: the resolved iteration with its dates, a `countsByState` map, and one shallow entry per work item — ID, type, title, state, iteration path, assignee and a browser URL. No descriptions, acceptance criteria or comments are fetched, which keeps the whole sprint to two calls.

Use `--state` to answer "what is done" or "what can be demoed" with your own board's vocabulary. It is repeatable, case-insensitive, and `countsByState` reflects only what survived the filter:

```powershell
ado-read sprint show --state Done --state Closed --compact
ado-read sprint show --sprint "Sprint 42" --state "In Test" --compact
```

For one item's full detail — description, acceptance criteria, comments, relations — follow a summary entry's ID into the existing command:

```powershell
ado-read work-item show 4821 --compact
```
