@echo off
chcp 65001 >nul
title U-CREW Guardians Guvenli Paket Hazirlayici

powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0GUARDIANS_SECURE_PACK.ps1"
set "EXITCODE=%ERRORLEVEL%"

echo.
if not "%EXITCODE%"=="0" (
    echo Islem hata ile sonlandi. Kod: %EXITCODE%
) else (
    echo Islem tamamlandi.
)
echo.
pause
exit /b %EXITCODE%
