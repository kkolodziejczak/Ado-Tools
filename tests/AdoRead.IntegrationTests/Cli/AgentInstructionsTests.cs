using System.Diagnostics;
using System.Text.RegularExpressions;

namespace AdoRead.IntegrationTests.Cli;

/// <summary>
/// The agent skill and the paste-in instructions name commands, options and JSON fields. Every
/// command must be a real command path, every option must be in that command's <c>--help</c>, and every
/// field must exist in the source, so the instructions fail CI instead of going stale. Only
/// <c>--help</c> is run: no configuration, vault or network.
/// </summary>
public sealed partial class AgentInstructionsTests
{
    private static readonly string[] Files =
    [
        "skills/ado-tools/SKILL.md",
        "docs/agent-instructions.md",
        "docs/agent-integration.md",
        "docs/agent-test-prompts.md",
    ];

    private static readonly Dictionary<string, string> Assemblies = new(StringComparer.Ordinal)
    {
        ["ado-read"] = "AdoRead.Cli",
        ["ado-read-pr"] = "AdoRead.PullRequests.Cli",
        ["ado-read-work-item"] = "AdoRead.WorkItems.Cli",
        ["ado-read-pipeline"] = "AdoRead.Pipelines.Cli",
        ["ado-read-sprint"] = "AdoRead.Sprints.Cli",
        ["ado-read-wiki"] = "AdoRead.Wiki.Cli",
        ["ado-write"] = "AdoWrite.Cli",
        ["ado-write-pr"] = "AdoWrite.PullRequests.Cli",
        ["ado-write-work-item"] = "AdoWrite.WorkItems.Cli",
    };

    private static readonly HashSet<string> FileExtensions = new(["com", "md", "json", "ps1", "cs", "yml"], StringComparer.Ordinal);

    private readonly Dictionary<string, Help> helpCache = new(StringComparer.Ordinal);

    [Fact]
    public async Task AgentInstructions_NameOnlyCommandsOptionsAndFieldsThatExist()
    {
        string root = FindToolRoot();
        string source = ReadSource(Path.Combine(root, "src"));
        var problems = new List<string>();
        int commandCount = 0;

        foreach (string file in Files)
        {
            string text = await File.ReadAllTextAsync(Path.Combine(root, file));
            foreach ((int line, string segment) in CodeSegments(text))
            {
                MatchCollection tools = ToolName().Matches(segment);
                if (tools.Count == 0)
                {
                    problems.AddRange(CheckField(segment.Trim(), source).Select(problem => $"{file}:{line}: {problem}"));
                    continue;
                }

                for (int index = 0; index < tools.Count; index++)
                {
                    Match tool = tools[index];
                    int end = index + 1 < tools.Count ? tools[index + 1].Index : segment.Length;
                    string rest = CommandEnd().Split(segment[(tool.Index + tool.Length)..end])[0];
                    List<string> tokens = [.. Token().Matches(rest).Select(match => match.Value)];
                    commandCount++;
                    foreach (string problem in await CheckCommandAsync(tool.Value, tokens))
                    {
                        problems.Add($"{file}:{line}: {problem}");
                    }
                }
            }
        }

        Assert.True(commandCount > 50, $"Only {commandCount} commands were found; the parser no longer reads the instructions.");
        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    // Lines of fenced code blocks, and the code spans of every other line.
    private static IEnumerable<(int Line, string Segment)> CodeSegments(string text)
    {
        bool inBlock = false;
        string[] lines = text.Split('\n');
        for (int index = 0; index < lines.Length; index++)
        {
            string line = lines[index].TrimEnd('\r');
            if (line.TrimStart().StartsWith("```", StringComparison.Ordinal))
            {
                inBlock = !inBlock;
                continue;
            }

            if (inBlock)
            {
                yield return (index + 1, line);
                continue;
            }

            foreach (Match span in CodeSpan().Matches(line))
            {
                yield return (index + 1, span.Groups[1].Value);
            }
        }
    }

    // Tokens after the tool name: subcommands first, then arguments and options. `show|list` stands
    // for each of its alternatives.
    private async Task<List<string>> CheckCommandAsync(string tool, List<string> tokens)
    {
        var path = new List<string>();
        int index = 0;
        while (index < tokens.Count)
        {
            Help help = await GetHelpAsync(tool, path);
            string token = tokens[index];
            string[] alternatives = token.Split('|');
            if (token[0] is '-' or '<' or '"' or '\'' or '[' || !alternatives.All(help.Commands.Contains))
            {
                break;
            }

            if (alternatives.Length > 1)
            {
                var problems = new List<string>();
                foreach (string alternative in alternatives)
                {
                    problems.AddRange(await CheckCommandAsync(tool, [.. path, alternative, .. tokens[(index + 1)..]]));
                }

                return problems;
            }

            path.Add(token);
            index++;
        }

        Help command = await GetHelpAsync(tool, path);
        string name = string.Join(' ', [tool, .. path]);
        List<string> remaining = tokens[index..];
        var found = new List<string>();
        if (command.Commands.Count > 0 && remaining.Count > 0 && SubcommandLike().IsMatch(remaining[0]))
        {
            found.Add($"unknown subcommand '{remaining[0]}' under '{name}'");
        }

        foreach (string token in remaining.Where(token => token[0] is not ('"' or '\'')))
        {
            found.AddRange(OptionName().Matches(token)
                .Select(option => option.Value)
                .Where(option => !command.Options.Contains(option))
                .Select(option => $"unknown option {option} for '{name}'"));
        }

        return found;
    }

    // A code span such as `results[0].runs[].failedSteps` or `scanLimitReached`: every part must be a
    // property (PascalCase in C#) or a string literal in src.
    private static IEnumerable<string> CheckField(string span, string source)
    {
        span = Index().Replace(span, "[]");
        if (FileExtensions.Contains(span[(span.LastIndexOf('.') + 1)..]) || !FieldLike().IsMatch(span) || !span.Any(c => c == '.' || char.IsUpper(c)))
        {
            yield break;
        }

        foreach (string part in span.Replace("[]", ".", StringComparison.Ordinal).Split('.', StringSplitOptions.RemoveEmptyEntries))
        {
            string pascal = char.ToUpperInvariant(part[0]) + part[1..];
            if (!Regex.IsMatch(source, $"\\b{pascal}\\b|\"{part}\""))
            {
                yield return $"'{part}' (in `{span}`) is not a property or string literal in src";
            }
        }
    }

    private async Task<Help> GetHelpAsync(string tool, List<string> path)
    {
        string key = string.Join(' ', [tool, .. path]);
        if (helpCache.TryGetValue(key, out Help? cached))
        {
            return cached;
        }

        var startInfo = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        startInfo.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, $"{Assemblies[tool]}.dll"));
        foreach (string part in path)
        {
            startInfo.ArgumentList.Add(part);
        }

