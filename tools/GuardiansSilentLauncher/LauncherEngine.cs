using System.Diagnostics;
using System.IO.Compression;
using System.Text;

namespace UCREW.GuardiansTR;

internal sealed record LauncherProgress(int Percent, string Status, string Detail);

internal sealed class LauncherEngine
{
    private const string GameExeName = "Guardians.exe";
    private const string LogoFileName = "ucrew-logo.png";
    private const string BackgroundFileName = "guardians-background.jpg";

    private static readonly HashSet<string> AllowedExtensions = new(
        StringComparer.OrdinalIgnoreCase)
    {
        ".landb",
        ".font",
        ".fnt",
        ".dds",
        ".d3dtx"
    };

    private readonly IReadOnlyList<string> _arguments;

    private readonly string _gameRoot;
    private readonly string _privateRoot;
    private readonly string _assetsRoot;
    private readonly string _archivesRoot;
    private readonly string _manifestPath;
    private readonly string _backupRoot;
    private readonly string _logPath;
    private readonly string _logoPath;
    private readonly string _backgroundPath;
    private readonly SecurePatchClient _securePatchClient;

    public LauncherEngine(IReadOnlyList<string> arguments)
    {
        _arguments = arguments;
        _gameRoot = Path.GetFullPath(AppContext.BaseDirectory);
        _privateRoot = Path.Combine(_gameRoot, ".ucrew");
        _assetsRoot = Path.Combine(_privateRoot, "assets");
        _archivesRoot = Path.Combine(_gameRoot, "archives");
        _manifestPath = Path.Combine(_privateRoot, "runtime_manifest.txt");
        _backupRoot = Path.Combine(_privateRoot, "runtime_backup");
        _logPath = Path.Combine(_privateRoot, "ucrew_launcher.log");
        _logoPath = Path.Combine(_assetsRoot, LogoFileName);
        _backgroundPath = Path.Combine(_assetsRoot, BackgroundFileName);

        PreparePrivateStorage();
        ImportVisualAssets();

        _securePatchClient = new SecurePatchClient(
            _gameRoot,
            _privateRoot,
            Log);
    }

    public string LogoPath => _logoPath;
    public string BackgroundPath => _backgroundPath;

    public async Task PreparePatchAsync(
        IProgress<LauncherProgress> progress,
        CancellationToken cancellationToken)
    {
        progress.Report(new LauncherProgress(
            3,
            "Yama hazırlanıyor…",
            "Guardians kurulumu ve gizli çalışma alanı kontrol ediliyor."));

        string gameExePath = Path.Combine(_gameRoot, GameExeName);
        if (!File.Exists(gameExePath))
        {
            throw new FileNotFoundException(
                "Başlatıcı Guardians.exe dosyasının yanında olmalıdır.",
                gameExePath);
        }

        cancellationToken.ThrowIfCancellationRequested();

        MoveLegacyFilesOutOfSight();
        CleanupStaleSession();

        using SecurePatchPackage package =
            await _securePatchClient.AcquireAsync(
                progress,
                cancellationToken).ConfigureAwait(false);

        progress.Report(new LauncherProgress(
            72,
            "Türkçe yama kuruluyor…",
            $"Sunucudan doğrulanan {package.Version} sürümü hazırlanıyor."));

        ExtractPatchIntoArchives(
            package.ZipPath,
            progress,
            cancellationToken);

        progress.Report(new LauncherProgress(
            94,
            "Yama kuruldu.",
            "Galaksinin Koruyucuları Türkçe olarak başlatılmaya hazır."));

        Log(
            $"Sunucu yaması hazırlandı. Sürüm={package.Version} " +
            $"Yeniİndirme={(package.Downloaded ? "evet" : "hayır")}");
    }

