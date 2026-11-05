@echo off
setlocal
set "SCRIPT_DIR=%~dp0"
powershell -NoProfile -ExecutionPolicy Bypass -File "%SCRIPT_DIR%Start-UnoClash.ps1" %*
if errorlevel 1 (
    echo.
    echo [ERRO] Falha ao iniciar o UNO Clash.
    pause
    exit /b 1
)
exit /b 0
