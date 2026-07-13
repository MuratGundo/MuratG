using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using System.Windows.Forms;

namespace UCREW.GuardiansTR;

internal static class Program
{
    private const string PatchZipName = "UCREW_Guardians_TR.zip";
    private const string GameExeName = "Guardians.exe";

    private static readonly HashSet<string> AllowedExtensions = new(
        StringComparer.OrdinalIgnoreCase)
    {
        ".landb",
        ".font",
        ".fnt",
        ".dds",
        ".d3dtx"
    };

    private static string _gameRoot = string.Empty;
    private static string _privateRoot = string.Empty;
    private static string _archivesRoot = string.Empty;
    private static string _hiddenZipPath = string.Empty;
    private static string _manifestPath = string.Empty;
    private static string _backupRoot = string.Empty;
    private static string _logPath = string.Empty;

    [STAThread]
    private static int Main(string[] args)
    {
        using var mutex = new Mutex(
            initiallyOwned: true,
            name: @"Local\UCREW_Guardians_TR_Launcher",
            createdNew: out bool createdNew);

        if (!createdNew)
        {
            ShowError("Türkçe başlatıcı zaten çalışıyor.");
            return 2;
        }

        try
        {
            InitializePaths();
            PreparePrivateStorage();
            Log("U-CREW Guardians Türkçe sessiz başlatıcı açıldı.");

            string gameExePath = Path.Combine(_gameRoot, GameExeName);
            if (!File.Exists(gameExePath))
            {
                throw new FileNotFoundException(
                    "Başlatıcı Guardians.exe dosyasının yanında olmalıdır.",
                    gameExePath);
            }

            ImportPatchZip();
            MoveLegacyFilesOutOfSight();
            CleanupStaleSession();

            try
            {
                ExtractPatchIntoArchives();
                Log("Guardians.exe başlatılıyor.");

                using Process process = StartGame(gameExePath, args);
                process.WaitForExit();

                Log($"Oyun kapandı. Çıkış kodu: {process.ExitCode}");
            }
            finally
            {
                CleanupStaleSession();
                Log("Archives klasörü oyun öncesi hâline getirildi.");
            }

            return 0;
        }
        catch (Exception exception)
        {
            TryLogException(exception);
            ShowError(
                "Türkçe yama başlatılamadı.\n\n" +
                exception.Message +
                "\n\nAyrıntı: .ucrew\\ucrew_launcher.log");
            return 1;
        }
    }

    private static void InitializePaths()
    {
        _gameRoot = Path.GetFullPath(AppContext.BaseDirectory);
        _privateRoot = Path.Combine(_gameRoot, ".ucrew");
        _archivesRoot = Path.Combine(_gameRoot, "archives");
        _hiddenZipPath = Path.Combine(_privateRoot, PatchZipName);
        _manifestPath = Path.Combine(_privateRoot, "runtime_manifest.txt");
        _backupRoot = Path.Combine(_privateRoot, "runtime_backup");
        _logPath = Path.Combine(_privateRoot, "ucrew_launcher.log");
    }

    private static void PreparePrivateStorage()
    {
        Directory.CreateDirectory(_privateRoot);
        SetHiddenSystem(_privateRoot);

        Directory.CreateDirectory(_archivesRoot);

        string? logDirectory = Path.GetDirectoryName(_logPath);
        if (!string.IsNullOrEmpty(logDirectory))
        {
            Directory.CreateDirectory(logDirectory);
        }
    }

    private static void ImportPatchZip()
    {
        string visibleZipPath = Path.Combine(_gameRoot, PatchZipName);

        if (File.Exists(visibleZipPath))
        {
            RemoveRestrictiveAttributes(_hiddenZipPath);
            File.Copy(visibleZipPath, _hiddenZipPath, overwrite: true);
            File.Delete(visibleZipPath);
            Log("Yeni yama ZIP'i gizli .ucrew klasörüne taşındı.");
        }

        if (!File.Exists(_hiddenZipPath))
        {
            throw new FileNotFoundException(
                $"{PatchZipName} bulunamadı. İlk çalıştırmada EXE ile aynı klasöre koy.",
                PatchZipName);
        }

        SetHiddenSystem(_hiddenZipPath);
    }