    public Process StartGame()
    {
        string gameExePath = Path.Combine(_gameRoot, GameExeName);

        var startInfo = new ProcessStartInfo
        {
            FileName = gameExePath,
            WorkingDirectory = _gameRoot,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (string argument in _arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        Log("Guardians.exe başlatılıyor.");

        return Process.Start(startInfo)
            ?? throw new InvalidOperationException(
                "Guardians.exe başlatılamadı.");
    }

    public void Cleanup()
    {
        CleanupStaleSession();
        Log("Archives klasörü oyun öncesi hâline getirildi.");
    }

    public void LogException(Exception exception)
    {
        try
        {
            Log(exception.ToString());
        }
        catch
        {
        }
    }

    private void PreparePrivateStorage()
    {
        Directory.CreateDirectory(_privateRoot);
        Directory.CreateDirectory(_assetsRoot);
        Directory.CreateDirectory(_archivesRoot);

        SetHiddenSystem(_privateRoot);
        SetHiddenSystem(_assetsRoot);
    }

    private void ImportVisualAssets()
    {
        ImportAsset(
            new[]
            {
                LogoFileName,
                "ucrew-logo-4k(1).png",
                "ucrew-logo-4k.png"
            },
            _logoPath);

        ImportAsset(
            new[]
            {
                BackgroundFileName,
                "Telltales-Guardians-of-the-Galaxy.jpg"
            },
            _backgroundPath);

        SetHiddenSystem(_logoPath);
        SetHiddenSystem(_backgroundPath);
    }

    private void ImportAsset(
        IEnumerable<string> sourceNames,
        string destinationPath)
    {
        foreach (string sourceName in sourceNames)
        {
            string sourcePath = Path.Combine(_gameRoot, sourceName);

            if (!File.Exists(sourcePath))
            {
                continue;
            }

            RemoveRestrictiveAttributes(sourcePath);
            RemoveRestrictiveAttributes(destinationPath);

            File.Copy(sourcePath, destinationPath, overwrite: true);
            File.Delete(sourcePath);
            SetHiddenSystem(destinationPath);
            return;
        }
    }

    private void MoveLegacyFilesOutOfSight()
    {
        string legacyRoot = Path.Combine(_privateRoot, "legacy");
        Directory.CreateDirectory(legacyRoot);
        SetHiddenSystem(legacyRoot);

        string[] simpleLegacyFiles =
        {
            "UCREW_GUARDIANS_TR_BASLAT.bat",
            "UCREW_GUARDIANS_TR_RUNTIME.ps1",
            "ucrew_runtime.log",
            "ucrew_winmm.log",
            "KURULUM.txt"
        };

        foreach (string fileName in simpleLegacyFiles)
        {
            MoveIntoLegacy(fileName, legacyRoot);
        }

        bool oldDllLoaderExists =
            File.Exists(Path.Combine(_gameRoot, "ucrew_loader.dll")) ||
            File.Exists(Path.Combine(_gameRoot, "ucrew_loader.ini"));

        if (oldDllLoaderExists)
        {
            foreach (string fileName in new[]
                     {
                         "ucrew_loader.dll",
                         "ucrew_loader.ini",
                         "winmm.dll"
                     })
            {
                MoveIntoLegacy(fileName, legacyRoot);
            }
        }
    }

    private void MoveIntoLegacy(
        string fileName,
        string legacyRoot)
    {
        string sourcePath = Path.Combine(_gameRoot, fileName);
        if (!File.Exists(sourcePath))
        {
            return;
        }

        string destinationPath = Path.Combine(legacyRoot, fileName);
        RemoveRestrictiveAttributes(sourcePath);
        RemoveRestrictiveAttributes(destinationPath);

        File.Move(sourcePath, destinationPath, overwrite: true);
        SetHiddenSystem(destinationPath);
        Log($"Eski yardımcı dosya gizlendi: {fileName}");
    }

    private void ExtractPatchIntoArchives(
        string zipPath,
        IProgress<LauncherProgress> progress,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_backupRoot);
        SetHiddenSystem(_backupRoot);

        using ZipArchive archive = ZipFile.OpenRead(zipPath);

        List<ZipArchiveEntry> entries = archive.Entries
            .Where(entry =>
                !string.IsNullOrWhiteSpace(entry.Name) &&
                AllowedExtensions.Contains(Path.GetExtension(entry.Name)))
            .ToList();

        if (entries.Count == 0)
        {
            throw new InvalidDataException(
                "Yama paketinde desteklenen LANDb veya yazı tipi dosyası bulunamadı.");
        }

        var extractedNames = new List<string>();
        var seenNames = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);

        for (int index = 0; index < entries.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            ZipArchiveEntry entry = entries[index];
            string fileName = Path.GetFileName(entry.Name);

            if (string.IsNullOrWhiteSpace(fileName) ||
                fileName is "." or "..")
            {
                continue;
            }

            if (!seenNames.Add(fileName))
            {
                throw new InvalidDataException(
                    "Yama paketinde aynı ada sahip birden fazla dosya var: " +
                    fileName);
            }

            string targetPath = Path.Combine(_archivesRoot, fileName);
            string backupPath = Path.Combine(_backupRoot, fileName);

            if (File.Exists(targetPath))
            {
                RemoveRestrictiveAttributes(targetPath);
                File.Copy(targetPath, backupPath, overwrite: true);
                SetHiddenSystem(backupPath);
                Log($"Mevcut gevşek dosya yedeklendi: {fileName}");
            }

            extractedNames.Add(fileName);
            WriteManifest(extractedNames);

            RemoveRestrictiveAttributes(targetPath);
            entry.ExtractToFile(targetPath, overwrite: true);
            SetHiddenSystem(targetPath);

            int percent = 73 + (int)Math.Round(
                ((index + 1d) / entries.Count) * 18d);

            progress.Report(new LauncherProgress(
                Math.Clamp(percent, 73, 91),
                "Türkçe yama kuruluyor…",
                $"{index + 1} / {entries.Count} dosya hazırlandı"));

            Log($"ARCHIVES İÇİNE AÇILDI: {fileName}");
        }

        if (extractedNames.Count == 0)
        {
            throw new InvalidDataException(
                "Yama paketinden hiçbir desteklenen dosya çıkarılamadı.");
        }

        Log(
            $"Toplam {extractedNames.Count} dosya archives içine hazırlandı.");
    }

