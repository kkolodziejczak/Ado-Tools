using System.Security;

namespace AdoRead.WorkItems;

internal static class WorkItemImageOutputDirectory
{
    public static bool TryResolve(string? value, out string fullPath)
    {
        fullPath = string.Empty;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        try
        {
            fullPath = Path.GetFullPath(value);
            if (File.Exists(fullPath))
            {
                return false;
            }

            if (!Directory.Exists(fullPath))
            {
                return true;
            }

            return IsEmptyNonReparseDirectory(fullPath);
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
        catch (SecurityException)
        {
            return false;
        }
        catch (NotSupportedException)
        {
            return false;
        }
    }

    public static bool TryCreate(string fullPath)
    {
        try
        {
            if (!TryResolve(fullPath, out _))
            {
                return false;
            }

            Directory.CreateDirectory(fullPath);
            if (!TryValidateExisting(fullPath))
            {
                return false;
            }

            string probePath = Path.Combine(fullPath, $".ado-read-write-test-{Guid.NewGuid():N}");
            try
            {
                using (new FileStream(
                    probePath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    bufferSize: 1,
                    options: FileOptions.SequentialScan))
                {
                }
            }
            finally
            {
                TryDelete(probePath);
            }

            return TryValidateExisting(fullPath);
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
        catch (SecurityException)
        {
            return false;
        }
        catch (NotSupportedException)
        {
            return false;
        }
    }

    public static bool TryValidateExisting(string path)
    {
        try
        {
            return IsEmptyNonReparseDirectory(Path.GetFullPath(path));
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
        catch (SecurityException)
        {
            return false;
        }
        catch (NotSupportedException)
        {
            return false;
        }
    }

    private static bool IsEmptyNonReparseDirectory(string path)
    {
        if (!Directory.Exists(path))
        {
            return false;
        }

        var directory = new DirectoryInfo(path);
        return !directory.Attributes.HasFlag(FileAttributes.ReparsePoint)
            && !directory.EnumerateFileSystemInfos().Any();
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
        catch (SecurityException)
        {
        }
    }
}
