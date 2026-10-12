using System.CommandLine;
using System.Text.Json;
using AdoRead.Commands;
using AdoRead.Configuration;
using AdoRead.Output;

namespace AdoRead.UnitTests.Commands;

public sealed class CommandApplicationTests
{
    [Fact]
    public async Task InvokeAsync_ReportsUnavailableUserProfileAsStructuredJson()
    {
        var writer = new StringWriter();
        CommandApplication application = new CommandFactory(
            new ConfigurationStore(string.Empty),
            new JsonOutput(writer),
            TimeProvider.System).Create();

        int exitCode = await application.InvokeAsync(["config", "show", "--compact"], CancellationToken.None);

        Assert.Equal(ExitCodes.InvalidInputOrConfiguration, exitCode);
        using JsonDocument document = JsonDocument.Parse(writer.ToString());
        Assert.Equal("configuration", document.RootElement.GetProperty("kind").GetString());
        Assert.Equal("userProfileUnavailable", document.RootElement.GetProperty("errors")[0].GetProperty("code").GetString());
        Assert.Single(writer.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries));
    }

    [Fact]
    public async Task InvokeAsync_PropagatesCancellationAndUsesRegisteredKind()
    {
        var root = new RootCommand();
        var workItem = new Command("work-item");
        var compact = new Option<bool>("--compact") { Recursive = true };
        root.Options.Add(compact);
        workItem.SetAction(async (_, cancellationToken) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return ExitCodes.Success;
        });
        root.Subcommands.Add(workItem);
        var writer = new StringWriter();
        var application = new CommandApplication(
            root,
            compact,
            new Dictionary<Command, string> { [workItem] = "workItemSearch" },
            new JsonOutput(writer),
            TimeProvider.System);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        int exitCode = await application.InvokeAsync(["work-item", "--compact=true"], cancellation.Token);

        Assert.Equal(ExitCodes.Cancelled, exitCode);
        using JsonDocument document = JsonDocument.Parse(writer.ToString());
        Assert.Equal("workItemSearch", document.RootElement.GetProperty("kind").GetString());
        Assert.Equal("cancelled", document.RootElement.GetProperty("errors")[0].GetProperty("code").GetString());
        Assert.Single(writer.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries));
    }

    [Theory]
    [InlineData("separate")]
    [InlineData("equals")]
    [InlineData("colon")]
    public async Task InvokeAsync_ApplicationCancellationForParsedImageWorkItemOptionsUsesSchema21(string form)
    {
        var root = new RootCommand();
        var workItem = new Command("work-item");
        var compact = new Option<bool>("--compact") { Recursive = true };
        root.Options.Add(compact);
        var downloadImages = new Option<string?>("--download-images");
        workItem.Options.Add(downloadImages);
        workItem.SetAction(async (_, cancellationToken) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return ExitCodes.Success;
        });
        root.Subcommands.Add(workItem);
        var writer = new StringWriter();
        var application = new CommandApplication(
            root,
            compact,
            new Dictionary<Command, string> { [workItem] = "workItemSearch" },
            new JsonOutput(writer),
            TimeProvider.System);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        string mediaArgument = form switch
        {
            "separate" => "--download-images",
            "equals" => "--download-images=C:\\images",
            "colon" => "--download-images:C:\\images",
            _ => throw new ArgumentOutOfRangeException(nameof(form))
        };
        string[] arguments = form == "separate"
            ? ["work-item", mediaArgument, "C:\\images", "--compact=true"]
            : ["work-item", mediaArgument, "--compact=true"];

        int exitCode = await application.InvokeAsync(arguments, cancellation.Token);

        Assert.Equal(ExitCodes.Cancelled, exitCode);
        using JsonDocument document = JsonDocument.Parse(writer.ToString());
        Assert.Equal("2.1", document.RootElement.GetProperty("schemaVersion").GetString());
        Assert.Equal("cancelled", document.RootElement.GetProperty("errors")[0].GetProperty("code").GetString());
    }

    [Fact]
    public async Task InvokeAsync_UsesRegisteredKindForUnexpectedFailures()
    {
        var root = new RootCommand();
        var pullRequest = new Command("pr");
        var compact = new Option<bool>("--compact") { Recursive = true };
        root.Options.Add(compact);
        Func<ParseResult, int> fail = _ => throw new InvalidOperationException("secret source message");
        pullRequest.SetAction(fail);
        root.Subcommands.Add(pullRequest);
        var writer = new StringWriter();
        var application = new CommandApplication(
            root,
            compact,
            new Dictionary<Command, string> { [pullRequest] = "pullRequestSearch" },
            new JsonOutput(writer),
            TimeProvider.System);

        int exitCode = await application.InvokeAsync(["pr", "--compact=true"], CancellationToken.None);

        Assert.Equal(ExitCodes.Error, exitCode);
        using JsonDocument document = JsonDocument.Parse(writer.ToString());
        Assert.Equal("pullRequestSearch", document.RootElement.GetProperty("kind").GetString());
        Assert.DoesNotContain("secret source message", writer.ToString(), StringComparison.Ordinal);
        Assert.Single(writer.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries));
    }

    [Fact]
    public async Task InvokeAsync_ApplicationFailureForImageWorkItemUsesSchema21()
    {
        var root = new RootCommand();
        var workItem = new Command("work-item");
        var compact = new Option<bool>("--compact") { Recursive = true };
        root.Options.Add(compact);
        workItem.Options.Add(new Option<string?>("--download-images"));
        Func<ParseResult, int> fail = _ => throw new InvalidOperationException("secret source message");
        workItem.SetAction(fail);
        root.Subcommands.Add(workItem);
        var writer = new StringWriter();
        var application = new CommandApplication(
            root,
            compact,
            new Dictionary<Command, string> { [workItem] = "workItemSearch" },
            new JsonOutput(writer),
            TimeProvider.System);

        int exitCode = await application.InvokeAsync(
            ["work-item", "--download-images", "C:\\images", "--compact=true"],
            CancellationToken.None);

        Assert.Equal(ExitCodes.Error, exitCode);
        using JsonDocument document = JsonDocument.Parse(writer.ToString());
        Assert.Equal("2.1", document.RootElement.GetProperty("schemaVersion").GetString());
        Assert.DoesNotContain("secret source message", writer.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("separate", "2.1", 5, "unexpectedError")]
    [InlineData("equals", "2.1", 5, "unexpectedError")]
    [InlineData("colon", "2.1", 5, "unexpectedError")]
    [InlineData("missing-id", "2.1", 2, "invalidArguments")]
    [InlineData("unrelated-typo", "2.1", 2, "invalidArguments")]
    [InlineData("mis-cased", "2.0", 2, "invalidArguments")]
    public async Task InvokeAsync_ApplicationErrorUsesParserMatchingMediaOptionDetection(
        string form,
        string expectedSchema,
        int expectedExitCode,
        string expectedErrorCode)
    {
        var root = new RootCommand();
        var workItem = new Command("work-item");
        var compact = new Option<bool>("--compact") { Recursive = true };
        root.Options.Add(compact);
        workItem.Options.Add(new Option<string?>("--download-images"));
        workItem.Arguments.Add(new Argument<string>("id"));
        workItem.SetAction((Func<ParseResult, int>)(_ => throw new InvalidOperationException("secret source message")));
        root.Subcommands.Add(workItem);
        var writer = new StringWriter();
        var application = new CommandApplication(
            root,
            compact,
            new Dictionary<Command, string> { [workItem] = "workItemSearch" },
            new JsonOutput(writer),
            TimeProvider.System);
        string[] arguments = form switch
        {
            "separate" => ["work-item", "42", "--download-images", "C:\\images", "--compact=true"],
            "equals" => ["work-item", "42", "--download-images=C:\\images", "--compact=true"],
            "colon" => ["work-item", "42", "--download-images:C:\\images", "--compact=true"],
            "missing-id" => ["work-item", "--download-images", "C:\\images", "--compact=true"],
            "unrelated-typo" => ["work-item", "42", "--download-images", "C:\\images", "--compac"],
            "mis-cased" => ["work-item", "42", "--DOWNLOAD-IMAGES=C:\\images", "--compact=true"],
            _ => throw new ArgumentOutOfRangeException(nameof(form))
        };

        int exitCode = await application.InvokeAsync(arguments, CancellationToken.None);

        Assert.Equal(expectedExitCode, exitCode);
        using JsonDocument document = JsonDocument.Parse(writer.ToString());
        Assert.Equal(expectedSchema, document.RootElement.GetProperty("schemaVersion").GetString());
        Assert.Equal(expectedErrorCode, document.RootElement.GetProperty("errors")[0].GetProperty("code").GetString());
    }
}
