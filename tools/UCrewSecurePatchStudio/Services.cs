using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Renci.SshNet;
using Renci.SshNet.Common;

namespace UCREW.SecurePatchStudio;

internal static class JsonDefaults
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };
}

internal sealed class StudioLogger
{
    private readonly object _sync = new();
    private readonly string _logPath;

    public StudioLogger()
    {
        string root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "U-CREW",
            "SecurePatchStudio");
        Directory.CreateDirectory(root);
        _logPath = Path.Combine(root, "studio.log");
    }

    public event Action<string>? MessageWritten;

    public string LogPath => _logPath;

    public void Write(string message)
    {
        string line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}";

        lock (_sync)
        {
            File.AppendAllText(_logPath, line + Environment.NewLine, new UTF8Encoding(false));
        }

        MessageWritten?.Invoke(line);
    }

    public string ReadAll()
    {
        lock (_sync)
        {
            return File.Exists(_logPath)
                ? File.ReadAllText(_logPath, Encoding.UTF8)
                : string.Empty;
        }
    }

    public void Clear()
    {
        lock (_sync)
        {
            File.WriteAllText(_logPath, string.Empty, new UTF8Encoding(false));
        }

        MessageWritten?.Invoke(string.Empty);
    }
}

internal sealed class SettingsService
{
    private readonly string _path;

    public SettingsService()
    {
        string root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "U-CREW",
            "SecurePatchStudio");
        Directory.CreateDirectory(root);
        _path = Path.Combine(root, "settings.json");
    }

    public StudioSettings Load()
    {
        try
        {
            if (!File.Exists(_path))
            {
                return new StudioSettings();
            }

            return JsonSerializer.Deserialize<StudioSettings>(
                       File.ReadAllText(_path, Encoding.UTF8),
                       JsonDefaults.Options)
                   ?? new StudioSettings();
        }
        catch
        {
            return new StudioSettings();
        }
    }

    public void Save(StudioSettings settings)
    {
        File.WriteAllText(
            _path,
            JsonSerializer.Serialize(settings, JsonDefaults.Options),
            new UTF8Encoding(false));
    }
}

internal static class StudioValidation
{
    private static readonly Regex SlugRegex = new(
        "^[a-z0-9][a-z0-9._-]{1,99}$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex DatabaseRegex = new(
        "^[A-Za-z0-9_]+$",
        RegexOptions.Compiled);

