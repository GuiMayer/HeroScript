# HeroScript Documentation

**Última atualização:** 2026-09-06
**Versão:** 1.0.0

---

## Quick Links

- **[Client Integration Guide](CLIENT_INTEGRATION.md)** - Integrate clients through the v1 contract
- **[Production Deployment Guide](PRODUCTION.md)** - Deploy API to production servers
- **[API v1 Guide](api/README.md)** - Contrato público e exemplos
- [Architecture Overview](architecture/overview.md)
- [API v1 Contract](api/README.md)
- [Development Roadmap](roadmap/README.md)

---

## Core Systems

### Combat & Gameplay

- **[Combat System](systems/combat/combat-system.md)** - Sistema de combate imutável persistido por commits autoritativos
- **[Turn Phase System](systems/combat/turn-phase-system.md)** - Fluxo canônico e configurável de ativações
- **[Damage & Calculation Pipeline](systems/damage/damage-pipeline.md)** - Dano como alteração genérica de recurso
- **[Damage Examples](systems/damage/damage-examples.md)** - Exemplos de efeitos e scaling
- **[Alternative Costs](systems/combat/alternative-costs.md)** - Sistema de custos alternativos para ações

### Data & Configuration

- **[Config System](systems/config/config-system.md)** - Packages, settings, bundles e revisões
- **[Composition Reference](systems/config/delta-reference.md)** - Definições e patches explícitos
- **[Package Authoring](content/packages-and-settings.md)** - Guia de autoria de packages/settings
- **[Resource System](systems/resources/resource-system.md)** - Gerenciamento de recursos (HP, Mana, Energy, etc.)
- **[Calculation System](systems/calculations/calculation-system.md)** - Pipelines, buckets, influências e traces
- **[Math Engine](systems/math/math-system.md)** - Motor de expressões matemáticas configuráveis
- **[Expression Modes](systems/math/expression-modes.md)** - Modos de avaliação de expressões

### Events & Effects

- **[EventBus System](systems/events/eventbus-system.md)** - Pub/sub e telemetria operacional
- **[EventBus Implementation](systems/events/eventbus-implementation.md)** - Detalhes de implementação
- **[Effect System](systems/effects/effect-system.md)** - Sistema de efeitos e modificadores
- **[Status System](systems/status/status-system.md)** - Status como containers de componentes
- **[Relic System](systems/relics/relic-system.md)** - Relíquias fixadas no estado da run
- **[Modifier System](systems/modifiers/modifier-system.md)** - Influências temporárias/persistentes com ownership

## Architecture

- **[System Overview](architecture/overview.md)** - Visão geral da arquitetura headless
- **[Deterministic Runs](architecture/deterministic-runs.md)** - Contrato de estado imutável, persistência e replay
- **[Cross-cutting Systems](architecture/cross-cutting-systems.md)** - Regras comuns de conteúdo, cache, matemática, comandos, eventos, erros e DI
- **[Timeline System](architecture/timeline-system.md)** - Sistema de Timeline (Undo/Redo/Simulação)
- **[Service Patterns](architecture/service-patterns.md)** - Padrões de serviço e injeção de dependências

---

## API Reference

- **[API v1 Contract Guide](api/README.md)** - Referência versionada para clientes
- **[API Conventions](roadmap/analysis/api-conventions.md)** - Convenções e padrões da API

---

## Development

### Roadmap

- **[Roadmap Overview](roadmap/README.md)** - Visão geral do roadmap técnico

### Phases

