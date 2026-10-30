# Clash Web (Clash) Migration & Renaming Implementation Plan

> **For Agent:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Rename project from `uno-clash-web` to `clash-web` and eliminate all copyright-infringing instances of "UNO" (including card backs, shouts "Gritou UNO!" -> "Gritou CLASH!", classes, variables, file names, setting IDs, action IDs, package IDs, and directory names), keeping only "Clash".

**Architecture:** Phased atomic migration preserving deterministic rules and visual presentation bindings:
1. Content folder & resource file renaming (`content/uno-clash` -> `content/clash`).
2. Update all 12 game definition JSON files and manifest.
3. Update launchers and CSS design tokens.
4. Update web presentation (`app.js`, `game-modes.js`, `index.html`, `theme.js`).
5. Update test fixtures and helper scripts (`make-fixture.py`, `Start-TestApi.ps1`).
6. Update all test suites and verify 78/78 tests pass.
7. Rename root directory to `examples/clash-web` and run final integration verification.

**Tech Stack:** HeroScript Runtime 0.1.0 (win-x64), Vanilla JS (ES Modules/Browser), HTML5/CSS3, Node.js test runner (`node:test`), PowerShell 7.

---

## 1. Mapeamento Geral de Mudanças

### 1.1 Tabela de Renomeação de Identificadores Chave

| Categoria | Identificador Anterior (`UNO`) | Novo Identificador (`Clash`) |
| :--- | :--- | :--- |
| **Pasta Raiz** | `examples/uno-clash-web` | `examples/clash-web` |
| **Pacote de Conteúdo** | `content/uno-clash` | `content/clash` |
| **Package ID** | `heroscript.uno-clash` | `heroscript.clash` |
| **Project ID** | `uno-clash-web` | `clash-web` |
| **Setting ID** | `uno-clash` | `clash` |
| **Run Definition ID** | `uno_clash_run` | `clash_run` |
| **Mode ID** | `uno_classic` | `clash_classic` |
| **Combat Rules ID** | `uno_combat` | `clash_combat` |
| **Card Zones ID** | `uno_zones` | `clash_zones` |
| **Card Pool ID** | `uno_deck_pool` | `clash_deck_pool` |
| **Enemy Pool ID** | `uno_opponents` | `clash_opponents` |
| **Entidades** | `uno_player`, `uno_bot` | `clash_player`, `clash_bot` |
| **Nó do Mapa** | `mesa_uno` | `mesa_clash` |
| **Ação: Setup** | `uno_setup_table` | `clash_setup_table` |
| **Ação: Comprar** | `uno_draw_card` | `clash_draw_card` |
| **Ação: Gritar** | `uno_shout_uno` | `clash_shout_clash` |
| **Ação: Cobrar** | `uno_catch_no_uno` | `clash_catch_missed_clash` |
| **Ação: Aceitar +2** | `uno_accept_draw2` | `clash_accept_draw2` |
| **Ações: Escolha de Cor**| `uno_choose_<cor>` | `clash_choose_<cor>` |
| **Cartas (108)** | `uno_<cor>_<val>` | `clash_<cor>_<val>` |
| **Coringas** | `uno_wild`, `uno_wild_draw4` | `clash_wild`, `clash_wild_draw4` |
| **Recursos de Turno** | `uno_energy`, `uno_called`, `uno_voice`, `uno_draw_available`, `uno_draw2_pending`, `uno_draw2_due`, `uno_draw_amount`, `uno_setup`, `uno_catch_available` | `clash_energy`, `clash_called`, `clash_voice`, `clash_draw_available`, `clash_draw2_pending`, `clash_draw2_due`, `clash_draw_amount`, `clash_setup`, `clash_catch_available` |
| **Gambits da IA** | `uno_draw`, `uno_end`, `uno_call`, `uno_accept`, `uno_catch`, `play_uno_*`, `uno_respond_*`, `uno_choose_*` | `clash_draw`, `clash_end`, `clash_call`, `clash_accept`, `clash_catch`, `play_clash_*`, `clash_respond_*`, `clash_choose_*` |
| **Formulas** | `uno_draw_one`, `uno_draw_two`, `uno_draw_four`, `uno_draw_stack`, `uno_normal_cost`, `uno_server_can_end` | `clash_draw_one`, `clash_draw_two`, `clash_draw_four`, `clash_draw_stack`, `clash_normal_cost`, `clash_server_can_end` |
| **CSS: Classes** | `.uno-card`, `.uno-card-back`, `.uno-emblem`, `.shout-uno-btn` | `.clash-card`, `.clash-card-back`, `.clash-emblem`, `.shout-clash-btn` |
| **CSS: Tokens** | `--uno-red*`, `--uno-blue*`, `--uno-green*`, `--uno-yellow*`, `--uno-button-end` | `--clash-red*`, `--clash-blue*`, `--clash-green*`, `--clash-yellow*`, `--clash-button-end` |
| **UI: Textos** | `"Gritou UNO!"`, `"UNO!"`, `"📣 O bot declarou UNO!"` | `"Gritou CLASH!"`, `"CLASH!"`, `"📣 O bot declarou CLASH!"` |
| **Verso da Carta** | Texto central `"UNO"` | Texto central `"CLASH"` |
| **Storage Local** | `'uno-clash.theme'` | `'clash.theme'` |
| **Launchers** | `Start-UnoClash.ps1/.bat` | `Start-Clash.ps1/.bat` |

