using System.Net;
using AdoRead.AzureDevOps;
using AdoRead.Configuration;
using AdoRead.Credentials;
using AdoRead.Terminal;
using AdoWrite.AzureDevOps;
using Microsoft.TeamFoundation.Policy.WebApi;
using Microsoft.TeamFoundation.SourceControl.WebApi;
using Newtonsoft.Json.Linq;
using Microsoft.VisualStudio.Services.WebApi;

namespace AdoWrite.UnitTests;

/// <summary>Fabricated people, pull request and work item shared by the tests.</summary>
internal static class Fabricated
{
    public static readonly Uri Organization = new("https://dev.azure.com/fabricated/");
    public static readonly Person Author = new(Guid.Parse("00000000-0000-0000-0000-00000000a001"), "Example Author");
    public static readonly Person Reviewer = new(Guid.Parse("00000000-0000-0000-0000-00000000b002"), "Example Reviewer");
    public static readonly Guid ProjectId = Guid.Parse("00000000-0000-0000-0000-0000000000a1");
    public static readonly Guid RepositoryId = Guid.Parse("00000000-0000-0000-0000-0000000000b1");
    public const int PullRequestId = 42;
    public const int WorkItemId = 7;
    public const string Project = "Fabricated App";
    public const string Repository = "Fabricated.Repo";
    public const string SourceCommit = "source-commit";
    public const string BaseCommit = "base-commit";

    public static VssServiceResponseException NotFound() => new(HttpStatusCode.NotFound, "Not found.", null);

    /// <summary>A pull-request comment written by <paramref name="person"/>, the signed-in author by default.</summary>
    public static Comment CommentBy(int id, string content, Person? person = null, short parentId = 0) =>
        new()
        {
            Id = (short)id,
            ParentCommentId = parentId,
            Content = content,
            Author = new IdentityRef { Id = (person ?? Author).Id.ToString() }
        };
}

internal sealed class FakePeopleDirectory : IPeopleDirectory
{
    public Dictionary<string, IReadOnlyList<Person>> Matches { get; } = new(StringComparer.OrdinalIgnoreCase)
    {
        ["reviewer@example.com"] = [Fabricated.Reviewer]
    };

    public List<string> Searches { get; } = [];

    public Person SignedIn { get; set; } = Fabricated.Author;

    public Task<Person> GetSignedInUserAsync(CancellationToken cancellationToken) => Task.FromResult(SignedIn);

    public Task<IReadOnlyList<Person>> FindAsync(string value, CancellationToken cancellationToken)
    {
        Searches.Add(value);
        return Task.FromResult(Matches.GetValueOrDefault(value) ?? []);
    }
}

internal sealed class FakePullRequestCommentClient : IPullRequestCommentClient
{
    public Exception? PullRequestFailure { get; set; }

    public PullRequestStatus Status { get; set; } = PullRequestStatus.Active;

    public bool IsDraft { get; set; }

    public List<IdentityRefWithVote> Reviewers { get; } = [];

    public List<GitPullRequestCommentThread> Threads { get; } = [];

    public List<GitPullRequestIteration> Iterations { get; } =
    [
        new() { Id = 1, SourceRefCommit = new GitCommitRef { CommitId = "old-source" }, CommonRefCommit = new GitCommitRef { CommitId = Fabricated.BaseCommit } },
        new() { Id = 3, SourceRefCommit = new GitCommitRef { CommitId = Fabricated.SourceCommit }, CommonRefCommit = new GitCommitRef { CommitId = Fabricated.BaseCommit } }
    ];

