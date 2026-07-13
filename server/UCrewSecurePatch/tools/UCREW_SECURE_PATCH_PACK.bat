@echo off
chcp 65001 >nul
title U-CREW Genel Guvenli Yama Hazirlayici

powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0UCREW_SECURE_PATCH_PACK_PS51.ps1"
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
