using System.Net.Http.Headers;
using System.Text.Json;

namespace UCREW.SecurePatch;

internal sealed class LoginService
{
    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromSeconds(45)
    };

    public async Task<string> LoginAsync(
        ClientConfig config,
        string email,
        string password,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            throw new InvalidOperationException("E-posta ve şifre boş bırakılamaz.");
        }

        string endpoint = config.ApiBase.TrimEnd('/') + "/login.php";
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["email"] = email.Trim(),
                ["password"] = password
            })
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.TryAddWithoutValidation("User-Agent", "UCREW-SecurePatch/1.1");

        using HttpResponseMessage response = await Http.SendAsync(
            request,
            HttpCompletionOption.ResponseContentRead,
            cancellationToken).ConfigureAwait(false);
        string raw = await response.Content.ReadAsStringAsync(cancellationToken)
            .ConfigureAwait(false);

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(raw);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(
                "Giriş sunucusu geçerli JSON yanıtı vermedi.", exception);
        }

        using (document)
        {
            string token = FindString(document.RootElement,
                "token", "authToken", "access_token", "accessToken");

            if (response.IsSuccessStatusCode && !string.IsNullOrWhiteSpace(token))
            {
                return token.Trim();
            }

            string message = FindString(document.RootElement,
                "message", "error", "detail");

            throw new InvalidOperationException(
                string.IsNullOrWhiteSpace(message)
                    ? "E-posta veya şifre hatalı."
                    : message);
        }
    }

    private static string FindString(JsonElement element, params string[] names)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty property in element.EnumerateObject())
            {
                if (names.Any(name =>
                        string.Equals(name, property.Name, StringComparison.OrdinalIgnoreCase)) &&
                    property.Value.ValueKind == JsonValueKind.String)
                {
                    return property.Value.GetString() ?? string.Empty;
                }
            }

            foreach (JsonProperty property in element.EnumerateObject())
            {
                string nested = FindString(property.Value, names);
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
                string nested = FindString(item, names);
                if (!string.IsNullOrWhiteSpace(nested))
                {
                    return nested;
                }
            }
        }

        return string.Empty;
    }
}
