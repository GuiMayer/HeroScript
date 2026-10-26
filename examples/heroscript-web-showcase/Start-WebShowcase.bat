@echo off
title HeroScript Web Showcase
setlocal enabledelayedexpansion

set "PROJECT_DIR=%~dp0"
if "%PROJECT_DIR:~-1%"=="\" set "PROJECT_DIR=%PROJECT_DIR:~0,-1%"

cd /d "%PROJECT_DIR%"

echo ======================================================================
echo           HeroScript Web Showcase - Inicializador Rapido
echo ======================================================================
echo.

powershell -NoProfile -ExecutionPolicy Bypass -File "%PROJECT_DIR%\Start-WebShowcase.ps1"

if errorlevel 1 (
    echo.
    echo [AVISO] O processo encerrou com codigo %ERRORLEVEL%.
    pause
)

exit /b 0