    public List<GitPullRequestChange> Changes { get; } =
    [
        new() { ChangeTrackingId = 11, ChangeType = VersionControlChangeType.Edit, Item = new GitItem { Path = "/src/App.cs" } },
        new() { ChangeTrackingId = 12, ChangeType = VersionControlChangeType.Add, Item = new GitItem { Path = "/src/New.cs" } },
        new() { ChangeTrackingId = 13, ChangeType = VersionControlChangeType.Rename | VersionControlChangeType.Edit, Item = new GitItem { Path = "/src/Renamed.cs" }, OriginalPath = "/src/Original.cs" },
        new() { ChangeTrackingId = 14, ChangeType = VersionControlChangeType.Add, Item = new GitItem { Path = "/src/Empty.cs" } },
        new() { ChangeTrackingId = 15, ChangeType = VersionControlChangeType.Add, Item = new GitItem { Path = "/src/FormFeed.cs" } }
    ];

    /// <summary>File text by (commit, path).</summary>
    public Dictionary<(string Commit, string Path), string> Files { get; } = new()
    {
        [(Fabricated.SourceCommit, "/src/App.cs")] = "line one\nline two is longer\nline three\n",
        [(Fabricated.BaseCommit, "/src/App.cs")] = "old one\nold two\n",
        [(Fabricated.SourceCommit, "/src/New.cs")] = "new\n",
        [(Fabricated.BaseCommit, "/src/Original.cs")] = "original one\noriginal two\n",
        [(Fabricated.SourceCommit, "/src/Empty.cs")] = "",
        [(Fabricated.SourceCommit, "/src/FormFeed.cs")] = "one\fstill one\u2028still one\r\ntwo\r\n"
    };

    public List<int> ChangeRequests { get; } = [];

    public List<GitPullRequestCommentThread> CreatedThreads { get; } = [];

    public List<(int ThreadId, Comment Comment)> CreatedComments { get; } = [];

    public Task<GitPullRequest> GetPullRequestAsync(int pullRequestId, CancellationToken cancellationToken)
    {
        if (PullRequestFailure is not null)
        {
            throw PullRequestFailure;
        }

        return Task.FromResult(new GitPullRequest
        {
            PullRequestId = pullRequestId,
            Status = Status,
            IsDraft = IsDraft,
            Reviewers = [.. Reviewers],
            Repository = new GitRepository
            {
                Id = Fabricated.RepositoryId,
                Name = Fabricated.Repository,
                ProjectReference = new Microsoft.TeamFoundation.Core.WebApi.TeamProjectReference
                {
                    Id = Fabricated.ProjectId,
                    Name = Fabricated.Project
                }
            }
        });
    }

    /// <summary>How long a thread lookup takes, so two runs can look before either posts.</summary>
    public TimeSpan LookupDelay { get; set; }

    /// <summary>Adds each created thread to <see cref="Threads"/>, as Azure DevOps does.</summary>
    public bool KeepsCreated { get; set; }

    public async Task<IReadOnlyList<GitPullRequestCommentThread>> GetThreadsAsync(PullRequestLocation location, CancellationToken cancellationToken)
    {
        await Task.Delay(LookupDelay, cancellationToken);
        lock (Threads)
        {
            return [.. Threads];
        }
    }

