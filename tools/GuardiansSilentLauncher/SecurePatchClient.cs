using System.Net;
using System.Net.Http.Headers;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace UCREW.GuardiansTR;

internal sealed class SecurePatchPackage : IDisposable
{
    public SecurePatchPackage(string zipPath, string version, bool downloaded)
    {
        ZipPath = zipPath;
        Version = version;
        Downloaded = downloaded;
    }

    public string ZipPath { get; }
    public string Version { get; }
    public bool Downloaded { get; }

    public void Dispose()
    {
        TryDeleteFile(ZipPath);

        string? directory = Path.GetDirectoryName(ZipPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            TryDeleteDirectory(directory);
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return;
            }

            File.SetAttributes(path, FileAttributes.Normal);
            File.Delete(path);
        }
        catch
        {
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (!Directory.Exists(path))
            {
                return;
            }

            foreach (string file in Directory.EnumerateFiles(
                         path,
                         "*",
                         SearchOption.AllDirectories))
            {
                try
                {
                    File.SetAttributes(file, FileAttributes.Normal);
                }
                catch
                {
                }
            }

            Directory.Delete(path, recursive: true);
        }
        catch
        {
        }
    }
}

internal sealed class SecurePatchClient
{
    private const string DefaultApiBase = "https://api.u-crew.net/api/";
    private const string DefaultFallbackApiBase = "https://www.u-crew.net/ucrew_patch_v3/api/";
    private const string DefaultSlug = "guardians";
    private const string VisibleConfigName = "ucrew_guardians_server.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private static readonly SocketsHttpHandler HttpHandler = new()
    {
        AutomaticDecompression =
            DecompressionMethods.GZip |
            DecompressionMethods.Deflate |
            DecompressionMethods.Brotli,
        AllowAutoRedirect = true,
        MaxAutomaticRedirections = 5,
        ConnectTimeout = TimeSpan.FromSeconds(20),
        PooledConnectionIdleTimeout = TimeSpan.FromSeconds(20),
        PooledConnectionLifetime = TimeSpan.FromMinutes(2),
        MaxConnectionsPerServer = 2,
        SslOptions = new System.Net.Security.SslClientAuthenticationOptions
        {
            EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13
        }
    };

    private static readonly HttpClient Http = new(HttpHandler)
    {
        Timeout = Timeout.InfiniteTimeSpan,
        DefaultRequestVersion = HttpVersion.Version11,
        DefaultVersionPolicy = HttpVersionPolicy.RequestVersionExact
    };

    private readonly string _gameRoot;
    private readonly string _privateRoot;
    private readonly string _cacheRoot;
    private readonly string _configPath;
    private readonly Action<string> _log;

    public SecurePatchClient(
        string gameRoot,
        string privateRoot,
        Action<string> log)
    {
        _gameRoot = gameRoot;
        _privateRoot = privateRoot;
        _cacheRoot = Path.Combine(privateRoot, "secure_cache");
        _configPath = Path.Combine(privateRoot, "server.json");
        _log = log;

        Directory.CreateDirectory(_cacheRoot);
        SetHiddenSystem(_cacheRoot);
    }

    public async Task<SecurePatchPackage> AcquireAsync(
        IProgress<LauncherProgress> progress,
        CancellationToken cancellationToken)
    {
        ServerPatchConfig config = LoadConfig();

        if (!config.Enabled)
        {
            throw new InvalidOperationException(
                "Sunucu yama sistemi yapılandırmada kapalı.");
        }

        string token = ReadRememberedLauncherToken();
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new InvalidOperationException(
                "U-CREW oturumu bulunamadı. U-CREW Launcher'a bir kez giriş yapıp " +
                "'Beni Hatırla' seçeneğini etkinleştir.");
        }

        progress.Report(new LauncherProgress(
            6,
            "Sunucuya bağlanılıyor…",
            "U-CREW hesabı ve Guardians lisansı doğrulanıyor."));

        TicketData ticket = await RequestTicketAsync(
            config,
            token,
            cancellationToken).ConfigureAwait(false);

        ValidateTicket(ticket);

        string encryptedCachePath = Path.Combine(
            _cacheRoot,
            "guardians_patch.ucrew");

