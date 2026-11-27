using System.CommandLine;
using System.CommandLine.Parsing;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using AdoWrite.Comments;

namespace AdoWrite.Approval;

/// <summary>
/// Writes waiting for their approval window, one folder each under <see cref="Directory"/>:
/// <c>request.json</c> plus copies of the files it attaches. Exactly one window may claim a write. The
/// command that saved it withdraws it when no window claims it in time, and the window deletes it when
/// it closes, so a write is shown in one window at most and never offered again.
/// </summary>
public sealed partial class PendingWrites(string directory, TimeProvider timeProvider)
{
    private const string RequestFile = "request.json";

    /// <summary>Created once, by whichever comes first: the window (it shows the write) or the command (it withdraws it).</summary>
    private const string ClaimFile = "claim";

    private const string ClaimedByWindow = "window";
    private const string Withdrawn = "withdrawn";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public string Directory { get; } = directory;

    public static PendingWrites ForCurrentUser() => new(
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".ado-read", "approvals"),
        TimeProvider.System);

    /// <summary>An ID this class made: only these name a folder, so no ID reaches outside <see cref="Directory"/>.</summary>
    public static bool IsValidId(string id) => IdPattern().IsMatch(id);

    [GeneratedRegex("^[0-9]{8}-[0-9]{6}-[0-9a-f]{8}$")]
    private static partial Regex IdPattern();

    /// <summary>Saves the parsed write command for <paramref name="organization"/>, with <paramref name="body"/> as its text when it has one.</summary>
    internal ApprovalRequest Save(string kind, ParseResult parseResult, string? body, Uri organization)
    {
        DateTimeOffset now = timeProvider.GetUtcNow();
        string id = now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + "-" + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(4));
        string folder = Folder(id);
        CreatePrivateDirectory(Directory);
        DeleteAbandoned(now);
        CreatePrivateDirectory(folder);
        try
        {
            int attachments = 0;
            IReadOnlyList<string> arguments = Freeze(parseResult, body, path =>
            {
                // The draft checked the file's size; one that grew since is not copied.
                if (new FileInfo(path).Length > CommentAttachment.MaxBytes)
                {
                    throw new IOException($"The --attach file '{Path.GetFileName(path)}' grew past {CommentAttachment.MaxBytes / (1024 * 1024)} MB after it was checked.");
                }

                string copy = Path.Combine(folder, "attachments", (attachments++).ToString(CultureInfo.InvariantCulture), Path.GetFileName(path));
                System.IO.Directory.CreateDirectory(Path.GetDirectoryName(copy)!);
                File.Copy(path, copy);
                return copy;
            });
            var request = new ApprovalRequest(id, kind, arguments, organization, now);
            File.WriteAllText(Path.Combine(folder, RequestFile), JsonSerializer.Serialize(request, JsonOptions));
            return request;
        }
        catch
        {
            Delete(id);
            throw;
        }
    }

    /// <summary>
    /// The window takes the write. Null when the command already withdrew it, another window took it, or
    /// it is gone; a write taken but unreadable is deleted, since nothing could ever show it.
    /// </summary>
    public ApprovalRequest? Claim(string id)
    {
        if (!IsValidId(id) || !TryCreateClaim(id, ClaimedByWindow))
        {
            return null;
        }

        ApprovalRequest? request = Read(id);
        if (request is null)
        {
            Delete(id);
        }

        return request;
    }

    /// <summary>Whether a window has taken the write.</summary>
    public bool IsClaimed(string id) => File.Exists(Path.Combine(Folder(id), ClaimFile));

    /// <summary>
    /// Takes the write back because no window claimed it, and deletes it. False when a window claimed it
    /// first: then it stays, and the window decides.
    /// </summary>
    public bool Withdraw(string id)
    {
        if (!TryCreateClaim(id, Withdrawn))
        {
            return false;
        }

        try
        {
            Delete(id);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // The claim already says withdrawn, so no window can take what is left.
        }

        return true;
    }

    /// <summary>Deletes the write with its copies. Deleting one that is gone is fine.</summary>
    public void Delete(string id)
    {
        string folder = Folder(id);
        if (IsValidId(id) && System.IO.Directory.Exists(folder))
        {
            System.IO.Directory.Delete(folder, recursive: true);
        }
    }

    private string Folder(string id) => Path.Combine(Directory, id);

    /// <summary>
    /// Deletes writes claimed more than a day ago: their window closed without cleaning up, for example
    /// with its close button or Ctrl+C, and nothing can show them again.
    /// </summary>
    private void DeleteAbandoned(DateTimeOffset now)
    {
        foreach (string folder in System.IO.Directory.EnumerateDirectories(Directory))
        {
            string claim = Path.Combine(folder, ClaimFile);
            if (IsValidId(Path.GetFileName(folder)) && File.Exists(claim) && now - File.GetLastWriteTimeUtc(claim) > TimeSpan.FromDays(1))
            {
                try
                {
                    System.IO.Directory.Delete(folder, recursive: true);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    // Tried again with the next write.
                }
            }
        }
    }

    private bool TryCreateClaim(string id, string claimant)
    {
        try
        {
            using var claim = new FileStream(Path.Combine(Folder(id), ClaimFile), FileMode.CreateNew, FileAccess.Write, FileShare.None);
            claim.Write(System.Text.Encoding.UTF8.GetBytes(claimant));
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // The claim exists (someone was first), or the folder is gone.
            return false;
        }
    }

    private ApprovalRequest? Read(string id)
    {
        try
        {
            return JsonSerializer.Deserialize<ApprovalRequest>(File.ReadAllText(Path.Combine(Folder(id), RequestFile)), JsonOptions) is
            { Kind: not null, Arguments: not null, Organization: not null } request
                && request.Id == id
                && ApprovalRequest.IsKnownKind(request.Kind)
                && !request.Arguments.Any(argument => argument.StartsWith("--dry-run", StringComparison.Ordinal))
                    ? request
                    : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    /// <summary>Only the user may read a pending write: on macOS and Linux the folder is created with mode 700.</summary>
    private static void CreatePrivateDirectory(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            System.IO.Directory.CreateDirectory(path);
        }
        else
        {
            System.IO.Directory.CreateDirectory(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    /// <summary>
    /// The leaf command's arguments in the <c>--name=value</c> form, which a value starting with a dash
    /// cannot break. The text replaces <c>--body</c> and <c>--body-file</c>, attachments point to copies,
    /// and <c>--config</c> becomes a full path, so nothing outside the folder changes what is posted.
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
}
