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

## Componentes

### Core (Biblioteca)

O Core é o coração da engine - uma biblioteca .NET que pode ser embarcada em qualquer projeto. Contém:

- **MathEngine**: Sistema de fórmulas matemáticas serializadas (JSON)
- **ConfigManager**: Sistema de configuração com herança delta
- **ResourceLoader**: Carregamento de recursos data-driven

**Output**: `Core.dll` - biblioteca embarcável

### API (REST API)

Camada de exposição HTTP do Core, permitindo consumo via REST API.

- Swagger UI disponível em desenvolvimento
- Endpoints para fórmulas matemáticas e expressões
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

O projeto segue a arquitetura descrita em `docs/01_Future/arquitetura-engine.md`:

- **Headless**: Core é completamente independente de UI
- **Data-driven**: Regras e fórmulas são dados (JSON), não código
- **Event Sourcing**: Sistema de log built-in (futuro)
- **Modular**: Configurações podem ser trocadas em runtime

## Documentação

### API REST

A API REST expõe funcionalidades do Core através de endpoints HTTP com documentação Swagger interativa.

**Documentação Completa:**
- [API-ENDPOINTS.md](docs/API_ENDPOINTS.md) - Documentação detalhada com exemplos em C#, JavaScript e Python
- Swagger UI disponível em `http://localhost:5000/` (modo desenvolvimento)

**APIs Disponíveis:**
- **Action Management** (`/api/action`) - Gerenciamento de definições de ações de combate
- **Resource Management** (`/api/game-resources`) - Gerenciamento de recursos de gameplay (HP, MP, etc)
- **Combat System** (`/api/combat`) - Sistema de combate integrado com ações e recursos
- **Configuration** (`/api/config`) - Gerenciamento de configurações e herança delta
- **Math Engine** (`/api/math`) - Execução de fórmulas matemáticas

**Roadmap:**
- [API Roadmap](docs/roadmap/README.md) - Roadmap completo da API (6 fases)
- [API Conventions](docs/roadmap/API_CONVENTIONS.md) - Convenções e padrões da API
- [Event Integration](docs/roadmap/EVENT_INTEGRATION.md) - Integração com EventBus (Polling vs WebSocket)

### Sistemas Core

- [CONFIG_SYSTEM.md](docs/CONFIG_SYSTEM.md) - Sistema de configuração com herança delta
- [CORE_MATH_SYSTEM.md](docs/CORE_MATH_SYSTEM.md) - Sistema matemático
- [EVENTBUS_IMPLEMENTATION_PLAN.md](docs/EVENTBUS_IMPLEMENTATION_PLAN.md) - Plano de implementação do EventBus

## Roadmap

Estamos atualmente na **Fase 1 — O Kernel**:

- ✅ MathEngine serializado (fórmulas JSON)
- ✅ ConfigManager com herança delta
- ✅ API REST básica implementada
- ✅ ActionManager API completa (14 endpoints)
- ✅ ResourceManager API completa (8 endpoints)
- ✅ Combat System integrado com Actions e Resources
- ✅ Documentação Swagger/OpenAPI
- ⏳ EventBus (próximo)
- ⏳ GameState imutável (próximo)
- ⏳ BucketPipeline (próximo)

**Novidades Recentes (2026-05-09):**
- 14 novos endpoints REST para Actions e Resources
- Integração completa entre Combat, Actions e Resources
- Documentação interativa via Swagger
- Exemplos de código em múltiplas linguagens (C#, JS, Python)
- Suporte para modding e extensibilidade

## Licença

[A definir]
