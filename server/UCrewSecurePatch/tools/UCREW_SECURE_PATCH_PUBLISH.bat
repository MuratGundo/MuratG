@echo off
chcp 65001 >nul
title U-CREW Guvenli Yama Yayinlama

powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0UCREW_SECURE_PATCH_PUBLISH.ps1"
set "EXITCODE=%ERRORLEVEL%"

echo.
if not "%EXITCODE%"=="0" (
    echo Yayinlama hata ile sonlandi. Kod: %EXITCODE%
) else (
    echo Yayinlama tamamlandi.
)
echo.
pause
exit /b %EXITCODE%