        startInfo.ArgumentList.Add("--help");
        using Process process = Process.Start(startInfo) ?? throw new InvalidOperationException($"Could not start {tool}.");
        Task<string> standardOutput = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
        Task<string> standardError = process.StandardError.ReadToEndAsync(CancellationToken.None);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await process.WaitForExitAsync(timeout.Token);
        await standardError;

        var help = new Help(new HashSet<string>(StringComparer.Ordinal), new HashSet<string>(StringComparer.Ordinal));
        string? section = null;
        foreach (string line in (await standardOutput).Split('\n').Select(line => line.TrimEnd('\r')))
        {
            if (line.EndsWith(':') && !line.StartsWith(' '))
            {
                section = line.TrimEnd(':');
                continue;
            }

            string entry = line.Trim();
            if (entry.Length == 0)
            {
                continue;
            }

            if (section == "Commands")
            {
                help.Commands.Add(entry.Split(' ')[0]);
            }
            else if (section == "Options")
            {
                help.Options.UnionWith(HelpOption().Matches(Columns().Split(entry)[0]).Select(match => match.Value));
            }
        }

        helpCache[key] = help;
        return help;
    }

    private static string ReadSource(string sourceRoot)
    {
        char separator = Path.DirectorySeparatorChar;
        return string.Join('\n', Directory.EnumerateFiles(sourceRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{separator}obj{separator}", StringComparison.Ordinal))
            .Select(File.ReadAllText));
    }

    private static string FindToolRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "AdoRead.slnx")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate AdoRead.slnx from the test output directory.");
    }

    private sealed record Help(HashSet<string> Commands, HashSet<string> Options);

    [GeneratedRegex(@"(?<![\w.-])ado-(?:read|write)(?:-(?:pr|work-item|pipeline|sprint|wiki))?(?![\w-])")]
    private static partial Regex ToolName();

    // Where the command ends inside prose: a comment, an arrow, "then", "and", "or", ";" or "(".
    [GeneratedRegex(@"\s(?:#|→|->|then\b|and\b|or\b)|[;(]")]
    private static partial Regex CommandEnd();

    [GeneratedRegex(@"""[^""]*""|'[^']*'|<[^>]*>|\S+")]
    private static partial Regex Token();

    [GeneratedRegex("`([^`]+)`")]
    private static partial Regex CodeSpan();

    [GeneratedRegex(@"^[a-z][a-z-]*(\|[a-z-]+)*$")]
    private static partial Regex SubcommandLike();

    [GeneratedRegex(@"(?<![\w-])--[a-z][a-z-]*")]
    private static partial Regex OptionName();

    [GeneratedRegex("--[a-z][a-z-]*")]
    private static partial Regex HelpOption();

    [GeneratedRegex(@"\s{2,}")]
    private static partial Regex Columns();

    [GeneratedRegex(@"\[\d+\]")]
    private static partial Regex Index();

    [GeneratedRegex(@"^[a-z][A-Za-z0-9]*(\[\])?(\.[a-z][A-Za-z0-9]*(\[\])?)*$")]
    private static partial Regex FieldLike();
}