    private void CleanupStaleSession()
    {
        if (File.Exists(_manifestPath))
        {
            RemoveRestrictiveAttributes(_manifestPath);

            foreach (string rawName in File.ReadAllLines(
                         _manifestPath,
                         Encoding.UTF8))
            {
                string fileName = Path.GetFileName(rawName.Trim());
                if (string.IsNullOrWhiteSpace(fileName))
                {
                    continue;
                }

                string targetPath = Path.Combine(
                    _archivesRoot,
                    fileName);

                if (File.Exists(targetPath))
                {
                    RemoveRestrictiveAttributes(targetPath);
                    File.Delete(targetPath);
                }
            }
        }

        if (Directory.Exists(_backupRoot))
        {
            foreach (string backupPath in Directory.EnumerateFiles(
                         _backupRoot))
            {
                string fileName = Path.GetFileName(backupPath);
                string targetPath = Path.Combine(
                    _archivesRoot,
                    fileName);

                RemoveRestrictiveAttributes(backupPath);
                RemoveRestrictiveAttributes(targetPath);
                File.Copy(backupPath, targetPath, overwrite: true);
                Log($"Yedek geri yüklendi: {fileName}");
            }

            RemoveRestrictiveAttributes(_backupRoot);
            Directory.Delete(_backupRoot, recursive: true);
        }

        if (File.Exists(_manifestPath))
        {
            RemoveRestrictiveAttributes(_manifestPath);
            File.Delete(_manifestPath);
        }
    }

    private void WriteManifest(IEnumerable<string> names)
    {
        Directory.CreateDirectory(_privateRoot);
        RemoveRestrictiveAttributes(_manifestPath);

        File.WriteAllLines(
            _manifestPath,
            names,
            new UTF8Encoding(false));

        SetHiddenSystem(_manifestPath);
        SetHiddenSystem(_privateRoot);
    }

    private static void SetHiddenSystem(string path)
    {
        if (!File.Exists(path) && !Directory.Exists(path))
        {
            return;
        }

        FileAttributes attributes = File.GetAttributes(path);
        File.SetAttributes(
            path,
            attributes | FileAttributes.Hidden | FileAttributes.System);
    }

    private static void RemoveRestrictiveAttributes(string path)
    {
        if (!File.Exists(path) && !Directory.Exists(path))
        {
            return;
        }

        FileAttributes attributes = File.GetAttributes(path);
        attributes &= ~FileAttributes.ReadOnly;
        attributes &= ~FileAttributes.Hidden;
        attributes &= ~FileAttributes.System;
        File.SetAttributes(path, attributes);
    }

    private void Log(string message)
    {
        try
        {
            Directory.CreateDirectory(_privateRoot);
            RemoveRestrictiveAttributes(_logPath);

            string line =
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] " +
                message +
                Environment.NewLine;

            File.AppendAllText(
                _logPath,
                line,
                new UTF8Encoding(false));

            SetHiddenSystem(_logPath);
            SetHiddenSystem(_privateRoot);
        }
        catch
        {
            // Log hatası oyunu engellemesin.
        }
    }
}
