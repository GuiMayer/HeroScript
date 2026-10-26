param(
    [string]$ApiUrl = "http://127.0.0.1:5271",
    [int]$Port = 5173,
    [switch]$NoBrowser
)

$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host "     HeroScript Web Showcase (Tabletop Ivory Edition)     " -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host "Target HeroScript Engine URL: $ApiUrl" -ForegroundColor Gray
Write-Host "Local Web Port: $Port" -ForegroundColor Gray

# Verifica se a engine backend já está respondendo
$engineOnline = $false
try {
    $res = Invoke-RestMethod -Uri "$ApiUrl/api/v1/health/ready" -TimeoutSec 1 -ErrorAction SilentlyContinue
    if ($null -ne $res) { $engineOnline = $true }
} catch {
    $engineOnline = $false
}

if ($engineOnline) {
    Write-Host "[OK] Engine HeroScript ja esta ativa em $ApiUrl" -ForegroundColor Green
} else {
    Write-Host "[INFO] Engine HeroScript offline em $ApiUrl." -ForegroundColor Yellow
    $demoStarter = Join-Path $PSScriptRoot "..\godot-engine-showcase\Start-Demo.ps1"
    if (Test-Path $demoStarter) {
        Write-Host "[...] Inicializando engine backend HeroScript em segundo plano..." -ForegroundColor Cyan
        try {
            Start-Process powershell -ArgumentList "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", "`"$demoStarter`"", "-ServerOnly", "-KeepEngine" -WindowStyle Minimized
            for ($i = 0; $i -lt 15; $i++) {
                Start-Sleep -Milliseconds 800
                try {
                    $testRes = Invoke-RestMethod -Uri "$ApiUrl/api/v1/health/ready" -TimeoutSec 1 -ErrorAction SilentlyContinue
                    if ($null -ne $testRes) {
                        $engineOnline = $true
                        Write-Host "[OK] Engine HeroScript pronta em $ApiUrl!" -ForegroundColor Green
                        break
                    }
                } catch { }
            }
        } catch {
            Write-Host "[AVISO] Nao foi possivel iniciar a engine automaticamente. O showcase web funcionara em modo offline / laboratorios." -ForegroundColor Yellow
        }
    }
}

if (-not $NoBrowser) {
    Start-Job -ScriptBlock {
        param($targetPort)
        Start-Sleep -Seconds 2
        Start-Process "http://localhost:$targetPort/"
    } -ArgumentList $Port | Out-Null
}

Write-Host "`nIniciando servidor de desenvolvimento web..." -ForegroundColor Cyan
Write-Host "Acesse: http://localhost:$Port/`n" -ForegroundColor Green
npm run dev -- --port $Port
