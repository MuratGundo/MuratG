@echo off
chcp 65001 >nul
title U-CREW Genel Guvenli Yama - VPS Kurulumu

powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0DEPLOY_FROM_WINDOWS.ps1"
set "EXITCODE=%ERRORLEVEL%"

echo.
if not "%EXITCODE%"=="0" (
    echo Kurulum hata ile sonlandi. Kod: %EXITCODE%
) else (
    echo Kurulum tamamlandi.
)
echo.
pause
exit /b %EXITCODE%