        bool useCache = await IsValidCachedFileAsync(
            encryptedCachePath,
            ticket.Sha256,
            cancellationToken).ConfigureAwait(false);

        bool downloaded = false;

        if (useCache)
        {
            progress.Report(new LauncherProgress(
                52,
                "Güncel yama hazır.",
                "Şifreli yama önbelleği doğrulandı."));

            _log("Sunucudaki paket ile gizli önbellek eşleşti; tekrar indirilmedi.");
        }
        else
        {
            downloaded = true;

            await DownloadEncryptedPatchAsync(
                ticket.DownloadUrl,
                encryptedCachePath,
                progress,
                cancellationToken).ConfigureAwait(false);
        }

        progress.Report(new LauncherProgress(
            59,
            "Dosyalar doğrulanıyor…",
            "Şifreli paketin SHA-256 özeti kontrol ediliyor."));

        string actualSha = await ComputeSha256Async(
            encryptedCachePath,
            cancellationToken).ConfigureAwait(false);

        if (!string.Equals(
                actualSha,
                NormalizeSha(ticket.Sha256),
                StringComparison.OrdinalIgnoreCase))
        {
            TryDeleteFile(encryptedCachePath);
            throw new InvalidDataException(
                "Şifreli yama paketinin SHA-256 doğrulaması başarısız. " +
                "Dosya silindi; sonraki açılışta yeniden indirilecek.");
        }

        progress.Report(new LauncherProgress(
            66,
            "Yama açılıyor…",
            "Şifreli paket güvenli çalışma alanında çözülüyor."));

        string runtimeRoot = Path.Combine(
            _privateRoot,
            "runtime_secure",
            Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(runtimeRoot);
        SetHiddenSystem(Path.GetDirectoryName(runtimeRoot)!);
        SetHiddenSystem(runtimeRoot);

        string zipPath = Path.Combine(runtimeRoot, "patch.zip");

        try
        {
            await Task.Run(
                () => DecryptFile(
                    encryptedCachePath,
                    zipPath,
                    ticket.KeyBase64,
                    ticket.IvBase64),
                cancellationToken).ConfigureAwait(false);

            SetHiddenSystem(zipPath);

            using (System.IO.Compression.ZipArchive validationArchive =
                   System.IO.Compression.ZipFile.OpenRead(zipPath))
            {
                if (validationArchive.Entries.Count == 0)
                {
                    throw new InvalidDataException(
                        "Çözülen yama paketi boş.");
                }
            }

            SaveCacheMetadata(ticket, encryptedCachePath);
            RemoveLegacyLocalPackageAfterServerSuccess();

            return new SecurePatchPackage(
                zipPath,
                string.IsNullOrWhiteSpace(ticket.Version)
                    ? "güncel"
                    : ticket.Version,
                downloaded);
        }
        catch
        {
            TryDeleteDirectory(runtimeRoot);
            throw;
        }
    }

    private ServerPatchConfig LoadConfig()
    {
        Directory.CreateDirectory(_privateRoot);

        string visibleConfigPath = Path.Combine(_gameRoot, VisibleConfigName);
        if (File.Exists(visibleConfigPath))
        {
            RemoveRestrictiveAttributes(visibleConfigPath);
            RemoveRestrictiveAttributes(_configPath);
            File.Copy(visibleConfigPath, _configPath, overwrite: true);
            File.Delete(visibleConfigPath);
            SetHiddenSystem(_configPath);
        }

        if (!File.Exists(_configPath))
        {
            var defaultConfig = new ServerPatchConfig();
            File.WriteAllText(
                _configPath,
                JsonSerializer.Serialize(defaultConfig, JsonOptions),
                new UTF8Encoding(false));
            SetHiddenSystem(_configPath);
            SetHiddenSystem(_privateRoot);
            return defaultConfig;
        }

        try
        {
            RemoveRestrictiveAttributes(_configPath);
            string json = File.ReadAllText(_configPath, Encoding.UTF8);
            ServerPatchConfig config =
                JsonSerializer.Deserialize<ServerPatchConfig>(json, JsonOptions)
                ?? new ServerPatchConfig();

            config.ApiBase = NormalizeApiBase(config.ApiBase, DefaultApiBase);
            config.FallbackApiBase = NormalizeApiBase(
                config.FallbackApiBase,
                DefaultFallbackApiBase);
            config.Slug = string.IsNullOrWhiteSpace(config.Slug)
                ? DefaultSlug
                : config.Slug.Trim();

            SetHiddenSystem(_configPath);
            SetHiddenSystem(_privateRoot);
            return config;
        }
        catch (Exception exception)
        {
            SetHiddenSystem(_configPath);
            throw new InvalidDataException(
                "Gizli sunucu yapılandırması okunamadı: " +
                exception.Message,
                exception);
        }
    }

