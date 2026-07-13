using System.Text.Json.Serialization;

namespace UCREW.SecurePatch.Manager;

internal sealed class GameProfile
{
    [JsonPropertyName("game_slug")]
    public string GameSlug { get; set; } = "";

    [JsonPropertyName("game_name")]
    public string GameName { get; set; } = "";

    [JsonPropertyName("game_exe")]
    public string GameExe { get; set; } = "";

    [JsonPropertyName("install_mode")]
    public string InstallMode { get; set; } = "overlay_tree";

    [JsonPropertyName("target_path")]
    public string TargetPath { get; set; } = "";

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
}

internal sealed class PackageResult
{
    public required string MetadataPath { get; init; }
    public required string EncryptedPackagePath { get; init; }
    public required string SqlPath { get; init; }
    public required string ProfilePath { get; init; }
    public required string Sha256 { get; init; }
    public required string GameSlug { get; init; }
    public required string Version { get; init; }
    public required string Channel { get; init; }
    public required string FileName { get; init; }
}

internal sealed class ServerSettings
{
    public string Host { get; set; } = "185.8.129.202";
    public int Port { get; set; } = 22;
    public string User { get; set; } = "root";
    public string Password { get; set; } = "";
    public string DatabaseName { get; set; } = "ucrewnet_ucrew_patch_v3";
    public string AppRoot { get; set; } = "/var/www/api.u-crew.net";
}