    public static void ValidateServerSettings(StudioSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.Host))
        {
            throw new InvalidDataException("VPS adresi boş olamaz.");
        }

        if (settings.Port is < 1 or > 65535)
        {
            throw new InvalidDataException("SSH portu geçersiz.");
        }

        if (string.IsNullOrWhiteSpace(settings.SshUser))
        {
            throw new InvalidDataException("SSH kullanıcı adı boş olamaz.");
        }

        if (!DatabaseRegex.IsMatch(settings.DatabaseName))
        {
            throw new InvalidDataException(
                "Veritabanı adı yalnızca harf, rakam ve alt çizgi içerebilir.");
        }

        if (!Regex.IsMatch(settings.DatabaseUser, "^[A-Za-z0-9_]+$"))
        {
            throw new InvalidDataException("Veritabanı kullanıcı adı geçersiz.");
        }
    }

    public static void ValidateProfile(GameProfile profile)
    {
        profile.GameSlug = (profile.GameSlug ?? string.Empty).Trim().ToLowerInvariant();
        profile.GameName = (profile.GameName ?? string.Empty).Trim();
        profile.GameExe = NormalizeRelativePath(profile.GameExe, "Oyun EXE");
        profile.TargetPath = NormalizeRelativePath(
            profile.TargetPath,
            "Hedef klasör",
            allowEmpty: true);
        profile.InstallMode = (profile.InstallMode ?? string.Empty).Trim().ToLowerInvariant();
        profile.BootstrapType = string.IsNullOrWhiteSpace(profile.BootstrapType)
            ? "launcher"
            : profile.BootstrapType.Trim().ToLowerInvariant();

        if (!SlugRegex.IsMatch(profile.GameSlug))
        {
            throw new InvalidDataException("Oyun slug değeri geçersiz.");
        }

        if (string.IsNullOrWhiteSpace(profile.GameName))
        {
            throw new InvalidDataException("Oyun adı boş olamaz.");
        }

        string[] modes =
        {
            "overlay_flat",
            "overlay_tree",
            "replace_files",
            "archive_replace",
            "custom"
        };

        if (!modes.Contains(profile.InstallMode, StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Kurulum modu geçersiz.");
        }

        profile.AllowedExtensions = (profile.AllowedExtensions ?? Array.Empty<string>())
            .Select(NormalizeExtension)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (profile.AllowedExtensions.Length == 0)
        {
            throw new InvalidDataException("En az bir dosya uzantısı girilmelidir.");
        }
    }

    public static string NormalizeExtension(string value)
    {
        string extension = (value ?? string.Empty).Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(extension))
        {
            return string.Empty;
        }

        if (!extension.StartsWith('.'))
        {
            extension = "." + extension;
        }

        if (!Regex.IsMatch(extension, "^\\.[a-z0-9]+$"))
        {
            throw new InvalidDataException("Geçersiz dosya uzantısı: " + value);
        }

        return extension;
    }

    public static string NormalizeRelativePath(
        string? value,
        string fieldName,
        bool allowEmpty = false)
    {
        string result = (value ?? string.Empty).Trim().Replace('\\', '/').Trim('/');

        if (string.IsNullOrWhiteSpace(result))
        {
            if (allowEmpty)
            {
                return string.Empty;
            }

            throw new InvalidDataException(fieldName + " boş olamaz.");
        }

        if (Path.IsPathRooted(result) ||
            result.Split('/', StringSplitOptions.RemoveEmptyEntries)
                .Any(segment => segment is "." or ".."))
        {
            throw new InvalidDataException(fieldName + " güvenli göreli yol olmalıdır.");
        }

        return result;
    }

    public static string ShellQuote(string value)
    {
        return "'" + (value ?? string.Empty).Replace("'", "'\\''") + "'";
    }

    public static string SqlEscape(string value)
    {
        return (value ?? string.Empty).Replace("'", "''");
    }
}

internal sealed class ProfileService
{
    public GameProfile Load(string path)
    {
        GameProfile profile = JsonSerializer.Deserialize<GameProfile>(
                                  File.ReadAllText(path, Encoding.UTF8),
                                  JsonDefaults.Options)
                              ?? throw new InvalidDataException("Profil dosyası boş.");
        StudioValidation.ValidateProfile(profile);
        return profile;
    }

    public void Save(string path, GameProfile profile)
    {
        StudioValidation.ValidateProfile(profile);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(
            path,
            JsonSerializer.Serialize(profile, JsonDefaults.Options),
            new UTF8Encoding(false));
    }
}

internal sealed class PackageBuilderService
{
    private readonly StudioLogger _logger;

    public PackageBuilderService(StudioLogger logger)
    {
        _logger = logger;
    }

