using System.Security.Cryptography;
using System.Text;

namespace UCREW.SecurePatch;

internal static class FileSystemUtil
{
    public static void SetHiddenSystem(string path)
    {
        if (!File.Exists(path) && !Directory.Exists(path))
        {
            return;
        }

        try
        {
            FileAttributes attributes = File.GetAttributes(path);
            File.SetAttributes(path, attributes | FileAttributes.Hidden | FileAttributes.System);
        }
        catch
        {
        }
    }

    public static void RemoveRestrictiveAttributes(string path)
    {
        if (!File.Exists(path) && !Directory.Exists(path))
        {
            return;
        }

        try
        {
            FileAttributes attributes = File.GetAttributes(path);
            attributes &= ~FileAttributes.ReadOnly;
            attributes &= ~FileAttributes.Hidden;
            attributes &= ~FileAttributes.System;
            File.SetAttributes(path, attributes);
        }
        catch
        {
        }
    }

    public static void TryDeleteFile(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return;
            }

            RemoveRestrictiveAttributes(path);
            File.Delete(path);
        }
        catch
        {
        }
    }

    public static void TryDeleteDirectory(string path)
    {
        try
        {
            if (!Directory.Exists(path))
            {
                return;
            }

            foreach (string file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
            {
                RemoveRestrictiveAttributes(file);
            }

            foreach (string directory in Directory.EnumerateDirectories(path, "*", SearchOption.AllDirectories)
                         .OrderByDescending(value => value.Length))
            {
                RemoveRestrictiveAttributes(directory);
            }

            RemoveRestrictiveAttributes(path);
            Directory.Delete(path, recursive: true);
        }
        catch
        {
        }
    }

    public static string ResolveSafePath(string root, string relativePath, bool allowEmpty = false)
    {
        string normalizedRoot = Path.GetFullPath(root)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        string value = (relativePath ?? string.Empty)
            .Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar)
            .Trim();

        if (string.IsNullOrWhiteSpace(value))
        {
            if (allowEmpty)
            {
                return normalizedRoot;
            }

            throw new InvalidDataException("Boş göreli yol kullanılamaz.");
        }

        if (Path.IsPathRooted(value))
        {
            throw new InvalidDataException("Mutlak yol kullanılamaz: " + value);
        }

        string combined = Path.GetFullPath(Path.Combine(normalizedRoot, value));
        string prefix = normalizedRoot + Path.DirectorySeparatorChar;

        if (!combined.Equals(normalizedRoot, StringComparison.OrdinalIgnoreCase) &&
            !combined.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Oyun klasörü dışına çıkan yol engellendi: " + value);
        }

        return combined;
    }

    public static string NormalizeExtension(string extension)
    {
        string value = (extension ?? string.Empty).Trim().ToLowerInvariant();
        if (!value.StartsWith('.'))
        {
            value = "." + value;
        }

        return value;
    }

    public static async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken)
    {
        using SHA256 sha = SHA256.Create();
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 1024 * 128,
            useAsync: true);

        byte[] hash = await sha.ComputeHashAsync(stream, cancellationToken).ConfigureAwait(false);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public static string NormalizeSha256(string? value)
    {
        return (value ?? string.Empty)
            .Trim()
            .Replace("-", string.Empty, StringComparison.Ordinal)
            .ToLowerInvariant();
    }

    public static string FormatBytes(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        double value = bytes;
        int unit = 0;

        while (value >= 1024d && unit < units.Length - 1)
        {
            value /= 1024d;
            unit++;
        }

        return $"{value:0.##} {units[unit]}";
    }

    public static void AppendLog(string path, string message)
    {
        try
        {
            string? directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            RemoveRestrictiveAttributes(path);
            File.AppendAllText(
                path,
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}",
                new UTF8Encoding(false));
            SetHiddenSystem(path);
        }
        catch
        {
        }
    }
}
