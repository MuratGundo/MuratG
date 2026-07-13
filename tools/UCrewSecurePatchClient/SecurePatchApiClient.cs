using System.Net;
using System.Net.Http.Headers;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Text.Json;

namespace UCREW.SecurePatch;

internal sealed class SecurePatchApiClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private static readonly SocketsHttpHandler Handler = new()
    {
        AutomaticDecompression = DecompressionMethods.GZip |
                                 DecompressionMethods.Deflate |
                                 DecompressionMethods.Brotli,
        AllowAutoRedirect = true,
        MaxAutomaticRedirections = 5,
        ConnectTimeout = TimeSpan.FromSeconds(20),
        PooledConnectionIdleTimeout = TimeSpan.FromSeconds(15),
        PooledConnectionLifetime = TimeSpan.FromMinutes(2),
        MaxConnectionsPerServer = 2,
        SslOptions = new System.Net.Security.SslClientAuthenticationOptions
        {
            EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13
        }
    };

    private static readonly HttpClient Http = new(Handler)
    {
        Timeout = Timeout.InfiniteTimeSpan,
        DefaultRequestVersion = HttpVersion.Version11,
        DefaultVersionPolicy = HttpVersionPolicy.RequestVersionExact
    };

    private readonly ClientConfig _config;
    private readonly string _privateRoot;
    private readonly string _cacheRoot;
    private readonly Action<string> _log;

    public SecurePatchApiClient(ClientConfig config, string privateRoot, Action<string> log)
    {
        _config = config;
        _privateRoot = privateRoot;
        _cacheRoot = Path.Combine(privateRoot, "cache");
        _log = log;

        Directory.CreateDirectory(_cacheRoot);
        FileSystemUtil.SetHiddenSystem(_cacheRoot);
    }

    public async Task<PreparedPatch> AcquireAsync(
        IProgress<LauncherProgress> progress,
        CancellationToken cancellationToken)
    {
        string token = TokenProvider.ReadRememberedToken(_log);
        string hwid = DeviceIdentity.GetHwid();

        progress.Report(new LauncherProgress(
            5,
            "Sunucuya bağlanılıyor…",
            "U-CREW hesabı ve oyun lisansı doğrulanıyor."));

        TicketData ticket = await RequestTicketWithFallbackAsync(
            token,
            hwid,
            cancellationToken).ConfigureAwait(false);

        ApplyEmbeddedProfileFallback(ticket);
        ValidateTicket(ticket);

        string encryptedPath = Path.Combine(_cacheRoot, "patch.ucp");
        bool cacheValid = await IsCacheValidAsync(
            encryptedPath,
            ticket.Sha256,
            cancellationToken).ConfigureAwait(false);
        bool downloaded = false;

        if (!cacheValid)
        {
            downloaded = true;
            await DownloadAsync(
                ticket.DownloadUrl,
                encryptedPath,
                progress,
                cancellationToken).ConfigureAwait(false);
        }
        else
        {
            progress.Report(new LauncherProgress(
                52,
                "Güncel yama hazır.",
                "Şifreli önbellek sunucudaki sürümle eşleşiyor."));
            _log("Şifreli paket önbellekten kullanılacak.");
        }

        progress.Report(new LauncherProgress(
            58,
            "Dosyalar doğrulanıyor…",
            "Şifreli paket SHA-256 ile kontrol ediliyor."));

        string actualSha = await FileSystemUtil.ComputeSha256Async(
            encryptedPath,
            cancellationToken).ConfigureAwait(false);
        string expectedSha = FileSystemUtil.NormalizeSha256(ticket.Sha256);

        if (!string.Equals(actualSha, expectedSha, StringComparison.OrdinalIgnoreCase))
        {
            FileSystemUtil.TryDeleteFile(encryptedPath);
            throw new InvalidDataException(
                "Şifreli yama paketinin SHA-256 doğrulaması başarısız. Paket silindi.");
        }

        string temporaryRoot = Path.Combine(
            _privateRoot,
            "runtime",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporaryRoot);
        FileSystemUtil.SetHiddenSystem(Path.GetDirectoryName(temporaryRoot)!);
        FileSystemUtil.SetHiddenSystem(temporaryRoot);

        string decryptedZipPath = Path.Combine(temporaryRoot, "patch.zip");

        try
        {
            progress.Report(new LauncherProgress(
                66,
                "Yama açılıyor…",
                "Şifreli paket güvenli geçici alanda çözülüyor."));

            await Task.Run(
                () => DecryptPackage(
                    encryptedPath,
                    decryptedZipPath,
                    ticket.KeyBase64,
                    ticket.IvBase64,
                    ticket.PackageFormat),
                cancellationToken).ConfigureAwait(false);

            FileSystemUtil.SetHiddenSystem(decryptedZipPath);

            using (System.IO.Compression.ZipArchive archive =
                   System.IO.Compression.ZipFile.OpenRead(decryptedZipPath))
            {
                if (!archive.Entries.Any(entry => !string.IsNullOrWhiteSpace(entry.Name)))
                {
                    throw new InvalidDataException("Çözülen yama paketi boş.");
                }
            }

            SaveCacheMetadata(ticket, actualSha);
            return new PreparedPatch(ticket, decryptedZipPath, temporaryRoot, downloaded);
        }
        catch
        {
            FileSystemUtil.TryDeleteDirectory(temporaryRoot);
            throw;
        }
    }

    private async Task<TicketData> RequestTicketWithFallbackAsync(
        string token,
        string hwid,
        CancellationToken cancellationToken)
    {
        string[] apiBases =
        {
            _config.ApiBase,
            _config.FallbackApiBase
        };

        Exception? lastException = null;

        foreach (string apiBase in apiBases
                     .Where(value => !string.IsNullOrWhiteSpace(value))
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            for (int attempt = 1; attempt <= 3; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    TicketData ticket = await RequestTicketOnceAsync(
                        apiBase,
                        token,
                        hwid,
                        cancellationToken).ConfigureAwait(false);

                    _log(
                        $"Yama bileti alındı. Oyun={ticket.GameSlug} " +
                        $"Sürüm={ticket.Version} Kanal={ticket.Channel}");
                    return ticket;
                }
                catch (Exception exception)
                {
                    lastException = exception;
                    _log(
                        $"API isteği başarısız. Sunucu={apiBase} " +
                        $"Deneme={attempt}/3 Hata={exception.Message}");

                    if (attempt < 3)
                    {
                        await Task.Delay(
                            TimeSpan.FromMilliseconds(700 * attempt),
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

    private async Task<TicketData> RequestTicketOnceAsync(
        string apiBase,
        string token,
        string hwid,
        CancellationToken cancellationToken)
    {
        string endpoint = apiBase.TrimEnd('/') + "/secure_patch_request.php";

        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Version = HttpVersion.Version11,
            VersionPolicy = HttpVersionPolicy.RequestVersionExact,
            Content = new FormUrlEncodedContent(
                new Dictionary<string, string>
                {
                    ["token"] = token,
                    ["slug"] = _config.GameSlug,
                    ["channel"] = _config.Channel,
                    ["hwid"] = hwid
                })
        };

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.TryAddWithoutValidation("Accept", "application/json");
        request.Headers.TryAddWithoutValidation("User-Agent", "UCREW-SecurePatch/1.0");
        request.Headers.TryAddWithoutValidation("Connection", "close");

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(45));

        using HttpResponseMessage response = await Http.SendAsync(
            request,
            HttpCompletionOption.ResponseContentRead,
            timeout.Token).ConfigureAwait(false);
        string raw = await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false);

        TicketEnvelope envelope;
        try
        {
            envelope = JsonSerializer.Deserialize<TicketEnvelope>(raw, JsonOptions)
                       ?? throw new JsonException("Boş JSON yanıtı.");
        }
        catch (JsonException exception)
        {
            string preview = raw.Length > 500 ? raw[..500] : raw;
            throw new InvalidDataException("Sunucu JSON yerine şu yanıtı verdi: " + preview, exception);
        }

        if (!response.IsSuccessStatusCode ||
            !string.Equals(envelope.Status, "ok", StringComparison.OrdinalIgnoreCase) ||
            envelope.Data is null)
        {
            string message = string.IsNullOrWhiteSpace(envelope.Message)
                ? $"Sunucu hatası: HTTP {(int)response.StatusCode}"
                : envelope.Message;
            throw new InvalidOperationException(message);
        }

        if (!Uri.TryCreate(envelope.Data.DownloadUrl, UriKind.Absolute, out Uri? absolute))
        {
            envelope.Data.DownloadUrl = new Uri(new Uri(apiBase), envelope.Data.DownloadUrl).ToString();
        }

        return envelope.Data;
    }

    private async Task DownloadAsync(
        string url,
        string destinationPath,
        IProgress<LauncherProgress> progress,
        CancellationToken cancellationToken)
    {
        string temporaryPath = destinationPath + ".download";
        FileSystemUtil.TryDeleteFile(temporaryPath);

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url)
            {
                Version = HttpVersion.Version11,
                VersionPolicy = HttpVersionPolicy.RequestVersionOrLower
            };
            request.Headers.TryAddWithoutValidation("User-Agent", "UCREW-SecurePatch/1.0");
            request.Headers.TryAddWithoutValidation("Connection", "close");

            using HttpResponseMessage response = await Http.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            long? total = response.Content.Headers.ContentLength;
            long received = 0;
            await using (Stream source = await response.Content.ReadAsStreamAsync(cancellationToken)
                             .ConfigureAwait(false))
            {
                await using (var destination = new FileStream(
                                 temporaryPath,
                                 FileMode.Create,
                                 FileAccess.Write,
                                 FileShare.None,
                                 bufferSize: 1024 * 128,
                                 useAsync: true))
                {
                    byte[] buffer = new byte[1024 * 128];
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
                            ? (int)Math.Clamp(Math.Round(received * 100d / total.Value), 0, 100)
                            : 0;

                        if (percent != lastPercent)
                        {
                            lastPercent = percent;
                            int overall = total is > 0
                                ? 12 + (int)Math.Round(percent * 0.40)
                                : 28;
                            string detail = total is > 0
                                ? $"Şifreli paket indiriliyor: %{percent} " +
                                  $"({FileSystemUtil.FormatBytes(received)} / {FileSystemUtil.FormatBytes(total.Value)})"
                                : $"Şifreli paket indiriliyor: {FileSystemUtil.FormatBytes(received)}";

                            progress.Report(new LauncherProgress(
                                Math.Clamp(overall, 12, 52),
                                "Türkçe yama indiriliyor…",
                                detail));
                        }
                    }

                    await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
                }
            }

            if (received <= 0)
            {
                throw new InvalidDataException("Sunucudan indirilen yama paketi boş.");
            }

            FileSystemUtil.RemoveRestrictiveAttributes(destinationPath);
            File.Move(temporaryPath, destinationPath, overwrite: true);
            FileSystemUtil.SetHiddenSystem(destinationPath);
            _log($"Şifreli paket indirildi. Boyut={received}");
        }
        catch
        {
            FileSystemUtil.TryDeleteFile(temporaryPath);
            throw;
        }
    }

    private void ApplyEmbeddedProfileFallback(TicketData ticket)
    {
        bool serverProfileUsable =
            ticket.RuntimeProfile is not null &&
            (!string.IsNullOrWhiteSpace(ticket.RuntimeProfile.GameExe) ||
             (ticket.RuntimeProfile.GameExecutables is { Length: > 0 })) &&
            ticket.RuntimeProfile.AllowedExtensions is { Length: > 0 } &&
            !string.IsNullOrWhiteSpace(ticket.RuntimeProfile.InstallMode);

        if (serverProfileUsable)
        {
            return;
        }

        if (ConfigLoader.TryLoadEmbeddedRuntimeProfile(out RuntimeProfile embeddedProfile))
        {
            ticket.RuntimeProfile = embeddedProfile;
            _log("Sunucu çalışma profili boştu; tek EXE içine gömülü oyun profili kullanıldı.");
        }
    }

    private static void ValidateTicket(TicketData ticket)
    {
        if (string.IsNullOrWhiteSpace(ticket.DownloadUrl))
        {
            throw new InvalidDataException("Sunucu indirme adresi göndermedi.");
        }

        if (string.IsNullOrWhiteSpace(ticket.KeyBase64) ||
            string.IsNullOrWhiteSpace(ticket.IvBase64))
        {
            throw new InvalidDataException("Sunucu paket anahtarı veya IV göndermedi.");
        }

        if (FileSystemUtil.NormalizeSha256(ticket.Sha256).Length != 64)
        {
            throw new InvalidDataException("Sunucu geçerli SHA-256 göndermedi.");
        }

        if (ticket.RuntimeProfile is null ||
            (string.IsNullOrWhiteSpace(ticket.RuntimeProfile.GameExe) &&
             (ticket.RuntimeProfile.GameExecutables is null ||
              ticket.RuntimeProfile.GameExecutables.Length == 0)))
        {
            throw new InvalidDataException(
                "Sunucu geçerli oyun çalışma profili göndermedi. game_exe veya game_exes boş.");
        }
    }

    private static async Task<bool> IsCacheValidAsync(
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
            string actual = await FileSystemUtil.ComputeSha256Async(path, cancellationToken)
                .ConfigureAwait(false);
            return string.Equals(
                actual,
                FileSystemUtil.NormalizeSha256(expectedSha),
                StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static void DecryptPackage(
        string inputPath,
        string outputPath,
        string keyBase64,
        string ivBase64,
        string packageFormat)
    {
        if (!string.Equals(
                packageFormat,
                "zip-aes256-cbc-v1",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new NotSupportedException("Desteklenmeyen paket biçimi: " + packageFormat);
        }

        byte[] key = Convert.FromBase64String(keyBase64);
        byte[] iv = Convert.FromBase64String(ivBase64);

        if (key.Length != 32 || iv.Length != 16)
        {
            throw new CryptographicException("AES-256 anahtarı veya IV uzunluğu geçersiz.");
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
        using var crypto = new CryptoStream(input, aes.CreateDecryptor(), CryptoStreamMode.Read);
        crypto.CopyTo(output);
        output.Flush(flushToDisk: true);
    }

    private void SaveCacheMetadata(TicketData ticket, string sha256)
    {
        string metadataPath = Path.Combine(_cacheRoot, "cache.json");
        FileSystemUtil.RemoveRestrictiveAttributes(metadataPath);
        File.WriteAllText(
            metadataPath,
            JsonSerializer.Serialize(
                new
                {
                    game_slug = ticket.GameSlug,
                    version = ticket.Version,
                    channel = ticket.Channel,
                    sha256,
                    verified_at = DateTimeOffset.UtcNow
                },
                new JsonSerializerOptions { WriteIndented = true }));
        FileSystemUtil.SetHiddenSystem(metadataPath);
    }
}