    public async Task<PackageBuildResult> BuildAsync(
        GameProfile profile,
        string sourcePath,
        string outputDirectory,
        string version,
        string channel,
        IProgress<int>? progress,
        CancellationToken cancellationToken)
    {
        return await Task.Run(() =>
        {
            StudioValidation.ValidateProfile(profile);

            version = (version ?? string.Empty).Trim();
            channel = string.IsNullOrWhiteSpace(channel)
                ? "stable"
                : channel.Trim().ToLowerInvariant();

            if (string.IsNullOrWhiteSpace(version))
            {
                throw new InvalidDataException("Yama sürümü boş olamaz.");
            }

            if (!Regex.IsMatch(channel, "^[a-z0-9._-]{1,32}$"))
            {
                throw new InvalidDataException("Yayın kanalı geçersiz.");
            }

            sourcePath = Path.GetFullPath(sourcePath);
            outputDirectory = Path.GetFullPath(outputDirectory);
            Directory.CreateDirectory(outputDirectory);

            string temporaryRoot = Path.Combine(
                Path.GetTempPath(),
                "UCREW_STUDIO_PACK_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(temporaryRoot);

            try
            {
                progress?.Report(5);
                string sourceZip = PrepareSourceZip(
                    profile,
                    sourcePath,
                    temporaryRoot,
                    cancellationToken);
                int entryCount = ValidateZip(profile, sourceZip, cancellationToken);

                progress?.Report(25);

                string safeVersion = Regex.Replace(version, "[^A-Za-z0-9._-]+", "_").Trim('_');
                if (string.IsNullOrWhiteSpace(safeVersion))
                {
                    safeVersion = "1.0.0";
                }

                string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string encryptedFile =
                    $"{profile.GameSlug}_{channel}_v{safeVersion}_{stamp}.ucp";
                string encryptedPath = Path.Combine(outputDirectory, encryptedFile);
                string metadataPath = Path.Combine(
                    outputDirectory,
                    $"{profile.GameSlug}_{channel}_metadata.json");
                string sqlPath = Path.Combine(
                    outputDirectory,
                    $"{profile.GameSlug}_{channel}_REGISTER.sql");
                string profileCopyPath = Path.Combine(
                    outputDirectory,
                    $"{profile.GameSlug}_profile.json");

                byte[] key = RandomNumberGenerator.GetBytes(32);
                byte[] iv = RandomNumberGenerator.GetBytes(16);

                EncryptZip(sourceZip, encryptedPath, key, iv, cancellationToken);
                progress?.Report(65);

                string sha256 = ComputeSha256(encryptedPath);
                long fileSize = new FileInfo(encryptedPath).Length;
                string serverPath = $"secure_patches/{profile.GameSlug}/{encryptedFile}";

                var metadata = new PatchMetadata
                {
                    GameSlug = profile.GameSlug,
                    GameName = profile.GameName,
                    Version = version,
                    Channel = channel,
                    EncryptedFile = encryptedFile,
                    ServerRelativePath = serverPath,
                    SourceEntries = entryCount,
                    FileSize = fileSize,
                    Sha256 = sha256,
                    KeyBase64 = Convert.ToBase64String(key),
                    IvBase64 = Convert.ToBase64String(iv),
                    RuntimeProfile = profile,
                    CreatedAt = DateTimeOffset.UtcNow
                };

                File.WriteAllText(
                    metadataPath,
                    JsonSerializer.Serialize(metadata, JsonDefaults.Options),
                    new UTF8Encoding(false));
                File.WriteAllText(
                    profileCopyPath,
                    JsonSerializer.Serialize(profile, JsonDefaults.Options),
                    new UTF8Encoding(false));
                File.WriteAllText(
                    sqlPath,
                    BuildSql(metadata),
                    new UTF8Encoding(false));

                progress?.Report(100);
                _logger.Write(
                    $"Paket hazırlandı: {encryptedFile}, {entryCount} dosya, SHA-256={sha256}");

                return new PackageBuildResult(
                    encryptedPath,
                    metadataPath,
                    sqlPath,
                    profileCopyPath,
                    metadata);
            }
            finally
            {
                TryDeleteDirectory(temporaryRoot);
            }
        }, cancellationToken).ConfigureAwait(false);
    }

    private static string PrepareSourceZip(
        GameProfile profile,
        string sourcePath,
        string temporaryRoot,
        CancellationToken cancellationToken)
    {
        if (File.Exists(sourcePath))
        {
            if (!string.Equals(
                    Path.GetExtension(sourcePath),
                    ".zip",
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("Kaynak dosya ZIP olmalıdır.");
            }

            return sourcePath;
        }

        if (!Directory.Exists(sourcePath))
        {
            throw new DirectoryNotFoundException("Yama kaynağı bulunamadı: " + sourcePath);
        }

        HashSet<string> allowed = profile.AllowedExtensions
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        FileInfo[] files = new DirectoryInfo(sourcePath)
            .EnumerateFiles("*", SearchOption.AllDirectories)
            .ToArray();

        if (files.Length == 0)
        {
            throw new InvalidDataException("Seçilen yama klasörü boş.");
        }

        foreach (FileInfo file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string extension = StudioValidation.NormalizeExtension(file.Extension);
            if (!allowed.Contains(extension))
            {
                throw new InvalidDataException(
                    "Profilin izin vermediği dosya bulundu: " + file.FullName);
            }
        }

        string zipPath = Path.Combine(temporaryRoot, "source.zip");
        ZipFile.CreateFromDirectory(
            sourcePath,
            zipPath,
            CompressionLevel.Optimal,
            includeBaseDirectory: false);
        return zipPath;
    }

    private static int ValidateZip(
        GameProfile profile,
        string zipPath,
        CancellationToken cancellationToken)
    {
        HashSet<string> allowed = profile.AllowedExtensions
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var flatNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        using ZipArchive archive = ZipFile.OpenRead(zipPath);
        ZipArchiveEntry[] entries = archive.Entries
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Name))
            .ToArray();

        if (entries.Length == 0)
        {
            throw new InvalidDataException("Yama ZIP paketi boş.");
        }

        foreach (ZipArchiveEntry entry in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string normalized = entry.FullName.Replace('\\', '/');
            string[] segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);

            if (normalized.StartsWith('/') || segments.Any(segment => segment is "." or ".."))
            {
                throw new InvalidDataException("ZIP içinde güvenli olmayan yol: " + normalized);
            }

            string extension = StudioValidation.NormalizeExtension(Path.GetExtension(entry.Name));
            if (!allowed.Contains(extension))
            {
                throw new InvalidDataException(
                    "Profilin izin vermediği dosya türü: " + normalized);
            }

            if (!profile.PreserveDirectoryTree ||
                string.Equals(profile.InstallMode, "overlay_flat", StringComparison.OrdinalIgnoreCase))
            {
                if (!flatNames.Add(entry.Name))
                {
                    throw new InvalidDataException(
                        "Düz kurulumda aynı isimli birden fazla dosya var: " + entry.Name);
                }
            }
        }

