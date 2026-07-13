using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace UCREW.SecurePatch;

internal static class ConfigLoader
{
    private const string PayloadMagic = "UCREW_PAYLOAD_V1";
    private static readonly Lazy<IReadOnlyDictionary<string, byte[]>?> EmbeddedFiles =
        new(LoadEmbeddedFiles);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public static ClientConfig Load(string[] args)
    {
        if (TryReadEmbeddedFile("ucrew_game.json", out byte[] embeddedConfig))
        {
            string embeddedJson = Encoding.UTF8.GetString(embeddedConfig);
            return ParseAndNormalize(embeddedJson, AppContext.BaseDirectory);
        }

        string configPath = ResolveConfigPath(args);

        if (!File.Exists(configPath))
        {
            var sample = new ClientConfig
            {
                GameSlug = "guardians",
                GameRoot = ".",
                GameExe = "Guardians.exe",
                LogoPath = "ucrew-logo.png",
                BackgroundPath = "guardians-background.jpg",
                WindowTitle = "U-CREW Guardians Türkçe Yama"
            };

            File.WriteAllText(
                configPath,
                JsonSerializer.Serialize(sample, JsonOptions),
                new UTF8Encoding(false));

            throw new FileNotFoundException(
                "ucrew_game.json oluşturuldu. GameSlug ve oyun bilgilerini düzenleyip tekrar çalıştırın.",
                configPath);
        }

        string json = File.ReadAllText(configPath, Encoding.UTF8);
        string configDirectory = Path.GetDirectoryName(configPath) ?? AppContext.BaseDirectory;
        return ParseAndNormalize(json, configDirectory);
    }

    public static bool TryReadEmbeddedFile(string path, out byte[] bytes)
    {
        bytes = Array.Empty<byte>();
        IReadOnlyDictionary<string, byte[]>? files = EmbeddedFiles.Value;
        if (files is null || string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        string name = Path.GetFileName(path);
        return files.TryGetValue(name, out bytes!);
    }

    public static string ResolveAssetPath(ClientConfig config, string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        if (Path.IsPathRooted(path))
        {
            return Path.GetFullPath(path);
        }

        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, path));
    }

    private static ClientConfig ParseAndNormalize(string json, string configDirectory)
    {
        ClientConfig config = JsonSerializer.Deserialize<ClientConfig>(json, JsonOptions)
            ?? throw new InvalidDataException("İstemci yapılandırması boş.");

        config.ApiBase = NormalizeApiBase(config.ApiBase, "https://api.u-crew.net/api/");
        config.FallbackApiBase = NormalizeApiBase(
            config.FallbackApiBase,
            "https://www.u-crew.net/ucrew_patch_v3/api/");
        config.GameSlug = (config.GameSlug ?? string.Empty).Trim().ToLowerInvariant();
        config.Channel = string.IsNullOrWhiteSpace(config.Channel)
            ? "stable"
            : config.Channel.Trim().ToLowerInvariant();

        if (!config.Enabled)
        {
            throw new InvalidOperationException("Bu oyun için U-CREW güvenli yama istemcisi kapalı.");
        }

        if (string.IsNullOrWhiteSpace(config.GameSlug) ||
            !System.Text.RegularExpressions.Regex.IsMatch(
                config.GameSlug,
                "^[a-z0-9][a-z0-9._-]{1,99}$",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase))
        {
            throw new InvalidDataException("GameSlug geçersiz.");
        }

        string gameRootValue = string.IsNullOrWhiteSpace(config.GameRoot) ? "." : config.GameRoot.Trim();
        config.GameRoot = Path.GetFullPath(Path.Combine(configDirectory, gameRootValue));
        config.GameArguments ??= Array.Empty<string>();

        return config;
    }

    private static IReadOnlyDictionary<string, byte[]>? LoadEmbeddedFiles()
    {
        try
        {
            string executablePath = Environment.ProcessPath
                ?? throw new InvalidOperationException("Çalışan uygulamanın yolu bulunamadı.");
            byte[] magic = Encoding.ASCII.GetBytes(PayloadMagic);

            using var executable = new FileStream(
                executablePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);

            long footerSize = sizeof(long) + magic.Length;
            if (executable.Length <= footerSize)
            {
                return null;
            }

            executable.Position = executable.Length - magic.Length;
            byte[] actualMagic = new byte[magic.Length];
            executable.ReadExactly(actualMagic);
            if (!actualMagic.AsSpan().SequenceEqual(magic))
            {
                return null;
            }

            executable.Position = executable.Length - footerSize;
            using var reader = new BinaryReader(executable, Encoding.UTF8, leaveOpen: true);
            long payloadLength = reader.ReadInt64();
            long payloadStart = executable.Length - footerSize - payloadLength;
            if (payloadLength <= 0 || payloadStart < 0 || payloadLength > int.MaxValue)
            {
                throw new InvalidDataException("EXE içindeki U-CREW oyun paketi geçersiz.");
            }

            executable.Position = payloadStart;
            byte[] payload = new byte[(int)payloadLength];
            executable.ReadExactly(payload);

            var files = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
            using var memory = new MemoryStream(payload, writable: false);
            using var archive = new ZipArchive(memory, ZipArchiveMode.Read, leaveOpen: false);
            foreach (ZipArchiveEntry entry in archive.Entries)
            {
                if (string.IsNullOrEmpty(entry.Name))
                {
                    continue;
                }

                using Stream source = entry.Open();
                using var destination = new MemoryStream();
                source.CopyTo(destination);
                files[entry.Name] = destination.ToArray();
            }

            return files;
        }
        catch (InvalidDataException)
        {
            throw;
        }
        catch
        {
            return null;
        }
    }

    private static string ResolveConfigPath(string[] args)
    {
        for (int index = 0; index < args.Length - 1; index++)
        {
            if (string.Equals(args[index], "--config", StringComparison.OrdinalIgnoreCase))
            {
                return Path.GetFullPath(args[index + 1]);
            }
        }

        return Path.Combine(AppContext.BaseDirectory, "ucrew_game.json");
    }

    private static string NormalizeApiBase(string? value, string fallback)
    {
        string result = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        return result.TrimEnd('/') + "/";
    }
}
