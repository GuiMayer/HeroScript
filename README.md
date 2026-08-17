# HeroScript Engine

**Headless card roguelike engine with REST API architecture**

HeroScript é uma engine headless para jogos de cartas roguelike exposta através de uma **REST API**. Game clients (Unity, Godot, Web) consomem a engine via HTTP, permitindo hot-reload, ferramentas web, e arquitetura modular data-driven.

## 🚀 Quick Start

### Start the API Server

```bash
# Option A: Docker (recommended)
docker-compose up -d
# API available at http://localhost:5260

# Option B: Local development
cd src/API
dotnet run
# API available at http://localhost:5260
```

### Verify API is Running

```bash
curl http://localhost:5260/api/health
```

### Integration Guides

- **[Unity/Godot/Web Integration](docs/CLIENT_INTEGRATION.md)** - Consume API from game clients
- **[Production Deployment](docs/PRODUCTION.md)** - Deploy API to production
- **[API v1 Guide](docs/api/README.md)** - Contrato público, exemplos e compatibilidade

**Performance:** 5-8ms latency on localhost (imperceptible for turn-based games)

## 🎯 Status do MVP

**Última análise:** 2026-08-16

### Engine (estabilização em andamento)
- **Core Systems:** Combat, Deck, Shop, Status Effects e Gambits implementados
- **Validação atual:** 1.250 testes Core e 125 testes unitários da API aprovados
- **Integração:** o bloqueio de inicialização foi corrigido; os fluxos legados ainda precisam ser alinhados aos contratos e ao conteúdo atual
- **Build:** concluído sem erros; warnings de nulidade restantes serão tratados incrementalmente

### Blockers Críticos para MVP Jogável ❌
- **Map Navigation System (0%)** - Sistema de progressão entre nós ausente
- **Event System (0%)** - Eventos de narrativa/escolha não implementados
- **Conteúdo (10%)** - 7 cartas vs 30+ necessárias, 3 inimigos vs 10+ necessários

### Timeline para MVP Jogável
- **Sprint 1 (1-2 semanas):** Implementar Map System + Event System
- **Sprint 2 (1-2 semanas):** Produção de conteúdo (20+ cartas, 7+ inimigos, 1 boss)
- **Sprint 3 (3-5 dias):** Balanceamento e QA
- **Total:** 3-4 semanas com esforço focado

**Estado Atual:** Engine sólida, mas sem loop de progressão jogável. Ideal para embedding em outros projetos ou como simulador de combate.

## Estrutura do Projeto

```
HeroScript/
├── src/                      # Código de produção
│   ├── Core/                 # Biblioteca principal (DLL embarcável)
│   │   ├── Abstractions/     # Interfaces e contratos (IEventStore, IRunStateRepository, etc.)
│   │   └── Infrastructure/   # Implementações (JsonFileEventStore, JsonFileRunStateRepository)
│   ├── API/                  # REST API para exposição do Core
│   │   └── Middleware/       # AdminKeyMiddleware, CorrelationIdMiddleware
│   └── Mods/                 # Sistema de mods (futuro)
├── tools/                    # Ferramentas de desenvolvimento (opcionais)
│   ├── Core.CLI/             # CLI de debug e testes
│   └── Calculator/           # Calculadora de debug
├── tests/                    # Testes automatizados
│   ├── Core.Tests/           # Testes do Core (1.248 testes)
│   ├── API.Tests/            # Testes da API (150 testes: 125 unit + 25 integration)
│   └── heroscript.runsettings # Configuração de timeout para testes
├── .github/workflows/        # CI/CD pipeline
│   └── ci.yml                # GitHub Actions
└── docs/                     # Documentação técnica
```

## Estado Atual

**Última atualização:** 2026-08-16
**Fase atual:** estabilização técnica da Fase 3; Map System e Event System continuam bloqueadores para o MVP jogável.

