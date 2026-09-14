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
curl http://localhost:5260/api/v1/health/live
```

### Integration Guides

- **[Playable Godot Showcase](examples/godot-engine-showcase/README.md)** - Execute a campanha e os laboratórios data-driven
- **[Unity/Godot/Web Integration](docs/CLIENT_INTEGRATION.md)** - Consume API from game clients
- **[Production Deployment](docs/PRODUCTION.md)** - Deploy API to production
- **[API v1 Guide](docs/api/README.md)** - Contrato público e exemplos

**Performance:** depende do comando e do tamanho do histórico. Consulte as
[medições e otimizações da demo](docs/roadmap/ENGINE_PERFORMANCE_AND_DEMO_UX.md).

## 🎯 Status do MVP

**Última análise:** 2026-09-11

### Engine e demo jogável
- **Core systems:** combate, cartas, resources, efeitos, status, relíquias, IA, turnos, prioridade e stack
- **Progressão:** mapa, encontros, recompensas, loja, preparação, upgrades e persistência de runs
- **Ferramentas determinísticas:** timeline, branches, simulação sem commit e verificação de replay
- **Validação atual:** 1.073 testes Core e 155 testes da API aprovados
- **Integração:** comandos e read models de run/combate são expostos pelo contrato único `/api/v1`
- **Demo Godot:** campanha completa e três sandboxes de regras consumindo somente a REST API; português/inglês, prévias canônicas e animações de cartas

### Escopo atual

O showcase é uma prova funcional de arquitetura, não um jogo final: conteúdo,
balanceamento, narrativa, arte e polimento ainda pertencem à produção de cada
*setting*. As regras executáveis e o estado autoritativo permanecem na engine;
a Godot cuida de apresentação, áudio e input.

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
│   ├── Core.Tests/           # Testes do Core (1.306 testes)
│   ├── API.Tests/            # Testes automatizados da API (219 testes)
│   └── heroscript.runsettings # Configuração de timeout para testes
├── .github/workflows/        # CI/CD pipeline
│   └── ci.yml                # GitHub Actions
└── docs/                     # Documentação técnica
```

## Estado Atual

**Última atualização:** 2026-08-16
**Fase atual:** estabilização técnica da Fase 3; Map System e Event System continuam bloqueadores para o MVP jogável.

- **Core.Tests:** 1.306 testes aprovados na última validação completa
- **API.Tests:** 219 testes aprovados na última validação completa
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

**Core.Tests (1.073 testes):**
- Testes do MathEngine
- Testes do ConfigManager
- Testes do ResourceLoader
- Testes de todos os sistemas Core

**API.Tests (155 testes):**
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
curl -X POST http://localhost:5260/api/v1/actions/reload \
  -H "X-Admin-Key: your-secret-key-here"
```

**Endpoints Protegidos:**
- `POST /api/v1/actions/reload`
- `POST /api/v1/admin/content/reload`
- `POST /api/v1/admin/config/load`
- `POST /api/v1/modifiers/reload`
- `POST /api/v1/gambits/reload`

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

**Restart Safety:** Após reiniciar o processo, `GET /api/v1/runs/{runId}` retorna o estado anterior. Use o journal e os eventos duráveis da run para recuperação.

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

Hot reload recompila o setting inteiro e publica uma nova revisão imutável; ele
nunca altera silenciosamente uma run ativa. No ambiente de desenvolvimento:

1. Edite os packages em `data/configs/default`.
2. Valide com `POST /api/v1/admin/settings/default/validate`.
3. Publique com `POST /api/v1/admin/content/reload`, corpo
   `{ "settingId": "default" }` e `X-Admin-Key`.
4. Para migrar uma run permitida, consulte primeiro
   `GET /api/v1/runs/{runId}/content/activation-preview?targetRevision={revision}` e depois envie
   `ACTIVATE_CONTENT_REVISION` como comando versionado.

Perfis operacionais e políticas do game mode são limites independentes. Consulte
`GET /api/v1/runs/{runId}/capabilities` antes de exibir ferramentas. O fluxo
completo está em
[`docs/content/hot-reload-and-tool-profiles.md`](docs/content/hot-reload-and-tool-profiles.md).

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

# Apenas Core
dotnet test tests/Core.Tests/Core.Tests.csproj

# Apenas API - testes unitários
dotnet test tests/API.Tests/API.Tests.csproj --filter "Category=Unit"

# Apenas API - testes de integração
dotnet test tests/API.Tests/API.Tests.csproj --filter "Category=Integration"
```

### CI/CD

Pipeline GitHub Actions em `.github/workflows/ci.yml`:

```yaml
jobs:
  test:
    steps:
      - Build solution
      - Core Tests
      - API Unit Tests - filtro Category=Unit
      - API Integration Tests - filtro Category=Integration, timeout 5min
```

Pipeline executa em:
- Push para `main`
- Pull requests

**Configuração de timeout:** `tests/heroscript.runsettings` define 30s por teste, 5min por sessão.

## Arquitetura

O projeto segue a arquitetura descrita em [`docs/architecture/overview.md`](docs/architecture/overview.md):

- **Headless**: Core é completamente independente de UI
- **Data-driven**: Regras e fórmulas são dados (JSON), não código
- **Journal autoritativo**: commits de run para recuperação/replay; EventBus somente para telemetria
- **Modular**: Configurações podem ser trocadas em runtime

## Documentação

### API REST

A API REST expõe funcionalidades do Core exclusivamente sob `/api/v1`.

**Documentação Completa:**
- [API v1](docs/api/README.md) - Referência dos endpoints e fluxos públicos
- OpenAPI em `http://localhost:5260/openapi/v1.json` e UI em `http://localhost:5260/docs/api` durante o desenvolvimento

**APIs Disponíveis:**
- **Runtime** (`/api/v1/runs`, `/api/v1/combats`) - Runs, comandos e read models autoritativos
- **Conteúdo** (`/api/v1/content`) - Revisões e catálogos imutáveis
- **Operação** (`/api/v1/health/*`, `/api/v1/capabilities`) - Saúde e capacidades
- **Administração e simulação** (`/api/v1/admin/*`, `/api/v1/simulations/*`) - Ferramentas fora do loop de jogo

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