    private async Task<TicketData> RequestTicketAsync(
        ServerPatchConfig config,
        string token,
        CancellationToken cancellationToken)
    {
        var bases = new[]
        {
            config.ApiBase,
            config.FallbackApiBase
        }
        .Where(value => !string.IsNullOrWhiteSpace(value))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();

        Exception? lastException = null;

        foreach (string apiBase in bases)
        {
            for (int attempt = 1; attempt <= 3; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    TicketData ticket = await RequestTicketOnceAsync(
                        apiBase,
                        config.Slug,
                        token,
                        cancellationToken).ConfigureAwait(false);

                    _log(
                        $"Güvenli yama bileti alındı. API={apiBase} " +
                        $"Sürüm={ticket.Version} SHA={ticket.Sha256}");

                    return ticket;
                }
                catch (Exception exception)
                {
                    lastException = exception;
                    _log(
                        $"Güvenli yama bileti başarısız. API={apiBase} " +
                        $"Deneme={attempt}/3 Hata={exception.Message}");

                    if (attempt < 3)
                    {
                        await Task.Delay(
                            TimeSpan.FromMilliseconds(650 * attempt),
                            cancellationToken).ConfigureAwait(false);
                    }
                }
            }
        }

        throw new InvalidOperationException(
            "U-CREW güvenli yama sunucusuna bağlanılamadı. " +
            (lastException?.Message ?? "Sunucudan yanıt alınamadı."),
            lastException);
    }

    private static async Task<TicketData> RequestTicketOnceAsync(
        string apiBase,
        string slug,
        string token,
        CancellationToken cancellationToken)
    {
        string endpoint = NormalizeApiBase(apiBase, DefaultApiBase) +
            "secure_patch_request.php";

        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Version = HttpVersion.Version11,
            VersionPolicy = HttpVersionPolicy.RequestVersionExact
        };

        request.Headers.Authorization =
            new AuthenticationHeaderValue("Bearer", token);
        request.Headers.TryAddWithoutValidation(
            "User-Agent",
            "UCREW-Guardians-Secure/1.0");
        request.Headers.TryAddWithoutValidation("Accept", "application/json");
        request.Headers.TryAddWithoutValidation("Connection", "close");

        request.Content = new FormUrlEncodedContent(
            new Dictionary<string, string>
            {
                ["token"] = token,
                ["slug"] = slug,
                ["hwid"] = GetHwid()
            });

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(45));

        using HttpResponseMessage response = await Http.SendAsync(
            request,
            HttpCompletionOption.ResponseContentRead,
            timeout.Token).ConfigureAwait(false);

        string raw = await response.Content.ReadAsStringAsync(
            timeout.Token).ConfigureAwait(false);

        TicketEnvelope envelope = ParseTicket(raw);

        if (!response.IsSuccessStatusCode ||
            !string.Equals(
                envelope.Status,
                "ok",
                StringComparison.OrdinalIgnoreCase) ||
            envelope.Data is null)
        {
            string message = string.IsNullOrWhiteSpace(envelope.Message)
                ? $"Sunucu hatası: HTTP {(int)response.StatusCode}"
                : envelope.Message;

            throw new InvalidOperationException(message);
        }

        TicketData ticket = envelope.Data;
        ticket.DownloadUrl = ResolveDownloadUrl(
            apiBase,
            ticket.DownloadUrl);

        return ticket;
    }

    private static TicketEnvelope ParseTicket(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            throw new InvalidDataException("Sunucudan boş yanıt geldi.");
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(raw);
            JsonElement root = document.RootElement;

            var envelope = new TicketEnvelope
            {
                Status = GetString(root, "status"),
                Message = GetString(root, "message")
            };

            if (root.TryGetProperty("data", out JsonElement data) &&
                data.ValueKind == JsonValueKind.Object)
            {
                envelope.Data = new TicketData
                {
                    DownloadUrl = GetString(
                        data,
                        "download_url",
                        "downloadUrl"),
                    KeyBase64 = GetString(
                        data,
                        "key_base64",
                        "keyBase64"),
                    IvBase64 = GetString(
                        data,
                        "iv_base64",
                        "ivBase64"),
                    Sha256 = GetString(data, "sha256"),
                    Version = GetString(data, "version"),
                    FileSize = GetInt64(
                        data,
                        "file_size",
                        "fileSize")
                };
            }

            return envelope;
        }
        catch (JsonException exception)
        {
            string safeRaw = raw.Length > 700
                ? raw[..700]
                : raw;

            throw new InvalidDataException(
                "Sunucu yanıtı JSON değil: " + safeRaw,
                exception);
        }
    }

    private static void ValidateTicket(TicketData ticket)
    {
        if (string.IsNullOrWhiteSpace(ticket.DownloadUrl))
        {
            throw new InvalidDataException(
                "Sunucu indirme adresi göndermedi.");
        }

        if (string.IsNullOrWhiteSpace(ticket.KeyBase64) ||
            string.IsNullOrWhiteSpace(ticket.IvBase64))
        {
            throw new InvalidDataException(
                "Sunucu şifre çözme anahtarını göndermedi.");
        }

        if (NormalizeSha(ticket.Sha256).Length != 64)
        {
            throw new InvalidDataException(
                "Sunucu geçerli bir SHA-256 özeti göndermedi.");
        }
    }

    private async Task DownloadEncryptedPatchAsync(
        string url,
        string destinationPath,
        IProgress<LauncherProgress> progress,
        CancellationToken cancellationToken)
    {
        string tempPath = destinationPath + ".download";
        TryDeleteFile(tempPath);

        progress.Report(new LauncherProgress(
            12,
            "Türkçe yama indiriliyor…",
            "Şifreli paket sunucudan alınıyor: %0"));

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url)
            {
                Version = HttpVersion.Version11,
                VersionPolicy = HttpVersionPolicy.RequestVersionOrLower
            };

            request.Headers.TryAddWithoutValidation(
                "User-Agent",
                "UCREW-Guardians-Secure/1.0");
            request.Headers.TryAddWithoutValidation("Connection", "close");

            using HttpResponseMessage response = await Http.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);

            response.EnsureSuccessStatusCode();

            long? total = response.Content.Headers.ContentLength;

            await using Stream source = await response.Content.ReadAsStreamAsync(
                cancellationToken).ConfigureAwait(false);
            await using var destination = new FileStream(
                tempPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 1024 * 128,
                useAsync: true);

            byte[] buffer = new byte[1024 * 128];
            long received = 0;
            int lastPercent = -1;

            while (true)
            {
                int read = await source.ReadAsync(
                    buffer.AsMemory(0, buffer.Length),
                    cancellationToken).ConfigureAwait(false);

                if (read <= 0)
                {
                    break;
                }

                await destination.WriteAsync(
                    buffer.AsMemory(0, read),
                    cancellationToken).ConfigureAwait(false);

                received += read;

                int percent = total is > 0
                    ? (int)Math.Clamp(
                        Math.Round(received * 100d / total.Value),
                        0,
                        100)
                    : 0;

                if (percent != lastPercent)
                {
                    lastPercent = percent;
                    int overall = total is > 0
                        ? 12 + (int)Math.Round(percent * 0.40)
                        : 28;

                    string detail = total is > 0
                        ? $"Şifreli paket indiriliyor: %{percent} " +
                          $"({FormatBytes(received)} / {FormatBytes(total.Value)})"
                        : $"Şifreli paket indiriliyor: {FormatBytes(received)}";

                    progress.Report(new LauncherProgress(
                        Math.Clamp(overall, 12, 52),
                        "Türkçe yama indiriliyor…",
                        detail));
                }
            }

            await destination.FlushAsync(cancellationToken).ConfigureAwait(false);

            if (received <= 0)
            {
                throw new InvalidDataException(
                    "Sunucudan indirilen yama paketi boş.");
            }

            RemoveRestrictiveAttributes(destinationPath);
            File.Move(tempPath, destinationPath, overwrite: true);
            SetHiddenSystem(destinationPath);
            SetHiddenSystem(_cacheRoot);

            _log(
                $"Şifreli yama indirildi. Boyut={received} URL={url}");
        }
        catch
        {
            TryDeleteFile(tempPath);
            throw;
        }
    }

    private static async Task<bool> IsValidCachedFileAsync(
        string path,
        string expectedSha,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            return false;
        }

        try
        {
            string actual = await ComputeSha256Async(
                path,
                cancellationToken).ConfigureAwait(false);

            return string.Equals(
                actual,
                NormalizeSha(expectedSha),
                StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static async Task<string> ComputeSha256Async(
        string path,
        CancellationToken cancellationToken)
    {
        using SHA256 sha = SHA256.Create();
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 1024 * 128,
            useAsync: true);

        byte[] hash = await sha.ComputeHashAsync(
            stream,
            cancellationToken).ConfigureAwait(false);

        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static void DecryptFile(
        string inputPath,
        string outputPath,
        string keyBase64,
        string ivBase64)
    {
        byte[] key = Convert.FromBase64String(keyBase64);
        byte[] iv = Convert.FromBase64String(ivBase64);

        if (key.Length != 32)
        {
            throw new CryptographicException(
                "Yama anahtarı AES-256 için 32 bayt değil.");
        }

        if (iv.Length != 16)
        {
            throw new CryptographicException(
                "Yama IV değeri 16 bayt değil.");
        }

        using Aes aes = Aes.Create();
        aes.KeySize = 256;
        aes.BlockSize = 128;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;
        aes.Key = key;
        aes.IV = iv;

        using FileStream input = File.OpenRead(inputPath);
        using FileStream output = File.Create(outputPath);
        using var crypto = new CryptoStream(
            input,
            aes.CreateDecryptor(),
            CryptoStreamMode.Read);

        crypto.CopyTo(output);
        output.Flush(flushToDisk: true);
    }

    private string ReadRememberedLauncherToken()
    {
        string roaming = Environment.GetFolderPath(
            Environment.SpecialFolder.ApplicationData);
        string local = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData);

        string[] candidates =
        {
            Path.Combine(roaming, "U-CREW", "Launcher", "settings.json"),
            Path.Combine(local, "U-CREW", "Launcher", "settings.json")
        };

        foreach (string path in candidates.Distinct(
                     StringComparer.OrdinalIgnoreCase))
        {
            if (!File.Exists(path))
            {
                continue;
            }

            try
            {
                using JsonDocument document = JsonDocument.Parse(
                    File.ReadAllText(path, Encoding.UTF8));

                string token = GetString(
                    document.RootElement,
                    "Token",
                    "token");

                if (!string.IsNullOrWhiteSpace(token))
                {
                    _log("U-CREW hatırlanan oturumu bulundu.");
                    return token.Trim();
                }
            }
            catch (Exception exception)
            {
                _log(
                    "U-CREW oturum dosyası okunamadı: " +
                    exception.Message);
            }
        }

        return string.Empty;
    }

    private void SaveCacheMetadata(
        TicketData ticket,
        string encryptedPath)
    {
        string metadataPath = Path.Combine(_cacheRoot, "cache.json");

        var metadata = new
        {
            slug = LoadConfig().Slug,
            version = ticket.Version,
            sha256 = NormalizeSha(ticket.Sha256),
            file_size = new FileInfo(encryptedPath).Length,
            verified_at = DateTimeOffset.UtcNow
        };

        RemoveRestrictiveAttributes(metadataPath);
        File.WriteAllText(
            metadataPath,
            JsonSerializer.Serialize(metadata, JsonOptions),
            new UTF8Encoding(false));
        SetHiddenSystem(metadataPath);
        SetHiddenSystem(_cacheRoot);
    }

    private void RemoveLegacyLocalPackageAfterServerSuccess()
    {
        string visibleZip = Path.Combine(
            _gameRoot,
            "UCREW_Guardians_TR.zip");
        string hiddenZip = Path.Combine(
            _privateRoot,
            "UCREW_Guardians_TR.zip");
        string legacyRoot = Path.Combine(_privateRoot, "legacy");

        Directory.CreateDirectory(legacyRoot);
        SetHiddenSystem(legacyRoot);

        MoveLegacyZip(visibleZip, legacyRoot);
        MoveLegacyZip(hiddenZip, legacyRoot);
    }

    private static void MoveLegacyZip(
        string sourcePath,
        string legacyRoot)
    {
        if (!File.Exists(sourcePath))
        {
            return;
        }

        string destinationPath = Path.Combine(
            legacyRoot,
            "local_patch_backup.zip");

        RemoveRestrictiveAttributes(sourcePath);
        RemoveRestrictiveAttributes(destinationPath);
        File.Move(sourcePath, destinationPath, overwrite: true);
        SetHiddenSystem(destinationPath);
    }

    private static string GetHwid()
    {
        string raw =
            Environment.MachineName + "|" +
            Environment.UserName + "|" +
            Environment.OSVersion.VersionString;

        byte[] bytes = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static string GetString(
        JsonElement element,
        params string[] names)
    {
        foreach (string name in names)
        {
            if (element.ValueKind == JsonValueKind.Object &&
                element.TryGetProperty(name, out JsonElement value))
            {
                if (value.ValueKind == JsonValueKind.String)
                {
                    return value.GetString() ?? string.Empty;
                }

                if (value.ValueKind is JsonValueKind.Number or
                    JsonValueKind.True or
                    JsonValueKind.False)
                {
                    return value.ToString();
                }
            }
        }

        return string.Empty;
    }

    private static long GetInt64(
        JsonElement element,
        params string[] names)
    {
        foreach (string name in names)
        {
            if (!element.TryGetProperty(name, out JsonElement value))
            {
                continue;
            }

            if (value.ValueKind == JsonValueKind.Number &&
                value.TryGetInt64(out long number))
            {
                return number;
            }

            if (value.ValueKind == JsonValueKind.String &&
                long.TryParse(value.GetString(), out number))
            {
                return number;
            }
        }

        return 0;
    }

    private static string ResolveDownloadUrl(
        string apiBase,
        string downloadUrl)
    {
        if (Uri.TryCreate(
                downloadUrl,
                UriKind.Absolute,
                out Uri? absolute))
        {
            return absolute.ToString();
        }

        var baseUri = new Uri(
            NormalizeApiBase(apiBase, DefaultApiBase));
        return new Uri(baseUri, downloadUrl).ToString();
    }

    private static string NormalizeApiBase(
        string? value,
        string fallback)
    {
        string result = string.IsNullOrWhiteSpace(value)
            ? fallback
            : value.Trim();

        return result.TrimEnd('/') + "/";
    }

    private static string NormalizeSha(string? value)
    {
        return (value ?? string.Empty)
            .Trim()
            .Replace("-", string.Empty, StringComparison.Ordinal)
            .ToLowerInvariant();
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB" };
        double value = bytes;
        int unit = 0;

        while (value >= 1024d && unit < units.Length - 1)
        {
            value /= 1024d;
            unit++;
        }

        return $"{value:0.##} {units[unit]}";
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

    private static void TryDeleteFile(string path)
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

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (!Directory.Exists(path))
            {
                return;
            }

            foreach (string file in Directory.EnumerateFiles(
                         path,
                         "*",
                         SearchOption.AllDirectories))
            {
                RemoveRestrictiveAttributes(file);
            }

            RemoveRestrictiveAttributes(path);
            Directory.Delete(path, recursive: true);
        }
        catch
        {
        }
    }

    private sealed class ServerPatchConfig
    {
        public bool Enabled { get; set; } = true;
        public string ApiBase { get; set; } = DefaultApiBase;
        public string FallbackApiBase { get; set; } = DefaultFallbackApiBase;
        public string Slug { get; set; } = DefaultSlug;
    }

    private sealed class TicketEnvelope
    {
        public string Status { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public TicketData? Data { get; set; }
    }

    private sealed class TicketData
    {
        public string DownloadUrl { get; set; } = string.Empty;
        public string KeyBase64 { get; set; } = string.Empty;
        public string IvBase64 { get; set; } = string.Empty;
        public string Sha256 { get; set; } = string.Empty;
        public string Version { get; set; } = string.Empty;
        public long FileSize { get; set; }
    }
}
