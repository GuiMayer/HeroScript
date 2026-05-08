# EventBus System

**Status:** ✅ Implementado  
**Versão:** 1.0.0  
**Data:** 2026-05-08

---

## Visão Geral

O EventBus é um sistema de pub/sub (publish/subscribe) thread-safe que permite comunicação desacoplada entre componentes do HeroScript. Ele implementa Event Sourcing, mantendo um histórico completo de todos os eventos publicados para auditoria, replay e debugging.

## Arquitetura

```
┌─────────────────────────────────────────────────────────────┐
│                         EventBus                             │
│  ┌────────────────────────────────────────────────────────┐ │
│  │  Publish/Subscribe Pattern                             │ │
│  │  - Thread-safe event publishing                        │ │
│  │  - Type-safe subscriptions                             │ │
│  │  - Automatic sequence numbering                        │ │
│  └────────────────────────────────────────────────────────┘ │
│  ┌────────────────────────────────────────────────────────┐ │
│  │  Event History (Event Sourcing)                        │ │
│  │  - Complete event log                                  │ │
│  │  - Queryable by category/severity                      │ │
│  │  - Immutable event records                             │ │
│  └────────────────────────────────────────────────────────┘ │
└─────────────────────────────────────────────────────────────┘
         │                    │                    │
         ▼                    ▼                    ▼
   ┌──────────┐        ┌──────────┐        ┌──────────┐
   │MathEngine│        │  Config  │        │  Combat  │
   │          │        │ Manager  │        │  System  │
   └──────────┘        └──────────┘        └──────────┘
```

## Estrutura de Eventos

### IEvent (Interface Base)

```csharp
public interface IEvent
{
    Guid EventId { get; }        // ID único do evento
    DateTime Timestamp { get; }   // Quando foi criado
    string EventType { get; }     // Nome do tipo
}
```

### GameEvent (Record Imutável)

```csharp
public record GameEvent : IEvent
{
    // Metadados
    public Guid EventId { get; init; }
    public DateTime Timestamp { get; init; }
    public string EventType { get; init; }
    public int Turn { get; init; }
    public int Sequence { get; init; }
    
    // Classificação
    public EventCategory Category { get; init; }
    public EventSeverity Severity { get; init; }
    
    // Conteúdo (notação subject-verb-target)
    public string Subject { get; init; }
    public string Verb { get; init; }
    public string Target { get; init; }
    public Dictionary<string, object> Payload { get; init; }
    
    // Estado (para Event Sourcing)
    public Dictionary<string, object>? StateBefore { get; init; }
    public Dictionary<string, object>? StateAfter { get; init; }
    public Dictionary<string, object>? Delta { get; init; }
}
```

### Categorias de Eventos

```csharp
public enum EventCategory
{
    COMBAT,         // Eventos de combate (ataque, dano, morte)
    PIPELINE,       // Eventos do pipeline de dano
    META,           // Eventos de sistema (config, resources)
    CONFIG,         // Eventos de configuração
    REALITY_BEND    // Eventos especiais (anomalias)
}
```

### Severidades

```csharp
public enum EventSeverity
{
    DEBUG,    // Informação detalhada de debug
    INFO,     // Informação normal
    WARN,     // Aviso (inesperado mas não crítico)
    ANOMALY   // Anomalia (Reality Bend, comportamento especial)
}
```

## Eventos de Domínio

### MathFormulaEvaluatedEvent

Publicado quando o MathEngine avalia uma fórmula.

```csharp
public record MathFormulaEvaluatedEvent : GameEvent
{
    public string FormulaName { get; init; }
    public float InputValue { get; init; }
    public float OutputValue { get; init; }
    public Dictionary<string, float> Parameters { get; init; }
}
```

