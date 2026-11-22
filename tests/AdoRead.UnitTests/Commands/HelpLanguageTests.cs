using System.Globalization;
using AdoRead.Configuration;
using AdoRead.Output;

namespace AdoRead.UnitTests.Commands;

// Help goes to Console.Out, which these tests replace, so they never run beside other tests.
[CollectionDefinition(nameof(HelpLanguageTests), DisableParallelization = true)]
public sealed class HelpLanguageCollection;

[Collection(nameof(HelpLanguageTests))]
public sealed class HelpLanguageTests
{
    [Fact]
    public async Task Help_IsInEnglishOnAPolishMachine()
    {
        CultureInfo originalUiCulture = CultureInfo.CurrentUICulture;
        TextWriter originalOut = Console.Out;
        var help = new StringWriter();
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("pl-PL");
            Console.SetOut(help);

            int exitCode = await TestTool.Umbrella(
                new ConfigurationStore(string.Empty),
                new JsonOutput(TextWriter.Null),
                TimeProvider.System).InvokeAsync(["--help"], CancellationToken.None);

            Assert.Equal(0, exitCode);
        }
        finally
        {
            Console.SetOut(originalOut);
            CultureInfo.CurrentUICulture = originalUiCulture;
        }

        string[] lines = help.ToString().Split('\n').Select(line => line.TrimEnd('\r')).ToArray();
        Assert.Contains("Usage:", lines);
        Assert.Contains("Options:", lines);
        Assert.Contains("Commands:", lines);
        Assert.EndsWith("Show version information", Assert.Single(lines, line => line.TrimStart().StartsWith("--version", StringComparison.Ordinal)));
    }
}
