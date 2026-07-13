using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace UCREW.SecurePatch.Manager;

internal static class PackageBuilder
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = null
    };

    public static PackageResult Build(
        GameProfile profile,
        string sourcePath,
        string outputDirectory,
        string version,
        string channel,
        Action<string> log)
    {
        ValidateProfile(profile);
        Directory.CreateDirectory(outputDirectory);

        string tempRoot = Path.Combine(Path.GetTempPath(), "UCREW_Manager_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        try
        {
            string sourceZip = PrepareSourceZip(profile, sourcePath, tempRoot, log);
            int entryCount = ValidateZip(profile, sourceZip);

            string safeVersion = Sanitize(version, "1.0.0");
            string safeChannel = Sanitize(channel, "stable");
            string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string fileName = $"{profile.GameSlug}_{safeChannel}_v{safeVersion}_{stamp}.ucp";
            string encryptedPath = Path.Combine(outputDirectory, fileName);
            string metadataPath = Path.Combine(outputDirectory, $"{profile.GameSlug}_{safeChannel}_metadata.json");
            string sqlPath = Path.Combine(outputDirectory, $"{profile.GameSlug}_{safeChannel}_REGISTER.sql");
            string profilePath = Path.Combine(outputDirectory, $"{profile.GameSlug}_profile.json");

            byte[] key = RandomNumberGenerator.GetBytes(32);
            byte[] iv = RandomNumberGenerator.GetBytes(16);
            Encrypt(sourceZip, encryptedPath, key, iv);

            string sha = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(encryptedPath))).ToLowerInvariant();
            long fileSize = new FileInfo(encryptedPath).Length;
            string serverPath = $"secure_patches/{profile.GameSlug}/{fileName}";
            string runtimeProfileJson = JsonSerializer.Serialize(profile, JsonOptions);

            var metadata = new
            {
                system = "U-CREW Secure Patch",
                package_format = "zip-aes256-cbc-v1",
                game_slug = profile.GameSlug,
                game_name = profile.GameName,
                version,
                channel = safeChannel,
                encrypted_file = fileName,
                server_relative_path = serverPath,
                source_entries = entryCount,
                file_size = fileSize,
                sha256 = sha,
                key_base64 = Convert.ToBase64String(key),
                iv_base64 = Convert.ToBase64String(iv),
                runtime_profile = profile,
                created_at = DateTimeOffset.Now
            };

            File.WriteAllText(metadataPath, JsonSerializer.Serialize(metadata, JsonOptions), new UTF8Encoding(false));
            File.WriteAllText(profilePath, runtimeProfileJson, new UTF8Encoding(false));

            string sql = BuildSql(
                profile.GameSlug,
                version,
                safeChannel,
                serverPath,
                fileName,
                fileSize,
                sha,
                Convert.ToBase64String(key),
                Convert.ToBase64String(iv),
                JsonSerializer.Serialize(profile));
            File.WriteAllText(sqlPath, sql, new UTF8Encoding(false));

            log($"Paket hazırlandı: {fileName}");
            log($"Dosya sayısı: {entryCount} | Boyut: {fileSize} | SHA-256: {sha}");

            return new PackageResult
            {
                MetadataPath = metadataPath,
                EncryptedPackagePath = encryptedPath,
                SqlPath = sqlPath,
                ProfilePath = profilePath,
                Sha256 = sha,
                GameSlug = profile.GameSlug,
                Version = version,
                Channel = safeChannel,
                FileName = fileName
            };
        }
        finally
        {
            try { Directory.Delete(tempRoot, true); } catch { }
        }
    }

    private static string PrepareSourceZip(GameProfile profile, string sourcePath, string tempRoot, Action<string> log)
    {
        if (File.Exists(sourcePath))
        {
            if (!string.Equals(Path.GetExtension(sourcePath), ".zip", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("Kaynak dosya ZIP olmalıdır.");
            }
            return sourcePath;
        }

        if (!Directory.Exists(sourcePath))
        {
            throw new DirectoryNotFoundException("Yama kaynağı bulunamadı: " + sourcePath);
        }

        string zipPath = Path.Combine(tempRoot, "source.zip");
        ZipFile.CreateFromDirectory(sourcePath, zipPath, CompressionLevel.Optimal, includeBaseDirectory: false);
        log("Yama klasörü geçici ZIP paketine dönüştürüldü.");
        return zipPath;
    }

    private static int ValidateZip(GameProfile profile, string zipPath)
    {
        HashSet<string> allowed = profile.AllowedExtensions
            .Select(NormalizeExtension)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        HashSet<string> flatNames = new(StringComparer.OrdinalIgnoreCase);

        using ZipArchive archive = ZipFile.OpenRead(zipPath);
        List<ZipArchiveEntry> entries = archive.Entries
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Name))
            .ToList();

        if (entries.Count == 0)
        {
            throw new InvalidDataException("Yama paketi boş.");
        }

        foreach (ZipArchiveEntry entry in entries)
        {
            string normalized = entry.FullName.Replace('\\', '/');
            if (normalized.StartsWith('/') || normalized.Split('/').Any(part => part is "." or ".."))
            {
                throw new InvalidDataException("ZIP içinde güvenli olmayan yol: " + entry.FullName);
            }

            string ext = NormalizeExtension(Path.GetExtension(entry.Name));
            if (!allowed.Contains(ext))
            {
                throw new InvalidDataException("İzin verilmeyen dosya türü: " + entry.FullName);
            }

            if (!profile.PreserveDirectoryTree)
            {
                if (!flatNames.Add(entry.Name))
                {
                    throw new InvalidDataException("Aynı isimli iki dosya var: " + entry.Name);
                }
            }
        }

        return entries.Count;
    }

    private static void Encrypt(string inputPath, string outputPath, byte[] key, byte[] iv)
    {
        using Aes aes = Aes.Create();
        aes.KeySize = 256;
        aes.BlockSize = 128;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;
        aes.Key = key;
        aes.IV = iv;

        using FileStream input = File.OpenRead(inputPath);
        using FileStream output = File.Create(outputPath);
        using CryptoStream crypto = new(output, aes.CreateEncryptor(), CryptoStreamMode.Write);
        input.CopyTo(crypto);
        crypto.FlushFinalBlock();
    }

    private static void ValidateProfile(GameProfile profile)
    {
        if (string.IsNullOrWhiteSpace(profile.GameSlug) ||
            !System.Text.RegularExpressions.Regex.IsMatch(profile.GameSlug, "^[a-z0-9][a-z0-9._-]{1,99}$"))
        {
            throw new InvalidDataException("Oyun slug değeri geçersiz.");
        }

        if (string.IsNullOrWhiteSpace(profile.GameName) || string.IsNullOrWhiteSpace(profile.GameExe))
        {
            throw new InvalidDataException("Oyun adı ve EXE bilgisi zorunludur.");
        }

        if (profile.AllowedExtensions.Length == 0)
        {
            throw new InvalidDataException("En az bir dosya uzantısı girilmelidir.");
        }
    }

    private static string BuildSql(
        string slug, string version, string channel, string serverPath, string fileName,
        long fileSize, string sha, string key64, string iv64, string profileJson)
    {
        return $"""
SET @ucrew_game_id := (SELECT id FROM games WHERE LOWER(slug)=LOWER('{Escape(slug)}') LIMIT 1);
SELECT @ucrew_game_id AS game_id;
UPDATE secure_patch_files SET status='archived', updated_at=NOW()
WHERE game_id=@ucrew_game_id AND channel='{Escape(channel)}' AND status='active';
INSERT INTO secure_patch_files
(game_id,version,channel,package_format,file_path,file_name,file_size,sha256,key_base64,iv_base64,runtime_profile_json,status,created_at,updated_at)
SELECT @ucrew_game_id,'{Escape(version)}','{Escape(channel)}','zip-aes256-cbc-v1','{Escape(serverPath)}','{Escape(fileName)}',{fileSize},'{Escape(sha)}','{Escape(key64)}','{Escape(iv64)}','{Escape(profileJson)}','active',NOW(),NOW()
WHERE @ucrew_game_id IS NOT NULL;
SELECT id,game_id,version,channel,file_name,file_size,sha256,status FROM secure_patch_files
WHERE game_id=@ucrew_game_id ORDER BY id DESC LIMIT 5;
""";
    }

    private static string Escape(string value) => value.Replace("'", "''");
    private static string NormalizeExtension(string value)
    {
        string result = value.Trim().ToLowerInvariant();
        return result.StartsWith('.') ? result : "." + result;
    }
    private static string Sanitize(string value, string fallback)
    {
        string result = new(value.Where(ch => char.IsLetterOrDigit(ch) || ch is '.' or '_' or '-').ToArray());
        return string.IsNullOrWhiteSpace(result) ? fallback : result;
    }
}
