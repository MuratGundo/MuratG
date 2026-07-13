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

function Select-JsonFile([string]$Title) {
    $dialog = New-Object System.Windows.Forms.OpenFileDialog
    $dialog.Title = $Title
    $dialog.Filter = "JSON dosyası (*.json)|*.json"
    if ($dialog.ShowDialog() -ne [System.Windows.Forms.DialogResult]::OK) { return $null }
    return [System.IO.Path]::GetFullPath($dialog.FileName)
}

function Select-Source {
    $choice = [System.Windows.Forms.MessageBox]::Show(
        "Yama kaynağı ZIP dosyası mı?`n`nEvet: ZIP seç`nHayır: Klasör seç",
        "U-CREW Güvenli Yama Hazırlayıcı",
        [System.Windows.Forms.MessageBoxButtons]::YesNoCancel,
        [System.Windows.Forms.MessageBoxIcon]::Question)

    if ($choice -eq [System.Windows.Forms.DialogResult]::Cancel) { return $null }

    if ($choice -eq [System.Windows.Forms.DialogResult]::Yes) {
        $dialog = New-Object System.Windows.Forms.OpenFileDialog
        $dialog.Title = "Yama ZIP dosyasını seç"
        $dialog.Filter = "ZIP dosyası (*.zip)|*.zip"
        if ($dialog.ShowDialog() -ne [System.Windows.Forms.DialogResult]::OK) { return $null }
        return [System.IO.Path]::GetFullPath($dialog.FileName)
    }

    $folder = New-Object System.Windows.Forms.FolderBrowserDialog
    $folder.Description = "Yama dosyalarının bulunduğu klasörü seç"
    $folder.ShowNewFolderButton = $false
    if ($folder.ShowDialog() -ne [System.Windows.Forms.DialogResult]::OK) { return $null }
    return [System.IO.Path]::GetFullPath($folder.SelectedPath)
}

function Select-Output {
    $dialog = New-Object System.Windows.Forms.FolderBrowserDialog
    $dialog.Description = "Çıktı klasörünü seç"
    $dialog.ShowNewFolderButton = $true
    if ($dialog.ShowDialog() -ne [System.Windows.Forms.DialogResult]::OK) { return $null }
    return [System.IO.Path]::GetFullPath($dialog.SelectedPath)
}

function Escape-Sql([string]$Value) {
    if ($null -eq $Value) { $Value = "" }
    return ($Value -replace "'", "''")
}

function New-RandomBytes([int]$Length) {
    $bytes = New-Object byte[] $Length
    $rng = [System.Security.Cryptography.RandomNumberGenerator]::Create()
    try { $rng.GetBytes($bytes) } finally { $rng.Dispose() }
    return $bytes
}

function Normalize-Extensions($Values) {
    $list = New-Object System.Collections.Generic.List[string]
    foreach ($item in @($Values)) {
        $extension = ([string]$item).Trim().ToLowerInvariant()
        if ([string]::IsNullOrWhiteSpace($extension)) { continue }
        if (-not $extension.StartsWith(".")) { $extension = "." + $extension }
        if ($extension -notmatch '^\.[a-z0-9]+$') { throw "Geçersiz uzantı: $extension" }
        if (-not $list.Contains($extension)) { $list.Add($extension) }
    }
    if ($list.Count -eq 0) { throw "allowed_extensions boş olamaz." }
    return $list.ToArray()
}