- **Core.Tests:** 1.306 testes aprovados na última validação completa
- **API.Tests:** 213 testes aprovados na última validação completa
- **Fase 0:** Config, Math e Resources implementados
- **Fase 1:** EventBus, Combat, Damage Pipeline, TurnPhase e TurnOrder implementados
- **Fase 2:** Status Effects, Script Modifiers, Gambit Engine e Effect Engine estabilizados
- **Fase 3:** Primeira fatia implementada
  - ✅ RunState e DeckState como núcleo
  - ✅ RunManager com persistência automática
  - ✅ Run API (start, state, deck, hand, draw, discard, shuffle)
  - ✅ CardSelection API (start, pick, reroll, decompose)
  - ✅ Shop API (start, buy, reroll, sell)
  - ✅ Preparation API (start, apply-modifier)
  - ✅ CombatRunCoordinator (integração combate ↔ deck/hand)
  - ❌ **Map navigation (BLOCKER)** - Sistema não implementado, bloqueia progressão
  - ❌ **Event nodes (BLOCKER)** - Eventos narrativos ausentes, bloqueia variação
  - ⏳ Rest nodes, boss encounters (após blockers)
- **Hardening concluído (Jun/2026):**
  - ✅ Event publishing (observabilidade de lifecycle)
  - ✅ API Key middleware (segurança em endpoints admin)
  - ✅ Logging estruturado + Correlation IDs
  - ✅ Persistência de eventos (JsonFileEventStore)
  - ✅ Persistência de run state (JsonFileRunStateRepository)
  - ✅ Estabilidade de testes + CI pipeline

## Componentes

### Core (Biblioteca)

O Core é o coração da engine - uma biblioteca .NET que pode ser embarcada em qualquer projeto. Contém:

- **MathEngine**: Sistema de fórmulas matemáticas serializadas (JSON)
- **ConfigManager**: Sistema de configuração com herança delta
- **ResourceLoader**: Carregamento de recursos data-driven
- **CombatSystem**: Execução de combate baseada em ações e efeitos data-driven
- **EffectResolver**: Aplicação central de efeitos para contextos de combate/run
- **StatusEffectManager**: Buffs, debuffs, DoT/HoT e modificadores de pipeline
- **ScriptModifierManager**: Modificadores de comportamento carregados de JSON
- **GambitEngine**: Decisões de IA/companions por regras JSON

**Output**: `Core.dll` - biblioteca embarcável

### API (REST API)

Camada de exposição HTTP do Core, permitindo consumo via REST API.

- Swagger UI disponível em desenvolvimento
- Endpoints para ações, combate, efeitos, status, modifiers, gambits, recursos, entidades, eventos, config e matemática
- CORS configurado para desenvolvimento

**Output**: `API.dll` - aplicação web ASP.NET Core

### Core.Abstractions (Contratos)

Camada de abstração com interfaces e tipos compartilhados:

- **IEventStore**: Contrato para persistência de eventos
- **IRunStateRepository**: Contrato para snapshots de run
- **ISnapshotStore**: Contrato para snapshots genéricos (futuro)
- **Typed Identifiers**: `RunId`, `CombatId`, `EntityId` para type safety
- **ICorrelatedEvent**: Interface para eventos com correlation tracking

**Output**: `Core.Abstractions.dll` - biblioteca de contratos

### Core.Infrastructure (Implementações)

Implementações concretas de infraestrutura:

- **JsonFileEventStore**: Persistência de eventos em `.jsonl`
- **JsonFileRunStateRepository**: Persistência de run state em JSON
- **LoggerAdapter**: Adapter de `ILogger` ASP.NET Core para `Core.Logging.ILogger`

**Output**: `Core.Infrastructure.dll` - biblioteca de infraestrutura

### Core.CLI (Debug Tool)

Ferramenta de linha de comando para debug e testes do Core.

- Execução de testes integrados
- Listagem de configurações disponíveis
- Carregamento de configs específicas

**Output**: `Core.CLI.exe` - executável de console

### Calculator (Debug Tool)

Calculadora simples para testar expressões matemáticas.

**Output**: `Calculator.exe` - executável de console

### Core.Tests e API.Tests (Testes)

Projetos de testes automatizados usando xUnit.

**Core.Tests (723 testes):**
- Testes do MathEngine
- Testes do ConfigManager
- Testes do ResourceLoader
- Testes de todos os sistemas Core

