param(
    [string]$ProfilePath,
    [string]$SourcePath,
    [string]$OutputDirectory,
    [string]$Version = "1.0.0",
    [string]$Channel = "stable"
)

$ErrorActionPreference = "Stop"

Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.IO.Compression.FileSystem

function Select-ProfileFile {
    $dialog = New-Object System.Windows.Forms.OpenFileDialog
    $dialog.Title = "Oyun profilini seç"
    $dialog.Filter = "U-CREW oyun profili (*.json)|*.json"
    $dialog.Multiselect = $false

    if ($dialog.ShowDialog() -ne [System.Windows.Forms.DialogResult]::OK) {
        return $null
    }

    return [System.IO.Path]::GetFullPath($dialog.FileName)
}

function Select-SourcePath {
    $choice = [System.Windows.Forms.MessageBox]::Show(
        "Yama kaynağı ZIP dosyası mı?`n`nEvet: ZIP seç`nHayır: Klasör seç",
        "U-CREW Güvenli Yama Hazırlayıcı",
        [System.Windows.Forms.MessageBoxButtons]::YesNoCancel,
        [System.Windows.Forms.MessageBoxIcon]::Question
    )

    if ($choice -eq [System.Windows.Forms.DialogResult]::Cancel) {
        return $null
    }

    if ($choice -eq [System.Windows.Forms.DialogResult]::Yes) {
        $dialog = New-Object System.Windows.Forms.OpenFileDialog
        $dialog.Title = "Yama ZIP dosyasını seç"
        $dialog.Filter = "ZIP dosyası (*.zip)|*.zip"
        $dialog.Multiselect = $false

        if ($dialog.ShowDialog() -ne [System.Windows.Forms.DialogResult]::OK) {
            return $null
        }

        return [System.IO.Path]::GetFullPath($dialog.FileName)
    }

    $folderDialog = New-Object System.Windows.Forms.FolderBrowserDialog
    $folderDialog.Description = "Yama dosyalarının bulunduğu klasörü seç"
    $folderDialog.ShowNewFolderButton = $false

    if ($folderDialog.ShowDialog() -ne [System.Windows.Forms.DialogResult]::OK) {
        return $null
    }

    return [System.IO.Path]::GetFullPath($folderDialog.SelectedPath)
}

function Select-OutputFolder {
    $dialog = New-Object System.Windows.Forms.FolderBrowserDialog
    $dialog.Description = "Şifreli yama paketinin kaydedileceği klasörü seç"
    $dialog.ShowNewFolderButton = $true

    if ($dialog.ShowDialog() -ne [System.Windows.Forms.DialogResult]::OK) {
        return $null
    }

    return [System.IO.Path]::GetFullPath($dialog.SelectedPath)
}

function Escape-SqlLiteral {
    param([string]$Value)
    return (($Value ?? "") -replace "'", "''")
}

function Get-NormalizedExtensionList {
    param($Extensions)

    $result = New-Object System.Collections.Generic.List[string]

    foreach ($item in @($Extensions)) {
        $value = ([string]$item).Trim().ToLowerInvariant()
        if ([string]::IsNullOrWhiteSpace($value)) { continue }
        if (-not $value.StartsWith(".")) { $value = "." + $value }

        if ($value -notmatch '^\.[a-z0-9]+$') {
            throw "Geçersiz dosya uzantısı: $value"
        }

        if (-not $result.Contains($value)) {
            $result.Add($value)
        }
    }

    if ($result.Count -eq 0) {
        throw "Profilde en az bir allowed_extensions değeri olmalıdır."
    }

    return $result.ToArray()
}

