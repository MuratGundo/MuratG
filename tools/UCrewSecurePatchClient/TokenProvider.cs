using System.Text;
using System.Text.Json;

namespace UCREW.SecurePatch;

internal static class TokenProvider
{
    private static string _runtimeToken = string.Empty;

    private static readonly string[] TokenPropertyNames =
    {
        "token",
        "Token",
        "authToken",
        "AuthToken",
        "access_token",
        "accessToken",
        "AccessToken"
    };

    public static bool TryUseSavedSecurePatchSession()
    {
        string path = GetSessionPath();
        if (!File.Exists(path))
        {
            return false;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path, Encoding.UTF8));
            string token = FindToken(document.RootElement);
            if (string.IsNullOrWhiteSpace(token))
            {
                return false;
            }

            _runtimeToken = token.Trim();
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static void SetRuntimeToken(string token, bool remember)
    {
        _runtimeToken = token?.Trim() ?? string.Empty;
        string sessionPath = GetSessionPath();

        if (remember && !string.IsNullOrWhiteSpace(_runtimeToken))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(sessionPath)!);
            File.WriteAllText(
                sessionPath,
                JsonSerializer.Serialize(new { Token = _runtimeToken }),
                new UTF8Encoding(false));
        }
        else if (File.Exists(sessionPath))
        {
            File.Delete(sessionPath);
        }
    }

    public static string ReadRememberedToken(Action<string> log)
    {
        if (!string.IsNullOrWhiteSpace(_runtimeToken))
        {
            log("U-CREW oturumu giriş ekranından alındı.");
            return _runtimeToken;
        }

        string environmentToken = Environment.GetEnvironmentVariable("UCREW_TOKEN") ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(environmentToken))
        {
            log("U-CREW oturumu ortam değişkeninden okundu.");
            return environmentToken.Trim();
        }

        string roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        string[] candidates =
        {
            GetSessionPath(),
            Path.Combine(roaming, "U-CREW", "Launcher", "settings.json"),
            Path.Combine(local, "U-CREW", "Launcher", "settings.json"),
            Path.Combine(roaming, "UCREWLauncher", "settings.json"),
            Path.Combine(local, "UCREWLauncher", "settings.json"),
            Path.Combine(roaming, "U-CREW", "settings.json"),
            Path.Combine(local, "U-CREW", "settings.json")
        };

        foreach (string path in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!File.Exists(path))
            {
                continue;
            }

            try
            {
                using JsonDocument document = JsonDocument.Parse(
                    File.ReadAllText(path, Encoding.UTF8));

                string token = FindToken(document.RootElement);
                if (!string.IsNullOrWhiteSpace(token))
                {
                    log("U-CREW Launcher hatırlanan oturumu bulundu: " + path);
                    return token.Trim();
                }
            }
            catch (Exception exception)
            {
                log("Oturum dosyası okunamadı: " + exception.Message);
            }
        }

        throw new InvalidOperationException(
            "U-CREW oturumu bulunamadı. U-CREW Launcher'a giriş yapıp 'Beni Hatırla' seçeneğini etkinleştirin.");
    }

    private static string GetSessionPath()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "U-CREW",
            "SecurePatch",
            "session.json");
    }

    private static string FindToken(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (string propertyName in TokenPropertyNames)
            {
                if (element.TryGetProperty(propertyName, out JsonElement value) &&
                    value.ValueKind == JsonValueKind.String)
                {
                    string token = value.GetString() ?? string.Empty;
                    if (!string.IsNullOrWhiteSpace(token))
                    {
                        return token;
                    }
                }
            }

            foreach (JsonProperty property in element.EnumerateObject())
            {
                string nested = FindToken(property.Value);
                if (!string.IsNullOrWhiteSpace(nested))
                {
                    return nested;
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in element.EnumerateArray())
            {
                string nested = FindToken(item);
                if (!string.IsNullOrWhiteSpace(nested))
                {
                    return nested;
                }
            }
        }

        return string.Empty;
    }
}
