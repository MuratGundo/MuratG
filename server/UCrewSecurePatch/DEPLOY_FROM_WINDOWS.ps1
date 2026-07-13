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
$sshTarget = "{0}@{1}" -f $SshUser, $HostName

function Require-Command([string]$Name) {
    if (-not (Get-Command $Name -ErrorAction SilentlyContinue)) {
        throw "$Name bulunamadi. Windows Ayarlar > Istega Bagli Ozellikler bolumunden OpenSSH Client kurulu olmalidir."
    }
}

Require-Command "ssh"
Require-Command "scp"

if ($DatabaseName -notmatch '^[A-Za-z0-9_]+$') {
    throw "Veritabani adi yalnizca harf, rakam ve alt cizgi icerebilir."
}

Write-Host ""
Write-Host "U-CREW GENEL GUVENLI YAMA SISTEMI - VPS KURULUMU" -ForegroundColor Green
Write-Host "Sunucu    : $sshTarget"
Write-Host "Veritabani: $DatabaseName"
Write-Host ""

if (Test-Path -LiteralPath $tempZip) {
    Remove-Item -LiteralPath $tempZip -Force
}

$items = Get-ChildItem -LiteralPath $scriptRoot -Force |
    Where-Object { $_.Name -notin @("DEPLOY_FROM_WINDOWS.ps1", "DEPLOY_FROM_WINDOWS.bat") }

if (-not $items) {
    throw "Kurulum paketinde gonderilecek dosya bulunamadi."
}

Compress-Archive -Path $items.FullName -DestinationPath $tempZip -CompressionLevel Optimal -Force

try {
    Write-Host "[1/5] Paket VPS'e yukleniyor..." -ForegroundColor Cyan
    & scp $tempZip ("{0}:/root/{1}.zip" -f $sshTarget, $remoteName)
    if ($LASTEXITCODE -ne 0) {
        throw "SCP yukleme basarisiz. Kod: $LASTEXITCODE"
    }

    Write-Host "[2/5] Sunucu dosyalari kuruluyor..." -ForegroundColor Cyan

    # Windows PowerShell 5.1 indented here-string kapanisini desteklemez.
    # Bu nedenle uzak komutlar dizi olarak olusturulup tek satirda birlestirilir.
    $remoteInstall = @(
        "set -e",
        "rm -rf /root/$remoteName",
        "mkdir -p /root/$remoteName",
        "unzip -oq /root/$remoteName.zip -d /root/$remoteName",
        "chmod +x /root/$remoteName/INSTALL_VPS.sh",
        "bash /root/$remoteName/INSTALL_VPS.sh"
    ) -join "; "

    & ssh -t $sshTarget $remoteInstall
    if ($LASTEXITCODE -ne 0) {
        throw "VPS kurulumu basarisiz. Kod: $LASTEXITCODE"
    }

    if (-not $SkipDatabase) {
        Write-Host "[3/5] MySQL semasi kuruluyor..." -ForegroundColor Cyan
        Write-Host "Once sifresiz socket girisi denenecek. Gerekirse MariaDB root sifresi istenecek." -ForegroundColor Yellow

        $remoteSqlPath = "/root/$remoteName/database/INSTALL_SCHEMA.sql"
        $remoteSql = "mysql $DatabaseName < $remoteSqlPath || mysql -u root -p $DatabaseName < $remoteSqlPath"

        & ssh -t $sshTarget $remoteSql
        if ($LASTEXITCODE -ne 0) {
            throw "MySQL semasi kurulamadi. Kod: $LASTEXITCODE"
        }
    }
    else {
        Write-Host "[3/5] MySQL adimi atlandi." -ForegroundColor DarkYellow
    }

    Write-Host "[4/5] API uc noktasi kontrol ediliyor..." -ForegroundColor Cyan
    $testCommand = "curl -sS -X POST https://api.u-crew.net/api/secure_patch_request.php -d slug=guardians -d hwid=test"

    & ssh $sshTarget $testCommand
    if ($LASTEXITCODE -ne 0) {
        throw "API testi calistirilamadi. Kod: $LASTEXITCODE"
    }

    Write-Host ""
    Write-Host "[5/5] Kurulum tamamlandi." -ForegroundColor Green
    Write-Host "Beklenen test sonucu JSON biciminde oturum veya lisans hatasidir." -ForegroundColor Green
    Write-Host ""
    Write-Host "Sonraki adim: tools\UCREW_SECURE_PATCH_PACK.bat ile ilk .ucp paketini uret." -ForegroundColor Yellow
}
finally {
    if (Test-Path -LiteralPath $tempZip) {
        Remove-Item -LiteralPath $tempZip -Force -ErrorAction SilentlyContinue
    }
}