function Normalize-RelativePath([string]$Value, [string]$Name, [bool]$AllowEmpty) {
    if ([string]::IsNullOrWhiteSpace($Value)) {
        if ($AllowEmpty) { return "" }
        throw "$Name boş olamaz."
    }

    if ([System.IO.Path]::IsPathRooted($Value)) { throw "$Name mutlak yol olamaz." }
    $normalized = $Value.Replace('\', '/').Trim('/')
    if ($normalized.Split('/') -contains '..') { throw "$Name içinde .. kullanılamaz." }
    return $normalized
}

function Test-Zip([string]$Path, [string[]]$AllowedExtensions, [bool]$PreserveTree) {
    $archive = [System.IO.Compression.ZipFile]::OpenRead($Path)
    try {
        $files = @($archive.Entries | Where-Object { -not [string]::IsNullOrWhiteSpace($_.Name) })
        if ($files.Count -eq 0) { throw "ZIP paketi boş." }
        $flatNames = @{}

        foreach ($entry in $files) {
            $fullName = $entry.FullName.Replace('\', '/')
            if ($fullName.StartsWith('/') -or $fullName.Split('/') -contains '..') {
                throw "ZIP içinde güvenli olmayan yol: $fullName"
            }

            $extension = [System.IO.Path]::GetExtension($entry.Name).ToLowerInvariant()
            if ($AllowedExtensions -notcontains $extension) {
                throw "İzin verilmeyen dosya türü: $fullName"
            }

            if (-not $PreserveTree) {
                $flatName = [System.IO.Path]::GetFileName($entry.Name).ToLowerInvariant()
                if ($flatNames.ContainsKey($flatName)) { throw "Aynı isimli iki dosya var: $flatName" }
                $flatNames[$flatName] = $true
            }
        }

        return $files.Count
    }
    finally { $archive.Dispose() }
}

function Encrypt-Zip([string]$InputPath, [string]$OutputPath, [byte[]]$Key, [byte[]]$Iv) {
    $aes = [System.Security.Cryptography.Aes]::Create()
    $aes.KeySize = 256
    $aes.BlockSize = 128
    $aes.Mode = [System.Security.Cryptography.CipherMode]::CBC
    $aes.Padding = [System.Security.Cryptography.PaddingMode]::PKCS7
    $aes.Key = $Key
    $aes.IV = $Iv

    $input = [System.IO.File]::OpenRead($InputPath)
    $output = [System.IO.File]::Create($OutputPath)
    $crypto = New-Object System.Security.Cryptography.CryptoStream(
        $output, $aes.CreateEncryptor(), [System.Security.Cryptography.CryptoStreamMode]::Write)

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

if ([string]::IsNullOrWhiteSpace($ProfilePath)) { $ProfilePath = Select-JsonFile "Oyun profilini seç" }
if ([string]::IsNullOrWhiteSpace($ProfilePath)) { exit 0 }
if (-not (Test-Path -LiteralPath $ProfilePath -PathType Leaf)) { throw "Profil bulunamadı: $ProfilePath" }

$profile = Get-Content -LiteralPath $ProfilePath -Raw -Encoding UTF8 | ConvertFrom-Json
$slug = ([string]$profile.game_slug).Trim().ToLowerInvariant()
$name = ([string]$profile.game_name).Trim()
$gameExe = Normalize-RelativePath ([string]$profile.game_exe) "game_exe" $false
$targetPath = Normalize-RelativePath ([string]$profile.target_path) "target_path" $true
$installMode = ([string]$profile.install_mode).Trim().ToLowerInvariant()
$extensions = Normalize-Extensions $profile.allowed_extensions
$cleanupOnExit = [bool]$profile.cleanup_on_exit
$preserveTree = if ($null -eq $profile.preserve_directory_tree) { $true } else { [bool]$profile.preserve_directory_tree }
$requiresBootstrap = if ($null -eq $profile.requires_bootstrap) { $false } else { [bool]$profile.requires_bootstrap }
$bootstrapType = if ([string]::IsNullOrWhiteSpace([string]$profile.bootstrap_type)) { "launcher" } else { ([string]$profile.bootstrap_type).Trim().ToLowerInvariant() }
$waitForExit = if ($null -eq $profile.wait_for_game_exit) { $true } else { [bool]$profile.wait_for_game_exit }
$backupExisting = if ($null -eq $profile.backup_existing_files) { $true } else { [bool]$profile.backup_existing_files }

if ($slug -notmatch '^[a-z0-9][a-z0-9._-]{1,99}$') { throw "Geçersiz game_slug: $slug" }
if ([string]::IsNullOrWhiteSpace($name)) { throw "game_name boş olamaz." }
if (@('overlay_flat','overlay_tree','replace_files','archive_replace','custom') -notcontains $installMode) { throw "Geçersiz install_mode: $installMode" }
if ($Channel -notmatch '^[a-z0-9._-]{1,32}$') { throw "Geçersiz kanal: $Channel" }

if ([string]::IsNullOrWhiteSpace($SourcePath)) { $SourcePath = Select-Source }
if ([string]::IsNullOrWhiteSpace($SourcePath)) { exit 0 }
$SourcePath = [System.IO.Path]::GetFullPath($SourcePath)

if ([string]::IsNullOrWhiteSpace($OutputDirectory)) { $OutputDirectory = Select-Output }
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) { exit 0 }
$OutputDirectory = [System.IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null

$tempRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("UCREW_PACK_" + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $tempRoot | Out-Null

try {
    if (Test-Path -LiteralPath $SourcePath -PathType Container) {
        $invalid = @(Get-ChildItem -LiteralPath $SourcePath -File -Recurse | Where-Object { $extensions -notcontains $_.Extension.ToLowerInvariant() })
        if ($invalid.Count -gt 0) { throw "İzin verilmeyen dosya: $($invalid[0].FullName)" }
        $sourceZip = Join-Path $tempRoot "source.zip"
        [System.IO.Compression.ZipFile]::CreateFromDirectory($SourcePath, $sourceZip, [System.IO.Compression.CompressionLevel]::Optimal, $false)
    }
    elseif (Test-Path -LiteralPath $SourcePath -PathType Leaf) {
        if ([System.IO.Path]::GetExtension($SourcePath).ToLowerInvariant() -ne '.zip') { throw "Kaynak dosya ZIP olmalıdır." }
        $sourceZip = $SourcePath
    }
    else { throw "Kaynak bulunamadı: $SourcePath" }

    $entryCount = Test-Zip $sourceZip $extensions $preserveTree
    $safeVersion = ($Version -replace '[^a-zA-Z0-9._-]+','_').Trim('_')
    if ([string]::IsNullOrWhiteSpace($safeVersion)) { $safeVersion = '1.0.0' }

    $stamp = Get-Date -Format 'yyyyMMdd_HHmmss'
    $fileName = "${slug}_${Channel}_v${safeVersion}_${stamp}.ucp"
    $encryptedPath = Join-Path $OutputDirectory $fileName
    $metadataPath = Join-Path $OutputDirectory "${slug}_${Channel}_metadata.json"
    $sqlPath = Join-Path $OutputDirectory "${slug}_${Channel}_REGISTER.sql"
    $profileOutput = Join-Path $OutputDirectory "${slug}_profile.json"

    $key = New-RandomBytes 32
    $iv = New-RandomBytes 16
    Encrypt-Zip $sourceZip $encryptedPath $key $iv

    $sha = (Get-FileHash -LiteralPath $encryptedPath -Algorithm SHA256).Hash.ToLowerInvariant()
    $size = (Get-Item -LiteralPath $encryptedPath).Length
    $key64 = [Convert]::ToBase64String($key)
    $iv64 = [Convert]::ToBase64String($iv)
    $serverPath = "secure_patches/$slug/$fileName"

    $runtimeProfile = [ordered]@{
        profile_version = 1
        game_slug = $slug
        game_name = $name
        game_exe = $gameExe
        install_mode = $installMode
        target_path = $targetPath
        allowed_extensions = $extensions
        cleanup_on_exit = $cleanupOnExit
        preserve_directory_tree = $preserveTree
        requires_bootstrap = $requiresBootstrap
        bootstrap_type = $bootstrapType
        wait_for_game_exit = $waitForExit
        backup_existing_files = $backupExisting
        theme = $profile.theme
    }

    $profileJson = $runtimeProfile | ConvertTo-Json -Depth 10 -Compress
    $metadata = [ordered]@{
        system = 'U-CREW Secure Patch'
        package_format = 'zip-aes256-cbc-v1'
        game_slug = $slug
        game_name = $name
        version = $Version
        channel = $Channel
        encrypted_file = $fileName
        server_relative_path = $serverPath
        source_entries = $entryCount
        file_size = $size
        sha256 = $sha
        key_base64 = $key64
        iv_base64 = $iv64
        runtime_profile = $runtimeProfile
        created_at = (Get-Date).ToString('o')
    }

    $metadata | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $metadataPath -Encoding UTF8
    $runtimeProfile | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $profileOutput -Encoding UTF8

    $sql = @"
SET @ucrew_game_id := (SELECT id FROM games WHERE LOWER(slug)=LOWER('$(Escape-Sql $slug)') LIMIT 1);
SELECT @ucrew_game_id AS game_id;
UPDATE secure_patch_files SET status='archived', updated_at=NOW()
WHERE game_id=@ucrew_game_id AND channel='$(Escape-Sql $Channel)' AND status='active';
INSERT INTO secure_patch_files
(game_id,version,channel,package_format,file_path,file_name,file_size,sha256,key_base64,iv_base64,runtime_profile_json,status,created_at,updated_at)
SELECT @ucrew_game_id,'$(Escape-Sql $Version)','$(Escape-Sql $Channel)','zip-aes256-cbc-v1','$(Escape-Sql $serverPath)','$(Escape-Sql $fileName)',$size,'$(Escape-Sql $sha)','$(Escape-Sql $key64)','$(Escape-Sql $iv64)','$(Escape-Sql $profileJson)','active',NOW(),NOW()
WHERE @ucrew_game_id IS NOT NULL;
SELECT id,game_id,version,channel,file_name,file_size,sha256,status FROM secure_patch_files
WHERE game_id=@ucrew_game_id ORDER BY id DESC LIMIT 5;
"@
    $sql | Set-Content -LiteralPath $sqlPath -Encoding UTF8

    Write-Host ""
    Write-Host "U-CREW GÜVENLİ YAMA PAKETİ HAZIR" -ForegroundColor Green
    Write-Host "Oyun   : $name"
    Write-Host "Slug   : $slug"
    Write-Host "Sürüm  : $Version"
    Write-Host "Paket  : $encryptedPath"
    Write-Host "SHA-256: $sha"
    Write-Host "SQL    : $sqlPath"

    [System.Windows.Forms.MessageBox]::Show(
        "Paket hazırlandı.`n`nOyun: $name`nSürüm: $Version`nDosya: $fileName",
        "U-CREW Güvenli Yama Hazırlayıcı",
        [System.Windows.Forms.MessageBoxButtons]::OK,
        [System.Windows.Forms.MessageBoxIcon]::Information) | Out-Null
}
finally {
    if (Test-Path -LiteralPath $tempRoot) { Remove-Item -LiteralPath $tempRoot -Recurse -Force -ErrorAction SilentlyContinue }
}
