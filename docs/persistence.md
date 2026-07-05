# Persistência - HeroScript Engine

HeroScript suporta persistência em disco via duas implementações de repositório JSON.

## Arquitetura

```
Core.Abstractions.Persistence/
├── IEventStore           # Interface para event sourcing
├── IRunStateRepository   # Interface para snapshots de run
└── ISnapshotStore        # Interface para snapshots genéricos (futuro)

Core.Infrastructure.Persistence/
├── JsonFileEventStore           # Implementação .jsonl
└── JsonFileRunStateRepository   # Implementação {runId}.json
```

## Configuração

**appsettings.json:**
```json
{
  "Persistence": {
    "EventStorePath": "data/events/",
    "RunStatePath": "data/runs/"
  }
}
```

**Variáveis de ambiente (sobrescrevem appsettings):**
```bash
export HERESCRIPT_EVENT_STORE_PATH="/var/data/events/"
export HERESCRIPT_RUN_STATE_PATH="/var/data/runs/"
```

## JsonFileEventStore

### Formato

Eventos são salvos em formato **JSON line-delimited** (`.jsonl`):

```jsonl
{"eventType":"CombatStartedEvent","timestamp":"2026-07-05T12:00:00Z","combatId":"abc123",...}
{"eventType":"ActionExecutedEvent","timestamp":"2026-07-05T12:00:05Z","actionId":"fireball",...}
{"eventType":"CombatEndedEvent","timestamp":"2026-07-05T12:01:30Z","combatId":"abc123",...}
```

### Características

- **Append-only**: Novos eventos são sempre adicionados ao final
- **Auditoria completa**: Histórico nunca é modificado
- **Filtros suportados**: `eventType`, `runId`, `afterSequence`
- **Thread-safe**: Usa lock para append concorrente

### API

```csharp
// Adicionar evento
await eventStore.AppendAsync(new CombatStartedEvent { ... });

// Buscar eventos
var events = await eventStore.GetEventsAsync(
    eventType: "CombatStartedEvent",
    runId: Guid.Parse("..."),
    afterSequence: 100
);

// Buscar por sequência
var range = await eventStore.GetBySequenceAsync(from: 1, to: 100);
```

### Dual Write no EventBus

`EventBus` faz **dual write**:
1. Adiciona ao histórico em memória (para subscribers)
2. Persiste via `IEventStore` (fire-and-forget)

Falha no store **não propaga** para `Publish<T>()` - eventos continuam sendo publicados mesmo se a persistência falhar.

## JsonFileRunStateRepository

### Formato

Cada run é salva como um arquivo JSON individual:

```
data/runs/
├── 3fa85f64-5717-4562-b3fc-2c963f66afa6.json
├── 7c2b4e89-1234-5678-abcd-ef1234567890.json
└── ...
```

**Conteúdo de exemplo:**
```json
{
  "runId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "gold": 150,
  "powerPoints": 8,
  "deck": [...],
  "hand": [...],
  "discard": [...],
  "exhaust": [...],
  "currentNodeId": "node_05",
  "completedNodes": ["node_01", "node_02", ...]
}
```

### Características

- **Write atômico**: Usa temp file + rename para evitar corrupção
- **Auto-load**: `RunManager.GetRun(runId)` busca do disco se não estiver em memória
- **Restart-safe**: Processo pode ser reiniciado sem perda de estado

### API

```csharp
// Salvar run
await repository.SaveAsync(runState);

// Carregar run
var runState = await repository.LoadAsync(runId);

// Verificar existência
bool exists = await repository.ExistsAsync(runId);

// Deletar run
await repository.DeleteAsync(runId);
```

### Integração com RunManager

`RunManager` persiste automaticamente em cada mutação de estado:

```csharp
public void UpdateGold(Guid runId, int delta)
{
    var run = GetRun(runId);
    run.Gold += delta;
    
    // Persiste automaticamente
    _repository?.SaveAsync(run).Wait();
}
```

## Retenção e Limpeza

**Eventos (.jsonl):**
- Crescem indefinidamente por design
- Implementar política de retenção manualmente (cronjob, archive old files)
- Considerar migração para banco temporal quando volume crescer

**Run State (.json):**
- Deletar manualmente runs antigas via `DELETE /api/run/{runId}`
- Considerar política de TTL para runs inativas (futuro)

## Migração Futura

A arquitetura baseada em interfaces permite trocar implementação sem alterar Core:

```csharp
// Trocar de JSON para SQLite
services.AddSingleton<IEventStore, SqliteEventStore>();
services.AddSingleton<IRunStateRepository, SqliteRunStateRepository>();
```

Candidatos para produção:
- **SQLite**: File-based, boa performance, SQL queries
- **PostgreSQL**: Produção distribuída, JSONB indexado
- **EventStoreDB**: Especializado em event sourcing

## Troubleshooting

**Problema**: Eventos não estão sendo persistidos

- Verificar permissões no diretório `EventStorePath`
- Verificar logs: `ILogger` registra falhas de persistência como Warning
- Eventos continuam funcionando em memória mesmo se store falhar

**Problema**: RunState não carrega após restart

- Verificar se `RunStatePath` está configurado corretamente
- Verificar se arquivo `{runId}.json` existe no diretório
- Testar deserialização manualmente para detectar JSON corrompido

## Performance

**JsonFileEventStore:**
- Append: ~1ms por evento (I/O sequencial)
- Read: O(n) - lê todo arquivo e filtra (não indexado)
- Limite prático: ~100k eventos antes de considerar banco

**JsonFileRunStateRepository:**
- Save: ~5ms por run (atomic write via rename)
- Load: ~2ms por run (deserialização)
- Limite prático: ~10k runs antes de considerar banco

## Implementação

Ver:
- `src/Core/Infrastructure/Persistence/JsonFileEventStore.cs`
- `src/Core/Infrastructure/Persistence/JsonFileRunStateRepository.cs`
- `src/Core/Abstractions/Persistence/IEventStore.cs`
- `src/Core/Abstractions/Persistence/IRunStateRepository.cs`
