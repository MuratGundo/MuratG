using System.Text.Json.Serialization;

namespace UCREW.SecurePatchStudio;

internal sealed class StudioSettings
{
    public string Host { get; set; } = "185.8.129.202";
    public int Port { get; set; } = 22;
    public string SshUser { get; set; } = "root";
    public string DatabaseName { get; set; } = "ucrewnet_ucrew_patch_v3";
    public string DatabaseUser { get; set; } = "root";
    public string EncryptedSshPassword { get; set; } = string.Empty;
    public string EncryptedDatabasePassword { get; set; } = string.Empty;
    public string LastProfilePath { get; set; } = string.Empty;
    public string LastSourcePath { get; set; } = string.Empty;
    public string LastOutputPath { get; set; } = string.Empty;
    public string LastMetadataPath { get; set; } = string.Empty;
}

internal sealed class GameProfile
{
    [JsonPropertyName("profile_version")]
    public int ProfileVersion { get; set; } = 1;

    [JsonPropertyName("game_slug")]
    public string GameSlug { get; set; } = string.Empty;

    [JsonPropertyName("game_name")]
    public string GameName { get; set; } = string.Empty;

    [JsonPropertyName("game_exe")]
    public string GameExe { get; set; } = string.Empty;

    [JsonPropertyName("install_mode")]
    public string InstallMode { get; set; } = "overlay_tree";

    [JsonPropertyName("target_path")]
    public string TargetPath { get; set; } = string.Empty;

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
    public ProfileTheme Theme { get; set; } = new();
}

internal sealed class ProfileTheme
{
    [JsonPropertyName("logo")]
    public string Logo { get; set; } = "ucrew-logo.png";

    [JsonPropertyName("background")]
    public string Background { get; set; } = "ucrew-background.jpg";

    [JsonPropertyName("accent")]
    public string Accent { get; set; } = "#59FF35";
}

internal sealed class PatchMetadata
{
    [JsonPropertyName("system")]
    public string System { get; set; } = "U-CREW Secure Patch";

    [JsonPropertyName("package_format")]
    public string PackageFormat { get; set; } = "zip-aes256-cbc-v1";

    [JsonPropertyName("game_slug")]
    public string GameSlug { get; set; } = string.Empty;

    [JsonPropertyName("game_name")]
    public string GameName { get; set; } = string.Empty;

    [JsonPropertyName("version")]
    public string Version { get; set; } = string.Empty;

    [JsonPropertyName("channel")]
    public string Channel { get; set; } = "stable";

    [JsonPropertyName("encrypted_file")]
    public string EncryptedFile { get; set; } = string.Empty;

    [JsonPropertyName("server_relative_path")]
    public string ServerRelativePath { get; set; } = string.Empty;

    [JsonPropertyName("source_entries")]
    public int SourceEntries { get; set; }

    [JsonPropertyName("file_size")]
    public long FileSize { get; set; }

    [JsonPropertyName("sha256")]
    public string Sha256 { get; set; } = string.Empty;

    [JsonPropertyName("key_base64")]
    public string KeyBase64 { get; set; } = string.Empty;

    [JsonPropertyName("iv_base64")]
    public string IvBase64 { get; set; } = string.Empty;

    [JsonPropertyName("runtime_profile")]
    public GameProfile RuntimeProfile { get; set; } = new();

    [JsonPropertyName("created_at")]
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

internal sealed record PackageBuildResult(
    string EncryptedPackagePath,
    string MetadataPath,
    string SqlPath,
    string ProfileCopyPath,
    PatchMetadata Metadata);

internal sealed record OperationResult(bool Success, string Message, string Details = "");
