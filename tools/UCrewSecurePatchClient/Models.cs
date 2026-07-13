using System.Text.Json.Serialization;

namespace UCREW.SecurePatch;

internal sealed class ClientConfig
{
    public bool Enabled { get; set; } = true;
    public string ApiBase { get; set; } = "https://api.u-crew.net/api/";
    public string FallbackApiBase { get; set; } = "https://www.u-crew.net/ucrew_patch_v3/api/";
    public string GameSlug { get; set; } = "";
    public string Channel { get; set; } = "stable";
    public string GameRoot { get; set; } = ".";
    public string GameExe { get; set; } = "";
    public string[] GameExecutables { get; set; } = Array.Empty<string>();
    public string[] GameArguments { get; set; } = Array.Empty<string>();
    public string LogoPath { get; set; } = "ucrew-logo.png";
    public string BackgroundPath { get; set; } = "ucrew-background.jpg";
    public string WindowTitle { get; set; } = "U-CREW Türkçe Yama";
    public bool HideRuntimeFiles { get; set; } = true;
    public bool CloseWindowWhenGameStarts { get; set; } = true;
}

internal sealed class TicketEnvelope
{
    [JsonPropertyName("status")]
    public string Status { get; set; } = "";

    [JsonPropertyName("message")]
    public string Message { get; set; } = "";

    [JsonPropertyName("data")]
    public TicketData? Data { get; set; }
}

internal sealed class TicketData
{
    [JsonPropertyName("game_id")]
    public long GameId { get; set; }

    [JsonPropertyName("game_slug")]
    public string GameSlug { get; set; } = "";

    [JsonPropertyName("game_title")]
    public string GameTitle { get; set; } = "";

    [JsonPropertyName("version")]
    public string Version { get; set; } = "";

    [JsonPropertyName("channel")]
    public string Channel { get; set; } = "stable";

    [JsonPropertyName("package_format")]
    public string PackageFormat { get; set; } = "";

    [JsonPropertyName("download_url")]
    public string DownloadUrl { get; set; } = "";

    [JsonPropertyName("key_base64")]
    public string KeyBase64 { get; set; } = "";

    [JsonPropertyName("iv_base64")]
    public string IvBase64 { get; set; } = "";

    [JsonPropertyName("sha256")]
    public string Sha256 { get; set; } = "";

    [JsonPropertyName("file_size")]
    public long FileSize { get; set; }

    [JsonPropertyName("runtime_profile")]
    public RuntimeProfile RuntimeProfile { get; set; } = new();

    [JsonPropertyName("expires_at")]
    public string ExpiresAt { get; set; } = "";
}

internal sealed class RuntimeProfile
{
    [JsonPropertyName("profile_version")]
    public int ProfileVersion { get; set; } = 1;

    [JsonPropertyName("game_slug")]
    public string GameSlug { get; set; } = "";

    [JsonPropertyName("game_name")]
    public string GameName { get; set; } = "";

    [JsonPropertyName("game_exe")]
    public string GameExe { get; set; } = "";

    [JsonPropertyName("game_exes")]
    public string[] GameExecutables { get; set; } = Array.Empty<string>();

    [JsonPropertyName("install_mode")]
    public string InstallMode { get; set; } = "overlay_tree";

    [JsonPropertyName("target_path")]
    public string TargetPath { get; set; } = "";

    [JsonPropertyName("target_paths")]
    public string[] TargetPaths { get; set; } = Array.Empty<string>();

    [JsonPropertyName("allowed_extensions")]
    public string[] AllowedExtensions { get; set; } = Array.Empty<string>();

    [JsonPropertyName("cleanup_on_exit")]
    public bool CleanupOnExit { get; set; } = true;

    [JsonPropertyName("preserve_directory_tree")]
    public bool PreserveDirectoryTree { get; set; } = true;

    [JsonPropertyName("requires_bootstrap")]
    public bool RequiresBootstrap { get; set; }

    [JsonPropertyName("bootstrap_type")]
    public string BootstrapType { get; set; } = "launcher";

    [JsonPropertyName("wait_for_game_exit")]
    public bool WaitForGameExit { get; set; } = true;

    [JsonPropertyName("backup_existing_files")]
    public bool BackupExistingFiles { get; set; } = true;

    [JsonPropertyName("theme")]
    public RuntimeTheme? Theme { get; set; }
}

internal sealed class RuntimeTheme
{
    [JsonPropertyName("logo")]
    public string Logo { get; set; } = "";

    [JsonPropertyName("background")]
    public string Background { get; set; } = "";

    [JsonPropertyName("accent")]
    public string Accent { get; set; } = "#55ff00";
}

internal sealed record LauncherProgress(int Percent, string Status, string Detail);

internal sealed class InstallSession
{
    public string SessionId { get; set; } = Guid.NewGuid().ToString("N");
    public string GameSlug { get; set; } = "";
    public string GameRoot { get; set; } = "";
    public bool CleanupOnExit { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public List<InstalledFileRecord> Files { get; set; } = new();
}

internal sealed class InstalledFileRecord
{
    public string TargetPath { get; set; } = "";
    public string BackupPath { get; set; } = "";
    public bool HadOriginal { get; set; }
}

internal sealed class PreparedPatch : IDisposable
{
    public PreparedPatch(
        TicketData ticket,
        string decryptedZipPath,
        string temporaryRoot,
        bool downloaded)
    {
        Ticket = ticket;
        DecryptedZipPath = decryptedZipPath;
        TemporaryRoot = temporaryRoot;
        Downloaded = downloaded;
    }

    public TicketData Ticket { get; }
    public string DecryptedZipPath { get; }
    public string TemporaryRoot { get; }
    public bool Downloaded { get; }

    public void Dispose()
    {
        FileSystemUtil.TryDeleteDirectory(TemporaryRoot);
    }
}