    public Task<IReadOnlyList<GitPullRequestIteration>> GetIterationsAsync(PullRequestLocation location, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<GitPullRequestIteration>>(Iterations);

    public Task<IReadOnlyList<GitPullRequestChange>> GetChangesAsync(PullRequestLocation location, int iterationId, CancellationToken cancellationToken)
    {
        ChangeRequests.Add(iterationId);
        return Task.FromResult<IReadOnlyList<GitPullRequestChange>>(Changes);
    }

    public Task<string> GetFileTextAsync(PullRequestLocation location, string path, string commitId, CancellationToken cancellationToken) =>
        Task.FromResult(Files[(commitId, path)]);

    public List<Attachment> Attachments { get; } = [];

    /// <summary>Uploaded files by stored name, with their bytes.</summary>
    public List<(string Name, byte[] Content)> Uploads { get; } = [];

    public Task<IReadOnlyList<Attachment>> GetAttachmentsAsync(PullRequestLocation location, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Attachment>>(Attachments);

    public async Task<Attachment> UploadAttachmentAsync(PullRequestLocation location, string fileName, Stream content, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, cancellationToken);
        Uploads.Add((fileName, buffer.ToArray()));
        return new Attachment
        {
            DisplayName = fileName,
            Url = $"https://dev.azure.com/fabricated/_apis/git/repositories/{Fabricated.RepositoryId}/pullRequests/42/attachments/{Uri.EscapeDataString(fileName)}"
        };
    }

    public Task<GitPullRequestCommentThread> CreateThreadAsync(PullRequestLocation location, GitPullRequestCommentThread thread, CancellationToken cancellationToken)
    {
        var created = new GitPullRequestCommentThread
        {
            Id = 900,
            Comments = thread.Comments.Select(comment => Fabricated.CommentBy(1, comment.Content)).ToList()
        };
        lock (Threads)
        {
            CreatedThreads.Add(thread);
            if (KeepsCreated)
            {
                Threads.Add(created);
            }
        }

        return Task.FromResult(created);
    }

    public Exception? CreateCommentFailure { get; set; }

    /// <summary>Runs after a reply is stored, for example another reviewer closing the thread meanwhile.</summary>
    public Action? AfterCreateComment { get; set; }

    public Task<Comment> CreateCommentAsync(PullRequestLocation location, int threadId, Comment comment, CancellationToken cancellationToken)
    {
        if (CreateCommentFailure is not null)
        {
            throw CreateCommentFailure;
        }

        Comment created = Fabricated.CommentBy(5, comment.Content, parentId: comment.ParentCommentId);
        lock (Threads)
        {
            CreatedComments.Add((threadId, comment));
            if (KeepsCreated && Threads.FirstOrDefault(thread => thread.Id == threadId) is { } thread)
            {
                thread.Comments = [.. thread.Comments ?? [], created];
            }
        }

        AfterCreateComment?.Invoke();
        return Task.FromResult(created);
    }

    /// <summary>Replaces a thread with a new object of another status, so a snapshot read earlier keeps the old one.</summary>
    public void ChangeStatusElsewhere(int threadId, CommentThreadStatus status)
    {
        lock (Threads)
        {
            int index = Threads.FindIndex(thread => thread.Id == threadId);
            GitPullRequestCommentThread old = Threads[index];
            Threads[index] = new GitPullRequestCommentThread { Id = old.Id, Status = status, Comments = old.Comments };
        }
    }

    public List<(int ThreadId, CommentThreadStatus Status)> StatusUpdates { get; } = [];

    public Exception? UpdateThreadFailure { get; set; }

    public Task<GitPullRequestCommentThread> UpdateThreadStatusAsync(
        PullRequestLocation location,
        int threadId,
        CommentThreadStatus status,
        CancellationToken cancellationToken)
    {
        if (UpdateThreadFailure is not null)
        {
            throw UpdateThreadFailure;
        }

        lock (Threads)
        {
            StatusUpdates.Add((threadId, status));
            GitPullRequestCommentThread thread = Threads.Single(candidate => candidate.Id == threadId);
            thread.Status = status;
            return Task.FromResult(thread);
        }
    }
}

internal sealed class FakeWorkItemCommentClient : IWorkItemCommentClient
{
    public string? Project { get; set; } = Fabricated.Project;

    public List<WorkItemComment> Comments { get; } = [];

    public List<(string Project, int WorkItemId, string Markdown)> Added { get; } = [];

    /// <summary>Simulates a service that rewrites the stored text, for example by dropping HTML comments.</summary>
    public Func<string, string> Store { get; set; } = text => text;

    public List<WorkItemAttachment> Attached { get; } = [];

    public List<string> Uploads { get; } = [];

    /// <summary>Each file added to the work item, in order.</summary>
    public List<WorkItemAttachment> AttachmentUpdates { get; } = [];

