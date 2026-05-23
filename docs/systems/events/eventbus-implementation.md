# Plano de Implementação: EventBus (Fase 1 - O Kernel)

**Data:** 2026-05-08  
**Status:** Planejamento  
**Fase:** Fase 1 — O Kernel

---

## Contexto

### Estado Atual do Projeto

**Infraestrutura Existente:**
- DI Container customizado (`IServiceCollection`, `IServiceProvider`, `ServiceCollection`)
- Logging abstraction (`ILogger` com 5 métodos: Debug, Information, Warning, Error)
- Padrões estabelecidos:
  - Interfaces com prefixo `I` (ex: `IMathEngine`, `IConfigManager`)
  - Result Pattern para tratamento de erros
  - Dependency Injection em todo o projeto
  - Namespace pattern: `Core.{Feature}`
- Pasta `Events/` existe mas está vazia (apenas `.gitkeep`)

**Componentes Implementados:**
- ✅ MathEngine com 17+ operações matemáticas
- ✅ ConfigManager com sistema delta estruturado (9 operações)
- ✅ ResourceLoader genérico para JSON
- ✅ API REST com Swagger
- ✅ 97 testes unitários passando

### Roadmap (do README)

**Fase 1 — O Kernel:**
- ✅ MathEngine serializado (fórmulas JSON)
- ✅ ConfigManager com herança delta
- ✅ API REST básica implementada
- ⏳ **EventBus** (próximo - este documento)
- ⏳ GameState imutável (depois)
- ⏳ BucketPipeline (depois)

### Requisitos do EventBus

Segundo o documento `arquitetura-engine.md` (seções 2.3, 4.3, 7):

1. **Padrão Observer** - Pub/Sub desacoplado entre sistemas
2. **Event Sourcing** - Todos os eventos são registrados para replay
3. **Três níveis de projeção:**
   - Notação de combate (humano-legível)
   - Debug verbose (desenvolvedor)
   - RL Dataset (máquina/IA)
4. **Estrutura LogEntry** com metadados completos:
   - Metadados: `id`, `timestamp`, `turn`, `sequence`
   - Classificação: `category`, `severity`
   - Conteúdo: `subject`, `verb`, `target`, `payload`
   - Estado: `state_before`, `state_after`, `delta`
5. **Replay capability** - `replay_to_turn(n)` reconstrói qualquer estado passado

### Escopo desta Implementação

