# Azure DevOps Services Wiki lookup for `ado-read`

## Decision

**Recommendation (inference):** add Wiki lookup as a read-only vertical slice using the already-pinned .NET client package's `WikiHttpClient` for Wiki/page retrieval, and a narrow authenticated REST client only for full-text search. Keep the public surface to discovery, metadata, page retrieval, and explicit search; do not fetch attachments, write page-view statistics, or expose any SDK mutation method.

This is a good fit for `ado-read`'s existing architecture: `AdoSession` already owns one authenticated `VssConnection`, readers turn SDK values into application-owned documents, and every command emits the common `SearchEnvelope<T>`. The current project pins `Microsoft.TeamFoundationServer.Client` 20.256.2, whose restored package contains `Microsoft.TeamFoundation.Wiki.WebApi.dll`; the corresponding official `WikiHttpClient` API exposes `GetAllWikisAsync` and page-get methods. The public SDK reference labels these methods Preview, so the CLI contract must be insulated from its model types. [WikiHttpClient](https://learn.microsoft.com/en-us/dotnet/api/microsoft.teamfoundation.wiki.webapi.wikihttpclient?view=azure-devops-dotnet)

## Verified Azure DevOps Services capabilities

| Need | Documented API | Consequence for the CLI |
| --- | --- | --- |
| Discover wikis | `GET /{organization}/{project?}/_apis/wiki/wikis?api-version=7.1`; `project` is optional. The result is `WikiV2[]`. | A collection-wide discovery command is possible; project narrowing is optional. [Wikis - List](https://learn.microsoft.com/en-us/rest/api/azure/devops/wiki/wikis/list?view=azure-devops-rest-7.1) |
| Resolve a wiki | `GET .../wikis/{wikiIdentifier}` accepts a Wiki ID or name. | Preserve both a GUID ID and name as valid inputs. [Wikis - Get](https://learn.microsoft.com/en-us/rest/api/azure/devops/wiki/wikis/get?view=azure-devops-rest-7.1) |
| Retrieve a page by path | `GET .../wikis/{wikiIdentifier}/pages?path=...`; it supports `includeContent`, recursion, and Git version descriptors. | This is the primary retrieval route. [Pages - Get Page](https://learn.microsoft.com/en-us/rest/api/azure/devops/wiki/pages/get-page?view=azure-devops-rest-7.1) |
| Retrieve a page by page ID | `GET .../wikis/{wikiIdentifier}/pages/{id}`; it supports `includeContent` and recursion. | Accept an explicit integer page ID; do not pretend it is globally unique. [Pages - Get Page By Id](https://learn.microsoft.com/en-us/rest/api/azure/devops/wiki/pages/get-page-by-id?view=azure-devops-rest-7.1) |
| Browse/list page records | `POST .../pagesbatch` returns a pageable list; its continuation token comes back in `x-ms-continuationtoken`. | Optional discovery enhancement; a POST can still be read-only, but the implementation must allow-list this one endpoint and never generalize to arbitrary POSTs. [Pages Batch - Get](https://learn.microsoft.com/en-us/rest/api/azure/devops/wiki/pages-batch/get?view=azure-devops-rest-7.1) |
| Full-text search | `POST https://almsearch.dev.azure.com/{organization}/{project?}/_apis/search/wikisearchresults?api-version=7.1`. | A documented full-text search exists; it should be an explicit command, not a hidden scan of every page. [Wiki Search Results - Fetch](https://learn.microsoft.com/en-us/rest/api/azure/devops/search/wiki-search-results/fetch-wiki-search-results?view=azure-devops-rest-7.1) |

`WikiV2` includes its ID, name, project/repository IDs, type, mapped path, version descriptors, REST URL, and browser URL; the documented types are `projectWiki` and `codeWiki`. [Wikis - Get](https://learn.microsoft.com/en-us/rest/api/azure/devops/wiki/wikis/get?view=azure-devops-rest-7.1) A page result contains path, order, Git item path, children, REST/browser URLs, page ID, and optionally content. [Pages - Get Page](https://learn.microsoft.com/en-us/rest/api/azure/devops/wiki/pages/get-page?view=azure-devops-rest-7.1)

### Full-text search: confirmed, but intentionally separate

The Search API accepts `searchText`, `$skip`, `$top`, `filters`, `$orderBy`, and `includeFacets`. It returns a total `count`, page results, facets, and `infoCode`; results include project, wiki ID/name/version, path, content ID, and highlighted hits. [Wiki Search Results - Fetch](https://learn.microsoft.com/en-us/rest/api/azure/devops/search/wiki-search-results/fetch-wiki-search-results?view=azure-devops-rest-7.1)

The documented response container is `results`, and each result's highlighted snippets are in
`hits`. Project scope is documented both as the optional URI segment and as the `Project` filter.
No Wiki-name filter key is documented. The implemented CLI therefore scopes by project through the
URI and rejects its reserved `--wiki` option with `unsupportedWikiFilter` instead of sending an
unverified request member.

Pagination is offset-based (`$skip`/`$top`), not continuation-token based. `infoCode` explicitly reports conditions such as onboarding/reindexing and top-value trimming; therefore a successful HTTP response can still be incomplete or temporarily unrepresentative. The documentation identifies indexing states but does **not** promise an indexing freshness interval. [Wiki Search Results - Fetch](https://learn.microsoft.com/en-us/rest/api/azure/devops/search/wiki-search-results/fetch-wiki-search-results?view=azure-devops-rest-7.1)

The implemented mapping treats 1, 2, 6, 7, and 9 as transient indexing states; 8 as transient
result trimming; 3, 4, 5, 19, and 20 as non-transient invalid queries; and 10 and 11 as visible
non-transient incomplete service conditions. Unknown nonzero values remain fail-safe transient
incompleteness. This classification is an application policy inferred from Microsoft's descriptions,
not a service-prescribed retry rule.

## Content, versions, and caching

Page GET uses `Accept` content negotiation and documents `application/json`, `text/plain`, and `application/zip`; JSON with `includeContent=true` returns metadata plus page content. `ado-read` should request JSON only, retaining the original Markdown text in a `content` field rather than rendering, sanitizing, or downloading linked files. [Pages - Get Page](https://learn.microsoft.com/en-us/rest/api/azure/devops/wiki/pages/get-page?view=azure-devops-rest-7.1)

The pinned SDK models `versionType` as a non-null enum whose default is `branch`; the CLI exposes
that normalized SDK value in catalogs. A caller-supplied version without a type is still resolved
against exactly one advertised descriptor before a direct page read, so it is never blindly treated
as a branch.

Microsoft's Search reference does not establish whether a Code Wiki result path is repository-rooted
or relative to `mappedPath`. The Git handoff therefore accepts only a path already inside the
resolved mapped repository boundary and does not prepend or rewrite it without live evidence.

The path endpoint accepts a Git `versionDescriptor` (branch, tag, or commit plus options); the page-ID endpoint does not document version-descriptor query parameters. Both documented GET responses include `ETag`. [Pages - Get Page](https://learn.microsoft.com/en-us/rest/api/azure/devops/wiki/pages/get-page?view=azure-devops-rest-7.1) [Pages - Get Page By Id](https://learn.microsoft.com/en-us/rest/api/azure/devops/wiki/pages/get-page-by-id?view=azure-devops-rest-7.1)

**Recommendation (inference):** expose the ETag as an opaque nullable `etag` value, but do not add cache validation or `If-None-Match` behavior in the first slice. Microsoft documents the response header but not conditional-GET semantics for these operations. Support an optional version only on the path route and return it in `query`; reject `--version` with `--page-id` locally rather than silently ignoring it.

The documented batch endpoint uses a continuation header and accepts a body containing `continuationToken`, `top`, and optional `pageViewsForDays`. The root-page recursion API is useful for a small tree, but the batch endpoint is the documented scalable page list. [Pages Batch - Get](https://learn.microsoft.com/en-us/rest/api/azure/devops/wiki/pages-batch/get?view=azure-devops-rest-7.1) **Recommendation:** defer the potentially large `wiki-pages` command until a caller needs it; page lookup and search meet the approved use case without unbounded collection output.

## Authentication and read-only boundary

Every examined Wiki/page/search read endpoint declares OAuth scope `vso.wiki`, which grants reading wikis, pages, and attachments and searching wiki pages. A least-privilege PAT should therefore have **Wiki (Read)** only; it should not gain the Wiki write scope. [Wikis - List](https://learn.microsoft.com/en-us/rest/api/azure/devops/wiki/wikis/list?view=azure-devops-rest-7.1) [Wiki Search Results - Fetch](https://learn.microsoft.com/en-us/rest/api/azure/devops/search/wiki-search-results/fetch-wiki-search-results?view=azure-devops-rest-7.1)

PATs remain usable with most Azure DevOps REST APIs, but Microsoft recommends Microsoft Entra tokens where practical and advises granting only the minimum scopes. `ado-read` already deliberately obtains a PAT from `ADO_READ_PAT`; this proposal changes neither its storage policy nor its credential mechanism. [Use personal access tokens](https://learn.microsoft.com/en-us/azure/devops/organizations/accounts/use-personal-access-tokens-to-authenticate?view=azure-devops)

The read-only guarantee must be enforced in code, not inferred from a command name:

- Only instantiate/invoke `WikiHttpClient` read methods: `GetAllWikisAsync`, `GetWikiAsync`, `GetPageAsync`, `GetPageByIdAsync`, and, if later needed, `GetPagesBatchAsync`.
- Permit the Search API's one documented `POST`; its request is a search query and not a mutation. Keep its base host literal/validated as `almsearch.dev.azure.com`, with organization and project URI-escaped.
- Do not invoke SDK methods named `Create*`, `Update*`, `Delete*`, `CreatePageMove*`, or `CreateOrUpdatePageViewStats*`; those mutable members appear beside the read methods in the official client API. [WikiHttpClient](https://learn.microsoft.com/en-us/dotnet/api/microsoft.teamfoundation.wiki.webapi.wikihttpclient?view=azure-devops-dotnet)
- Do not download attachments. The Wiki attachment operation index currently documents creation but no 7.1 attachment-list/download read operation; linked attachment URLs are untrusted content, not authorization to request more resources. [Wiki attachments](https://learn.microsoft.com/en-us/rest/api/azure/devops/wiki/attachments?view=azure-devops-rest-7.1)

## Recommended CLI surface

These are recommendations, not existing commands.

```text
ado-read wikis [--project <name-or-id>] [--config <path>] [--compact]
ado-read wiki <wiki-id-or-name-or-url> [--project <name-or-id>] [--config <path>] [--compact]
ado-read wiki page --wiki <wiki-id-or-name-or-url> (--path </page> | --page-id <positive-int>)
                   [--version <branch|tag|commit>] [--version-type branch|tag|commit]
                   [--config <path>] [--compact]
ado-read wiki search <text> [--project <name-or-id>] [--skip <non-negative-int>] [--top <positive-int>]
                   [--config <path>] [--compact]
```

`wiki page` should default to content included, since its purpose is lookup. A later `--metadata-only` is compatible; do not add an ambiguous boolean such as `--include-content` whose default callers must remember. Default search should use a bounded `top` (for example 25), return the service `count`, and reject a requested `top` beyond a documented/tested local maximum rather than loop indefinitely.

**URL handling recommendation:** recognize only HTTPS `dev.azure.com/{organization}/...` and legacy `{organization}.visualstudio.com/...` URLs, as the current work-item/PR parser does. Parse Wiki UI URLs and Wiki REST URLs into organization, project, wiki identifier, and (where present) decoded page path/page ID; verify the organization before service calls. A Wiki URL is an input convenience, not an authoritative redirect target. If a URL lacks the project or page selector required by the chosen command, return a structured `invalidWikiReference` error and request the missing flag. This needs URL fixtures from the target organization because Microsoft documents page endpoints, but does not publish a normative grammar for every browser URL shape.

## Stable JSON contracts

Use the existing envelope (`schemaVersion`, `kind`, `status`, `query`, `generatedAtUtc`, `results`, `errors`) and introduce **schema version `1.0`** for the four new kinds. Collections must always be arrays, dates must remain UTC ISO-8601 values, and output order should be deterministic. Do not leak SDK classes, unbounded `properties`, HTTP response bodies, raw request URLs containing accidental query data, or PAT values.

| Kind | Query | Result (proposed, stable application document) |
| --- | --- | --- |
| `wikiSearch` (from `wikis`) | `{ project: string? }` | `{ id, name, type, projectId, repositoryId, mappedPath, versions: [{ version, versionType, versionOptions }], rest, browser, isDisabled }` |
| `wikiLookup` | `{ identifier, project }` | the same `WikiDocument` |
| `wikiPageLookup` | `{ project, wikiIdentifier, path?, pageId?, version? }` | `{ id, path, order, isParentPage, gitItemPath, content, subPages: [{ id?, path, order, isParentPage?, gitItemPath, rest, browser }], etag, rest, browser }` |
| `wikiFullTextSearch` | `{ text, project?, skip, top }` | `{ fileName, path, contentId, project: { id, name }, wiki: { id, name, mappedPath, version }, hits: [{ fieldReferenceName, highlights: [] }] }` plus one response-level representation of service `count`, `infoCode`, and facets |

**Recommendation:** keep `query` limited to caller input. Represent search-wide `totalCount`, `infoCode`, and optional facets once in a purpose-built response metadata object rather than duplicating them onto every hit. Because the current generic envelope has no metadata member, the implementation plan must explicitly choose and version that contract before coding. `infoCode != 0` should be surfaced in structured `errors` while retaining returned results; classify reindex/onboarding codes as transient and invalid-query codes as non-transient. This is an inference from the documented codes, not a Microsoft-prescribed CLI mapping. [Wiki Search Results - Fetch](https://learn.microsoft.com/en-us/rest/api/azure/devops/search/wiki-search-results/fetch-wiki-search-results?view=azure-devops-rest-7.1)

Use `notFound` / exit `3` only after a completed single wiki/page lookup returns HTTP 404. Map 401, 403, 429, timeout/network, and 5xx through the existing `AdoReadExceptionTranslator` conventions; use stable codes `unauthorized`, `forbidden`, `throttled`, `timeout`, `networkError`, `serviceUnavailable`, and `azureDevOpsError`. This is a repository-consistency recommendation: the service references document successful responses but do not specify a Wiki-operation error schema or CLI exit codes.

## Architecture and implementation boundary

1. Add `WikiHttpClient` to `AdoSession`, then expose a narrow `IWikiSource`—not the SDK client—from `IAdoSession`. Its source contract should accept project and typed references, return application-neutral DTOs, and carry ETag where the SDK result alone cannot.
2. Put parsing in a `WikiReferenceParser` beside `AdoReferenceParser`, but use typed references that distinguish Wiki ID/name, page path, and page ID. Never use a page ID as an organization-wide identifier.
3. Add `WikiReader` and `WikiSearchReader` that map source records to the documents above and own ordering/error conversion. Keep the HTTP Search client behind `IWikiSearchSource`; share `VssConnection` credentials/settings or an explicitly constructed authenticated handler rather than copying PAT handling into commands.
4. Add command registration in `ToolComposition`, retain the global timeout/cancellation policy, and use the same one-JSON-document output guarantee as the existing commands.

## Test strategy

- **Parser unit tests:** numeric Wiki GUID/name validation; canonical and legacy organization matching; accepted UI/REST URL fixtures; percent-encoded paths, query `pagePath`, spaces, Unicode, malformed URLs, missing project/wiki/page, and organization mismatch.
- **Source tests:** mock `IWikiSource`/`IWikiSearchSource` for correct SDK argument mapping; `includeContent=true`; path versus ID dispatch; ETag preservation; page children mapping; null/optional fields; and no write method reachable from the source contract.
- **Reader/contract tests:** exact JSON property names and schema version; deterministic Wiki/page/hit ordering; always-array collections; Markdown content preserved byte-for-character as a JSON string; `notFound`, authorization, throttling, and partial/non-zero search-info behavior.
- **Pagination tests:** bounded `$skip`/`$top` validation and search `count` handling. If/when `wiki pages` is added, fake the `x-ms-continuationtoken` header across multiple batches and prove termination on missing/repeated token.
- **CLI/fake-host tests:** help text, `--compact`, `ADO_READ_PAT` requirement, organization mismatch, one JSON output document, exit codes, and explicit proof that URI/method allow-lists contain only GET plus the Search/pages-batch POSTs.
- **Optional live smoke test:** a dedicated Wiki PAT with Wiki (Read), a configured project/wiki/path and page ID, and assertions only against read responses; never save returned page content in fixtures or logs.

## Documented gaps and non-goals

- Page lookup APIs require project and wiki identifier; no documented endpoint accepts an arbitrary browser URL, so URL support is client-side parsing followed by normal lookup.
- There is no separate `GET pages` list endpoint. Root recursion gives a tree and `pagesbatch` gives a pageable list. [Pages - Get Page](https://learn.microsoft.com/en-us/rest/api/azure/devops/wiki/pages/get-page?view=azure-devops-rest-7.1) [Pages Batch - Get](https://learn.microsoft.com/en-us/rest/api/azure/devops/wiki/pages-batch/get?view=azure-devops-rest-7.1)
- Microsoft documents ETags but not conditional read semantics here; no cache behavior is proposed.
- Full-text search is indexed and returns an `infoCode`; the documentation does not make it a strongly consistent alternative to direct page retrieval.
- Attachments, page history, comments, rendering Markdown, ZIP/text response modes, and recursive all-page export are deliberately outside the first lookup slice.

## Source inventory

All external claims above use Microsoft-owned primary sources, consulted 2026-08-17:

- [Wiki REST API index](https://learn.microsoft.com/en-us/rest/api/azure/devops/wiki/wikis?view=azure-devops-rest-7.1)
- [Wikis - List](https://learn.microsoft.com/en-us/rest/api/azure/devops/wiki/wikis/list?view=azure-devops-rest-7.1)
- [Wikis - Get](https://learn.microsoft.com/en-us/rest/api/azure/devops/wiki/wikis/get?view=azure-devops-rest-7.1)
- [Pages - Get Page](https://learn.microsoft.com/en-us/rest/api/azure/devops/wiki/pages/get-page?view=azure-devops-rest-7.1)
- [Pages - Get Page By Id](https://learn.microsoft.com/en-us/rest/api/azure/devops/wiki/pages/get-page-by-id?view=azure-devops-rest-7.1)
- [Pages Batch - Get](https://learn.microsoft.com/en-us/rest/api/azure/devops/wiki/pages-batch/get?view=azure-devops-rest-7.1)
- [Wiki Search Results - Fetch](https://learn.microsoft.com/en-us/rest/api/azure/devops/search/wiki-search-results/fetch-wiki-search-results?view=azure-devops-rest-7.1)
- [WikiHttpClient](https://learn.microsoft.com/en-us/dotnet/api/microsoft.teamfoundation.wiki.webapi.wikihttpclient?view=azure-devops-dotnet)
- [Use personal access tokens](https://learn.microsoft.com/en-us/azure/devops/organizations/accounts/use-personal-access-tokens-to-authenticate?view=azure-devops)