- [Phase 0](roadmap/phases/phase-0.md) - ✅ Fundação (Config, Math, Resources)
- [Phase 1](roadmap/phases/phase-1.md) - ✅ EventBus e Combate Básico
- [Phase 2](roadmap/phases/phase-2.md) - 📋 Camadas de Combate (Status, Modifiers, Gambits)
- [Phase 3](roadmap/phases/phase-3.md) - 📋 Loop de Run (Run, CardSelection, Shop)
- [Phase 4](roadmap/phases/phase-4.md) - 📋 Conteúdo MVP (Races, Powers, Companions, Enemies)
- [Phase 5](roadmap/phases/phase-5.md) - 📋 Persistência (Save/Load, MetaProgression)
- [Phase 6](roadmap/phases/phase-6.md) - 📋 Modos Especiais (Seed, Daily, Custom)

### Analysis

- **[Core Modules Analysis](roadmap/analysis/core-modules.md)** - Análise de módulos implementados vs necessários
- **[Data-driven Compliance](roadmap/analysis/data-driven-compliance.md)** - Aderência à filosofia data-driven
- **[Frontend Integration Gaps](roadmap/analysis/frontend-integration-gaps.md)** - Lacunas para protótipo visual
- **[Resource Cost Analysis](roadmap/analysis/resource-costs.md)** - Análise do sistema de custos
- **[Event Integration](roadmap/analysis/event-integration.md)** - Integração com EventBus

---

## Examples

- **[Alternative Costs Examples](examples/alternative-costs.json)** - Exemplos de configuração de custos alternativos

---

## Getting Started

### Quick Start with Docker

```bash
# 1. Start API server
docker-compose up -d

# 2. Verify health
curl http://localhost:5260/api/v1/health/live

# 3. Integrate with your game client
# See CLIENT_INTEGRATION.md for the v1 client workflow
```

### Local Development

```bash
# Build and run API
cd src/API
dotnet run

# API available at http://localhost:5260
# OpenAPI contract at http://localhost:5260/openapi/v1.json
# Interactive docs in development at http://localhost:5260/docs/api
```

### Testing

```bash
# Run all tests
dotnet test

# Core tests only
dotnet test tests/Core.Tests/Core.Tests.csproj

# API tests only
dotnet test tests/API.Tests/API.Tests.csproj
```

### Next Steps

1. **[Integrate your game client](CLIENT_INTEGRATION.md)** - Unity, Godot, Web, Python examples
2. **[Deploy to production](PRODUCTION.md)** - Docker, systemd, nginx configuration
3. **[Explore the API contract](api/README.md)** - Complete v1 reference

---

## Estado verificado

A validação final desta revisão deve ser registrada no plano de consolidação. Os
números neste índice não são mantidos manualmente para evitar estatísticas
obsoletas; execute `dotnet test` para o total atual.

O caminho autoritativo implementado inclui:

- runs e combates imutáveis com journal, replay semântico e branches;
- cartas componentizadas, deck, custos, upgrades, shop e preparação;
- fluxo de ativações, IA por gambits e dois orçamentos iniciais de ação;
- efeitos atômicos, recursos genéricos e cálculos por buckets;
- status, relíquias e modifiers como componentes com ownership/revisão;
- conteúdo versionado, publicação validada e ativação explícita para hot reload;
- API v1, previews, timeline e filas de animação para clientes headless.

Reações/pilha permanecem reservadas e desabilitadas. O dashboard não faz parte da
validação atual.

---

## Documentation Structure

This documentation is organized into the following sections:

- **systems/** - Technical documentation for each Core system
- **architecture/** - High-level architectural concepts and patterns
- **api/** - REST API reference
- **roadmap/** - Development roadmap and planning
- **examples/** - Configuration examples and sample code

---

## Contributing

When adding new features or systems:

1. Implement the feature in `src/Core/`
2. Add comprehensive tests in `tests/Core.Tests/`
3. Document the system in `docs/systems/[category]/`
4. Update this README with links to the new documentation
5. Update the roadmap if applicable

---

## File Reorganization

This documentation was recently reorganized for better structure and discoverability. If you have bookmarks to old paths, see [MOVED.md](MOVED.md) for a mapping of old paths to new locations.

---

## License

*(Add license information here)*

---

## Contact

*(Add contact information here)*
