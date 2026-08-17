# Observabilidade - HeroScript API

## Correlation IDs

Todos os requests HTTP recebem um **Correlation ID** único que rastreia o request através de toda a stack.

### Implementação

`CorrelationIdMiddleware` (registrado no pipeline da API):

1. Lê header `X-Correlation-ID` do request (se fornecido pelo cliente)
2. Se ausente, gera `Guid.NewGuid()`
3. Adiciona ao response header `X-Correlation-ID`
4. Usa `ILogger.BeginScope()` para incluir em todos os logs do request

### Uso pelo Cliente

**Enviar Correlation ID:**
```bash
curl -X GET http://localhost:5260/api/v1/combats/abc123 \
  -H "X-Correlation-ID: 3fa85f64-5717-4562-b3fc-2c963f66afa6"
```

**Response:**
```
HTTP/1.1 200 OK
X-Correlation-ID: 3fa85f64-5717-4562-b3fc-2c963f66afa6
Content-Type: application/json
```

### Logs Correlacionados

Todos os logs gerados durante o request incluem o Correlation ID:

```
[12:00:00 INF] [3fa85f64-5717-4562-b3fc-2c963f66afa6] Combat abc123 started
[12:00:01 INF] [3fa85f64-5717-4562-b3fc-2c963f66afa6] Action fireball executed
[12:00:02 INF] [3fa85f64-5717-4562-b3fc-2c963f66afa6] Combat abc123 ended
```

Útil para:
- Troubleshooting de erros específicos
- Rastreamento distribuído (quando integrado com APM)
- Debugging de race conditions

## Logging Estruturado

HeroScript usa **logging estruturado** via `ILogger` com templates em vez de interpolação de string.

### Formato Correto

✅ **Estruturado** (indexável, pesquisável):
```csharp
_logger.LogInformation(
    "Effect {EffectId} resolved for entity {EntityId} with result {Result}",
    effectId,
    entityId,
    result
);
```

❌ **Não estruturado** (string literal, não indexável):
```csharp
_logger.LogInformation($"Effect {effectId} resolved for entity {entityId} with result {result}");
```

### Vantagens

1. **Indexação**: Log aggregators (Seq, Elasticsearch) podem indexar campos individuais
2. **Queries estruturadas**: `WHERE EffectId = 'poison'` em vez de busca textual
3. **Dashboards**: Métricas por campo (ex: contagem de efeitos por tipo)
4. **Performance**: Templates são compilados uma vez

### Níveis de Log

| Nível | Uso | Exemplo |
|---|---|---|
| `Trace` | Detalhes ultra-verbosos (raramente usado) | Cada step de pipeline |
| `Debug` | Informações de desenvolvimento | Path resolution, discovery details |
| `Information` | Eventos normais de aplicação | Combat started, effect applied |
| `Warning` | Situações anormais mas recuperáveis | Config missing (usando default), validation warning |
| `Error` | Erros que requerem atenção | Exception não tratada, falha de persistência |
| `Critical` | Falhas catastróficas | Sistema não pode continuar |

### Configuração

**appsettings.Development.json:**
```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Debug",
      "Microsoft.AspNetCore": "Information",
      "System.Net.Http.HttpClient": "Warning"
    }
  }
}
```

**appsettings.Production.json:**
```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  }
}
```

### Scopes

Use scopes para adicionar contexto a múltiplos logs:

```csharp
using (_logger.BeginScope(new { CombatId = combatId, ActorId = actorId }))
{
    _logger.LogInformation("Action executed");
    _logger.LogInformation("Damage calculated");
    _logger.LogInformation("Status applied");
}
```

Todos os 3 logs incluirão `CombatId` e `ActorId` automaticamente.

## Console.WriteLine Eliminado

Trilha 3 de hardening removeu **todas as 32 ocorrências** de `Console.WriteLine` nos arquivos Core:

- `DeltaMerger.cs`
- `ResourcePathResolver.cs`
- `ConfigValidator.cs`
- `FormulaLoader.cs`
- `ResourceProviderFactory.cs`

Todos foram substituídos por `ILogger` com nível apropriado.

## Fallback de Logger

Código Core não usa mais `ConsoleLogger` como fallback. Todos os managers requerem `ILogger` obrigatório via construtor.

Antes (inseguro):
```csharp
_logger = logger ?? new ConsoleLogger("EntityDefinitionLoader");
```

Depois (obrigatório):
```csharp
public EntityDefinitionLoader(ILogger<EntityDefinitionLoader> logger)
{
    _logger = logger; // Sem fallback - DI sempre injeta
}
```

## Roadmap: OpenTelemetry

**Status**: Planejado para produção (não MVP)

Próximos passos:
1. Adicionar `AddOpenTelemetry()` em `Program.cs`
2. Configurar exportador via `OTEL_EXPORTER_OTLP_ENDPOINT`
3. Adicionar `ActivitySource` no `EventBus.Publish()`
4. Instrumentar operações de I/O (persistência)
5. Integrar com APM (Jaeger, Zipkin, Application Insights)

## Troubleshooting

**Problema**: Logs não aparecem

- Verificar `appsettings.json` - `LogLevel` pode estar muito alto
- Em Development, `Debug` deve estar habilitado
- Verificar se `ILogger` está sendo injetado corretamente

**Problema**: Logs sem Correlation ID

- Verificar se `CorrelationIdMiddleware` está registrado no pipeline
- Deve estar antes de `app.MapControllers()`

**Problema**: Performance de logging

- Não usar logging em loops tight (ex: cada iteração de cálculo matemático)
- Usar `IsEnabled(LogLevel.Debug)` antes de logs caros

## Implementação

Ver:
- `src/API/Middleware/CorrelationIdMiddleware.cs`
- `src/Core/Logging/ILogger.cs`
- `src/API/Logging/LoggerAdapter.cs`
