using AdoRead.Configuration;

namespace AdoRead.UnitTests.Configuration;

public sealed class PatProviderTests
{
    [Fact]
    public void GetRequiredPat_ReturnsEnvironmentValue()
    {
        var provider = new PatProvider(name => name == PatProvider.EnvironmentVariableName ? "pat-value" : null);

        Assert.Equal("pat-value", provider.GetRequiredPat());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void GetRequiredPat_ReportsSafeErrorWhenMissing(string? value)
    {
        var provider = new PatProvider(_ => value);

        CredentialException exception = Assert.Throws<CredentialException>(() => provider.GetRequiredPat());

        Assert.Equal("missingPat", exception.Code);
        Assert.DoesNotContain("pat-value", exception.Message, StringComparison.Ordinal);
    }
}