        return entries.Length;
    }

    private static void EncryptZip(
        string sourceZip,
        string destination,
        byte[] key,
        byte[] iv,
        CancellationToken cancellationToken)
    {
        using Aes aes = Aes.Create();
        aes.KeySize = 256;
        aes.BlockSize = 128;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;
        aes.Key = key;
        aes.IV = iv;

        using FileStream input = File.OpenRead(sourceZip);
        using FileStream output = File.Create(destination);
        using var crypto = new CryptoStream(
            output,
            aes.CreateEncryptor(),
            CryptoStreamMode.Write);

        byte[] buffer = new byte[1024 * 128];
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int read = input.Read(buffer, 0, buffer.Length);
            if (read <= 0)
            {
                break;
            }

            crypto.Write(buffer, 0, read);
        }

        crypto.FlushFinalBlock();
    }

    private static string ComputeSha256(string path)
    {
        using SHA256 sha = SHA256.Create();
        using FileStream stream = File.OpenRead(path);
        return Convert.ToHexString(sha.ComputeHash(stream)).ToLowerInvariant();
    }

    private static string BuildSql(PatchMetadata metadata)
    {
        string profileJson = JsonSerializer.Serialize(
            metadata.RuntimeProfile,
            new JsonSerializerOptions { PropertyNamingPolicy = null });
        string slug = StudioValidation.SqlEscape(metadata.GameSlug);
        string channel = StudioValidation.SqlEscape(metadata.Channel);

        return $"""
-- U-CREW genel güvenli yama kaydı
-- Oyun: {metadata.GameName}
-- Sürüm: {metadata.Version}
-- Paket: {metadata.ServerRelativePath}

SET @ucrew_game_id := (
    SELECT id FROM games WHERE LOWER(slug)=LOWER('{slug}') LIMIT 1
);

SELECT @ucrew_game_id AS game_id;

UPDATE secure_patch_files
SET status='archived', updated_at=NOW()
WHERE game_id=@ucrew_game_id
  AND channel='{channel}'
  AND status='active';

INSERT INTO secure_patch_files
(game_id, version, channel, package_format,
 file_path, file_name, file_size, sha256,
 key_base64, iv_base64, runtime_profile_json,
 status, created_at, updated_at)
SELECT
    @ucrew_game_id,
    '{StudioValidation.SqlEscape(metadata.Version)}',
    '{channel}',
    'zip-aes256-cbc-v1',
    '{StudioValidation.SqlEscape(metadata.ServerRelativePath)}',
    '{StudioValidation.SqlEscape(metadata.EncryptedFile)}',
    {metadata.FileSize},
    '{metadata.Sha256}',
    '{StudioValidation.SqlEscape(metadata.KeyBase64)}',
    '{StudioValidation.SqlEscape(metadata.IvBase64)}',
    '{StudioValidation.SqlEscape(profileJson)}',
    'active',
    NOW(),
    NOW()
WHERE @ucrew_game_id IS NOT NULL;

SELECT id, game_id, version, channel, file_name, file_size, sha256, status
FROM secure_patch_files
WHERE game_id=@ucrew_game_id
ORDER BY id DESC
LIMIT 5;
""";
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
        }
    }
}