**API.Tests (150 testes: 125 unit + 25 integration):**
- Testes unitários de controllers (mocks)
- Testes de integração com TestServer
- Categorização via `[Trait("Category", "Unit|Integration")]`

## Segurança

### API Key Middleware

Endpoints administrativos são protegidos por `AdminKeyMiddleware` que requer o header `X-Admin-Key`.

**Configuração:**

Via `appsettings.json`:
```json
{
  "Admin": {
    "ApiKey": "your-secret-key-here"
  }
}
```

Ou via variável de ambiente (recomendado para produção):
```bash
export HERESCRIPT_ADMIN_KEY="your-secret-key-here"
```

**Uso:**
```bash
curl -X POST http://localhost:5260/api/action/reload \
  -H "X-Admin-Key: your-secret-key-here"
```

**Endpoints Protegidos:**
- `POST /api/action/reload`
- `POST /api/game-resources/reload`
- `POST /api/config/load`
- `POST /api/modifiers/reload`
- `POST /api/gambits/reload`
- `POST /api/status/reload`

⚠️ **Produção:** Configure uma chave forte antes de deploy. Não commite chaves em `appsettings.json`.

**Documentação completa:** [docs/security.md](docs/security.md)

---

## Persistência

HeroScript suporta persistência em disco para restart-safety e auditoria.

**Configuração:**

```json
{
  "Persistence": {
    "EventStorePath": "data/events/",
    "RunStatePath": "data/runs/"
  }
}
```

**Componentes:**

- **JsonFileEventStore**: Eventos em formato `.jsonl` (append-only, auditoria completa)
- **JsonFileRunStateRepository**: Run state em `{runId}.json` (write atômico, auto-load)

**Restart Safety:** Após reiniciar o processo, `GET /api/run/{runId}` retorna o estado anterior e `GET /api/events` inclui eventos de sessões anteriores.

**Documentação completa:** [docs/persistence.md](docs/persistence.md)

---

## Observabilidade

### Correlation IDs

Todos os requests recebem um `X-Correlation-ID` único (gerado automaticamente ou fornecido pelo cliente). Este ID é incluído em todos os logs do request para facilitar troubleshooting.

**Response Header:**
```
X-Correlation-ID: 3fa85f64-5717-4562-b3fc-2c963f66afa6
```

### Logging Estruturado

HeroScript usa `ILogger` com templates estruturados (indexáveis por log aggregators):

```csharp
// ✅ Estruturado
_logger.LogInformation("Effect {EffectId} resolved for {EntityId}", effectId, entityId);

// ❌ Não estruturado
_logger.LogInformation($"Effect {effectId} resolved for {entityId}");
```

**Níveis:** `Debug` (path resolution), `Information` (lifecycle events), `Warning` (fallbacks), `Error` (exceptions)

**Documentação completa:** [docs/observability.md](docs/observability.md)

---

## Architecture

HeroScript uses a **REST API architecture** where game clients consume the engine over HTTP:

```
Game Client (Unity/Godot/Web)
    ↓ HTTP (5-8ms localhost)
API Layer (ASP.NET Core)
    ↓ In-process calls
Core Engine (C# .NET)
    ↓ File I/O
Data (JSON configs, events, runs)
```

**Benefits:**
- **Hot-reload**: Update game configs without restarting clients
- **Multi-client**: Share engine across Unity + Godot + Web tools
- **Web tools**: Build dashboards, editors, simulators
- **Testing**: Run 10k+ simulations via Python scripts
- **Future-proof**: Easy path to multiplayer/cloud

**Performance:** 5-8ms latency on localhost (imperceptible for turn-based games)

---

## Client Integration

See **[CLIENT_INTEGRATION.md](docs/CLIENT_INTEGRATION.md)** for complete integration guides:

### Unity (C#)
```csharp
// UnityWebRequest example
StartCoroutine(client.StartCombat(
    new CombatStartRequest { 
        heroEntityId = "knight", 
        enemyEntityIds = new[] { "goblin" } 
    },
    OnCombatStarted,
    OnError
));
```

### Godot (GDScript)
```gdscript
# HTTPRequest example
var combat = await client.start_combat("knight", ["goblin"])
print("Combat started: ", combat["combatId"])
```