    /// <summary>Fails the upload of this file name, to simulate an interrupted run.</summary>
    public string? FailUploadOf { get; set; }

    public Exception? AddCommentFailure { get; set; }

    public Task<WorkItemTarget> GetWorkItemAsync(int workItemId, CancellationToken cancellationToken) =>
        Task.FromResult(new WorkItemTarget(Project, [.. Attached]));

    public Task<Uri> UploadAttachmentAsync(string project, string fileName, Stream content, CancellationToken cancellationToken)
    {
        if (fileName == FailUploadOf)
        {
            throw new VssServiceResponseException(HttpStatusCode.ServiceUnavailable, "Down.", null);
        }

        Uploads.Add(fileName);
        return Task.FromResult(new Uri($"https://dev.azure.com/fabricated/_apis/wit/attachments/{Uploads.Count}?fileName={Uri.EscapeDataString(fileName)}"));
    }

    public Task AddAttachmentAsync(int workItemId, WorkItemAttachment attachment, CancellationToken cancellationToken)
    {
        AttachmentUpdates.Add(attachment);
        Attached.Add(attachment);
        return Task.CompletedTask;
    }

    /// <summary>How long a comment lookup takes, so two runs can look before either posts.</summary>
    public TimeSpan LookupDelay { get; set; }

    /// <summary>Adds each posted comment to <see cref="Comments"/>, as Azure DevOps does.</summary>
    public bool KeepsAdded { get; set; }

    public async Task<IReadOnlyList<WorkItemComment>> GetCommentsAsync(string project, int workItemId, CancellationToken cancellationToken)
    {
        await Task.Delay(LookupDelay, cancellationToken);
        lock (Comments)
        {
            return [.. Comments];
        }
    }

    public Task<WorkItemComment> AddCommentAsync(string project, int workItemId, string markdown, CancellationToken cancellationToken)
    {
        if (AddCommentFailure is not null)
        {
            throw AddCommentFailure;
        }

        var created = new WorkItemComment(31, Store(markdown), false, Fabricated.Author.Id);
        lock (Comments)
        {
            Added.Add((project, workItemId, markdown));
            if (KeepsAdded)
            {
                Comments.Add(created);
            }
        }

        return Task.FromResult(created);
    }
}

internal sealed class FakePullRequestReviewClient : IPullRequestReviewClient
{
    public static readonly Guid BuildPolicyType = Guid.Parse("0609b952-1397-4640-95ec-e00a01b2c241");

    public List<PolicyEvaluationRecord> Evaluations { get; } = [];

    public List<(Guid ProjectId, Guid EvaluationId)> Requeued { get; } = [];

    public List<(PullRequestLocation Location, Guid ReviewerId, short Vote)> Votes { get; } = [];

    /// <summary>What a single read returns instead of the listed record, for an evaluation that changed since the list.</summary>
    public Dictionary<Guid, PolicyEvaluationRecord> Changed { get; } = [];

    /// <summary>Fails the requeue of these evaluations, after recording nothing.</summary>
    public Dictionary<Guid, Exception> RequeueFailures { get; } = [];

    /// <summary>The vote Azure DevOps stores, when it differs from the one sent.</summary>
    public short? StoredVote { get; set; }

    /// <summary>A build-policy evaluation as Azure DevOps reports it; <paramref name="context"/> is its internal context JSON.</summary>
    public static PolicyEvaluationRecord Build(
        Guid evaluationId,
        PolicyEvaluationStatus status,
        string? context = null,
        string? displayName = "PR build",
        Guid? policyType = null) =>
        new()
        {
            EvaluationId = evaluationId,
            Status = status,
            Context = context is null ? null : JObject.Parse(context),
            Configuration = new PolicyConfiguration
            {
                IsBlocking = true,
                Type = new PolicyTypeRef { Id = policyType ?? BuildPolicyType, DisplayName = "Build" },
                Settings = displayName is null ? new JObject() : new JObject { ["displayName"] = displayName }
            }
        };

