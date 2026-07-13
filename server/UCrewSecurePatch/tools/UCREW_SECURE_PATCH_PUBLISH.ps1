param(
    [string]$MetadataPath,
    [string]$HostName = "185.8.129.202",
    [string]$SshUser = "root",
    [string]$DatabaseName = "ucrewnet_ucrew_patch_v3"
)

$ErrorActionPreference = "Stop"

Add-Type -AssemblyName System.Windows.Forms

function Require-Command([string]$Name) {
    if (-not (Get-Command $Name -ErrorAction SilentlyContinue)) {
        throw "$Name bulunamadi. Windows OpenSSH Client kurulu olmalidir."
    }
}

function Select-MetadataFile {
    $dialog = New-Object System.Windows.Forms.OpenFileDialog
    $dialog.Title = "U-CREW yama metadata dosyasini sec"
    $dialog.Filter = "U-CREW metadata (*_metadata.json)|*_metadata.json|JSON dosyasi (*.json)|*.json"
    $dialog.Multiselect = $false

    if ($dialog.ShowDialog() -ne [System.Windows.Forms.DialogResult]::OK) {
        return $null
    }

    return [System.IO.Path]::GetFullPath($dialog.FileName)
}

function Assert-SafeSlug([string]$Value) {
    if ($Value -notmatch '^[a-z0-9][a-z0-9._-]{1,99}$') {
        throw "Gecersiz game_slug: $Value"
    }
}

function Assert-SafeFileName([string]$Value) {
    if ([string]::IsNullOrWhiteSpace($Value) -or
        [System.IO.Path]::GetFileName($Value) -ne $Value -or
        $Value -match '[\x00-\x1F]') {
        throw "Gecersiz paket dosya adi: $Value"
    }
}

Require-Command "ssh"
Require-Command "scp"

if ($DatabaseName -notmatch '^[A-Za-z0-9_]+$') {
    throw "Veritabani adi yalnizca harf, rakam ve alt cizgi icerebilir."
}

if ([string]::IsNullOrWhiteSpace($MetadataPath)) {
    $MetadataPath = Select-MetadataFile
}

if ([string]::IsNullOrWhiteSpace($MetadataPath)) {
    Write-Host "Islem iptal edildi."
    exit 0
}

$MetadataPath = [System.IO.Path]::GetFullPath($MetadataPath)
if (-not (Test-Path -LiteralPath $MetadataPath -PathType Leaf)) {
    throw "Metadata dosyasi bulunamadi: $MetadataPath"
}

$metadata = Get-Content -LiteralPath $MetadataPath -Raw -Encoding UTF8 | ConvertFrom-Json
$gameSlug = ([string]$metadata.game_slug).Trim().ToLowerInvariant()
$version = ([string]$metadata.version).Trim()
$channel = ([string]$metadata.channel).Trim().ToLowerInvariant()
$encryptedFile = ([string]$metadata.encrypted_file).Trim()
$sha256 = ([string]$metadata.sha256).Trim().ToLowerInvariant()

Assert-SafeSlug $gameSlug
Assert-SafeFileName $encryptedFile

if ($sha256 -notmatch '^[a-f0-9]{64}$') {
    throw "Metadata SHA-256 degeri gecersiz."
}

$metadataDirectory = [System.IO.Path]::GetDirectoryName($MetadataPath)
$encryptedPath = Join-Path $metadataDirectory $encryptedFile
$sqlPath = Join-Path $metadataDirectory ("{0}_{1}_REGISTER.sql" -f $gameSlug, $channel)

if (-not (Test-Path -LiteralPath $encryptedPath -PathType Leaf)) {
    throw "Sifreli .ucp paketi bulunamadi: $encryptedPath"
}

if (-not (Test-Path -LiteralPath $sqlPath -PathType Leaf)) {
    throw "Veritabani kayit dosyasi bulunamadi: $sqlPath"
}

$actualSha = (Get-FileHash -LiteralPath $encryptedPath -Algorithm SHA256).Hash.ToLowerInvariant()
if ($actualSha -ne $sha256) {
    throw "Yerel .ucp paketinin SHA-256 degeri metadata ile eslesmiyor."
}