    private static void MoveLegacyFilesOutOfSight()
    {
        string legacyRoot = Path.Combine(_privateRoot, "legacy");
        Directory.CreateDirectory(legacyRoot);
        SetHiddenSystem(legacyRoot);

        string[] simpleLegacyFiles =
        {
            "UCREW_GUARDIANS_TR_BASLAT.bat",
            "UCREW_GUARDIANS_TR_RUNTIME.ps1",
            "ucrew_runtime.log",
            "ucrew_winmm.log"
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
                         "version.dll",
                         "ucrew_loader.dll",
                         "ucrew_loader.ini",
                         "winmm.dll"
                     })
            {
                MoveIntoLegacy(fileName, legacyRoot);
            }
        }
    }

    private static void MoveIntoLegacy(string fileName, string legacyRoot)
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

    private static void ExtractPatchIntoArchives()
    {
        Directory.CreateDirectory(_backupRoot);
        SetHiddenSystem(_backupRoot);

        var extractedNames = new List<string>();
        var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        using ZipArchive archive = ZipFile.OpenRead(_hiddenZipPath);

        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            if (string.IsNullOrWhiteSpace(entry.Name))
            {
                continue;
            }

            string extension = Path.GetExtension(entry.Name);
            if (!AllowedExtensions.Contains(extension))
            {
                continue;
            }

            string fileName = Path.GetFileName(entry.Name);
            if (string.IsNullOrWhiteSpace(fileName) ||
                fileName is "." or "..")
            {
                continue;
            }

            if (!seenNames.Add(fileName))
            {
                throw new InvalidDataException(
                    $"ZIP içinde aynı ada sahip birden fazla dosya var: {fileName}");
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
            SetHidden(targetPath);

            Log($"ARCHIVES İÇİNE AÇILDI: {fileName}");
        }

        if (extractedNames.Count == 0)
        {
            throw new InvalidDataException(
                "ZIP içinde desteklenen LANDb veya font dosyası bulunamadı.");
        }

        Log($"Toplam {extractedNames.Count} dosya archives içine hazırlandı.");
    }

    private static Process StartGame(string gameExePath, IReadOnlyList<string> args)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = gameExePath,
            WorkingDirectory = _gameRoot,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (string argument in args)
        {
            startInfo.ArgumentList.Add(argument);
        }

        return Process.Start(startInfo)
            ?? throw new InvalidOperationException("Guardians.exe başlatılamadı.");
    }

    private static void CleanupStaleSession()
    {
        if (File.Exists(_manifestPath))
        {
            foreach (string rawName in File.ReadAllLines(_manifestPath, Encoding.UTF8))
            {
                string fileName = Path.GetFileName(rawName.Trim());
                if (string.IsNullOrWhiteSpace(fileName))
                {
                    continue;
                }

                string targetPath = Path.Combine(_archivesRoot, fileName);
                if (File.Exists(targetPath))
                {
                    RemoveRestrictiveAttributes(targetPath);
                    File.Delete(targetPath);
                }
            }
        }

        if (Directory.Exists(_backupRoot))
        {
            foreach (string backupPath in Directory.EnumerateFiles(_backupRoot))
            {
                string fileName = Path.GetFileName(backupPath);
                string targetPath = Path.Combine(_archivesRoot, fileName);

                RemoveRestrictiveAttributes(backupPath);
                RemoveRestrictiveAttributes(targetPath);
                File.Copy(backupPath, targetPath, overwrite: true);
                Log($"Yedek geri yüklendi: {fileName}");
            }

            Directory.Delete(_backupRoot, recursive: true);
        }

        if (File.Exists(_manifestPath))
        {
            RemoveRestrictiveAttributes(_manifestPath);
            File.Delete(_manifestPath);
        }
    }

    private static void WriteManifest(IEnumerable<string> names)
    {
        File.WriteAllLines(_manifestPath, names, new UTF8Encoding(false));
        SetHiddenSystem(_manifestPath);
    }

    private static void SetHidden(string path)
    {
        if (!File.Exists(path) && !Directory.Exists(path))
        {
            return;
        }

        FileAttributes attributes = File.GetAttributes(path);
        File.SetAttributes(path, attributes | FileAttributes.Hidden);
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

    private static void Log(string message)
    {
        try
        {
            Directory.CreateDirectory(_privateRoot);
            string line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}";
            File.AppendAllText(_logPath, line, new UTF8Encoding(false));
            SetHiddenSystem(_logPath);
        }
        catch
        {
            // Log hatası oyunu engellemesin.
        }
    }

    private static void TryLogException(Exception exception)
    {
        try
        {
            Log(exception.ToString());
        }
        catch
        {
        }
    }

    private static void ShowError(string message)
    {
        MessageBox.Show(
            message,
            "U-CREW Guardians Türkçe",
            MessageBoxButtons.OK,
            MessageBoxIcon.Error);
    }
}