    public Task<IReadOnlyList<PolicyEvaluationRecord>> GetPolicyEvaluationsAsync(PullRequestLocation location, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<PolicyEvaluationRecord>>(Evaluations);

    public Task<PolicyEvaluationRecord> GetPolicyEvaluationAsync(PullRequestLocation location, Guid evaluationId, CancellationToken cancellationToken) =>
        Task.FromResult(Changed.GetValueOrDefault(evaluationId) ?? Evaluations.First(evaluation => evaluation.EvaluationId == evaluationId));

    public Task<PolicyEvaluationRecord> RequeuePolicyEvaluationAsync(PullRequestLocation location, Guid evaluationId, CancellationToken cancellationToken)
    {
        if (RequeueFailures.TryGetValue(evaluationId, out Exception? failure))
        {
            throw failure;
        }

        Requeued.Add((location.ProjectId, evaluationId));
        PolicyEvaluationRecord record = Evaluations.First(evaluation => evaluation.EvaluationId == evaluationId);
        record.Status = PolicyEvaluationStatus.Queued;
        record.Context = JObject.Parse("{\"isExpired\":false}");
        return Task.FromResult(record);
    }

    public Task<IdentityRefWithVote> SetVoteAsync(PullRequestLocation location, Guid reviewerId, short vote, CancellationToken cancellationToken)
    {
        Votes.Add((location, reviewerId, vote));
        return Task.FromResult(new IdentityRefWithVote { Id = reviewerId.ToString(), Vote = StoredVote ?? vote });
    }
}

/// <summary>The fabricated repository, whose default branch is main, and the pull requests created in it.</summary>
internal sealed class FakePullRequestCreateClient : IPullRequestCreateClient
{
    public const int CreatedId = 900;

    public List<(string? Project, string Repository)> RepositoryLookups { get; } = [];

    public Exception? RepositoryFailure { get; set; }

    public List<GitPullRequest> Active { get; } = [];

    /// <summary>Full ref names the repository does not have; every other branch exists.</summary>
    public HashSet<string> MissingBranches { get; } = [];

    public Task<bool> BranchExistsAsync(GitRepository repository, string refName, CancellationToken cancellationToken) =>
        Task.FromResult(!MissingBranches.Contains(refName));

    public List<GitPullRequest> Created { get; } = [];

    public Exception? CreateFailure { get; set; }

    /// <summary>The pull request another run opened just before this one tried, so Azure DevOps refuses the create.</summary>
    public GitPullRequest? AppearsOnConflict { get; set; }

    public Task<GitRepository> GetRepositoryAsync(string? project, string repository, CancellationToken cancellationToken)
    {
        RepositoryLookups.Add((project, repository));
        return RepositoryFailure is not null
            ? throw RepositoryFailure
            : Task.FromResult(new GitRepository
            {
                Id = Fabricated.RepositoryId,
                Name = Fabricated.Repository,
                DefaultBranch = "refs/heads/main",
                ProjectReference = new Microsoft.TeamFoundation.Core.WebApi.TeamProjectReference
                {
                    Id = Fabricated.ProjectId,
                    Name = Fabricated.Project
                }
            });
    }

    public Task<IReadOnlyList<GitPullRequest>> GetActivePullRequestsAsync(
        GitRepository repository,
        string sourceRefName,
        string targetRefName,
        CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<GitPullRequest>>(Active
            .Where(pullRequest => pullRequest.SourceRefName == sourceRefName && pullRequest.TargetRefName == targetRefName)
            .ToArray());

    public Task<GitPullRequest> CreatePullRequestAsync(GitRepository repository, GitPullRequest pullRequest, CancellationToken cancellationToken)
    {
        if (CreateFailure is not null)
        {
            if (AppearsOnConflict is not null)
            {
                Active.Add(AppearsOnConflict);
            }

            throw CreateFailure;
        }

        Created.Add(pullRequest);
        return Task.FromResult(new GitPullRequest
        {
            PullRequestId = CreatedId,
            Title = pullRequest.Title,
            SourceRefName = pullRequest.SourceRefName,
            TargetRefName = pullRequest.TargetRefName,
            IsDraft = pullRequest.IsDraft
        });
    }
}

internal sealed class FakeWriteSession : IAdoWriteSession
{
    public FakePullRequestCommentClient FakePullRequests { get; } = new();