$sshTarget = "{0}@{1}" -f $SshUser, $HostName
$remoteDirectory = "/var/www/api.u-crew.net/secure_patches/$gameSlug"
$remotePackage = "$remoteDirectory/$encryptedFile"
$remoteSql = "/root/ucrew_register_${gameSlug}_$([DateTime]::UtcNow.ToString('yyyyMMdd_HHmmss')).sql"

Write-Host ""
Write-Host "U-CREW GUVENLI YAMA YAYINLAMA" -ForegroundColor Green
Write-Host "Oyun       : $gameSlug"
Write-Host "Surum      : $version"
Write-Host "Kanal      : $channel"
Write-Host "Paket      : $encryptedFile"
Write-Host "Sunucu     : $sshTarget"
Write-Host ""

Write-Host "[1/5] Sunucu klasoru hazirlaniyor..." -ForegroundColor Cyan
$prepareCommand = "install -d -o www-data -g www-data -m 0750 '$remoteDirectory'"
& ssh $sshTarget $prepareCommand
if ($LASTEXITCODE -ne 0) {
    throw "Sunucu yama klasoru hazirlanamadi. Kod: $LASTEXITCODE"
}

Write-Host "[2/5] Sifreli .ucp paketi yukleniyor..." -ForegroundColor Cyan
& scp $encryptedPath ("{0}:{1}" -f $sshTarget, $remotePackage)
if ($LASTEXITCODE -ne 0) {
    throw "Yama paketi yuklenemedi. Kod: $LASTEXITCODE"
}

Write-Host "[3/5] Paket izinleri ve SHA-256 kontrol ediliyor..." -ForegroundColor Cyan
$verifyCommand = "chown www-data:www-data '$remotePackage' && chmod 0640 '$remotePackage' && test `$(sha256sum '$remotePackage' | awk '{print `$1}') = '$sha256'"
& ssh $sshTarget $verifyCommand
if ($LASTEXITCODE -ne 0) {
    throw "Sunucu SHA-256 dogrulamasi basarisiz. Paket veritabanina kaydedilmedi."
}

Write-Host "[4/5] MySQL kaydi yukleniyor..." -ForegroundColor Cyan
& scp $sqlPath ("{0}:{1}" -f $sshTarget, $remoteSql)
if ($LASTEXITCODE -ne 0) {
    throw "SQL dosyasi VPS'e yuklenemedi. Kod: $LASTEXITCODE"
}

Write-Host "Once sifresiz socket girisi denenecek. Gerekirse MariaDB root sifresi istenecek." -ForegroundColor Yellow
$databaseCommand = "set -e; mysql '$DatabaseName' < '$remoteSql' || mysql -u root -p '$DatabaseName' < '$remoteSql'; rm -f '$remoteSql'"
& ssh -t $sshTarget $databaseCommand
if ($LASTEXITCODE -ne 0) {
    throw "Yama veritabanina kaydedilemedi. Kod: $LASTEXITCODE"
}

Write-Host "[5/5] Sunucu kaydi dogrulaniyor..." -ForegroundColor Cyan
$escapedSlug = $gameSlug.Replace("'", "''")
$verifySql = "SELECT g.slug,f.version,f.channel,f.file_name,f.file_size,f.sha256,f.status FROM secure_patch_files f INNER JOIN games g ON g.id=f.game_id WHERE LOWER(g.slug)=LOWER('$escapedSlug') ORDER BY f.id DESC LIMIT 3;"
$verifyDbCommand = "mysql -N -B '$DatabaseName' -e `"$verifySql`" || mysql -u root -p -N -B '$DatabaseName' -e `"$verifySql`""
& ssh -t $sshTarget $verifyDbCommand
if ($LASTEXITCODE -ne 0) {
    throw "Veritabani kaydi dogrulanamadi. Kod: $LASTEXITCODE"
}

Write-Host ""
Write-Host "YAMA BASARIYLA YAYINLANDI" -ForegroundColor Green
Write-Host "Oyun  : $gameSlug"
Write-Host "Surum : $version"
Write-Host "Paket : $remotePackage"
Write-Host "SHA   : $sha256"
Write-Host ""

[System.Windows.Forms.MessageBox]::Show(
    "Yama basariyla yayinlandi.`n`nOyun: $gameSlug`nSurum: $version`nKanal: $channel",
    "U-CREW Guvenli Yama",
    [System.Windows.Forms.MessageBoxButtons]::OK,
    [System.Windows.Forms.MessageBoxIcon]::Information
) | Out-Null
