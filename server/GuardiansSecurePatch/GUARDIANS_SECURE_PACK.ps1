param(
    [string]$ZipPath,
    [string]$OutputDirectory,
    [string]$Version = "1.0.0",
    [string]$GameSlug = "guardians"
)

$ErrorActionPreference = "Stop"

Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Security

function Select-ZipFile {
    $dialog = New-Object System.Windows.Forms.OpenFileDialog
    $dialog.Title = "UCREW_Guardians_TR.zip dosyasını seç"
    $dialog.Filter = "ZIP dosyası (*.zip)|*.zip"
    $dialog.Multiselect = $false

    if ($dialog.ShowDialog() -ne [System.Windows.Forms.DialogResult]::OK) {
        return $null
    }

    return [System.IO.Path]::GetFullPath($dialog.FileName)
}

function Select-OutputFolder {
    $dialog = New-Object System.Windows.Forms.FolderBrowserDialog
    $dialog.Description = "Şifreli sunucu paketinin kaydedileceği klasörü seç"
    $dialog.ShowNewFolderButton = $true

    if ($dialog.ShowDialog() -ne [System.Windows.Forms.DialogResult]::OK) {
        return $null
    }

    return [System.IO.Path]::GetFullPath($dialog.SelectedPath)
}

function Escape-SqlLiteral {
    param([string]$Value)
    return ($Value -replace "'", "''")
}

if ([string]::IsNullOrWhiteSpace($ZipPath)) {
    $ZipPath = Select-ZipFile
}

if ([string]::IsNullOrWhiteSpace($ZipPath)) {
    Write-Host "İşlem iptal edildi."
    exit 0
}

$ZipPath = [System.IO.Path]::GetFullPath($ZipPath)

if (-not (Test-Path -LiteralPath $ZipPath -PathType Leaf)) {
    throw "ZIP bulunamadı: $ZipPath"
}

if ([System.IO.Path]::GetExtension($ZipPath) -ne ".zip") {
    throw "Kaynak dosya ZIP olmalıdır."
}

if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Select-OutputFolder
}

if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    Write-Host "İşlem iptal edildi."
    exit 0
}

$OutputDirectory = [System.IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null

$SafeVersion = ($Version -replace '[^a-zA-Z0-9._-]+', '_').Trim('_')
if ([string]::IsNullOrWhiteSpace($SafeVersion)) {
    $SafeVersion = "1.0.0"
}

$Timestamp = Get-Date -Format "yyyyMMdd_HHmmss"
$EncryptedName = "guardians_v${SafeVersion}_${Timestamp}.ucrew"
$EncryptedPath = Join-Path $OutputDirectory $EncryptedName
$MetadataPath = Join-Path $OutputDirectory "guardians_secure_metadata.json"
$SqlPath = Join-Path $OutputDirectory "REGISTER_IN_DATABASE.sql"

$Key = New-Object byte[] 32
$Iv = New-Object byte[] 16
[System.Security.Cryptography.RandomNumberGenerator]::Fill($Key)
[System.Security.Cryptography.RandomNumberGenerator]::Fill($Iv)

$Aes = [System.Security.Cryptography.Aes]::Create()
$Aes.KeySize = 256
$Aes.BlockSize = 128
$Aes.Mode = [System.Security.Cryptography.CipherMode]::CBC
$Aes.Padding = [System.Security.Cryptography.PaddingMode]::PKCS7
$Aes.Key = $Key
$Aes.IV = $Iv

$Input = [System.IO.File]::OpenRead($ZipPath)
$Output = [System.IO.File]::Create($EncryptedPath)
$Crypto = New-Object System.Security.Cryptography.CryptoStream(
    $Output,
    $Aes.CreateEncryptor(),
    [System.Security.Cryptography.CryptoStreamMode]::Write
)

try {
    $Input.CopyTo($Crypto)
    $Crypto.FlushFinalBlock()
}
finally {
    $Crypto.Dispose()
    $Output.Dispose()
    $Input.Dispose()
    $Aes.Dispose()
}

$Sha256 = (Get-FileHash -LiteralPath $EncryptedPath -Algorithm SHA256).Hash.ToLowerInvariant()
$FileSize = (Get-Item -LiteralPath $EncryptedPath).Length
$KeyBase64 = [Convert]::ToBase64String($Key)
$IvBase64 = [Convert]::ToBase64String($Iv)
$ServerRelativePath = "secure_patches/guardians/$EncryptedName"

$Metadata = [ordered]@{
    game_slug = $GameSlug
    version = $Version
    encrypted_file = $EncryptedName
    server_relative_path = $ServerRelativePath
    file_size = $FileSize
    sha256 = $Sha256
    key_base64 = $KeyBase64
    iv_base64 = $IvBase64
    created_at = (Get-Date).ToString("o")
}

$Metadata |
    ConvertTo-Json -Depth 5 |
    Set-Content -LiteralPath $MetadataPath -Encoding UTF8

$SqlSlug = Escape-SqlLiteral $GameSlug
$SqlVersion = Escape-SqlLiteral $Version
$SqlPathValue = Escape-SqlLiteral $ServerRelativePath
$SqlFileName = Escape-SqlLiteral $EncryptedName
$SqlSha = Escape-SqlLiteral $Sha256
$SqlKey = Escape-SqlLiteral $KeyBase64
$SqlIv = Escape-SqlLiteral $IvBase64

$Sql = @"
-- U-CREW Guardians güvenli yama kaydı
-- Önce şifreli dosyayı şu konuma yükleyin:
-- secure_patches/guardians/$EncryptedName

SET @ucrew_guardians_game_id := (
    SELECT id FROM games WHERE LOWER(slug)=LOWER('$SqlSlug') LIMIT 1
);

-- @ucrew_guardians_game_id NULL ise panelde slug='$SqlSlug' olan oyunu oluşturun.
SELECT @ucrew_guardians_game_id AS guardians_game_id;

UPDATE secure_patch_files
SET status='archived', updated_at=NOW()
WHERE game_id=@ucrew_guardians_game_id AND status='active';

INSERT INTO secure_patch_files
(game_id, version, file_path, file_name, file_size, sha256,
 key_base64, iv_base64, status, created_at, updated_at)
SELECT
    @ucrew_guardians_game_id,
    '$SqlVersion',
    '$SqlPathValue',
    '$SqlFileName',
    $FileSize,
    '$SqlSha',
    '$SqlKey',
    '$SqlIv',
    'active',
    NOW(),
    NOW()
WHERE @ucrew_guardians_game_id IS NOT NULL;

SELECT id, game_id, version, file_name, file_size, sha256, status
FROM secure_patch_files
WHERE game_id=@ucrew_guardians_game_id
ORDER BY id DESC
LIMIT 3;
"@

$Sql | Set-Content -LiteralPath $SqlPath -Encoding UTF8

Write-Host ""
Write-Host "ŞİFRELİ GUARDIANS PAKETİ HAZIR" -ForegroundColor Green
Write-Host "Kaynak ZIP : $ZipPath"
Write-Host "Şifreli    : $EncryptedPath"
Write-Host "Boyut       : $FileSize bayt"
Write-Host "SHA-256     : $Sha256"
Write-Host "Metadata    : $MetadataPath"
Write-Host "SQL         : $SqlPath"
Write-Host ""
Write-Host "Anahtar ve IV değerlerini yalnızca veritabanındaki secure_patch_files kaydında tutun." -ForegroundColor Yellow