internal sealed class SshSessionFactory
{
    public PasswordConnectionInfo Create(
        StudioSettings settings,
        string sshPassword)
    {
        StudioValidation.ValidateServerSettings(settings);

        var connection = new PasswordConnectionInfo(
            settings.Host,
            settings.Port,
            settings.SshUser,
            sshPassword)
        {
            Timeout = TimeSpan.FromSeconds(25)
        };

        return connection;
    }
}

internal sealed class ServerDeploymentService
{
    private readonly StudioLogger _logger;
    private readonly SshSessionFactory _factory = new();

    public ServerDeploymentService(StudioLogger logger)
    {
        _logger = logger;
    }

    public async Task<OperationResult> DeployAsync(
        StudioSettings settings,
        string sshPassword,
        string databasePassword,
        IProgress<int>? progress,
        CancellationToken cancellationToken)
    {
        return await Task.Run(() =>
        {
            StudioValidation.ValidateServerSettings(settings);

            if (string.IsNullOrEmpty(sshPassword))
            {
                throw new InvalidDataException("SSH root şifresi boş olamaz.");
            }

            string localRoot = ExtractServerPayload();
            string remoteRoot = "/root/ucrew_studio_deploy_" +
                                DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");

            try
            {
                using PasswordConnectionInfo connection = _factory.Create(settings, sshPassword);
                using var ssh = new SshClient(connection);
                using var sftp = new SftpClient(connection);

                ssh.Connect();
                sftp.Connect();
                _logger.Write("VPS bağlantısı kuruldu: " + settings.Host);
                progress?.Report(10);

                Execute(ssh, $"mkdir -p {StudioValidation.ShellQuote(remoteRoot + "/api")} " +
                             $"{StudioValidation.ShellQuote(remoteRoot + "/database")}");

                Upload(sftp, Path.Combine(localRoot, "INSTALL_VPS.sh"), remoteRoot + "/INSTALL_VPS.sh");
                Upload(sftp, Path.Combine(localRoot, "api", "secure_patch_request.php"), remoteRoot + "/api/secure_patch_request.php");
                Upload(sftp, Path.Combine(localRoot, "api", "secure_patch_download.php"), remoteRoot + "/api/secure_patch_download.php");
                Upload(sftp, Path.Combine(localRoot, "database", "INSTALL_SCHEMA.sql"), remoteRoot + "/database/INSTALL_SCHEMA.sql");
                progress?.Report(35);

                string installOutput = Execute(
                    ssh,
                    $"chmod +x {StudioValidation.ShellQuote(remoteRoot + "/INSTALL_VPS.sh")} && " +
                    $"bash {StudioValidation.ShellQuote(remoteRoot + "/INSTALL_VPS.sh")}");
                _logger.Write(installOutput.Trim());
                progress?.Report(60);

                string mysqlCommand = BuildMySqlImportCommand(
                    settings,
                    databasePassword,
                    remoteRoot + "/database/INSTALL_SCHEMA.sql");
                string mysqlOutput = Execute(ssh, mysqlCommand);
                _logger.Write(mysqlOutput.Trim());
                progress?.Report(82);

                string apiOutput = Execute(
                    ssh,
                    "curl -sS -X POST https://api.u-crew.net/api/secure_patch_request.php " +
                    "-d slug=guardians -d hwid=test");
                _logger.Write("API testi: " + apiOutput.Trim());
                progress?.Report(100);

                if (!apiOutput.Contains("status", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        "API testi JSON yanıtı vermedi: " + apiOutput);
                }

                return new OperationResult(
                    true,
                    "Genel güvenli yama sunucusu başarıyla kuruldu.",
                    apiOutput.Trim());
            }
            finally
            {
                TryDeleteDirectory(localRoot);
            }
        }, cancellationToken).ConfigureAwait(false);
    }

    private static string ExtractServerPayload()
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            "UCREW_STUDIO_SERVER_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "api"));
        Directory.CreateDirectory(Path.Combine(root, "database"));