    public FakeWorkItemCommentClient FakeWorkItems { get; } = new();

    public FakePeopleDirectory FakePeople { get; } = new();

    public FakePullRequestReviewClient FakeReviews { get; } = new();

    public IPullRequestReviewClient Reviews => FakeReviews;

    public IPullRequestCommentClient PullRequests => FakePullRequests;

    public IWorkItemCommentClient WorkItems => FakeWorkItems;

    public IPeopleDirectory People => FakePeople;

    public FakePullRequestCreateClient FakeCreation { get; } = new();

    public IPullRequestCreateClient PullRequestCreation => FakeCreation;

    public bool Disposed { get; private set; }

    public ValueTask DisposeAsync()
    {
        Disposed = true;
        return ValueTask.CompletedTask;
    }
}

internal sealed class FakeWriteSessionFactory(FakeWriteSession session) : IAdoWriteSessionFactory
{
    public string? Pat { get; private set; }

    /// <summary>Thrown by <see cref="Create"/>, as the SDK does when it connects with a token Azure DevOps refuses.</summary>
    public Exception? CreateFailure { get; set; }

    public IAdoWriteSession Create(Uri organizationUrl, string pat)
    {
        Pat = pat;
        return CreateFailure is null ? session : throw CreateFailure;
    }
}

/// <summary>Says every token belongs to <see cref="DisplayName"/>, without Azure DevOps.</summary>
internal sealed class FakeTokenOwners : ITokenOwnerLookup
{
    public const string DisplayName = "Fabricated Owner";

    public Task<string> FindDisplayNameAsync(Uri organizationUrl, string pat, CancellationToken cancellationToken) =>
        Task.FromResult(DisplayName);
}

internal sealed class FixedConfigurationStore : IConfigurationStore
{
    public string DefaultPath => "memory://config";

    public Task<AdoReadConfiguration> LoadAsync(string? overridePath, CancellationToken cancellationToken) =>
        Task.FromResult(AdoReadConfiguration.Create(
            Fabricated.Organization,
            AdoReadConfiguration.DefaultRequestTimeoutSeconds,
            AdoReadConfiguration.DefaultMaxParallelProjects));

    public Task SaveAsync(string? overridePath, AdoReadConfiguration configuration, bool overwrite, CancellationToken cancellationToken) =>
        throw new NotSupportedException();
}

internal sealed class MemoryCredentialVault : ICredentialVault
{
    private readonly Dictionary<string, string> _secrets = new(StringComparer.Ordinal);

    public string Kind => "memory";

    public string? Read(string account) => _secrets.GetValueOrDefault(account);

    public void Write(string account, string secret) => _secrets[account] = secret;

    public bool Remove(string account) => _secrets.Remove(account);
}

/// <summary>Answers the masked token prompt with <paramref name="answer"/> and each plain prompt with the next of <paramref name="lines"/>.</summary>
internal sealed class ScriptedTerminalInput(string answer, params string[] lines) : ITerminalInput
{
    private readonly Queue<string> _lines = new(lines);

    public List<string> Prompts { get; } = [];

    public bool IsInputRedirected => false;

    public string ReadMaskedLine(string prompt)
    {
        Prompts.Add(prompt);
        return answer;
    }

    public string ReadLine(string prompt) =>
        _lines.TryDequeue(out string? line) ? line : throw new InvalidOperationException($"Unexpected prompt: {prompt}");
}
