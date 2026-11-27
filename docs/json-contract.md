# JSON contract and exit codes

Each JSON result has stable `schemaVersion`, `kind`, `status`, `query`, `generatedAtUtc`, `results`, and `errors` properties. Read responses that are not errors also carry a `privacy` section (see [Privacy](privacy.md)). `schemaVersion` is `2.0` for `pullRequestSearch`, normal `workItemSearch` reads (including `work-item list --full`), and every Wiki kind. An explicit `--download-images` work-item read uses additive `workItemSearch` schema `2.1` for success, not-found, partial, and error envelopes. It is `1.0` for `configuration`, `workItemList`, `pullRequestList`, `projectSearch`, `teamSearch`, `iterationSearch`, `sprintSearch`, the pipeline kinds `pipelinePullRequest`, `pipelineRun`, and `pipelineTests`, and the write kinds `pullRequestCreate`, `pullRequestComment`, `pullRequestReply`, `pullRequestResolve`, `pullRequestUnresolve`, `workItemComment`, `pullRequestRequeue`, and `pullRequestVote`. Work-item person fields use nullable `...UniqueName` values only; no display names, IDs, descriptors, or profile URLs are emitted. Dates are UTC ISO-8601 values, collections are always arrays, and results/errors use deterministic ordering.

| Exit code | Status | Meaning |
| ---: | --- | --- |
| `0` | `success` | Complete lookup with one or more results, or a successful config command. |
| `0` | `pendingApproval` | A write waits in its own [approval window](writing.md#approving-writes) and nothing is posted yet. Its result has `outcome: "pendingApproval"`, `approvalId` and `message`. |
| `2` | `error` | Invalid command input or configuration. |
| `3` | `notFound` | Complete work-item, PR, or build lookup found no result, or a write target (pull request, thread, work item, build policy evaluation) does not exist. |
| `4` | `partial` | One or more PR results were returned with project errors, one or more pipeline runs could not be read completely, requested work-item visual evidence is incomplete, or a build requeue stopped part-way. |
| `5` | `error` | Authentication, network, timeout, or Azure DevOps error. |
| `130` | `error` | The command was cancelled where the host can report Ctrl+C. |

A PR response is deliberately partial rather than silently incomplete when a project cannot be searched, and a pipeline response is partial when a run's evidence cannot be read. Agents should consume `results` for `success` and `partial`, then surface the structured `errors` collection before treating the response as complete.
