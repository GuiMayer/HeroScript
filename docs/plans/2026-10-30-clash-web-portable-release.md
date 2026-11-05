# Clash-Web Portable Release Workflow Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Distribute Clash (`clash-web`) as a self-contained portable `.zip` for Windows featuring a single-file executable launcher (`Clash.exe`), the precompiled HeroScript engine runtime, game content, and client assets—requiring zero installation from players—fully automated via GitHub Actions.

**Architecture:** A lightweight, self-contained C# launcher (`Clash.exe`) manages the lifecycle of the local HeroScript REST/SSE server (`API.exe`), verifies readiness on `127.0.0.1:15260`, and opens the default browser with `index.html`. A distribution packaging script (`tools/distribution/package-clash-web.py`) bundles the engine, launcher, game content, and web interface into a clean `.zip` with SHA-256 verification. A GitHub Actions workflow (`.github/workflows/clash-web-release.yml`) executes verification, builds the portable bundle on Windows, runs a headless smoke test, and drafts or publishes GitHub releases.

**Tech Stack:** .NET 10 SDK (C# 13, self-contained single-file publishing), Python 3.12 (packaging automation), PowerShell 7 (smoke testing & process validation), GitHub Actions (`windows-latest` & `ubuntu-latest`).

---

## Deliverables & Distribution Layout

When extracted from `clash-web-v{version}-win-x64.zip`, the user receives:

```
Clash/
├── Clash.exe                 # Executável portátil: 2 cliques inicia o servidor e abre o navegador
├── Jogar-Clash.bat           # Atalho secundário via batch (fallback amigável)
├── Start-Clash.bat           # Inicializador original mantido para compatibilidade
├── heroscript.json           # Manifesto de configuração do jogo
├── index.html                # Cliente web da mesa de cartas
├── app.js                    # Lógica do jogo e conexão com o motor
├── styles.css                # Estilos do jogo
├── theme.css                 # Tokens visuais e paletas de cores
├── theme.js                  # Persistência de tema (claro/escuro)
├── game-modes.js             # Modos de jogo (1v1, FFA 4P, etc.)
├── LEIA-ME.txt               # Instruções simples para o jogador ("Extrair e abrir Clash.exe")
├── content/                  # Regras de combate e baralhos
│   ├── circular-table/
│   ├── clash/
│   └── default/
└── heroscript/               # Runtime local pré-compilado (API.exe self-contained win-x64)
    ├── API.exe               # Servidor ASP.NET Core local (sem necessidade de instalar .NET)
    ├── runtime-manifest.json # Metadados de versão e commit
    ├── Start-HeroScript.ps1  # Script de suporte do runtime
    └── ... (DLLs e dependências nativas win-x64 embutidas)
```

---

## Tasks

### Task 1: Create Portable C# Launcher (`tools/ClashLauncher`)

**Files:**
- Create: `tools/ClashLauncher/ClashLauncher.csproj`
- Create: `tools/ClashLauncher/Program.cs`
- Modify: `HeroScript.slnx` (register new project under `/tools/`)

**Step 1: Write `tools/ClashLauncher/ClashLauncher.csproj`**
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <AssemblyName>Clash</AssemblyName>
    <RootNamespace>ClashLauncher</RootNamespace>
    <PublishSingleFile>true</PublishSingleFile>
    <SelfContained>true</SelfContained>
    <IncludeNativeLibrariesForSelfExtract>true</IncludeNativeLibrariesForSelfExtract>
    <EnableCompressionInSingleFile>true</EnableCompressionInSingleFile>
  </PropertyGroup>
</Project>
```

**Step 2: Write `tools/ClashLauncher/Program.cs`**
Implement the robust launcher that:
1. Locates `heroscript.json` and `heroscript/API.exe` relative to `AppContext.BaseDirectory`.
2. Reads the target port (default `15260`) or finds a free loopback port if `15260` is occupied.
3. Launches `heroscript/API.exe` with production flags:
   `--urls http://127.0.0.1:{port} --environment Production --Admin:Enabled=false --AllowConfigReload=false --ToolAccess:Profile=normal --Mods:PackageRoots:0={contentDir} --Persistence:RunStatePath={dataRoot}/runs --Persistence:ContentStorePath={dataRoot}/content --Persistence:OperationalTelemetryPath={dataRoot}/telemetry --Content:StartupSettings:0=clash --Content:StartupSettings:1=circular-table --AllowedOrigins:0=null`
4. Registers graceful shutdown hooks on `AppDomain.CurrentDomain.ProcessExit` and `Console.CancelKeyPress` to ensure `API.exe` is killed and leaves no orphan background processes.
5. Polls `http://127.0.0.1:{port}/api/v1/health/ready` with a 30s timeout.
6. Opens the default browser at `file:///{clientEntry}?api=http%3A%2F%2F127.0.0.1%3A{port}` using `Process.Start(new ProcessStartInfo(url) { UseShellExecute = true })`.
7. Supports `--check-only` / `--check` flag for CI validation without launching a browser.
8. Displays an informative console banner:
   ```
   ==================================================
              CLASH! (HeroScript Engine)
   ==================================================
   Servidor local ativo em: http://127.0.0.1:15260
   O jogo foi aberto no seu navegador padrão.
   Mantenha esta janela aberta enquanto joga.
   Para fechar o jogo, feche esta janela ou pressione Ctrl+C.
   ==================================================
   ```

**Step 3: Register in `HeroScript.slnx`**
Add `<Project Path="tools/ClashLauncher/ClashLauncher.csproj" />` under `<Folder Name="/tools/">`.

**Step 4: Build and test compilation**
Run: `dotnet build tools/ClashLauncher/ClashLauncher.csproj -c Release`
Expected: Build succeeds with 0 errors.

**Step 5: Commit**
```bash
git add tools/ClashLauncher/ HeroScript.slnx
git commit -m "feat(clash-web): criar launcher executavel portatil ClashLauncher"
```

---

### Task 2: Create Packaging Automation Script (`tools/distribution/package-clash-web.py`)

**Files:**
- Create: `tools/distribution/package-clash-web.py`
- Create: `examples/clash-web/LEIA-ME.txt`
- Create: `examples/clash-web/Jogar-Clash.bat`

**Step 1: Create `examples/clash-web/LEIA-ME.txt`**
```text
================================================================================
                                CLASH!
================================================================================

COMO JOGAR:
1. Extraia todo o conteúdo deste arquivo .zip para uma pasta no seu computador.
2. Dê dois cliques no executável "Clash.exe" (ou "Jogar-Clash.bat").
3. Uma janela preta do servidor local será aberta e o jogo abrirá automaticamente
   no seu navegador padrão!

REQUISITOS:
- Windows 10 ou 11 (64 bits).
- Nenhum software adicional é necessário (o motor já está embutido).
- Não precisa instalar .NET, nem Node, nem nada.

PARA FECHAR O JOGO:
- Feche a janela do Clash.exe ou pressione Ctrl+C nela.

Bom jogo!
================================================================================
```

**Step 2: Create `examples/clash-web/Jogar-Clash.bat`**
```bat
@echo off
setlocal
set "SCRIPT_DIR=%~dp0"
if exist "%SCRIPT_DIR%Clash.exe" (
    start "" "%SCRIPT_DIR%Clash.exe" %*
) else (
    powershell -NoProfile -ExecutionPolicy Bypass -File "%SCRIPT_DIR%Start-Clash.ps1" %*
)
exit /b 0
```

**Step 3: Implement `tools/distribution/package-clash-web.py`**
The script will:
1. Accept `--version` (e.g. `0.1.0`) and optional `--output` (defaults to `ROOT / artifacts/distribution`).
2. Create staging directory: `artifacts/distribution/clash-web-v{version}-win-x64`.
3. Publish `src/API/API.csproj` for `win-x64` self-contained into `staging/heroscript`.
4. Publish `tools/ClashLauncher/ClashLauncher.csproj` for `win-x64` self-contained single-file into `staging/Clash.exe`.
5. Copy web assets:
   - `index.html`, `app.js`, `styles.css`, `theme.css`, `theme.js`, `game-modes.js`
   - `LEIA-ME.txt`, `Jogar-Clash.bat`, `Start-Clash.bat`, `Start-Clash.ps1`
   - `content/` from `examples/clash-web/content/`
   - `heroscript.json` (with updated version)
   - `tools/distribution/Start-HeroScript.ps1` into `staging/heroscript/`
   - Generate `staging/heroscript/runtime-manifest.json` containing engine version, API contract, and git commit hash.
6. Create ZIP archive `clash-web-v{version}-win-x64.zip`.
7. Generate SHA-256 hash file `clash-web-v{version}-win-x64.zip.sha256`.

**Step 4: Test package creation locally**
Run: `python tools/distribution/package-clash-web.py --version 0.1.0`
Expected: Produces `clash-web-v0.1.0-win-x64.zip` and `.sha256` under `artifacts/distribution/`.

**Step 5: Commit**
```bash
git add tools/distribution/package-clash-web.py examples/clash-web/LEIA-ME.txt examples/clash-web/Jogar-Clash.bat
git commit -m "feat(clash-web): script de empacotamento portatil para distribuicao"
```

---

### Task 3: Create Smoke Test Script (`tools/distribution/smoke-clash-web.ps1`)

**Files:**
- Create: `tools/distribution/smoke-clash-web.ps1`

**Step 1: Implement `smoke-clash-web.ps1`**
1. Extract ZIP to a temporary test directory.
2. Validate required directory structure (`Clash.exe`, `heroscript/API.exe`, `content/`, `index.html`, `app.js`, `heroscript.json`).
3. Execute `.\Clash.exe --check-only` and assert:
   - Process exits with code 0 within 20s.
   - API starts and responds 200 OK to `health/ready`.
   - No orphan `API.exe` process is left behind after `Clash.exe` exits.
4. Execute `.\Start-Clash.bat -CheckOnly` and assert exit code 0.
5. Print clean `PASS` summary and clean up temporary directory.

**Step 2: Run smoke test against the generated package**
Run: `pwsh ./tools/distribution/smoke-clash-web.ps1 -Package artifacts/distribution/clash-web-v0.1.0-win-x64.zip`
Expected: Output `PASS: Clash portable package verified successfully.`

**Step 3: Commit**
```bash
git add tools/distribution/smoke-clash-web.ps1
git commit -m "test(clash-web): script de teste de fumaca do pacote portatil"
```

---

### Task 4: Create GitHub Actions Workflow (`.github/workflows/clash-web-release.yml`)

**Files:**
- Create: `.github/workflows/clash-web-release.yml`

**Step 1: Write `.github/workflows/clash-web-release.yml`**
```yaml
name: Clash Web Portable Distribution
on:
  push:
    tags: ['clash-v*']
  workflow_dispatch:
    inputs:
      version:
        description: 'Package version without v (e.g. 0.1.0)'
        required: true
      create_release:
        description: 'Publish draft release on GitHub'
        type: boolean
        default: true

permissions:
  contents: write

jobs:
  verify:
    runs-on: windows-latest
    timeout-minutes: 30
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          global-json-file: global.json
      - name: Core regression tests
        run: dotnet test tests/Core.Tests/Core.Tests.csproj -c Release --logger trx --results-directory TestResults

  package-windows:
    needs: verify
    runs-on: windows-latest
    timeout-minutes: 45
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          global-json-file: global.json
      - uses: actions/setup-python@v5
        with:
          python-version: '3.12'

      - name: Resolve version
        shell: pwsh
        env:
          INPUT_VERSION: ${{ inputs.version }}
          RELEASE_TAG: ${{ github.ref_name }}
        run: |
          $version = if ($env:INPUT_VERSION) {
            $env:INPUT_VERSION
          } elseif ($env:RELEASE_TAG -match '^clash-v(?<ver>.+)$') {
            $Matches['ver']
          } else {
            $env:RELEASE_TAG -replace '^v',''
          }
          if ($version -notmatch '^\d+\.\d+\.\d+(-[A-Za-z0-9.-]+)?$') { throw "Versão inválida: $version" }
          Write-Host "Versão do pacote: $version"
          "PACKAGE_VERSION=$version" >> $env:GITHUB_ENV

      - name: Build and package portable distribution
        run: python tools/distribution/package-clash-web.py --version $env:PACKAGE_VERSION

      - name: Test extracted portable package (Smoke Test)
        shell: pwsh
        run: |
          $zip = Get-ChildItem artifacts/distribution/clash-web-*.zip | Select-Object -First 1
          ./tools/distribution/smoke-clash-web.ps1 -Package $zip.FullName

      - name: Upload artifact
        uses: actions/upload-artifact@v4
        with:
          name: clash-web-win-x64
          path: |
            artifacts/distribution/*.zip
            artifacts/distribution/*.sha256
          if-no-files-found: error

  release:
    if: startsWith(github.ref, 'refs/tags/clash-v') || (github.event_name == 'workflow_dispatch' && inputs.create_release)
    needs: package-windows
    runs-on: ubuntu-latest
    permissions:
      contents: write
    steps:
      - uses: actions/download-artifact@v4
        with:
          name: clash-web-win-x64
          path: packages

      - name: Resolve release tag
        id: tag
        run: |
          if [ -n "${{ github.ref_name }}" ] && [[ "${{ github.ref }}" == refs/tags/* ]]; then
            echo "tag=${{ github.ref_name }}" >> $GITHUB_OUTPUT
          else
            echo "tag=clash-v${{ inputs.version }}" >> $GITHUB_OUTPUT
          fi

      - name: Create draft release on GitHub
        env:
          GH_TOKEN: ${{ github.token }}
          GH_REPO: ${{ github.repository }}
          TAG: ${{ steps.tag.outputs.tag }}
        run: |
          gh release create "$TAG" packages/* \
            --title "Clash $TAG (Portable Windows)" \
            --notes "### CLASH! - Versão Portátil para Windows
          
          **Zero Instalação:**
          1. Baixe o arquivo \`.zip\` abaixo.
          2. Extraia para qualquer pasta no seu computador.
          3. Dê dois cliques em **Clash.exe**.
          4. O servidor local iniciará e abrirá o jogo automaticamente no seu navegador!" \
            --draft
```

**Step 2: Commit**
```bash
git add .github/workflows/clash-web-release.yml
git commit -m "ci(clash-web): workflow de release do pacote portatil zip"
```

---

### Task 5: End-to-End Verification and Documentation

**Files:**
- Modify: `examples/clash-web/README.md` (Add section "Distribuição Portátil")
- Artifacts: Test local package generation and clean up build artifacts.

**Step 1: Update `examples/clash-web/README.md`**
Add clear instructions on:
- How to trigger the GitHub Actions workflow manually (`workflow_dispatch`) or by pushing tag `clash-v0.1.0`.
- How the portable zip works.
- How to generate the zip locally with `python tools/distribution/package-clash-web.py --version 0.1.0`.

**Step 2: Dry-run and clean up**
Run `git status` to ensure working tree is clean.

**Step 3: Commit**
```bash
git add examples/clash-web/README.md
git commit -m "docs(clash-web): documentar distribuicao e empacotamento portatil"
```

---

## Execution Handoff

Plan complete and saved to `docs/plans/2026-10-30-clash-web-portable-release.md`. Two execution options:

1. **Subagent-Driven (this session)** - I dispatch fresh subagents per task, review between tasks, fast iteration.
2. **Parallel Session (separate)** - Open a new session with executing-plans, batch execution with checkpoints.

Which approach?
