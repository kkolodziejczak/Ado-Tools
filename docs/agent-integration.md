# Agent integration

Use compact output when passing a result to another program or agent:

```powershell
ado-read work-item show 12345 --compact | ConvertFrom-Json
ado-read pr show 412 --compact | ConvertFrom-Json
```

Check `status` before acting on `results`. Do not infer that a missing PR is absent when the status is `partial` or `error`; inspect `errors` first. The tool returns context only—it does not evaluate requirements or review feedback.

For visual work-item acceptance, create a unique empty temporary directory owned by the consumer and pass each argument separately:

```powershell
ado-read work-item "12345" --download-images "C:\agent-temp\ado-read-12345" --compact
```

Require `kind: "workItemSearch"`, schema `2.1`, and the matching exit/status pair. For `success` and `partial`, also require `query.downloadImagesDirectory` and `media.outputDirectory` to equal the resolved requested directory. A valid `notFound` or `error` envelope may have no resolved download directory or media result because validation, configuration, authentication, cancellation, or lookup can fail before visual evidence exists; do not attempt to consume images from those statuses. Validate every `downloaded` path and load it with the available vision facility. Never pass a PAT, authorization header, protected URL, cookie, or image URL to a model. Remove only the consumer-created temporary directory after all observations and provenance are captured, and validate the resolved target before recursive cleanup; the tool itself removes invocation-owned staging/final files on global failure or cancellation without deleting caller-owned files or recursively deleting the supplied directory.

For PR review context, verify file and ancestry details from the v2 response in the matching local repository. For example, after confirming the response's source and target revisions exist locally, use `git diff <targetCommitId>...<sourceCommitId> -- <thread.filePath>` and inspect the reported line range. Do not treat a nullable `isPossiblyOutdated` value as a relevance verdict.
