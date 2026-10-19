using System.Net;
using AdoRead.Configuration;
using AdoRead.Credentials;
using AdoRead.Terminal;
using AdoWrite.AzureDevOps;
using Microsoft.TeamFoundation.SourceControl.WebApi;
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

    public Task<Person> GetSignedInUserAsync(CancellationToken cancellationToken) => Task.FromResult(Fabricated.Author);

    public Task<IReadOnlyList<Person>> FindAsync(string value, CancellationToken cancellationToken)
    {
        Searches.Add(value);
        return Task.FromResult(Matches.GetValueOrDefault(value) ?? []);
    }
}

internal sealed class FakePullRequestCommentClient : IPullRequestCommentClient
{
    public Exception? PullRequestFailure { get; set; }

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

    public Task<IReadOnlyList<GitPullRequestCommentThread>> GetThreadsAsync(PullRequestLocation location, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<GitPullRequestCommentThread>>(Threads);

    public Task<IReadOnlyList<GitPullRequestIteration>> GetIterationsAsync(PullRequestLocation location, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<GitPullRequestIteration>>(Iterations);

    public Task<IReadOnlyList<GitPullRequestChange>> GetChangesAsync(PullRequestLocation location, int iterationId, CancellationToken cancellationToken)
    {
        ChangeRequests.Add(iterationId);
        return Task.FromResult<IReadOnlyList<GitPullRequestChange>>(Changes);
    }

    public Task<string> GetFileTextAsync(PullRequestLocation location, string path, string commitId, CancellationToken cancellationToken) =>
        Task.FromResult(Files[(commitId, path)]);

    public Task<GitPullRequestCommentThread> CreateThreadAsync(PullRequestLocation location, GitPullRequestCommentThread thread, CancellationToken cancellationToken)
    {
        CreatedThreads.Add(thread);
        var created = new GitPullRequestCommentThread
        {
            Id = 900,
            Comments = thread.Comments.Select(comment => Fabricated.CommentBy(1, comment.Content)).ToList()
        };
        return Task.FromResult(created);
    }

    public Task<Comment> CreateCommentAsync(PullRequestLocation location, int threadId, Comment comment, CancellationToken cancellationToken)
    {
        CreatedComments.Add((threadId, comment));
        return Task.FromResult(Fabricated.CommentBy(5, comment.Content, parentId: comment.ParentCommentId));
    }
}

internal sealed class FakeWorkItemCommentClient : IWorkItemCommentClient
{
    public string? Project { get; set; } = Fabricated.Project;

    public List<WorkItemComment> Comments { get; } = [];

    public List<(string Project, int WorkItemId, string Markdown)> Added { get; } = [];

    /// <summary>Simulates a service that rewrites the stored text, for example by dropping HTML comments.</summary>
    public Func<string, string> Store { get; set; } = text => text;

    public Task<string?> GetProjectAsync(int workItemId, CancellationToken cancellationToken) => Task.FromResult(Project);

    public Task<IReadOnlyList<WorkItemComment>> GetCommentsAsync(string project, int workItemId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<WorkItemComment>>(Comments);

    public Task<WorkItemComment> AddCommentAsync(string project, int workItemId, string markdown, CancellationToken cancellationToken)
    {
        Added.Add((project, workItemId, markdown));
        return Task.FromResult(new WorkItemComment(31, Store(markdown), false, Fabricated.Author.Id));
    }
}

internal sealed class FakeWriteSession : IAdoWriteSession
{
    public FakePullRequestCommentClient FakePullRequests { get; } = new();

    public FakeWorkItemCommentClient FakeWorkItems { get; } = new();

    public FakePeopleDirectory FakePeople { get; } = new();

    public IPullRequestCommentClient PullRequests => FakePullRequests;

    public IWorkItemCommentClient WorkItems => FakeWorkItems;

    public IPeopleDirectory People => FakePeople;

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

    public IAdoWriteSession Create(Uri organizationUrl, string pat)
    {
        Pat = pat;
        return session;
    }
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

internal sealed class ScriptedTerminalInput(string answer) : ITerminalInput
{
    public List<string> Prompts { get; } = [];

    public bool IsInputRedirected => false;

    public string ReadMaskedLine(string prompt)
    {
        Prompts.Add(prompt);
        return answer;
    }

    public string ReadLine(string prompt) => throw new InvalidOperationException($"Unexpected prompt: {prompt}");
}
