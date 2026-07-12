param()

$ErrorActionPreference = "Stop"

Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

function Select-Folder {
    param([string]$Description)

    $dialog = New-Object System.Windows.Forms.FolderBrowserDialog
    $dialog.Description = $Description
    $dialog.ShowNewFolderButton = $false

    if ($dialog.ShowDialog() -ne [System.Windows.Forms.DialogResult]::OK) {
        return $null
    }

    return [System.IO.Path]::GetFullPath($dialog.SelectedPath)
}

$SourceFolder = Select-Folder "Çevrilmiş LANDb ve font dosyalarının bulunduğu klasörü seç"

if (-not $SourceFolder) {
    Write-Host "İşlem iptal edildi."
    exit 0
}

$AllowedExtensions = @(
    ".landb",
    ".font",
    ".fnt",
    ".dds",
    ".d3dtx"
)

$Files = Get-ChildItem -Path $SourceFolder -File -Recurse |
    Where-Object {
        $AllowedExtensions -contains $_.Extension.ToLowerInvariant()
    } |
    Sort-Object Name, FullName

if (-not $Files -or $Files.Count -eq 0) {
    throw "Desteklenen yama dosyası bulunamadı."
}

$DuplicateGroups = $Files |
    Group-Object { $_.Name.ToLowerInvariant() } |
    Where-Object { $_.Count -gt 1 }

if ($DuplicateGroups) {
    $Report = Join-Path $SourceFolder "UCREW_ZIP_AYNI_ADLI_DOSYALAR.txt"
    $Lines = New-Object System.Collections.Generic.List[string]

    $Lines.Add("Aynı dosya adına sahip birden fazla kaynak bulundu.")
    $Lines.Add("ZIP kökünde dosya adları tekil olmalıdır.")
    $Lines.Add("")

    foreach ($Group in $DuplicateGroups) {
        $Lines.Add("DOSYA: $($Group.Name)")

        foreach ($Item in $Group.Group) {
            $Lines.Add("  $($Item.FullName)")
        }

        $Lines.Add("")
    }

    $Lines | Set-Content -Path $Report -Encoding UTF8
    throw "Aynı adlı dosyalar bulundu. Rapor: $Report"
}

$SaveDialog = New-Object System.Windows.Forms.SaveFileDialog
$SaveDialog.Title = "UCREW_Guardians_TR.zip dosyasını kaydet"
$SaveDialog.Filter = "ZIP dosyası (*.zip)|*.zip"
$SaveDialog.FileName = "UCREW_Guardians_TR.zip"
$SaveDialog.OverwritePrompt = $true

if ($SaveDialog.ShowDialog() -ne [System.Windows.Forms.DialogResult]::OK) {
    Write-Host "İşlem iptal edildi."
    exit 0
}

$OutputZip = [System.IO.Path]::GetFullPath($SaveDialog.FileName)

if (Test-Path $OutputZip) {
    Remove-Item $OutputZip -Force
}

$Stream = [System.IO.File]::Open(
    $OutputZip,
    [System.IO.FileMode]::CreateNew,
    [System.IO.FileAccess]::ReadWrite,
    [System.IO.FileShare]::None
)

try {
    $Archive = New-Object System.IO.Compression.ZipArchive(
        $Stream,
        [System.IO.Compression.ZipArchiveMode]::Create,
        $false
    )

    try {
        foreach ($File in $Files) {
            [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
                $Archive,
                $File.FullName,
                $File.Name,
                [System.IO.Compression.CompressionLevel]::Optimal
            ) | Out-Null

            Write-Host "Eklendi: $($File.Name)"
        }
    }
    finally {
        $Archive.Dispose()
    }
}
finally {
    $Stream.Dispose()
}

Write-Host ""
Write-Host "ZIP hazırlandı." -ForegroundColor Green
Write-Host "Dosya sayısı: $($Files.Count)"
Write-Host "Çıktı: $OutputZip"
