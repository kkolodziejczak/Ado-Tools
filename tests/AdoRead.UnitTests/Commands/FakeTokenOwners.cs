using AdoRead.AzureDevOps;

namespace AdoRead.UnitTests.Commands;

/// <summary>Answers who a token belongs to without Azure DevOps, or fails with <see cref="Failure"/>.</summary>
internal sealed class FakeTokenOwners : ITokenOwnerLookup
{
    public const string DisplayName = "Fabricated Owner";

    public TokenOwnerException? Failure { get; set; }

    public List<string> CheckedTokens { get; } = [];

    public Task<string> FindDisplayNameAsync(Uri organizationUrl, string pat, CancellationToken cancellationToken)
    {
        CheckedTokens.Add(pat);
        return Failure is null ? Task.FromResult(DisplayName) : Task.FromException<string>(Failure);
    }
}
