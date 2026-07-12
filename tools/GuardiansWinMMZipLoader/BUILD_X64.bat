@echo off
chcp 65001 >nul
title Guardians WinMM ZIP Loader Build

where cmake >nul 2>nul
if errorlevel 1 (
    echo HATA: CMake bulunamadi.
    echo Visual Studio 2022 C++ ve CMake bilesenlerini kur.
    pause
    exit /b 1
)

cmake -S "%~dp0" -B "%~dp0build" -A x64
if errorlevel 1 goto :error

cmake --build "%~dp0build" --config Release
if errorlevel 1 goto :error

echo.
echo DLL:
echo %~dp0build\Release\winmm.dll
echo.
pause
exit /b 0

:error
echo.
echo Derleme basarisiz.
echo.
pause
exit /b 1