        var resources = new Dictionary<string, string>
        {
            ["ServerPayload.api.secure_patch_request.php"] = Path.Combine(root, "api", "secure_patch_request.php"),
            ["ServerPayload.api.secure_patch_download.php"] = Path.Combine(root, "api", "secure_patch_download.php"),
            ["ServerPayload.database.INSTALL_SCHEMA.sql"] = Path.Combine(root, "database", "INSTALL_SCHEMA.sql"),
            ["ServerPayload.INSTALL_VPS.sh"] = Path.Combine(root, "INSTALL_VPS.sh")
        };

        Assembly assembly = Assembly.GetExecutingAssembly();
        foreach ((string resourceName, string destination) in resources)
        {
            using Stream stream = assembly.GetManifestResourceStream(resourceName)
                                  ?? throw new InvalidOperationException(
                                      "Gömülü sunucu dosyası bulunamadı: " + resourceName);
            using FileStream output = File.Create(destination);
            stream.CopyTo(output);
        }

        return root;
    }

    private static string BuildMySqlImportCommand(
        StudioSettings settings,
        string databasePassword,
        string sqlPath)
    {
        string user = StudioValidation.ShellQuote(settings.DatabaseUser);
        string database = StudioValidation.ShellQuote(settings.DatabaseName);
        string path = StudioValidation.ShellQuote(sqlPath);

        if (string.IsNullOrEmpty(databasePassword))
        {
            return $"mysql -u {user} {database} < {path}";
        }

        return $"MYSQL_PWD={StudioValidation.ShellQuote(databasePassword)} " +
               $"mysql -u {user} {database} < {path}";
    }

    private static void Upload(SftpClient sftp, string localPath, string remotePath)
    {
        using FileStream stream = File.OpenRead(localPath);
        sftp.UploadFile(stream, remotePath, canOverride: true);
    }

    private static string Execute(SshClient ssh, string command)
    {
        using SshCommand result = ssh.RunCommand(command);
        string output = (result.Result ?? string.Empty) + (result.Error ?? string.Empty);

        if (result.ExitStatus != 0)
        {
            throw new InvalidOperationException(
                $"Uzak komut başarısız ({result.ExitStatus}): {output.Trim()}");
        }

        return output;
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
        }
    }
}

internal sealed class PatchPublisherService
{
    private readonly StudioLogger _logger;
    private readonly SshSessionFactory _factory = new();

    public PatchPublisherService(StudioLogger logger)
    {
        _logger = logger;
    }