function Assert-RelativeSafePath {
    param(
        [string]$Value,
        [string]$FieldName,
        [switch]$AllowEmpty
    )

    if ([string]::IsNullOrWhiteSpace($Value)) {
        if ($AllowEmpty) { return "" }
        throw "$FieldName boş olamaz."
    }

    $normalized = $Value.Replace('\', '/').Trim('/')

    if ([System.IO.Path]::IsPathRooted($Value) -or
        $normalized.Split('/') -contains '..') {
        throw "$FieldName yalnızca oyun klasörüne göre güvenli göreli yol olabilir."
    }

    return $normalized
}

function Get-Sha256Lower {
    param([string]$Path)
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function New-RandomBytes {
    param([int]$Length)

    $bytes = New-Object byte[] $Length
    $rng = [System.Security.Cryptography.RandomNumberGenerator]::Create()
    try {
        $rng.GetBytes($bytes)
    }
    finally {
        $rng.Dispose()
    }

    return $bytes
}

function New-SourceZip {
    param(
        [string]$InputPath,
        [string]$TemporaryRoot,
        [string[]]$AllowedExtensions
    )

    if (Test-Path -LiteralPath $InputPath -PathType Leaf) {
        if ([System.IO.Path]::GetExtension($InputPath).ToLowerInvariant() -ne ".zip") {
            throw "Kaynak dosya ZIP olmalıdır: $InputPath"
        }

        return [System.IO.Path]::GetFullPath($InputPath)
    }

    if (-not (Test-Path -LiteralPath $InputPath -PathType Container)) {
        throw "Yama kaynağı bulunamadı: $InputPath"
    }

    $files = Get-ChildItem -LiteralPath $InputPath -File -Recurse
    if ($files.Count -eq 0) {
        throw "Seçilen yama klasörü boş."
    }

    $invalid = @($files | Where-Object {
        $AllowedExtensions -notcontains $_.Extension.ToLowerInvariant()
    })

    if ($invalid.Count -gt 0) {
        $examples = ($invalid | Select-Object -First 10 -ExpandProperty FullName) -join "`n"
        throw "Profilin izin vermediği dosyalar bulundu:`n$examples"
    }

    $zipPath = Join-Path $TemporaryRoot "source_patch.zip"
    if (Test-Path -LiteralPath $zipPath) {
        Remove-Item -LiteralPath $zipPath -Force
    }

    [System.IO.Compression.ZipFile]::CreateFromDirectory(
        [System.IO.Path]::GetFullPath($InputPath),
        $zipPath,
        [System.IO.Compression.CompressionLevel]::Optimal,
        $false
    )

    return $zipPath
}

function Test-ZipContents {
    param(
        [string]$ZipPath,
        [string[]]$AllowedExtensions,
        [bool]$PreserveDirectoryTree
    )

    $archive = [System.IO.Compression.ZipFile]::OpenRead($ZipPath)
    try {
        $fileEntries = @($archive.Entries | Where-Object { -not [string]::IsNullOrWhiteSpace($_.Name) })

        if ($fileEntries.Count -eq 0) {
            throw "Yama ZIP paketi boş."
        }

        $seenFlatNames = @{}

        foreach ($entry in $fileEntries) {
            $extension = [System.IO.Path]::GetExtension($entry.Name).ToLowerInvariant()
            if ($AllowedExtensions -notcontains $extension) {
                throw "Profilin izin vermediği dosya türü: $($entry.FullName)"
            }

            $normalized = $entry.FullName.Replace('\', '/')
            if ($normalized.StartsWith('/') -or $normalized.Split('/') -contains '..') {
                throw "ZIP içinde güvenli olmayan yol bulundu: $normalized"
            }

            if (-not $PreserveDirectoryTree) {
                $flatName = [System.IO.Path]::GetFileName($entry.Name).ToLowerInvariant()
                if ($seenFlatNames.ContainsKey($flatName)) {
                    throw "Düz kurulum profilinde aynı isimli iki dosya var: $flatName"
                }
                $seenFlatNames[$flatName] = $true
            }
        }

        return $fileEntries.Count
    }
    finally {
        $archive.Dispose()
    }
}

function Protect-ZipWithAes {
    param(
        [string]$InputZip,
        [string]$OutputPath,
        [byte[]]$Key,
        [byte[]]$Iv
    )

    $aes = [System.Security.Cryptography.Aes]::Create()
    $aes.KeySize = 256
    $aes.BlockSize = 128
    $aes.Mode = [System.Security.Cryptography.CipherMode]::CBC
    $aes.Padding = [System.Security.Cryptography.PaddingMode]::PKCS7
    $aes.Key = $Key
    $aes.IV = $Iv

    $input = [System.IO.File]::OpenRead($InputZip)
    $output = [System.IO.File]::Create($OutputPath)
    $crypto = New-Object System.Security.Cryptography.CryptoStream(
        $output,
        $aes.CreateEncryptor(),
        [System.Security.Cryptography.CryptoStreamMode]::Write
    )

    try {
        $input.CopyTo($crypto)
        $crypto.FlushFinalBlock()
    }
    finally {
        $crypto.Dispose()
        $output.Dispose()
        $input.Dispose()
        $aes.Dispose()
    }
}

if ([string]::IsNullOrWhiteSpace($ProfilePath)) {
    $ProfilePath = Select-ProfileFile
}

if ([string]::IsNullOrWhiteSpace($ProfilePath)) {
    Write-Host "İşlem iptal edildi."
    exit 0
}

$ProfilePath = [System.IO.Path]::GetFullPath($ProfilePath)
if (-not (Test-Path -LiteralPath $ProfilePath -PathType Leaf)) {
    throw "Oyun profili bulunamadı: $ProfilePath"
}

$profile = Get-Content -LiteralPath $ProfilePath -Raw -Encoding UTF8 | ConvertFrom-Json

$gameSlug = ([string]$profile.game_slug).Trim().ToLowerInvariant()
$gameName = ([string]$profile.game_name).Trim()
$gameExe = Assert-RelativeSafePath ([string]$profile.game_exe) "game_exe"
$targetPath = Assert-RelativeSafePath ([string]$profile.target_path) "target_path" -AllowEmpty
$installMode = ([string]$profile.install_mode).Trim().ToLowerInvariant()
$allowedExtensions = Get-NormalizedExtensionList $profile.allowed_extensions
$cleanupOnExit = [bool]$profile.cleanup_on_exit
$preserveTree = if ($null -eq $profile.preserve_directory_tree) { $true } else { [bool]$profile.preserve_directory_tree }
$requiresBootstrap = if ($null -eq $profile.requires_bootstrap) { $false } else { [bool]$profile.requires_bootstrap }
$bootstrapType = if ([string]::IsNullOrWhiteSpace([string]$profile.bootstrap_type)) { "launcher" } else { ([string]$profile.bootstrap_type).Trim().ToLowerInvariant() }
$waitForGameExit = if ($null -eq $profile.wait_for_game_exit) { $true } else { [bool]$profile.wait_for_game_exit }
$backupExistingFiles = if ($null -eq $profile.backup_existing_files) { $true } else { [bool]$profile.backup_existing_files }

if ($gameSlug -notmatch '^[a-z0-9][a-z0-9._-]{1,99}$') {
    throw "Geçersiz game_slug: $gameSlug"
}

if ([string]::IsNullOrWhiteSpace($gameName)) {
    throw "game_name boş olamaz."
}

$validInstallModes = @("overlay_flat", "overlay_tree", "replace_files", "archive_replace", "custom")
if ($validInstallModes -notcontains $installMode) {
    throw "Geçersiz install_mode: $installMode"
}

if ([string]::IsNullOrWhiteSpace($Version)) {
    throw "Version boş olamaz."
}

if ($Channel -notmatch '^[a-z0-9._-]{1,32}$') {
    throw "Geçersiz kanal: $Channel"
}

if ([string]::IsNullOrWhiteSpace($SourcePath)) {
    $SourcePath = Select-SourcePath
}

if ([string]::IsNullOrWhiteSpace($SourcePath)) {
    Write-Host "İşlem iptal edildi."
    exit 0
}

$SourcePath = [System.IO.Path]::GetFullPath($SourcePath)

if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Select-OutputFolder
}

if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    Write-Host "İşlem iptal edildi."
    exit 0
}

$OutputDirectory = [System.IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null

$tempRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("UCREW_SECURE_PACK_" + [Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Force -Path $tempRoot | Out-Null

try {
    $sourceZip = New-SourceZip $SourcePath $tempRoot $allowedExtensions
    $entryCount = Test-ZipContents $sourceZip $allowedExtensions $preserveTree

    $safeVersion = ($Version -replace '[^a-zA-Z0-9._-]+', '_').Trim('_')
    if ([string]::IsNullOrWhiteSpace($safeVersion)) { $safeVersion = "1.0.0" }

    $timestamp = Get-Date -Format "yyyyMMdd_HHmmss"
    $encryptedName = "${gameSlug}_${Channel}_v${safeVersion}_${timestamp}.ucp"
    $encryptedPath = Join-Path $OutputDirectory $encryptedName
    $metadataPath = Join-Path $OutputDirectory "${gameSlug}_${Channel}_metadata.json"
    $sqlPath = Join-Path $OutputDirectory "${gameSlug}_${Channel}_REGISTER.sql"
    $profileCopyPath = Join-Path $OutputDirectory "${gameSlug}_profile.json"

    $key = New-RandomBytes 32
    $iv = New-RandomBytes 16

    Protect-ZipWithAes $sourceZip $encryptedPath $key $iv

    $sha256 = Get-Sha256Lower $encryptedPath
    $fileSize = (Get-Item -LiteralPath $encryptedPath).Length
    $keyBase64 = [Convert]::ToBase64String($key)
    $ivBase64 = [Convert]::ToBase64String($iv)
    $serverRelativePath = "secure_patches/$gameSlug/$encryptedName"

    $runtimeProfile = [ordered]@{
        profile_version = 1
        game_slug = $gameSlug
        game_name = $gameName
        game_exe = $gameExe
        install_mode = $installMode
        target_path = $targetPath
        allowed_extensions = $allowedExtensions
        cleanup_on_exit = $cleanupOnExit
        preserve_directory_tree = $preserveTree
        requires_bootstrap = $requiresBootstrap
        bootstrap_type = $bootstrapType
        wait_for_game_exit = $waitForGameExit
        backup_existing_files = $backupExistingFiles
        theme = $profile.theme
    }

    $runtimeProfileJson = $runtimeProfile | ConvertTo-Json -Depth 10 -Compress

    $metadata = [ordered]@{
        system = "U-CREW Secure Patch"
        package_format = "zip-aes256-cbc-v1"
        game_slug = $gameSlug
        game_name = $gameName
        version = $Version
        channel = $Channel
        encrypted_file = $encryptedName
        server_relative_path = $serverRelativePath
        source_entries = $entryCount
        file_size = $fileSize
        sha256 = $sha256
        key_base64 = $keyBase64
        iv_base64 = $ivBase64
        runtime_profile = $runtimeProfile
        created_at = (Get-Date).ToString("o")
    }

    $metadata | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $metadataPath -Encoding UTF8
    $runtimeProfile | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $profileCopyPath -Encoding UTF8

    $sqlSlug = Escape-SqlLiteral $gameSlug
    $sqlVersion = Escape-SqlLiteral $Version
    $sqlChannel = Escape-SqlLiteral $Channel
    $sqlPathValue = Escape-SqlLiteral $serverRelativePath
    $sqlFileName = Escape-SqlLiteral $encryptedName
    $sqlSha = Escape-SqlLiteral $sha256
    $sqlKey = Escape-SqlLiteral $keyBase64
    $sqlIv = Escape-SqlLiteral $ivBase64
    $sqlProfile = Escape-SqlLiteral $runtimeProfileJson

    $sql = @"
-- U-CREW genel güvenli yama kaydı
-- Oyun: $gameName
-- Profil: $gameSlug / $Channel / $Version
-- Önce şifreli dosyayı yükleyin:
-- $serverRelativePath

SET @ucrew_game_id := (
    SELECT id FROM games WHERE LOWER(slug)=LOWER('$sqlSlug') LIMIT 1
);

SELECT @ucrew_game_id AS game_id;

UPDATE secure_patch_files
SET status='archived', updated_at=NOW()
WHERE game_id=@ucrew_game_id
  AND channel='$sqlChannel'
  AND status='active';

INSERT INTO secure_patch_files
(game_id, version, channel, package_format,
 file_path, file_name, file_size, sha256,
 key_base64, iv_base64, runtime_profile_json,
 status, created_at, updated_at)
SELECT
    @ucrew_game_id,
    '$sqlVersion',
    '$sqlChannel',
    'zip-aes256-cbc-v1',
    '$sqlPathValue',
    '$sqlFileName',
    $fileSize,
    '$sqlSha',
    '$sqlKey',
    '$sqlIv',
    '$sqlProfile',
    'active',
    NOW(),
    NOW()
WHERE @ucrew_game_id IS NOT NULL;

SELECT id, game_id, version, channel, file_name, file_size, sha256, status
FROM secure_patch_files
WHERE game_id=@ucrew_game_id
ORDER BY id DESC
LIMIT 5;
"@

    $sql | Set-Content -LiteralPath $sqlPath -Encoding UTF8

    Write-Host ""
    Write-Host "U-CREW GÜVENLİ YAMA PAKETİ HAZIR" -ForegroundColor Green
    Write-Host "Oyun        : $gameName"
    Write-Host "Slug        : $gameSlug"
    Write-Host "Sürüm       : $Version"
    Write-Host "Kanal       : $Channel"
    Write-Host "Dosya sayısı: $entryCount"
    Write-Host "Şifreli     : $encryptedPath"
    Write-Host "Boyut       : $fileSize bayt"
    Write-Host "SHA-256     : $sha256"
    Write-Host "Metadata    : $metadataPath"
    Write-Host "Profil      : $profileCopyPath"
    Write-Host "SQL         : $sqlPath"
    Write-Host ""
    Write-Host "Anahtar ve IV değerlerini yalnızca güvenli veritabanında tutun." -ForegroundColor Yellow

    [System.Windows.Forms.MessageBox]::Show(
        "Güvenli yama paketi hazırlandı.`n`nOyun: $gameName`nSürüm: $Version`nDosya: $encryptedName",
        "U-CREW Güvenli Yama Hazırlayıcı",
        [System.Windows.Forms.MessageBoxButtons]::OK,
        [System.Windows.Forms.MessageBoxIcon]::Information
    ) | Out-Null
}
finally {
    if (Test-Path -LiteralPath $tempRoot) {
        Remove-Item -LiteralPath $tempRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
}
