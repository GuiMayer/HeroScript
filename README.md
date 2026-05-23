# HeroScript Engine

HeroScript é uma engine headless para jogos de cartas roguelike, projetada para ser embarcável em qualquer game engine (Unity, Godot, etc.) através de uma arquitetura modular e data-driven.

## Estrutura do Projeto

```
HeroScript/
├── src/                    # Código de produção
│   ├── Core/              # Biblioteca principal (DLL embarcável)
│   ├── API/               # REST API para exposição do Core
│   └── Mods/              # Sistema de mods (futuro)
├── tools/                  # Ferramentas de desenvolvimento
│   ├── Core.CLI/          # CLI de debug e testes
│   └── Calculator/        # Calculadora de debug
└── tests/                  # Testes unitários
    └── Core.Tests/        # Testes do Core
```

## Estado Atual

**Última atualização:** 2026-05-23
**Fase atual:** Fase 2 estabilizada. O próximo foco técnico é a **Fase 3 - Loop de Run**.

- **Core.Tests:** 553 testes passando.
- **Fase 0:** Config, Math e Resources implementados.
- **Fase 1:** EventBus, Combat, Damage Pipeline, TurnPhase e TurnOrder implementados.
- **Fase 2:** Status Effects, Script Modifiers, Gambit Engine e Effect Engine estabilizados.
- **Fase 3:** ainda não implementada; faltam RunState, DeckState, RunManager, CardSelection, Shop e Preparation.
- **API.Tests:** o projeto compila, mas o runner local ainda apresenta timeout/congelamento em partes da suite. Use testes filtrados/menores até a investigação ser concluída.

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

### Core.CLI (Debug Tool)

Ferramenta de linha de comando para debug e testes do Core.

- Execução de testes integrados
- Listagem de configurações disponíveis
- Carregamento de configs específicas

**Output**: `Core.CLI.exe` - executável de console

### Calculator (Debug Tool)

Calculadora simples para testar expressões matemáticas.

**Output**: `Calculator.exe` - executável de console

### Core.Tests (Testes)

Projeto de testes unitários usando xUnit.

- Testes do MathEngine
- Testes do ConfigManager
- Testes do ResourceLoader

## Como Usar

### Como Biblioteca Embarcável

```csharp
using Core.Math;

var engine = new MathEngine();
var expr = engine.BuildFromFormula("HYPERBOLIC_CURVE", 100);
var result = expr.Build();
Console.WriteLine($"Result: {result}");
```

### Como API REST

```bash
cd src/API
dotnet run
# Acesse http://localhost:5260
```

### Como CLI de Debug

```bash
cd tools/Core.CLI
dotnet run -- --help
dotnet run -- --test
dotnet run -- --config alisyum
```

## Build

```bash
# Build Core library
dotnet build src/Core/Core.csproj

# Build API
dotnet build src/API/API.csproj

# Build CLI
dotnet build tools/Core.CLI/Core.CLI.csproj

# Run tests
dotnet test tests/Core.Tests/Core.Tests.csproj
```

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

## Roadmap

Estamos atualmente após a **Fase 2 — Camadas de Combate**:

- ✅ MathEngine serializado (fórmulas JSON)
- ✅ ConfigManager com herança delta
- ✅ API REST data-driven implementada para sistemas Core atuais
- ✅ ActionManager com discovery JSON e efeitos como contrato principal
- ✅ ResourceManager API
- ✅ Combat System integrado com Actions e Resources
- ✅ EventBus e Damage Pipeline
- ✅ Status Effects, Script Modifiers, Gambit Engine e Effect Engine
- ⏳ Fase 3: Run Management, Deck/Hand, CardSelection, Shop e Preparation

**Próximos passos recomendados:**
- Sincronizar `RunState` e `DeckState` como núcleo da Fase 3.
- Expor contratos mínimos para frontend: hand/deck, end-turn dedicado e processamento de IA.
- Implementar Run API, CardSelection, Shop e Preparation em fatias verticais.

## Licença

[A definir]