    public async Task<OperationResult> PublishAsync(
        StudioSettings settings,
        string sshPassword,
        string databasePassword,
        string metadataPath,
        IProgress<int>? progress,
        CancellationToken cancellationToken)
    {
        return await Task.Run(() =>
        {
            StudioValidation.ValidateServerSettings(settings);

            PatchMetadata metadata = JsonSerializer.Deserialize<PatchMetadata>(
                                         File.ReadAllText(metadataPath, Encoding.UTF8),
                                         JsonDefaults.Options)
                                     ?? throw new InvalidDataException("Metadata dosyası boş.");
            StudioValidation.ValidateProfile(metadata.RuntimeProfile);

            string directory = Path.GetDirectoryName(Path.GetFullPath(metadataPath))!;
            string packagePath = Path.Combine(directory, metadata.EncryptedFile);
            string sqlPath = Path.Combine(
                directory,
                $"{metadata.GameSlug}_{metadata.Channel}_REGISTER.sql");

            if (!File.Exists(packagePath))
            {
                throw new FileNotFoundException("Şifreli .ucp paketi bulunamadı.", packagePath);
            }

            if (!File.Exists(sqlPath))
            {
                throw new FileNotFoundException("REGISTER.sql dosyası bulunamadı.", sqlPath);
            }

            string actualSha = ComputeSha256(packagePath);
            if (!string.Equals(actualSha, metadata.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    "Yerel .ucp SHA-256 değeri metadata ile eşleşmiyor.");
            }

            using PasswordConnectionInfo connection = _factory.Create(settings, sshPassword);
            using var ssh = new SshClient(connection);
            using var sftp = new SftpClient(connection);
            ssh.Connect();
            sftp.Connect();
            progress?.Report(10);

            string remoteDirectory =
                "/var/www/api.u-crew.net/secure_patches/" + metadata.GameSlug;
            string remotePackage = remoteDirectory + "/" + metadata.EncryptedFile;
            string remoteSql = "/root/ucrew_register_" + metadata.GameSlug + "_" +
                               DateTime.UtcNow.ToString("yyyyMMdd_HHmmss") + ".sql";

            Execute(ssh,
                $"install -d -o www-data -g www-data -m 0750 " +
                StudioValidation.ShellQuote(remoteDirectory));
            progress?.Report(20);

            Upload(sftp, packagePath, remotePackage);
            progress?.Report(55);

            string remoteSha = Execute(
                    ssh,
                    $"sha256sum {StudioValidation.ShellQuote(remotePackage)} | awk '{{print $1}}'")
                .Trim()
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .LastOrDefault()?
                .Trim() ?? string.Empty;

            if (!string.Equals(remoteSha, metadata.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    "Sunucuya yüklenen paketin SHA-256 doğrulaması başarısız.");
            }

            Execute(ssh,
                $"chown www-data:www-data {StudioValidation.ShellQuote(remotePackage)} && " +
                $"chmod 0640 {StudioValidation.ShellQuote(remotePackage)}");
            progress?.Report(70);

            Upload(sftp, sqlPath, remoteSql);
            string mysqlCommand = BuildMySqlImportCommand(
                settings,
                databasePassword,
                remoteSql) +
                $"; rm -f {StudioValidation.ShellQuote(remoteSql)}";
            string sqlOutput = Execute(ssh, mysqlCommand);
            _logger.Write(sqlOutput.Trim());
            progress?.Report(92);

            string query =
                "SELECT g.slug,f.version,f.channel,f.file_name,f.sha256,f.status " +
                "FROM secure_patch_files f INNER JOIN games g ON g.id=f.game_id " +
                $"WHERE LOWER(g.slug)=LOWER('{StudioValidation.SqlEscape(metadata.GameSlug)}') " +
                "ORDER BY f.id DESC LIMIT 3;";
            string verifyCommand = BuildMySqlQueryCommand(
                settings,
                databasePassword,
                query);
            string verifyOutput = Execute(ssh, verifyCommand);
            _logger.Write("Yayın doğrulaması: " + verifyOutput.Trim());
            progress?.Report(100);

            return new OperationResult(
                true,
                $"{metadata.GameName} {metadata.Version} başarıyla yayınlandı.",
                verifyOutput.Trim());
        }, cancellationToken).ConfigureAwait(false);
    }

    private static string BuildMySqlImportCommand(
        StudioSettings settings,
        string databasePassword,
        string sqlPath)
    {
        string prefix = string.IsNullOrEmpty(databasePassword)
            ? string.Empty
            : "MYSQL_PWD=" + StudioValidation.ShellQuote(databasePassword) + " ";

        return prefix +
               "mysql -u " + StudioValidation.ShellQuote(settings.DatabaseUser) + " " +
               StudioValidation.ShellQuote(settings.DatabaseName) + " < " +
               StudioValidation.ShellQuote(sqlPath);
    }

    private static string BuildMySqlQueryCommand(
        StudioSettings settings,
        string databasePassword,
        string query)
    {
        string prefix = string.IsNullOrEmpty(databasePassword)
            ? string.Empty
            : "MYSQL_PWD=" + StudioValidation.ShellQuote(databasePassword) + " ";

        return prefix +
               "mysql -N -B -u " + StudioValidation.ShellQuote(settings.DatabaseUser) + " " +
               StudioValidation.ShellQuote(settings.DatabaseName) + " -e " +
               StudioValidation.ShellQuote(query);
    }

    private static string ComputeSha256(string path)
    {
        using SHA256 sha = SHA256.Create();
        using FileStream stream = File.OpenRead(path);
        return Convert.ToHexString(sha.ComputeHash(stream)).ToLowerInvariant();
    }

    private static void Upload(SftpClient sftp, string localPath, string remotePath)
    {
        using FileStream stream = File.OpenRead(localPath);
        sftp.UploadFile(stream, remotePath, canOverride: true);
    }

    private static string Execute(SshClient ssh, string command)
    {
        using SshCommand result = ssh.RunCommand(command);
        string output = (result.Result ?? string.Empty) + (result.Error ?? string.Empty);

        if (result.ExitStatus != 0)
        {
            throw new InvalidOperationException(
                $"Uzak komut başarısız ({result.ExitStatus}): {output.Trim()}");
        }

        return output;
    }
}