**Exemplo:**
```json
{
  "eventId": "550e8400-e29b-41d4-a716-446655440000",
  "timestamp": "2026-05-08T14:30:00Z",
  "eventType": "MathFormulaEvaluatedEvent",
  "sequence": 42,
  "category": "META",
  "severity": "DEBUG",
  "subject": "MathEngine",
  "verb": "evaluated",
  "target": "HYPERBOLIC_CURVE",
  "formulaName": "HYPERBOLIC_CURVE",
  "inputValue": 10.0,
  "outputValue": 85.5,
  "parameters": {
    "SCALE": 100.0,
    "RATE": 0.5
  }
}
```

### ConfigLoadedEvent

Publicado quando o ConfigManager carrega uma configuração.

```csharp
public record ConfigLoadedEvent : GameEvent
{
    public string ConfigName { get; init; }
    public string? ParentConfig { get; init; }
    public int ResourcesLoaded { get; init; }
}
```

**Exemplo:**
```json
{
  "eventId": "660e8400-e29b-41d4-a716-446655440001",
  "timestamp": "2026-05-08T14:31:00Z",
  "eventType": "ConfigLoadedEvent",
  "sequence": 43,
  "category": "CONFIG",
  "severity": "INFO",
  "subject": "ConfigManager",
  "verb": "loaded",
  "target": "my-mod",
  "configName": "my-mod",
  "parentConfig": "alisyum",
  "resourcesLoaded": 3
}
```

### ConfigChangedEvent

Publicado quando a configuração ativa é trocada.

```csharp
public record ConfigChangedEvent : GameEvent
{
    public string OldConfig { get; init; }
    public string NewConfig { get; init; }
    public string Reason { get; init; }
}
```

### ResourceReloadedEvent

Publicado quando recursos são recarregados (futuro).

```csharp
public record ResourceReloadedEvent : GameEvent
{
    public string ResourcePath { get; init; }
    public string ConfigName { get; init; }
    public int ResourceCount { get; init; }
}
```

## API do EventBus

### IEventBus Interface

```csharp
public interface IEventBus
{
    // Publicar evento
    void Publish<TEvent>(TEvent @event) where TEvent : IEvent;
    
    // Registrar handler (retorna IDisposable para unsubscribe)
    IDisposable Subscribe<TEvent>(Action<TEvent> handler) where TEvent : IEvent;
    
    // Consultar histórico
    IReadOnlyList<IEvent> GetEventHistory();
    IReadOnlyList<IEvent> GetEventHistory(EventCategory category);
    IReadOnlyList<IEvent> GetEventHistory(EventSeverity severity);
    
    // Limpar histórico (dev/testing)
    void ClearHistory();
}
```

### Uso Básico

**Publicar Evento:**
```csharp
_eventBus.Publish(new ConfigLoadedEvent
{
    ConfigName = "my-mod",
    ParentConfig = "alisyum",
    ResourcesLoaded = 5,
    Target = "my-mod"
});
```

**Subscrever a Eventos:**
```csharp
// Subscription com IDisposable
using var subscription = _eventBus.Subscribe<ConfigLoadedEvent>(e =>
{
    Console.WriteLine($"Config loaded: {e.ConfigName}");
});

// Subscription manual
var subscription = _eventBus.Subscribe<MathFormulaEvaluatedEvent>(e =>
{
    Console.WriteLine($"Formula {e.FormulaName}: {e.InputValue} -> {e.OutputValue}");
});

// Unsubscribe quando não precisar mais
subscription.Dispose();
```

**Consultar Histórico:**
```csharp
// Todos os eventos
var allEvents = _eventBus.GetEventHistory();

// Filtrar por categoria
var configEvents = _eventBus.GetEventHistory(EventCategory.CONFIG);

// Filtrar por severidade
var anomalies = _eventBus.GetEventHistory(EventSeverity.ANOMALY);
```

## REST API

### GET /api/events

Lista eventos da sessão atual com filtros opcionais.

**Query Parameters:**
- `category` (opcional): COMBAT, PIPELINE, META, CONFIG, REALITY_BEND
- `severity` (opcional): DEBUG, INFO, WARN, ANOMALY
- `limit` (opcional): Número máximo de eventos (padrão: 100)

