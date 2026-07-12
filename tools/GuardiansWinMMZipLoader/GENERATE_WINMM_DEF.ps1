param(
    [string]$OutputPath = (Join-Path $PSScriptRoot "winmm_proxy.generated.def")
)

$ErrorActionPreference = "Stop"

$DumpBinCandidates = @()
$VisualStudioRoots = @(
    (Join-Path $env:ProgramFiles "Microsoft Visual Studio"),
    (Join-Path ${env:ProgramFiles(x86)} "Microsoft Visual Studio")
) | Where-Object { $_ -and (Test-Path $_) }

foreach ($Root in $VisualStudioRoots) {
    $DumpBinCandidates += Get-ChildItem `
        -Path $Root `
        -Filter dumpbin.exe `
        -File `
        -Recurse `
        -ErrorAction SilentlyContinue |
        Where-Object {
            $_.FullName -match "Hostx64\\x64\\dumpbin\.exe$"
        }
}

$DumpBin = $DumpBinCandidates |
    Sort-Object FullName -Descending |
    Select-Object -First 1

if (-not $DumpBin) {
    throw "dumpbin.exe bulunamadı. Visual Studio C++ araçları kurulu olmalı."
}

$RealWinMM = Join-Path $env:SystemRoot "System32\winmm.dll"

if (-not (Test-Path $RealWinMM)) {
    throw "Gerçek System32 winmm.dll bulunamadı: $RealWinMM"
}

$DumpLines = & $DumpBin.FullName /nologo /exports $RealWinMM 2>&1 |
    ForEach-Object { $_.ToString() }

$ExcludedExports = @{
    "timeBeginPeriod" = $true
    "timeEndPeriod" = $true
    "timeGetTime" = $true
}

$Exports = New-Object System.Collections.Generic.List[object]
$FallbackCount = 0

foreach ($Line in $DumpLines) {
    if ($Line -notmatch '^\s*(?<ordinal>\d+)\s+(?<hint>[0-9A-Fa-f]+)\s+(?<rva>[0-9A-Fa-f]+)\s+(?<name>[^\s=]+)(?:\s+=\s+(?<target>\S+))?\s*$') {
        continue
    }

    $Ordinal = [int]$Matches.ordinal
    $Name = [string]$Matches.name
    $Target = [string]$Matches.target

    if ([string]::IsNullOrWhiteSpace($Name)) {
        continue
    }

    if ($ExcludedExports.ContainsKey($Name)) {
        continue
    }

    if ([string]::IsNullOrWhiteSpace($Target)) {
        # Modern Windows winmm.dll normalde WINMMBASE.dll'e yönlendirir.
        # dumpbin bir hedef göstermiyorsa aynı adlı WINMMBASE dışa aktarımını kullan.
        $Target = "WINMMBASE.$Name"
        $FallbackCount++
    }

    $Exports.Add([pscustomobject]@{
        Ordinal = $Ordinal
        Name = $Name
        Target = $Target
    })
}

if ($Exports.Count -eq 0) {
    throw "winmm.dll dışa aktarımları okunamadı."
}

if (-not ($Exports.Name -contains "waveOutClose")) {
    throw "waveOutClose dışa aktarımı oluşturulamadı."
}

$Lines = New-Object System.Collections.Generic.List[string]
$Lines.Add('LIBRARY "winmm"')
$Lines.Add('')
$Lines.Add('EXPORTS')

foreach ($Export in ($Exports | Sort-Object Ordinal, Name)) {
    $Lines.Add("    $($Export.Name)=$($Export.Target) @$($Export.Ordinal)")
}

[System.IO.File]::WriteAllLines(
    [System.IO.Path]::GetFullPath($OutputPath),
    $Lines,
    [System.Text.Encoding]::ASCII
)

Write-Host "WinMM proxy DEF oluşturuldu: $OutputPath"
Write-Host "Yönlendirilen dışa aktarım sayısı: $($Exports.Count)"
Write-Host "WINMMBASE varsayımı kullanılan satır: $FallbackCount"
Write-Host "Yerel C++ işlevleri: timeBeginPeriod, timeEndPeriod, timeGetTime"