### Web (TypeScript)
```typescript
// Fetch API example
const combat = await heroScriptClient.startCombat({
  heroEntityId: 'knight',
  enemyEntityIds: ['goblin']
});
```

### Python (Simulations)
```python
# Requests library for headless testing
client = HeroScriptClient()
combat = client.start_combat("knight", ["goblin"])
```

---

## Hot-Reload Workflow

For rapid iteration during development:

1. **Edit config files** (e.g., `data/configs/default/Actions/fireball.json`)
2. **Reload via API** (no restart needed):
   ```bash
   curl -X POST http://localhost:5260/api/actions/reload \
     -H "X-Admin-Key: dev-admin-key"
   ```
3. **Test immediately** in running game client

This works for all definition types: Actions, Entities, Status Effects, Gambits.

## Build e Testes

### Build Local

```bash
# Build solution completa
dotnet build HeroScript.slnx

# Build Core library
dotnet build src/Core/Core.csproj

# Build API
dotnet build src/API/API.csproj

# Build CLI (opcional)
dotnet build tools/Core.CLI/Core.CLI.csproj
```

### Testes

```bash
# Todos os testes
dotnet test

# Apenas Core (723 testes)
dotnet test tests/Core.Tests/Core.Tests.csproj

# Apenas API - testes unitários (125 testes, rápido)
dotnet test tests/API.Tests/API.Tests.csproj --filter "Category=Unit"

# Apenas API - testes de integração (25 testes, usa TestServer)
dotnet test tests/API.Tests/API.Tests.csproj --filter "Category=Integration"
```

### CI/CD

Pipeline GitHub Actions em `.github/workflows/ci.yml`:

```yaml
jobs:
  test:
    steps:
      - Build solution
      - Core Tests (723 testes)
      - API Unit Tests (125 testes) - filtro Category=Unit
      - API Integration Tests (25 testes) - filtro Category=Integration, timeout 5min
```

Pipeline executa em:
- Push para `main`
- Pull requests

**Configuração de timeout:** `tests/heroscript.runsettings` define 30s por teste, 5min por sessão.

## Arquitetura

O projeto segue a arquitetura descrita em [`docs/architecture/overview.md`](docs/architecture/overview.md):

- **Headless**: Core é completamente independente de UI
- **Data-driven**: Regras e fórmulas são dados (JSON), não código
- **Event Sourcing**: EventBus com histórico/replay para auditoria e integração
- **Modular**: Configurações podem ser trocadas em runtime

## Documentação

### API REST

A API REST expõe funcionalidades do Core através de endpoints HTTP com documentação Swagger interativa.

**Documentação Completa:**
- [API Endpoints](docs/api/endpoints.md) - Referência dos endpoints atuais
- Swagger UI disponível em `http://localhost:5260/swagger` em desenvolvimento

**APIs Disponíveis:**
- **Action Management** (`/api/action`) - Gerenciamento de definições de ações de combate
- **Resource Management** (`/api/game-resources`) - Gerenciamento de recursos de gameplay (HP, MP, etc)
- **Combat System** (`/api/combat`) - Sistema de combate integrado com ações e recursos
- **Configuration** (`/api/config`) - Gerenciamento de configurações e herança delta
- **Math Engine** (`/api/formula`, `/api/math/expression`, `/api/operation`) - Execução de fórmulas e expressões
- **Effect Engine** (`/api/effect`) - Aplicação central de efeitos data-driven
- **Status Effects** (`/api/status`) - Status ativos, stacks, ticks e modifiers de pipeline
- **Script Modifiers** (`/api/modifiers`) - Modificadores data-driven aplicáveis por owner
- **Gambits** (`/api/gambits`) - Decisão de ações por regras JSON
- **Entities** (`/api/entity`) - Definições e criação de entidades
- **Events** (`/api/events`) - Histórico e consulta de eventos

**Roadmap:**
- [API Roadmap](docs/roadmap/README.md) - Roadmap completo da API (6 fases)
- [API Conventions](docs/roadmap/analysis/api-conventions.md) - Convenções e padrões da API
- [Event Integration](docs/roadmap/analysis/event-integration.md) - Integração com EventBus
- [Frontend Integration Gaps](docs/roadmap/analysis/frontend-integration-gaps.md) - Lacunas para protótipo visual

