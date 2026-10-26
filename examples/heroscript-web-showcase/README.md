# HeroScript — Web Showcase (Ivory Edition)

Port para Web (**TypeScript + React 19 + Vite + Tailwind CSS v4 + Vitest**) do projeto `godot-engine-showcase`, preservando rigorosamente a arquitetura em camadas e o princípio de **projeção passiva** (zero lógica/matemática de regras de jogo no cliente).

## Arquitetura em Camadas

1. **`src/contracts/`** (`types.ts`, `snapshotProjection.ts`):
   - Contratos de dados imutáveis (`deepClone`, `deepEqual`, `deterministicHash`, `Signal<T>`) e normalização de snapshots (`SnapshotProjection.run` e `SnapshotProjection.combat`).
2. **`src/engine/`** (`httpTransport.ts`, `engineGateway.ts`, `deckCompartmentGateway.ts`, `readTicket.ts`, `requestNumbers.ts`):
   - Transporte HTTP com fila de prioridade (comandos `POST` têm precedência sobre leituras `GET`), coalescência de leituras idênticas, cabeçalho `Prefer: return=minimal` e invalidação por época de endpoint.
3. **`src/application/`** (`gameSession.ts`, `preferences.ts`, `runResumeIndex.ts`, `activityChoices.ts`, `loadoutWorkspace.ts`, `deckCompartmentWorkspace.ts`):
   - Orquestração de sessão, persistência local (`localStorage`), retomada por chave composta (`endpoint + profile + setting + mode`), e espaços de trabalho de loadout e compartimentos de deck.
4. **`src/presentation/`** (`i18n.ts`, `combatPresenter.ts`, `cardSectionPresenter.ts`, `cardFactsPresenter.ts`, `cardCalculationPresenter.ts`, `cardZonePresenter.ts`, `activityPresenter.ts`, `dialoguePresenter.ts`, `mapLayout.ts`, `playback.ts`, `presentationPlanner.ts`, `visualAndLayout.ts`, `timelineAndReplayPresenters.ts`):
   - Presenters puros que formatam fatos publicados pelo motor, layout de grafo Kosaraju + varredura baricêntrica (`MapLayout`), geometria de leque de cartas (`HandLayout`) e fila de reprodução de quadros (`Playback`).
5. **`src/ui/`** (`controllers/`, `components/`, `screens/`, `gameAudio.ts`):
   - Componentes visuais da estética **Ivory / Printed Circuit** (`CardView`, `CardHandView`, `ActorPanel`, `ArtSlot`, `CircuitTrace`) e telas completas (Menu Principal, Combate, Jornada/Atividades, Mapa Interativo, Diálogo, Loadout de Inscrições, Compartimentos de Deck, Timeline/Branches, Histórico, Replay, Sandbox, Codex, Configurações e Laboratórios Offline).

## Como Executar

```powershell
# Instalar dependências
npm install

# Executar suíte de testes de arquitetura e camadas
npm test

# Iniciar servidor de desenvolvimento
npm run dev

# Gerar build de produção
npm run build
```
