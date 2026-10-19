using System.Security.Cryptography;

namespace AdoWrite.Comments;

/// <summary>
/// A local file to upload with a comment, read and hashed before any Azure DevOps call. The hash
/// is part of the duplicate key and of <see cref="StoredName"/>, so a retry recognises its own upload.
/// </summary>
public sealed record CommentAttachment(string Path, string Name, long Length, string Sha256)
{
    public const int MaxFiles = 10;
    public const long MaxBytes = 25 * 1024 * 1024;

    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".gif", ".webp", ".bmp"
    };

    /// <summary>Images are shown in the comment; other files are linked.</summary>
    public bool IsImage => ImageExtensions.Contains(System.IO.Path.GetExtension(Name));

    /// <summary>The name a pull-request upload is stored under; the hash prefix makes it unique and recognisable.</summary>
    public string StoredName => $"{Sha256[..12]}-{Name}";

    public static async Task<CommentAttachment> ReadAsync(string path, CancellationToken cancellationToken)
    {
        var file = new FileInfo(path);
        if (!file.Exists)
        {
            throw WriteException.Rejected("attachmentUnreadable", $"The --attach file '{path}' does not exist.");
        }

        if (file.Length == 0 || file.Length > MaxBytes)
        {
            throw WriteException.Rejected(
                "attachmentTooLarge",
                $"The --attach file '{file.Name}' must be between 1 byte and {MaxBytes / (1024 * 1024)} MB.");
        }

        try
        {
            await using FileStream stream = file.OpenRead();
            byte[] hash = await SHA256.HashDataAsync(stream, cancellationToken);
            return new CommentAttachment(file.FullName, file.Name, file.Length, Convert.ToHexStringLower(hash));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw WriteException.Rejected("attachmentUnreadable", $"The --attach file '{file.Name}' could not be read.");
        }
    }

    public Stream Open() => File.OpenRead(Path);
}

/// <summary>An attachment in the result. <see cref="Url"/> is null in a preview, where nothing is uploaded.</summary>
public sealed record AttachmentDocument(string Name, long Length, string Sha256, Uri? Url, bool Reused);

/// <summary>An attachment and the link the comment shows for it.</summary>
internal sealed record AttachmentLink(CommentAttachment File, Uri? Url, bool Reused)
{
    public AttachmentDocument ToDocument() => new(File.Name, File.Length, File.Sha256, Url, Reused);
}
