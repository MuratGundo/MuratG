param(
    [string]$HostName = "185.8.129.202",
    [string]$SshUser = "root",
    [string]$DatabaseName = "ucrewnet_ucrew_patch_v3",
    [switch]$SkipDatabase
)

$ErrorActionPreference = "Stop"
$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$stamp = Get-Date -Format "yyyyMMdd_HHmmss"
$remoteName = "ucrew_secure_patch_$stamp"
$tempZip = Join-Path $env:TEMP "$remoteName.zip"

function Require-Command([string]$Name) {
    if (-not (Get-Command $Name -ErrorAction SilentlyContinue)) {
        throw "$Name bulunamadı. Windows Ayarlar > İsteğe Bağlı Özellikler bölümünden OpenSSH Client kurulu olmalıdır."
    }
}

Require-Command "ssh"
Require-Command "scp"

Write-Host ""
Write-Host "U-CREW GENEL GÜVENLİ YAMA SİSTEMİ — VPS KURULUMU" -ForegroundColor Green
Write-Host "Sunucu : $SshUser@$HostName"
Write-Host "Veritabanı: $DatabaseName"
Write-Host ""

if (Test-Path -LiteralPath $tempZip) {
    Remove-Item -LiteralPath $tempZip -Force
}

$items = Get-ChildItem -LiteralPath $scriptRoot -Force |
    Where-Object { $_.Name -notin @("DEPLOY_FROM_WINDOWS.ps1", "DEPLOY_FROM_WINDOWS.bat") }

Compress-Archive -Path $items.FullName -DestinationPath $tempZip -CompressionLevel Optimal -Force

try {
    Write-Host "[1/5] Paket VPS'e yükleniyor..." -ForegroundColor Cyan
    & scp $tempZip ("{0}@{1}:/root/{2}.zip" -f $SshUser, $HostName, $remoteName)
    if ($LASTEXITCODE -ne 0) { throw "SCP yükleme başarısız. Kod: $LASTEXITCODE" }

    Write-Host "[2/5] Sunucu dosyaları kuruluyor..." -ForegroundColor Cyan
    $remoteInstall = @"
set -e
rm -rf /root/$remoteName
mkdir -p /root/$remoteName
unzip -oq /root/$remoteName.zip -d /root/$remoteName
chmod +x /root/$remoteName/INSTALL_VPS.sh
bash /root/$remoteName/INSTALL_VPS.sh
"@

    & ssh -t ("{0}@{1}" -f $SshUser, $HostName) $remoteInstall
    if ($LASTEXITCODE -ne 0) { throw "VPS kurulumu başarısız. Kod: $LASTEXITCODE" }

    if (-not $SkipDatabase) {
        Write-Host "[3/5] MySQL şeması kuruluyor..." -ForegroundColor Cyan
        Write-Host "MariaDB root şifresini birazdan gir." -ForegroundColor Yellow

        $remoteSql = "mysql -u root -p '$DatabaseName' < /root/$remoteName/database/INSTALL_SCHEMA.sql"
        & ssh -t ("{0}@{1}" -f $SshUser, $HostName) $remoteSql
        if ($LASTEXITCODE -ne 0) { throw "MySQL şeması kurulamadı. Kod: $LASTEXITCODE" }
    }
    else {
        Write-Host "[3/5] MySQL adımı atlandı." -ForegroundColor DarkYellow
    }

    Write-Host "[4/5] API uç noktası kontrol ediliyor..." -ForegroundColor Cyan
    $testCommand = "curl -sS -X POST https://api.u-crew.net/api/secure_patch_request.php -d 'slug=guardians' -d 'hwid=test'"
    & ssh ("{0}@{1}" -f $SshUser, $HostName) $testCommand
    if ($LASTEXITCODE -ne 0) { throw "API testi çalıştırılamadı. Kod: $LASTEXITCODE" }

    Write-Host ""
    Write-Host "[5/5] Kurulum tamamlandı." -ForegroundColor Green
    Write-Host "Beklenen test sonucu JSON biçiminde oturum/lisans hatasıdır." -ForegroundColor Green
    Write-Host ""
    Write-Host "Sonraki adım: tools\UCREW_SECURE_PATCH_PACK.bat ile ilk .ucp paketini üret." -ForegroundColor Yellow
}
finally {
    if (Test-Path -LiteralPath $tempZip) {
        Remove-Item -LiteralPath $tempZip -Force -ErrorAction SilentlyContinue
    }
}
