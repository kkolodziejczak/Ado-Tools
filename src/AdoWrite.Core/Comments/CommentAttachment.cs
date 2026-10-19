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
        try
        {
            var file = new FileInfo(path);
            if (!file.Exists)
            {
                throw WriteException.Rejected("attachmentUnreadable", $"The --attach file '{path}' does not exist.");
            }

            (byte[] content, string sha256) = await ReadBoundedAsync(file, cancellationToken);
            return new CommentAttachment(file.FullName, file.Name, content.Length, sha256);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            throw WriteException.Rejected("attachmentUnreadable", $"The --attach file '{path}' could not be read.");
        }
    }

    /// <summary>
    /// The file's bytes for upload, read again and checked against the hash taken during validation,
    /// so a file changed in between is rejected instead of uploaded under the old identity.
    /// </summary>
    public async Task<Stream> OpenVerifiedAsync(CancellationToken cancellationToken)
    {
        try
        {
            (byte[] content, string sha256) = await ReadBoundedAsync(new FileInfo(Path), cancellationToken);
            return sha256 == Sha256
                ? new MemoryStream(content, writable: false)
                : throw WriteException.Rejected("attachmentChanged", $"The --attach file '{Name}' changed after it was checked. Run the command again.");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw WriteException.Rejected("attachmentUnreadable", $"The --attach file '{Name}' could not be read.");
        }
    }

    private static async Task<(byte[] Content, string Sha256)> ReadBoundedAsync(FileInfo file, CancellationToken cancellationToken)
    {
        await using FileStream stream = file.OpenRead();
        using var buffer = new MemoryStream();
        byte[] chunk = new byte[81920];
        int read;
        while ((read = await stream.ReadAsync(chunk, cancellationToken)) > 0)
        {
            if (buffer.Length + read > MaxBytes)
            {
                throw TooLarge(file.Name);
            }

            buffer.Write(chunk, 0, read);
        }

        return buffer.Length == 0
            ? throw TooLarge(file.Name)
            : (buffer.ToArray(), Convert.ToHexStringLower(SHA256.HashData(buffer.GetBuffer().AsSpan(0, (int)buffer.Length))));
    }

    private static WriteException TooLarge(string name) =>
        WriteException.Rejected(
            "attachmentTooLarge",
            $"The --attach file '{name}' must be between 1 byte and {MaxBytes / (1024 * 1024)} MB.");
}

/// <summary>An attachment in the result. <see cref="Url"/> is null in a preview, where nothing is uploaded.</summary>
public sealed record AttachmentDocument(string Name, long Length, string Sha256, Uri? Url, bool Reused);

/// <summary>An attachment and the link the comment shows for it.</summary>
internal sealed record AttachmentLink(CommentAttachment File, Uri? Url, bool Reused)
{
    public AttachmentDocument ToDocument() => new(File.Name, File.Length, File.Sha256, Url, Reused);
}
