@echo off
setlocal
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Start-ContentEditor.ps1" %*
if errorlevel 1 (
    echo.
    pause
    exit /b 1
)
exit /b 0
