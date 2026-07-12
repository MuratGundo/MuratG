@echo off
chcp 65001 >nul
title U-CREW Guardians ZIP Loader Kaldir

echo Bu klasorden su dosyalar silinecek:
echo winmm.dll
echo ucrew_loader.ini
echo UCREW_Guardians_TR.zip
echo ucrew_winmm.log
echo.
choice /C EH /M "Devam edilsin mi? E=Evet H=Hayir"
if errorlevel 2 exit /b 0

del /f /q "%~dp0winmm.dll" 2>nul
del /f /q "%~dp0ucrew_loader.ini" 2>nul
del /f /q "%~dp0UCREW_Guardians_TR.zip" 2>nul
del /f /q "%~dp0ucrew_winmm.log" 2>nul

rmdir /s /q "%LOCALAPPDATA%\UCREW\Guardians\Cache" 2>nul

echo.
echo Kaldirma tamamlandi.
echo.
pause
