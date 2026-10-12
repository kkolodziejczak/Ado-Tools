using AdoRead.Text;

namespace AdoRead.UnitTests.Text;

public sealed class SecretMaskerTests
{
    private const string Sha = "3f2a9c1d8e7b6a5f4e3d2c1b0a9f8e7d6c5b4a39";

    [Theory]
    [InlineData("Server=db;password=hunter2;User=x", "Server=db;password=***;User=x")]
    [InlineData("pwd=hunter2 next", "pwd=*** next")]
    [InlineData("Secret=abc123&other=1", "Secret=***&other=1")]
    [InlineData("client_secret=abc123xyz", "client_secret=***")]
    [InlineData("token=abc123xyz", "token=***")]
    [InlineData("GITHUB_TOKEN=abc123xyz", "GITHUB_TOKEN=***")]
    [InlineData("apikey=abc123xyz", "apikey=***")]
    [InlineData("api_key=abc123xyz", "api_key=***")]
    [InlineData("AccountKey=abc123xyz==;Endpoint=x", "AccountKey=***;Endpoint=x")]
    [InlineData("SharedAccessKey=abc123xyz==", "SharedAccessKey=***")]
    [InlineData("password=\"hunter2\"", "password=\"***\"")]
    public void Mask_KeyValueSecret_ReplacesOnlyTheValue(string input, string expected)
    {
        var masker = new SecretMasker();

        Assert.Equal(expected, masker.Mask(input));
        Assert.Equal(1, masker.MaskedCount);
    }

    [Theory]
    [InlineData("Authorization: Bearer abc.def-123_xyz", "Authorization: Bearer ***")]
    [InlineData("authorization: Basic dXNlcjpwYXNzd29yZA==", "authorization: Basic ***")]
    public void Mask_AuthorizationHeader_ReplacesTheCredential(string input, string expected)
    {
        var masker = new SecretMasker();

        Assert.Equal(expected, masker.Mask(input));
        Assert.Equal(1, masker.MaskedCount);
    }

    [Fact]
    public void Mask_BareBearerToken_ReplacesTheToken()
    {
        var masker = new SecretMasker();

        Assert.Equal("send Bearer ***", masker.Mask("send Bearer abcdefghijklmnop1234"));
        Assert.Equal(1, masker.MaskedCount);
    }

    [Fact]
    public void Mask_JsonWebToken_ReplacesTheWholeToken()
    {
        var masker = new SecretMasker();

        Assert.Equal(
            "token ***",
            masker.Mask("token eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiJmYWJyaWNhdGVkIn0.ZmFrZS1zaWduYXR1cmU"));
        Assert.Equal(1, masker.MaskedCount);
    }

    [Fact]
    public void Mask_Base32PersonalAccessToken_ReplacesTheWord()
    {
        string pat = "abcdefghijklmnopqrstuvwxyz234567abcdefghijklmnopqrstuvwx"[..52];
        var masker = new SecretMasker();

        Assert.Equal($"pat ***", masker.Mask($"pat {pat}"));
        Assert.Equal(1, masker.MaskedCount);
    }

    [Fact]
    public void Mask_NewFormatPersonalAccessToken_ReplacesTheWord()
    {
        string pat = string.Concat(Enumerable.Repeat("Ab3", 28));
        var masker = new SecretMasker();

        Assert.Equal("pat ***", masker.Mask($"pat {pat}"));
        Assert.Equal(1, masker.MaskedCount);
    }

    [Fact]
    public void Mask_CredentialsInUrl_ReplacesUserAndPassword()
    {
        var masker = new SecretMasker();

        Assert.Equal(
            "clone https://***@example.invalid/repo.git",
            masker.Mask("clone https://user:pass123@example.invalid/repo.git"));
        Assert.Equal(1, masker.MaskedCount);
    }

    [Fact]
    public void Mask_PrivateKeyBlock_ReplacesTheWholeBlock()
    {
        var masker = new SecretMasker();

        Assert.Equal(
            "key: ***\nnext line",
            masker.Mask("key: -----BEGIN RSA PRIVATE KEY-----\nMIIBOgIBAAJBAKj\n-----END RSA PRIVATE KEY-----\nnext line"));
        Assert.Equal(1, masker.MaskedCount);
    }

    [Fact]
    public void Mask_AwsAccessKey_ReplacesTheKey()
    {
        var masker = new SecretMasker();

        Assert.Equal("key ***", masker.Mask("key AKIAABCDEFGHIJKLMNOP"));
        Assert.Equal(1, masker.MaskedCount);
    }

    [Theory]
    [InlineData("Expected 5 but was 3")]
    [InlineData("Assert.Equal() Failure: Expected 'token' but was ''")]
    [InlineData("the Bearer token is missing")]
    [InlineData("commit 3f2a9c1d8e7b6a5f4e3d2c1b0a9f8e7d6c5b4a39 failed")]
    public void Mask_NormalText_IsUnchanged(string input)
    {
        var masker = new SecretMasker();

        Assert.Equal(input, masker.Mask(input));
        Assert.Equal(0, masker.MaskedCount);
    }

    [Fact]
    public void Mask_GitShaInText_IsUnchanged()
    {
        var masker = new SecretMasker();

        Assert.Equal($"merged {Sha}", masker.Mask($"merged {Sha}"));
        Assert.Equal(0, masker.MaskedCount);
    }

    [Fact]
    public void Mask_Null_ReturnsNull()
    {
        var masker = new SecretMasker();

        Assert.Null(masker.Mask(null));
        Assert.Equal(0, masker.MaskedCount);
    }

    [Fact]
    public void Mask_CountsEveryReplacementAcrossCalls()
    {
        var masker = new SecretMasker();

        masker.Mask("password=one");
        masker.Mask("token=two and apikey=three");

        Assert.Equal(3, masker.MaskedCount);
    }
}
