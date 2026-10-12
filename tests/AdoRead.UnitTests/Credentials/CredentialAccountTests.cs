using AdoRead.Credentials;

namespace AdoRead.UnitTests.Credentials;

public sealed class CredentialAccountTests
{
    [Theory]
    [InlineData("https://dev.azure.com/Fabricated/", "https://dev.azure.com/fabricated")]
    [InlineData("https://dev.azure.com/fabricated", "https://dev.azure.com/fabricated")]
    public void For_NormalizesOrganizationUrl(string organization, string expected)
    {
        Assert.Equal(expected, CredentialAccount.For(new Uri(organization)));
    }
}
