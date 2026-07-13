using System.Text;
using System.Text.Json;

namespace UCREW.SecurePatch;

internal static class ConfigLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public static ClientConfig Load(string[] args)
    {
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

        string configDirectory = Path.GetDirectoryName(configPath) ?? AppContext.BaseDirectory;
        string gameRootValue = string.IsNullOrWhiteSpace(config.GameRoot) ? "." : config.GameRoot.Trim();
        config.GameRoot = Path.GetFullPath(Path.Combine(configDirectory, gameRootValue));
        config.GameArguments ??= Array.Empty<string>();

        return config;
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
