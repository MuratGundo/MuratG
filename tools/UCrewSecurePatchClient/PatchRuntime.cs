using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace UCREW.SecurePatch;

internal sealed class PatchRuntime
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private readonly ClientConfig _config;
    private readonly string _privateRoot;
    private readonly string _sessionsRoot;
    private readonly string _activeSessionPath;
    private readonly Action<string> _log;

    public PatchRuntime(ClientConfig config, string privateRoot, Action<string> log)
    {
        _config = config;
        _privateRoot = privateRoot;
        _sessionsRoot = Path.Combine(privateRoot, "sessions");
        _activeSessionPath = Path.Combine(privateRoot, "active_session.json");
        _log = log;

        Directory.CreateDirectory(_sessionsRoot);
        FileSystemUtil.SetHiddenSystem(_sessionsRoot);
    }

    public void RecoverStaleSession()
    {
        if (!File.Exists(_activeSessionPath))
        {
            return;
        }

        _log("Önceki yarım kalmış yama oturumu bulundu; geri alınıyor.");
        CleanupActiveSession();
    }

    public InstallSession Install(
        PreparedPatch preparedPatch,
        IProgress<LauncherProgress> progress,
        CancellationToken cancellationToken)
    {
        RuntimeProfile profile = preparedPatch.Ticket.RuntimeProfile;
        ValidateProfile(profile);

        var session = new InstallSession
        {
            GameSlug = preparedPatch.Ticket.GameSlug,
            GameRoot = _config.GameRoot,
            CleanupOnExit = profile.CleanupOnExit
        };

        string sessionRoot = Path.Combine(_sessionsRoot, session.SessionId);
        string backupRoot = Path.Combine(sessionRoot, "backup");
        Directory.CreateDirectory(backupRoot);
        FileSystemUtil.SetHiddenSystem(sessionRoot);
        FileSystemUtil.SetHiddenSystem(backupRoot);

        SaveSession(session);

        try
        {
            using ZipArchive archive = ZipFile.OpenRead(preparedPatch.DecryptedZipPath);
            List<ZipArchiveEntry> entries = archive.Entries
                .Where(entry => !string.IsNullOrWhiteSpace(entry.Name))
                .ToList();

            if (entries.Count == 0)
            {
                throw new InvalidDataException("Yama paketi boş.");
            }

            HashSet<string> allowedExtensions = profile.AllowedExtensions
                .Select(FileSystemUtil.NormalizeExtension)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var targetPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (int index = 0; index < entries.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                ZipArchiveEntry entry = entries[index];
                string extension = FileSystemUtil.NormalizeExtension(Path.GetExtension(entry.Name));
                if (!allowedExtensions.Contains(extension))
                {
                    throw new InvalidDataException(
                        "Paket profilin izin vermediği bir dosya içeriyor: " + entry.FullName);
                }

                string targetPath = ResolveEntryTarget(profile, entry);
                if (!targetPaths.Add(targetPath))
                {
                    throw new InvalidDataException(
                        "Paket aynı hedefe birden fazla dosya kurmaya çalışıyor: " + targetPath);
                }

                string backupPath = Path.Combine(backupRoot, $"{index:D8}.bak");
                bool hadOriginal = File.Exists(targetPath);

                if (hadOriginal && profile.BackupExistingFiles)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(backupPath)!);
                    FileSystemUtil.RemoveRestrictiveAttributes(targetPath);
                    File.Copy(targetPath, backupPath, overwrite: true);
                    FileSystemUtil.SetHiddenSystem(backupPath);
                }
                else if (hadOriginal && !profile.BackupExistingFiles)
                {
                    throw new InvalidOperationException(
                        "Hedef dosya zaten var ve profil yedeklemeyi kapatmış: " + targetPath);
                }

                session.Files.Add(new InstalledFileRecord
                {
                    TargetPath = targetPath,
                    BackupPath = hadOriginal ? backupPath : string.Empty,
                    HadOriginal = hadOriginal
                });
                SaveSession(session);

                Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
                FileSystemUtil.RemoveRestrictiveAttributes(targetPath);
                entry.ExtractToFile(targetPath, overwrite: true);

                if (_config.HideRuntimeFiles)
                {
                    FileSystemUtil.SetHiddenSystem(targetPath);
                }

                int percent = 70 + (int)Math.Round(((index + 1d) / entries.Count) * 23d);
                progress.Report(new LauncherProgress(
                    Math.Clamp(percent, 70, 93),
                    "Türkçe yama kuruluyor…",
                    $"{index + 1} / {entries.Count} dosya hazırlandı"));

                _log("Yama dosyası kuruldu: " + targetPath);
            }

            progress.Report(new LauncherProgress(
                95,
                "Yama kuruldu.",
                $"{preparedPatch.Ticket.GameTitle} Türkçe olarak başlatılmaya hazır."));

            return session;
        }
        catch
        {
            CleanupActiveSession();
            throw;
        }
    }

    public Process StartGame(RuntimeProfile profile)
    {
        string[] candidates = (_config.GameExecutables ?? Array.Empty<string>())
            .Concat(string.IsNullOrWhiteSpace(_config.GameExe)
                ? Array.Empty<string>()
                : new[] { _config.GameExe })
            .Concat(profile.GameExecutables ?? Array.Empty<string>())
            .Concat(string.IsNullOrWhiteSpace(profile.GameExe)
                ? Array.Empty<string>()
                : new[] { profile.GameExe })
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var checkedPaths = new List<string>();
        string? gameExePath = null;
        foreach (string candidate in candidates)
        {
            string candidatePath = FileSystemUtil.ResolveSafePath(_config.GameRoot, candidate);
            checkedPaths.Add(candidatePath);
            if (File.Exists(candidatePath))
            {
                gameExePath = candidatePath;
                break;
            }
        }

        if (gameExePath is null)
        {
            throw new FileNotFoundException(
                "Steam veya Game Pass oyun EXE'si bulunamadı. Kontrol edilen yollar:" +
                Environment.NewLine + string.Join(Environment.NewLine, checkedPaths));
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = gameExePath,
            WorkingDirectory = Path.GetDirectoryName(gameExePath) ?? _config.GameRoot,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (string argument in _config.GameArguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        startInfo.Environment["UCREW_PATCH_READY"] = "1";
        startInfo.Environment["UCREW_GAME_SLUG"] = _config.GameSlug;

        _log("Oyun başlatılıyor: " + gameExePath);
        return Process.Start(startInfo)
               ?? throw new InvalidOperationException("Oyun başlatılamadı.");
    }

    public void CleanupActiveSession()
    {
        if (!File.Exists(_activeSessionPath))
        {
            return;
        }

        InstallSession? session = null;

        try
        {
            FileSystemUtil.RemoveRestrictiveAttributes(_activeSessionPath);
            session = JsonSerializer.Deserialize<InstallSession>(
                File.ReadAllText(_activeSessionPath, Encoding.UTF8),
                JsonOptions);
        }
        catch (Exception exception)
        {
            _log("Oturum bildirimi okunamadı: " + exception.Message);
        }

        if (session is not null)
        {
            foreach (InstalledFileRecord record in session.Files.AsEnumerable().Reverse())
            {
                try
                {
                    if (File.Exists(record.TargetPath))
                    {
                        FileSystemUtil.RemoveRestrictiveAttributes(record.TargetPath);
                        File.Delete(record.TargetPath);
                    }

                    if (record.HadOriginal && File.Exists(record.BackupPath))
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(record.TargetPath)!);
                        FileSystemUtil.RemoveRestrictiveAttributes(record.BackupPath);
                        File.Copy(record.BackupPath, record.TargetPath, overwrite: true);
                    }

                    RemoveEmptyParentDirectories(record.TargetPath, session.GameRoot);
                }
                catch (Exception exception)
                {
                    _log("Yama dosyası geri alınamadı: " + exception.Message);
                }
            }

            FileSystemUtil.TryDeleteDirectory(Path.Combine(_sessionsRoot, session.SessionId));
        }

        FileSystemUtil.TryDeleteFile(_activeSessionPath);
        _log("Geçici yama oturumu temizlendi.");
    }

    public void CompletePersistentSession()
    {
        if (!File.Exists(_activeSessionPath))
        {
            return;
        }

        try
        {
            FileSystemUtil.RemoveRestrictiveAttributes(_activeSessionPath);
            InstallSession? session = JsonSerializer.Deserialize<InstallSession>(
                File.ReadAllText(_activeSessionPath, Encoding.UTF8),
                JsonOptions);

            if (session is not null)
            {
                FileSystemUtil.TryDeleteDirectory(Path.Combine(_sessionsRoot, session.SessionId));
            }
        }
        finally
        {
            FileSystemUtil.TryDeleteFile(_activeSessionPath);
        }
    }

    private string ResolveEntryTarget(RuntimeProfile profile, ZipArchiveEntry entry)
    {
        string fullName = entry.FullName.Replace('\\', '/').TrimStart('/');
        string[] segments = fullName.Split('/', StringSplitOptions.RemoveEmptyEntries);

        if (segments.Length == 0 || segments.Any(segment => segment is "." or ".."))
        {
            throw new InvalidDataException("ZIP içinde güvenli olmayan yol: " + entry.FullName);
        }

        string[] targetCandidates = (profile.TargetPaths ?? Array.Empty<string>())
            .Concat(string.IsNullOrWhiteSpace(profile.TargetPath)
                ? Array.Empty<string>()
                : new[] { profile.TargetPath })
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        string targetRoot = _config.GameRoot;
        if (targetCandidates.Length > 0)
        {
            string[] resolvedTargets = targetCandidates
                .Select(value => FileSystemUtil.ResolveSafePath(_config.GameRoot, value))
                .ToArray();
            targetRoot = resolvedTargets.FirstOrDefault(Directory.Exists)
                ?? resolvedTargets[0];
        }

        string relativeTarget = profile.InstallMode.ToLowerInvariant() switch
        {
            "overlay_flat" => Path.GetFileName(entry.Name),
            "overlay_tree" => Path.Combine(segments),
            "replace_files" => Path.Combine(segments),
            "archive_replace" => Path.Combine(segments),
            "custom" => throw new NotSupportedException(
                "Bu oyun custom adaptör gerektiriyor. Genel istemci tek başına kuramaz."),
            _ => throw new NotSupportedException(
                "Desteklenmeyen kurulum modu: " + profile.InstallMode)
        };

        return FileSystemUtil.ResolveSafePath(targetRoot, relativeTarget);
    }

    private void SaveSession(InstallSession session)
    {
        Directory.CreateDirectory(_privateRoot);
        FileSystemUtil.RemoveRestrictiveAttributes(_activeSessionPath);
        File.WriteAllText(
            _activeSessionPath,
            JsonSerializer.Serialize(session, JsonOptions),
            new UTF8Encoding(false));
        FileSystemUtil.SetHiddenSystem(_activeSessionPath);
    }

    private static void ValidateProfile(RuntimeProfile profile)
    {
        if (string.IsNullOrWhiteSpace(profile.GameExe) &&
            (profile.GameExecutables is null || profile.GameExecutables.Length == 0))
        {
            throw new InvalidDataException("Çalışma profilinde game_exe veya game_exes boş.");
        }

        if (profile.AllowedExtensions is null || profile.AllowedExtensions.Length == 0)
        {
            throw new InvalidDataException("Çalışma profilinde allowed_extensions boş.");
        }

        string mode = (profile.InstallMode ?? string.Empty).Trim().ToLowerInvariant();
        string[] supported =
        {
            "overlay_flat",
            "overlay_tree",
            "replace_files",
            "archive_replace",
            "custom"
        };

        if (!supported.Contains(mode, StringComparer.OrdinalIgnoreCase))
        {
            throw new NotSupportedException("Desteklenmeyen kurulum modu: " + mode);
        }
    }

    private static void RemoveEmptyParentDirectories(string filePath, string stopRoot)
    {
        string normalizedStopRoot = Path.GetFullPath(stopRoot)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        DirectoryInfo? directory = Directory.GetParent(filePath);

        while (directory is not null &&
               !directory.FullName.Equals(normalizedStopRoot, StringComparison.OrdinalIgnoreCase) &&
               directory.FullName.StartsWith(
                   normalizedStopRoot + Path.DirectorySeparatorChar,
                   StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                if (directory.EnumerateFileSystemInfos().Any())
                {
                    break;
                }

                DirectoryInfo? parent = directory.Parent;
                FileSystemUtil.RemoveRestrictiveAttributes(directory.FullName);
                directory.Delete();
                directory = parent;
            }
            catch
            {
                break;
            }
        }
    }
}