---

## 2. Inventário de Arquivos (46 arquivos)

- **Manifesto & Docs (6):** `heroscript.json`, `index.html`, `README.md`, `INTEGRATION.md`, `Start-UnoClash.ps1`, `Start-UnoClash.bat`
- **Estilos & Scripts (5):** `styles.css`, `theme.css`, `theme.js`, `game-modes.js`, `app.js`
- **Conteúdo e Definições (14):** `content/uno-clash/package.json`, `settings/uno-clash.json`, e 12 arquivos em `Resources/*/uno.json`
- **Ferramentas de Teste (5):** `tests/Start-TestApi.ps1`, `tests/make-fixture.py`, `tests/real-deck-client.cjs`, `tests/launcher.tests.ps1`, `tests/update.tests.ps1`
- **Suíte de Testes (16):** Todos os testes unitários e de integração em `tests/*.cjs`

---

## 3. Fases de Execução

### Task 1: Renomeação de Estrutura de Pastas e Arquivos de Conteúdo
**Arquivos:**
- Mover: `content/uno-clash` -> `content/clash`
- Renomear: `content/clash/settings/uno-clash.json` -> `content/clash/settings/clash.json`
- Renomear: todos os 12 `content/clash/Resources/*/uno.json` -> `clash.json`
- Modificar: `content/clash/package.json`

### Task 2: Atualização dos 12 JSONs de Definições e Regras
**Arquivos:**
- Modificar: `content/clash/settings/clash.json`
- Modificar: `content/clash/Resources/actions/clash.json`
- Modificar: `content/clash/Resources/card-pools/clash.json`
- Modificar: `content/clash/Resources/card-zone-systems/clash.json`
- Modificar: `content/clash/Resources/cards/clash.json`
- Modificar: `content/clash/Resources/combat-rules/clash.json`
- Modificar: `content/clash/Resources/enemy-pools/clash.json`
- Modificar: `content/clash/Resources/entities/clash.json`
- Modificar: `content/clash/Resources/formulas/clash.json`
- Modificar: `content/clash/Resources/gambits/clash.json`
- Modificar: `content/clash/Resources/modes/clash.json`
- Modificar: `content/clash/Resources/resources/clash.json`
- Modificar: `content/clash/Resources/runs/clash.json`

### Task 3: Atualização de Manifesto, Launchers e Estilos
**Arquivos:**
- Modificar: `heroscript.json`
- Renomear/Modificar: `Start-UnoClash.ps1` -> `Start-Clash.ps1`
- Renomear/Modificar: `Start-UnoClash.bat` -> `Start-Clash.bat`
- Modificar: `styles.css`
- Modificar: `theme.css`
- Modificar: `theme.js`

### Task 4: Apresentação Web e Frontend
**Arquivos:**
- Modificar: `game-modes.js` (`ClashModes`, `clash_` actions & resources)
- Modificar: `index.html` (title, emblem, backs, shout button, radial actions)
- Modificar: `app.js` (DOM elements, announcements, parsing, back rendering)

### Task 5: Utilitários e Fixtures de Teste
**Arquivos:**
- Modificar: `tests/Start-TestApi.ps1`
- Modificar: `tests/make-fixture.py`
- Modificar: `tests/launcher.tests.ps1`
- Modificar: `tests/update.tests.ps1`

### Task 6: Atualização da Suíte de Testes e Validação Unitária
**Arquivos:**
- Modificar: 21 arquivos de teste em `tests/*.cjs`
- Executar: `node --test (Get-ChildItem tests/*.test.cjs).FullName`
- Critério: 78/78 testes passando

### Task 7: Renomeação da Pasta Raiz e Validação End-to-End
**Arquivos:**
- Mover pasta: `examples/uno-clash-web` -> `examples/clash-web`
- Modificar: `README.md` e `INTEGRATION.md`
- Executar: `Start-Clash.ps1 -CheckOnly`, `launcher.tests.ps1`, `update.tests.ps1`, testes de integração (`real-deck.integration.cjs`, `modes.integration.cjs`)