**Response:**
```json
{
  "total": 150,
  "returned": 100,
  "events": [
    {
      "eventId": "550e8400-e29b-41d4-a716-446655440000",
      "timestamp": "2026-05-08T14:30:00Z",
      "eventType": "MathFormulaEvaluatedEvent",
      "sequence": 42,
      "category": "META",
      "severity": "DEBUG",
      "subject": "MathEngine",
      "verb": "evaluated",
      "target": "HYPERBOLIC_CURVE",
      "formulaName": "HYPERBOLIC_CURVE",
      "inputValue": 10.0,
      "outputValue": 85.5
    }
  ]
}
```

### GET /api/events/{eventId}

Obtém evento específico por ID.

**Response:**
```json
{
  "eventId": "550e8400-e29b-41d4-a716-446655440000",
  "timestamp": "2026-05-08T14:30:00Z",
  "eventType": "MathFormulaEvaluatedEvent",
  "sequence": 42,
  "category": "META",
  "severity": "DEBUG",
  "subject": "MathEngine",
  "verb": "evaluated",
  "target": "HYPERBOLIC_CURVE"
}
```

### GET /api/events/categories

Lista categorias de eventos disponíveis.

**Response:**
```json
{
  "categories": ["COMBAT", "PIPELINE", "META", "CONFIG", "REALITY_BEND"]
}
```

### GET /api/events/severities

Lista severidades de eventos disponíveis.

**Response:**
```json
{
  "severities": ["DEBUG", "INFO", "WARN", "ANOMALY"]
}
```

### DELETE /api/events

Limpa histórico de eventos (apenas dev mode).

**Response:**
```json
{
  "message": "Event history cleared successfully"
}
```

## Integrações

### MathEngine

O MathEngine publica eventos após avaliar fórmulas:

```csharp
// src/Core/Math/MathEngine.cs:275
var result = expression.Build();

_eventBus?.Publish(new MathFormulaEvaluatedEvent
{
    FormulaName = formulaName,
    InputValue = inputValue,
    OutputValue = result,
    Parameters = parameters,
    Target = formulaName
});
```

### ConfigManager

O ConfigManager publica eventos ao carregar/trocar configs:

```csharp
// src/Core/Config/ConfigManager.cs:203
_eventBus?.Publish(new ConfigLoadedEvent
{
    ConfigName = configName,
    ParentConfig = metadata?.Parent,
    ResourcesLoaded = chain.Count,
    Target = configName
});

// src/Core/Config/ConfigManager.cs:214
if (oldConfig != configName)
{
    _eventBus?.Publish(new ConfigChangedEvent
    {
        OldConfig = oldConfig,
        NewConfig = configName,
        Reason = "LoadConfig called",
        Target = configName
    });
}
```

## Dependency Injection

O EventBus é registrado como singleton no DI Container:

```csharp
// src/API/Program.cs:17
builder.Services.AddSingleton<IEventBus, EventBus>(sp =>
{
    var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
    var logger = new CoreLoggerAdapter(loggerFactory.CreateLogger("EventBus"));
    return new EventBus(logger);
});

// Injetado em outros serviços
builder.Services.AddSingleton<IConfigManager, ConfigManager>(sp =>
{
    var logger = new CoreLoggerAdapter(loggerFactory.CreateLogger("ConfigManager"));
    var validator = sp.GetRequiredService<ConfigValidator>();
    var eventBus = sp.GetRequiredService<IEventBus>();
    return new ConfigManager(logger, validator, eventBus);
});
```

## Thread Safety

O EventBus é completamente thread-safe:

- **Lock interno** protege estruturas de dados compartilhadas
- **Handlers invocados fora do lock** para evitar deadlocks
- **Cópia de handlers** antes de invocar para evitar race conditions
- **Exception handling** em handlers não afeta outros subscribers

