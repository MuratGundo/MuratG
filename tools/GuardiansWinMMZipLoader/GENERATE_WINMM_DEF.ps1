param()

$ErrorActionPreference = "Stop"

function Find-VCTool {
    param([string]$FileName)

    $Roots = @(
        (Join-Path $env:ProgramFiles "Microsoft Visual Studio"),
        (Join-Path ${env:ProgramFiles(x86)} "Microsoft Visual Studio")
    ) | Where-Object { $_ -and (Test-Path $_) }

    $Candidates = foreach ($Root in $Roots) {
        Get-ChildItem `
            -Path $Root `
            -Filter $FileName `
            -File `
            -Recurse `
            -ErrorAction SilentlyContinue |
            Where-Object {
                $_.FullName -match "Hostx64\\x64\\$([regex]::Escape($FileName))$"
            }
    }

    return $Candidates |
        Sort-Object FullName -Descending |
        Select-Object -First 1
}

function Get-ExportMap {
    param(
        [string]$DllPath,
        [string]$DumpBinPath
    )

    $Map = @{}

    $Lines = & $DumpBinPath /nologo /exports $DllPath 2>&1 |
        ForEach-Object { $_.ToString() }

    foreach ($Line in $Lines) {
        if ($Line -notmatch '^\s*(?<ordinal>\d+)\s+(?<hint>[0-9A-Fa-f]+)\s+(?<rva>[0-9A-Fa-f]+)\s+(?<name>[^\s=]+)(?:\s+=\s+(?<target>\S+))?\s*$') {
            continue
        }

        $Name = [string]$Matches.name

        if ([string]::IsNullOrWhiteSpace($Name)) {
            continue
        }

        $Map[$Name] = [pscustomobject]@{
            Name = $Name
            Ordinal = [int]$Matches.ordinal
        }
    }

    return $Map
}

$DumpBin = Find-VCTool "dumpbin.exe"
$LibTool = Find-VCTool "lib.exe"

if (-not $DumpBin) {
    throw "dumpbin.exe bulunamadı. Visual Studio C++ araçları kurulu olmalı."
}

if (-not $LibTool) {
    throw "lib.exe bulunamadı. Visual Studio C++ araçları kurulu olmalı."
}

$System32 = Join-Path $env:SystemRoot "System32"
$WinMMPath = Join-Path $System32 "winmm.dll"
$WinMMBasePath = Join-Path $System32 "winmmbase.dll"

if (-not (Test-Path $WinMMPath)) {
    throw "System32 winmm.dll bulunamadı: $WinMMPath"
}

if (-not (Test-Path $WinMMBasePath)) {
    throw "System32 winmmbase.dll bulunamadı: $WinMMBasePath"
}

$WinMMExports = Get-ExportMap $WinMMPath $DumpBin.FullName
$WinMMBaseExports = Get-ExportMap $WinMMBasePath $DumpBin.FullName

$Excluded = @{
    "timeBeginPeriod" = $true
    "timeEndPeriod" = $true
    "timeGetTime" = $true
}

$Forwarders = New-Object System.Collections.Generic.List[object]

foreach ($Name in $WinMMExports.Keys) {
    if ($Excluded.ContainsKey($Name)) {
        continue
    }

    if (-not $WinMMBaseExports.ContainsKey($Name)) {
        continue
    }

    $Forwarders.Add([pscustomobject]@{
        Name = $Name
        Ordinal = $WinMMExports[$Name].Ordinal
    })
}

$Forwarders = @($Forwarders | Sort-Object Ordinal, Name)

if ($Forwarders.Count -eq 0) {
    throw "WinMM/WinMMBase ortak dışa aktarımları bulunamadı."
}

if (-not ($Forwarders.Name -contains "waveOutClose")) {
    throw "waveOutClose WinMMBase içinde bulunamadı."
}

$AsmPath = Join-Path $PSScriptRoot "winmm_forwarders.generated.asm"
$ExportDefPath = Join-Path $PSScriptRoot "winmm_exports.generated.def"
$ImportDefPath = Join-Path $PSScriptRoot "winmmbase_import.generated.def"
$ImportLibPath = Join-Path $PSScriptRoot "winmmbase_import.generated.lib"

$Asm = New-Object System.Collections.Generic.List[string]
$Asm.Add("option casemap:none")
$Asm.Add("")

foreach ($Export in $Forwarders) {
    $Asm.Add("EXTERN __imp_$($Export.Name):QWORD")
}

$Asm.Add("")
$Asm.Add(".code")
$Asm.Add("")

foreach ($Export in $Forwarders) {
    $Asm.Add("$($Export.Name) PROC")
    $Asm.Add("    jmp QWORD PTR [__imp_$($Export.Name)]")
    $Asm.Add("$($Export.Name) ENDP")
    $Asm.Add("")
}

$Asm.Add("END")

[System.IO.File]::WriteAllLines(
    $AsmPath,
    $Asm,
    [System.Text.Encoding]::ASCII
)

$ExportDef = New-Object System.Collections.Generic.List[string]
$ExportDef.Add('LIBRARY "winmm"')
$ExportDef.Add("")
$ExportDef.Add("EXPORTS")

foreach ($Export in $Forwarders) {
    $ExportDef.Add("    $($Export.Name) @$($Export.Ordinal)")
}

[System.IO.File]::WriteAllLines(
    $ExportDefPath,
    $ExportDef,
    [System.Text.Encoding]::ASCII
)

$ImportDef = New-Object System.Collections.Generic.List[string]
$ImportDef.Add('LIBRARY "WINMMBASE.dll"')
$ImportDef.Add("")
$ImportDef.Add("EXPORTS")

foreach ($Export in $Forwarders) {
    $ImportDef.Add("    $($Export.Name)")
}

[System.IO.File]::WriteAllLines(
    $ImportDefPath,
    $ImportDef,
    [System.Text.Encoding]::ASCII
)

& $LibTool.FullName `
    /nologo `
    "/def:$ImportDefPath" `
    /machine:x64 `
    "/out:$ImportLibPath"

if ($LASTEXITCODE -ne 0 -or -not (Test-Path $ImportLibPath)) {
    throw "WinMMBase import kütüphanesi oluşturulamadı."
}

Write-Host "WinMM proxy dosyaları oluşturuldu."
Write-Host "Ortak yönlendirme sayısı: $($Forwarders.Count)"
Write-Host "waveOutClose: hazır"
Write-Host "ASM: $AsmPath"
Write-Host "Export DEF: $ExportDefPath"
Write-Host "Import LIB: $ImportLibPath"
Write-Host "Yerel C++ işlevleri: timeBeginPeriod, timeEndPeriod, timeGetTime"
