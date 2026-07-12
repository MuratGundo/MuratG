@echo off
chcp 65001 >nul
title U-CREW Guardians ZIP Hazirlayici

powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0UCREW_GUARDIANS_ZIP_HAZIRLA.ps1"

echo.
pause