```csharp
// Exemplo de uso multi-thread
var tasks = new List<Task>();
for (int i = 0; i < 10; i++)
{
    tasks.Add(Task.Run(() =>
    {
        _eventBus.Publish(new ConfigLoadedEvent { ConfigName = $"config-{i}" });
    }));
}
await Task.WhenAll(tasks);
```

## Testes

### Cobertura de Testes

- **13 testes unitários** do EventBus core
- **5 testes de integração** (thread-safety, exception handling)
- **100% de cobertura** das funcionalidades principais

### Exemplos de Testes

```csharp
[Fact]
public void Publish_SingleSubscriber_ReceivesEvent()
{
    ConfigLoadedEvent? receivedEvent = null;
    using var subscription = _eventBus.Subscribe<ConfigLoadedEvent>(e => receivedEvent = e);
    
    _eventBus.Publish(new ConfigLoadedEvent { ConfigName = "test" });
    
    Assert.NotNull(receivedEvent);
    Assert.Equal("test", receivedEvent.ConfigName);
}

[Fact]
public void GetEventHistory_FilterByCategory_ReturnsMatchingEvents()
{
    _eventBus.Publish(new ConfigLoadedEvent { ConfigName = "config1" }); // CONFIG
    _eventBus.Publish(new MathFormulaEvaluatedEvent { FormulaName = "formula1" }); // META
    
    var configEvents = _eventBus.GetEventHistory(EventCategory.CONFIG);
    
    Assert.Single(configEvents);
}
```

## Padrões de Uso

### Pattern 1: Auditoria de Operações

```csharp
// Publicar evento antes de operação crítica
_eventBus.Publish(new ConfigChangedEvent
{
    OldConfig = currentConfig,
    NewConfig = newConfig,
    Reason = "User requested change"
});

// Executar operação
LoadConfig(newConfig);

// Consultar histórico para auditoria
var configChanges = _eventBus.GetEventHistory(EventCategory.CONFIG);
```

### Pattern 2: Debugging e Replay

```csharp
// Durante desenvolvimento, consultar eventos para debug
var mathEvents = _eventBus.GetEventHistory(EventCategory.META)
    .OfType<MathFormulaEvaluatedEvent>()
    .Where(e => e.FormulaName == "DAMAGE_CALCULATION");

foreach (var evt in mathEvents)
{
    Console.WriteLine($"{evt.InputValue} -> {evt.OutputValue}");
}
```

### Pattern 3: Notificações Cross-System

```csharp
// Sistema A publica evento
_eventBus.Publish(new ConfigLoadedEvent { ConfigName = "new-config" });

// Sistema B reage ao evento
_eventBus.Subscribe<ConfigLoadedEvent>(e =>
{
    InvalidateCache();
    ReloadResources();
});
```

## Limitações Atuais

### Não Implementado (Fase 4)

- ❌ Persistência em disco (arquivos `.acl`)
- ❌ Replay completo de estados
- ❌ Dashboard web para visualização
- ❌ WebSocket/async streaming
- ❌ Três projeções de log (notação, debug, RL)

### Implementado

- ✅ Pub/sub thread-safe
- ✅ Histórico em memória
- ✅ Filtros por categoria/severidade
- ✅ REST API para consulta
- ✅ Integração com MathEngine e ConfigManager
- ✅ Sequence numbers automáticos
- ✅ Exception handling em handlers

## Próximos Passos

1. **Combat System (Fase 1):** Adicionar eventos de combate
2. **Damage Pipeline (Fase 1):** Eventos de cada bucket
3. **Status/Modifiers (Fase 2):** Eventos de aplicação/remoção
4. **Persistência (Fase 4):** Salvar eventos em disco
5. **Dashboard (Fase 4):** Visualização web de eventos

## Referências

- **Código:** `src/Core/Events/`
- **Testes:** `tests/Core.Tests/Events/`
- **API:** `src/API/Controllers/EventsController.cs`
- **DI Setup:** `src/API/Program.cs:17-52`

---

**Última atualização:** 2026-05-08  
**Versão:** 1.0.0  
**Status:** ✅ Implementado e testado
