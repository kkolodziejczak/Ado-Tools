using System.CommandLine;
using System.CommandLine.Parsing;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;

namespace AdoWrite.Approval;

/// <summary>
/// The writes waiting for approval, one folder each under <see cref="Directory"/>: <c>request.json</c>
/// plus copies of the files it attaches. A request appears only once its file is complete, and is
/// removed once it is posted or rejected.
/// </summary>
public sealed class ApprovalOutbox(string directory, TimeProvider timeProvider)
{
    private const string RequestFile = "request.json";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public string Directory { get; } = directory;

    public static ApprovalOutbox ForCurrentUser() => new(
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".ado-read", "outbox"),
        TimeProvider.System);

    /// <summary>Queues the parsed write command, with <paramref name="body"/> as its text when it has one.</summary>
    internal ApprovalRequest Enqueue(string kind, ParseResult parseResult, string? body)
    {
        DateTimeOffset now = timeProvider.GetUtcNow();
        string id = now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + "-" + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(3));
        string folder = Path.Combine(Directory, id);
        System.IO.Directory.CreateDirectory(folder);
        int attachments = 0;
        IReadOnlyList<string> arguments = Freeze(parseResult, body, path =>
        {
            string copy = Path.Combine(folder, "attachments", (attachments++).ToString(CultureInfo.InvariantCulture), Path.GetFileName(path));
            System.IO.Directory.CreateDirectory(Path.GetDirectoryName(copy)!);
            File.Copy(path, copy);
            return copy;
        });
        var request = new ApprovalRequest(id, kind, arguments, now);
        string temporary = Path.Combine(folder, RequestFile + ".tmp");
        File.WriteAllText(temporary, JsonSerializer.Serialize(request, JsonOptions));
        File.Move(temporary, Path.Combine(folder, RequestFile));
        return request;
    }

    /// <summary>
    /// The leaf command's arguments in the <c>--name=value</c> form, which a value starting with a dash
    /// cannot break. The text replaces <c>--body</c> and <c>--body-file</c>, attachments point to copies,
    /// and <c>--config</c> becomes a full path, so nothing outside the outbox changes what is posted.
    /// </summary>
    internal static IReadOnlyList<string> Freeze(ParseResult parseResult, string? body, Func<string, string> storeAttachment)
    {
        var positional = new List<string>();
        var options = new List<string>();
        foreach (SymbolResult child in parseResult.CommandResult.Children)
        {
            switch (child)
            {
                case ArgumentResult argument:
                    positional.AddRange(argument.Tokens.Select(token => token.Value));
                    break;
                // Root options such as --compact only shape this command's output.
                case OptionResult option when !option.Implicit && !option.Option.Recursive:
                    options.AddRange(FreezeOption(option.Option, option.Tokens.Select(token => token.Value).ToArray(), storeAttachment));
                    break;
            }
        }

        if (body is not null)
        {
            options.Add("--body=" + body);
        }

        return [.. positional, .. options];
    }

    private static IEnumerable<string> FreezeOption(Option option, string[] values, Func<string, string> storeAttachment) => option.Name switch
    {
        "--body" or "--body-file" or "--dry-run" => [],
        "--attach" => values.Select(path => "--attach=" + storeAttachment(Path.GetFullPath(path))),
        "--config" => values.Select(path => "--config=" + Path.GetFullPath(path)),
        _ when values.Length == 0 => [option.Name],
        _ => values.Select(value => option.Name + "=" + value)
    };

    /// <summary>Every complete request, oldest first. A folder that is unreadable or names an unknown kind is skipped.</summary>
    public IReadOnlyList<ApprovalRequest> List()
    {
        if (!System.IO.Directory.Exists(Directory))
        {
            return [];
        }

        var requests = new List<ApprovalRequest>();
        foreach (string folder in System.IO.Directory.EnumerateDirectories(Directory))
        {
            if (Read(Path.Combine(folder, RequestFile)) is { } request
                && request.Id == Path.GetFileName(folder)
                && ApprovalRequest.IsKnownKind(request.Kind))
            {
                requests.Add(request);
            }
        }

        return [.. requests.OrderBy(request => request.QueuedAtUtc).ThenBy(request => request.Id, StringComparer.Ordinal)];
    }

    private static ApprovalRequest? Read(string path)
    {
        try
        {
            return File.Exists(path) ? JsonSerializer.Deserialize<ApprovalRequest>(File.ReadAllText(path), JsonOptions) : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    /// <summary>Removes a posted or rejected request with its copies. Removing one that is gone is fine.</summary>
    public void Remove(string id)
    {
        string folder = Path.Combine(Directory, id);
        if (Path.GetFileName(folder) == id && System.IO.Directory.Exists(folder))
        {
            System.IO.Directory.Delete(folder, recursive: true);
        }
    }
}
