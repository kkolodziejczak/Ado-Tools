using System.Security.Cryptography;
using System.Text;

namespace AdoWrite.Comments;

/// <summary>
/// Lets one run on this machine at a time look for and post the comment with a given key, so two
/// identical runs started together cannot both miss the other's post. It is an exclusive lock on a
/// file in the temp directory, released when the run ends or its process dies.
/// </summary>
internal static class PostLock
{
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(100);

    /// <summary>Waits until no other run holds the key, or until <paramref name="cancellationToken"/> ends the wait.</summary>
    public static async Task<IDisposable> AcquireAsync(Uri organizationUrl, string key, CancellationToken cancellationToken)
    {
        string directory = Path.Combine(Path.GetTempPath(), "ado-write-locks");
        Directory.CreateDirectory(directory);
        string name = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes($"{organizationUrl.AbsoluteUri.ToLowerInvariant()}\n{key}")));
        string path = Path.Combine(directory, $"{name}.lock");
        while (true)
        {
            try
            {
                // Only Windows deletes the file safely: elsewhere another run could lock a new file
                // while this one still holds the deleted one, so the empty file stays.
                return new FileStream(
                    path,
                    FileMode.OpenOrCreate,
                    FileAccess.ReadWrite,
                    FileShare.None,
                    bufferSize: 1,
                    OperatingSystem.IsWindows() ? FileOptions.DeleteOnClose : FileOptions.None);
            }
            catch (IOException)
            {
                await Task.Delay(RetryDelay, cancellationToken);
            }
        }
    }
}