**Incluído:**
- EventBus básico (pub/sub síncrono)
- Estrutura de eventos imutável (records C#)
- Logging de eventos para Event Sourcing
- Integração com MathEngine e ConfigManager
- Endpoints REST para consulta de eventos
- Testes unitários e de integração

**NÃO incluído (fases futuras):**
- Persistência em disco (arquivos `.acl`) - Fase 4
- Dashboard web - Fase 4
- Replay completo de estados - Fase 4
- WebSocket/async - futuro
- Três projeções de log (notação, debug, RL) - Fase 4

---

## Plano de Implementação

### Etapa 1: Criar Estrutura Base de Eventos

**1.1** Criar `src/Core/Events/IEvent.cs`
- Interface base para todos os eventos
- Propriedades: `EventId` (Guid), `Timestamp` (DateTime), `EventType` (string)

**1.2** Criar `src/Core/Events/EventCategory.cs`
- Enum com categorias: `COMBAT`, `PIPELINE`, `META`, `CONFIG`, `REALITY_BEND`

**1.3** Criar `src/Core/Events/EventSeverity.cs`
- Enum com severidades: `INFO`, `DEBUG`, `WARN`, `ANOMALY`

**1.4** Criar `src/Core/Events/GameEvent.cs`
- Record imutável que implementa `IEvent`
- Propriedades completas conforme LogEntry da arquitetura:
  - Metadados: `Id`, `Timestamp`, `Turn`, `Sequence`
  - Classificação: `Category`, `Severity`
  - Conteúdo: `Subject`, `Verb`, `Target`, `Payload` (Dictionary<string, object>)
  - Estado: `StateBefore`, `StateAfter`, `Delta` (todos Dictionary<string, object>)

### Etapa 2: Implementar EventBus Core

**2.1** Criar `src/Core/Events/IEventBus.cs`
- Interface com métodos:
  - `void Publish<TEvent>(TEvent @event) where TEvent : IEvent`
  - `IDisposable Subscribe<TEvent>(Action<TEvent> handler) where TEvent : IEvent`
  - `IReadOnlyList<IEvent> GetEventHistory()`
  - `void ClearHistory()`

**2.2** Criar `src/Core/Events/EventBus.cs`
- Implementação thread-safe do IEventBus
- Usar `Dictionary<Type, List<Delegate>>` para armazenar handlers por tipo
- Usar `List<IEvent>` para histórico de eventos
- Implementar `EventSubscription` como classe interna que implementa `IDisposable`
- Adicionar logging via `ILogger` injetado

**2.3** Criar `src/Core/Events/EventSubscription.cs`
- Classe que implementa `IDisposable` para gerenciar unsubscribe
- Armazena referência ao EventBus e ao handler

### Etapa 3: Criar Eventos de Domínio

**3.1** Criar `src/Core/Events/Domain/MathFormulaEvaluatedEvent.cs`
- Record que herda de `GameEvent`
- Propriedades específicas: `FormulaName`, `InputValue`, `OutputValue`, `Parameters`

**3.2** Criar `src/Core/Events/Domain/ConfigLoadedEvent.cs`
- Record que herda de `GameEvent`
- Propriedades: `ConfigName`, `ParentConfig`, `ResourcesLoaded`

**3.3** Criar `src/Core/Events/Domain/ConfigChangedEvent.cs`
- Record que herda de `GameEvent`
- Propriedades: `OldConfig`, `NewConfig`, `Reason`

**3.4** Criar `src/Core/Events/Domain/FormulaOverriddenEvent.cs`
- Record que herda de `GameEvent`
- Propriedades: `FormulaName`, `SourceConfig`, `OverriddenBy`

### Etapa 4: Integrar EventBus com MathEngine

**4.1** Modificar `src/Core/Math/MathEngine.cs`
- Adicionar campo opcional `private readonly IEventBus? _eventBus`
- Modificar construtor para aceitar `IEventBus? eventBus = null` (opcional para retrocompatibilidade)
- No método `BuildFromFormula`, após calcular resultado, publicar `MathFormulaEvaluatedEvent`
- Verificar se `_eventBus != null` antes de publicar (não quebrar testes existentes)

**4.2** Atualizar testes em `tests/Core.Tests/Math/MathEngineTests.cs`
- Adicionar teste `BuildFromFormula_WithEventBus_PublishesEvent`
- Criar mock simples de EventBus para capturar eventos
- Verificar que evento é publicado com dados corretos

### Etapa 5: Integrar EventBus com ConfigManager

**5.1** Modificar `src/Core/Config/ConfigManager.cs`
- Adicionar campo opcional `private readonly IEventBus? _eventBus`
- Modificar construtor para aceitar `IEventBus? eventBus = null`
- No método `LoadConfig`, publicar `ConfigLoadedEvent` após carregar
- Ao detectar override de fórmula, publicar `FormulaOverriddenEvent`

**5.2** Criar testes em `tests/Core.Tests/Events/EventBusTests.cs`
- Teste: `Publish_SingleSubscriber_ReceivesEvent`
- Teste: `Publish_MultipleSubscribers_AllReceiveEvent`
- Teste: `Subscribe_Dispose_StopsReceivingEvents`
- Teste: `GetEventHistory_ReturnsAllPublishedEvents`
- Teste: `Publish_DifferentEventTypes_OnlyMatchingSubscribersReceive`

### Etapa 6: Registrar EventBus no DI Container

**6.1** Criar `src/Core/Events/EventBusServiceExtensions.cs`
- Método de extensão: `AddEventBus(this IServiceCollection services)`
- Registrar `IEventBus` como singleton apontando para `EventBus`

**6.2** Atualizar documentação em `src/Core/Events/README.md` (criar)
- Explicar o que é o EventBus
- Como usar (publish/subscribe)
- Exemplos de código
- Lista de eventos de domínio disponíveis
- Integração com DI

### Etapa 7: Adicionar Endpoints na API

**7.1** Criar `src/API/Controllers/EventsController.cs`
- Endpoint: `GET /api/events` - Lista eventos da sessão atual
- Endpoint: `GET /api/events/{eventId}` - Obtém evento específico
- Endpoint: `DELETE /api/events` - Limpa histórico (apenas dev mode)
- Injetar `IEventBus` via DI

**7.2** Atualizar [`endpoints.md`](../../api/endpoints.md)
- Documentar novos endpoints de eventos
- Incluir exemplos de resposta JSON

### Etapa 8: Criar Testes de Integração

**8.1** Criar `tests/Core.Tests/Integration/EventBusIntegrationTests.cs`
- Teste: `MathEngine_EvaluatesFormula_PublishesEvent`
- Teste: `ConfigManager_LoadsConfig_PublishesEvent`
- Teste: `EventHistory_PreservesOrder`
- Teste: `MultipleComponents_PublishEvents_AllCaptured`

**8.2** Executar todos os testes
- Garantir que os 97 testes existentes continuam passando
- Novos testes devem passar

### Etapa 9: Atualizar Documentação do Projeto

**9.1** Atualizar `README.md`
- Marcar EventBus como ✅ no roadmap (linha 128)
- Adicionar exemplo de uso do EventBus

**9.2** Criar `docs/EVENT_SYSTEM.md`
- Arquitetura do EventBus
- Padrões de uso (pub/sub)
- Como criar eventos customizados
- Integração com componentes existentes
- Roadmap futuro (persistência, replay, WebSocket)

---

## Riscos e Mitigações

### 1. Retrocompatibilidade com Código Existente

**Risco:** Integração do EventBus pode quebrar testes ou código existente que não espera eventos.

**Mitigação:**
- EventBus é **opcional** via DI (parâmetro `IEventBus? eventBus = null`)
- Verificar `_eventBus != null` antes de publicar eventos
- Todos os 97 testes existentes devem continuar passando sem modificação
- Apenas novos testes usarão EventBus explicitamente

### 2. Performance com Muitos Eventos

**Risco:** EventBus pode se tornar gargalo se houver muitos eventos/handlers.

**Mitigação:**
- Começar com implementação síncrona simples
- Usar `List<IEvent>` com capacidade inicial adequada
- Adicionar método `ClearHistory()` para limpar eventos antigos
- Documentar que async/await virá em versão futura se necessário
- Considerar limite configurável de eventos em memória (ex: últimos 1000)

### 3. Thread Safety

**Risco:** Múltiplas threads publicando/subscrevendo simultaneamente podem causar race conditions.

**Mitigação:**
- Usar `lock` em operações críticas (subscribe, unsubscribe, publish)
- Documentar que a implementação atual é thread-safe mas síncrona
- Considerar `ConcurrentDictionary` se performance for problema

### 4. Memory Leaks com Subscriptions

**Risco:** Subscribers que não fazem `Dispose()` podem causar memory leaks.

**Mitigação:**
- Implementar `EventSubscription : IDisposable` corretamente
- Documentar importância do `using` ou `Dispose()` explícito
- Considerar `WeakReference` em versão futura se necessário
- Adicionar testes que verificam unsubscribe

### 5. Serialização de Payloads Complexos

**Risco:** `Dictionary<string, object>` pode conter objetos não serializáveis.

**Mitigação:**
- Documentar que payloads devem ser tipos primitivos ou DTOs simples
- Adicionar validação/logging se serialização falhar
- Considerar usar `JsonElement` do `System.Text.Json` para payloads
- Testes devem cobrir casos de serialização

### 6. Ordem de Eventos em Cenários Concorrentes

**Risco:** Em cenários futuros com async, ordem de eventos pode ser importante.

**Mitigação:**
- Adicionar campo `Sequence` aos eventos desde o início
- Usar contador atômico para garantir sequência única
- Documentar garantias de ordem (FIFO dentro de uma thread)
- Preparar arquitetura para async futuro

### 7. Tamanho do Histórico de Eventos

**Risco:** Histórico pode crescer indefinidamente em sessões longas.

**Mitigação:**
- Implementar `ClearHistory()` desde o início
- Documentar que persistência em disco virá na Fase 4
- Considerar limite configurável (ex: últimos 1000 eventos)
- API deve ter endpoint para limpar histórico (apenas dev mode)

---

## Decisões Pendentes

Antes de prosseguir com a implementação, confirmar:

1. **Escopo de persistência:** Deixar a persistência em disco (arquivos `.acl`) para a Fase 4?
2. **Integração obrigatória vs opcional:** EventBus deve ser opcional (recomendado) ou obrigatório?
3. **Limite de histórico:** Implementar limite configurável de eventos em memória ou deixar ilimitado?
4. **Async desde o início:** Implementação síncrona agora e async depois, ou já começar com async/await?
5. **Prioridade de testes:** Criar testes de integração completos ou focar apenas em testes unitários?

---

## Referências

- `docs/arquitetura-engine.md` - Arquitetura completa do sistema
- `README.md` - Roadmap do projeto
- [`config-system.md`](../config/config-system.md) - Sistema de configuração com herança delta
- `src/Core/Math/MathEngine.cs` - Engine de fórmulas matemáticas
- `src/Core/Config/ConfigManager.cs` - Gerenciador de configurações

---

**Próximos Passos:**
1. Confirmar decisões pendentes com o time
2. Iniciar implementação pela Etapa 1
3. Executar testes após cada etapa
4. Atualizar este documento conforme progresso