### Sistemas Core

- [Config System](docs/systems/config/config-system.md) - Sistema de configuração com herança delta
- [Math System](docs/systems/math/math-system.md) - Sistema matemático
- [EventBus System](docs/systems/events/eventbus-system.md) - Sistema pub/sub e histórico de eventos
- [Effect System](docs/systems/effects/effect-system.md) - Sistema de efeitos
- [Damage Pipeline](docs/systems/damage/damage-pipeline.md) - Pipeline de dano configurável

### Infraestrutura

- [Security](docs/security.md) - Segurança da API (AdminKeyMiddleware)
- [Persistence](docs/persistence.md) - Persistência de eventos e run state
- [Observability](docs/observability.md) - Logging estruturado e Correlation IDs

## Roadmap

### Estado das Fases

| Fase | Status | Descrição |
|------|--------|-----------|
| **Fase 0** | ✅ Implementado | Fundação (Config, Math, Resources) |
| **Fase 1** | ✅ Implementado | EventBus, Combate Básico, TurnPhase System |
| **Fase 2** | ✅ Estabilizado | Camadas de Combate (Status, Modifiers, Gambits) |
| **Fase 3** | 🚧 Em implementação | Loop de Run (primeira fatia concluída) |
| **Fase 4** | 📋 Planejado | Conteúdo MVP (Races, Powers, Companions, Enemies) |
| **Fase 5** | 📋 Planejado | Meta-progressão e Save/Load |
| **Fase 6** | 📋 Planejado | Modos Especiais (Seed, Daily, Custom) |

### Fase 3 - Detalhamento

**Implementado:**
- ✅ `RunState` e `DeckState` como núcleo
- ✅ `RunManager` com persistência automática
- ✅ `RunController` (start, state, deck, hand, draw, discard, shuffle)
- ✅ `CardSelectionController` (start, pick, reroll, decompose)
- ✅ `ShopController` (start, buy, reroll, sell)
- ✅ `PreparationController` (start, apply-modifier)
- ✅ `CombatRunCoordinator` (integração combate ↔ deck/hand, consumo real de cartas)
- ✅ Operações compostas com rollback transacional

**Próximos passos:**
- ⏳ Map generation e navigation
- ⏳ Node system (advance to next node)
- ⏳ Event nodes
- ⏳ Rest nodes (cura, remoção de cartas)

### Hardening de Produção (Jun/2026)

O projeto passou por 6 trilhas de hardening:

1. **✅ Event Publishing**: EventBus publica eventos de lifecycle (`CombatStartedEvent`, `StatusAppliedEvent`, etc.)
2. **✅ API Security**: `AdminKeyMiddleware` protege endpoints administrativos
3. **✅ Logging & Observability**: `Console.WriteLine` eliminado, `CorrelationIdMiddleware`, templates estruturados
4. **✅ Event Persistence**: `JsonFileEventStore` com append-only `.jsonl`
5. **✅ Run State Persistence**: `JsonFileRunStateRepository` com write atômico
6. **✅ Test Stability**: `FindProjectRoot()` corrigido, categorização de testes, CI pipeline

**Commits:**
```bash
git log --oneline -6
# 9162814 test(api): improve test stability and CI readiness
# 38ecae9 feat(persistence): add JsonFile event store and run state repository
# c1ab29f feat(logging): eliminate Console.WriteLine, add correlation id middleware
# b22655b feat(api): secure admin endpoints with api key middleware
# c15d05f feat(core): publish lifecycle events across managers
# b1e7d6a feat(core): add Core.Abstractions layer with typed identifiers
```

Para o roadmap completo de hardening, consulte [docs/roadmap/trilhas.md](docs/roadmap/trilhas.md).

**Próximos passos recomendados:**
- Completar Fase 3: Map generation, node navigation, event/rest nodes
- Conteúdo MVP (Fase 4): Definir races, powers iniciais, companions e enemies
- Iteração visual: Frontend prototype consumindo a API REST

## Licença

[A definir]
