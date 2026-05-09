# Core Service Patterns

Este documento define os padrões obrigatórios para todos os serviços no Core layer.

## Princípios Fundamentais

1. **Core não depende de camadas superiores** - Apenas System libraries e outros módulos Core
2. **Interfaces para tudo** - Todo serviço público tem uma interface
3. **Error handling consistente** - Result<T> para operações de negócio
4. **Logging obrigatório** - ILogger injetado em todos os serviços
5. **Thread-safety quando necessário** - Documentar e implementar quando compartilhado

---

## Anatomia de um Serviço Core

### Template Básico

```csharp
namespace Core.{Module};

using Core.Common;
using Core.Logging;

/// <summary>
/// Interface pública do serviço
/// </summary>
public interface IMyService
{
    Result<TOutput> DoOperation(TInput input);
}

/// <summary>
/// Implementação do serviço
/// </summary>
public class MyService : IMyService
{
    private readonly ILogger _logger;
    private readonly IDependency _dependency;

    public MyService(ILogger logger, IDependency dependency)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _dependency = dependency ?? throw new ArgumentNullException(nameof(dependency));
    }

    public Result<TOutput> DoOperation(TInput input)
    {
        // 1. Validar entrada
        if (input == null)
            return Result<TOutput>.Failure("Input cannot be null");

        // 2. Logar início
        _logger.LogDebug($"Starting operation with input: {input}");

        try
        {
            // 3. Executar lógica
            var result = PerformLogic(input);

            // 4. Logar sucesso
            _logger.LogDebug($"Operation completed successfully");

            return Result<TOutput>.Success(result);
        }
        catch (Exception ex)
        {
            // 5. Logar erro e retornar falha
            _logger.LogError($"Operation failed: {ex.Message}", ex);
            return Result<TOutput>.Failure($"Operation failed: {ex.Message}");
        }
    }

    private TOutput PerformLogic(TInput input)
    {
        // Lógica interna pode lançar exceções
        // Serão capturadas pelo método público
        return default!;
    }
}
```

---

## Padrão de Error Handling

### Quando usar Result<T>

**USE Result<T> para:**
- Operações de negócio que podem falhar previsivelmente
- Validações de entrada
- Operações que dependem de estado externo (arquivo, rede, etc)
- Qualquer operação onde falha é um cenário esperado

**Exemplos:**
```csharp
Result<CombatState> StartCombat(string heroId, List<string> enemyIds);
Result<ActionDefinition> GetDefinition(string actionId);
Result<float> Evaluate(ExpressionEvaluationRequest request);
```

### Quando lançar exceções

**LANCE EXCEÇÕES para:**
- Violações de contrato (ArgumentNullException, ArgumentException)
- Bugs de programação (InvalidOperationException para estados impossíveis)
- Falhas irrecuperáveis do sistema

**Exemplos:**
```csharp
if (logger == null)
    throw new ArgumentNullException(nameof(logger));

if (_isDisposed)
    throw new ObjectDisposedException(nameof(MyService));
```

### NUNCA engula exceções

**ERRADO:**
```csharp
catch (Exception ex)
{
    _logger.LogError($"Error: {ex.Message}");
    return defaultValue; // ❌ Silencioso
}
```

**CORRETO:**
```csharp
catch (Exception ex)
{
    _logger.LogError($"Error: {ex.Message}", ex);
    return Result<T>.Failure($"Operation failed: {ex.Message}");
}
```

---

## Padrão de Logging

### Níveis de Log

- **LogDebug:** Fluxo detalhado de execução (início/fim de operações)
- **LogInformation:** Eventos importantes do sistema (combate iniciado, config carregada)
- **LogWarning:** Situações anormais mas recuperáveis
- **LogError:** Erros que impedem operação de completar

### Exemplos

```csharp
// Início de operação
_logger.LogDebug($"Starting combat with hero {heroId}");

// Evento importante
_logger.LogInformation($"Combat {combatId} started successfully");

// Situação anormal
_logger.LogWarning($"Action {actionId} not found, using default");

// Erro
_logger.LogError($"Failed to load config: {ex.Message}", ex);
```

---

## Padrão de Validação

### Validação de Construtor

```csharp
public MyService(ILogger logger, IDependency dependency)
{
    _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    _dependency = dependency ?? throw new ArgumentNullException(nameof(dependency));
}
```

### Validação de Entrada

```csharp
public Result<T> DoOperation(string input)
{
    if (string.IsNullOrWhiteSpace(input))
        return Result<T>.Failure("Input cannot be empty");

    if (input.Length > 100)
        return Result<T>.Failure("Input too long (max 100 characters)");

    // Continuar com lógica
}
```

---

## Padrão de Thread-Safety

### Quando necessário

- Serviços registrados como Singleton no DI
- Serviços que mantêm estado mutável compartilhado
- Caches e dicionários compartilhados

### Implementação

```csharp
public class ThreadSafeService
{
    private readonly Dictionary<string, T> _cache = new();
    private readonly object _lock = new();

    public T GetOrAdd(string key, Func<T> factory)
    {
        lock (_lock)
        {
            if (_cache.TryGetValue(key, out var value))
                return value;

            var newValue = factory();
            _cache[key] = newValue;
            return newValue;
        }
    }
}
```

**Alternativa:** Use `ConcurrentDictionary<TKey, TValue>` quando apropriado.

---

## Checklist de Novo Serviço

Antes de criar um novo serviço no Core, verifique:

- [ ] Tem interface pública (`I{ServiceName}`)
- [ ] Construtor valida todas as dependências (null checks)
- [ ] Injeta `ILogger` via construtor
- [ ] Métodos públicos retornam `Result<T>` para operações que podem falhar
- [ ] Exceções são lançadas apenas para bugs/violações de contrato
- [ ] Logging adequado (início de operações, erros, eventos importantes)
- [ ] Thread-safe se registrado como Singleton
- [ ] Testes unitários cobrem casos de sucesso e erro
- [ ] Documentação XML em interface pública

---

## Exemplos de Serviços Bem Implementados

### Bons Exemplos no Core

- `ActionManager` - Validação completa, Result<T>, logging
- `EventBus` - Thread-safe, logging, error handling
- `ResourceManager` - Result<T>, validação, logging
- `PipelineManager` - Thread-safe, lazy initialization

### Serviços que Precisam de Melhoria

- `ActionAffordabilityService` - Falta logging, validação, Result<T>
- `EntityFactory` - Falta validação, logging, Result<T>
- `MathEngine` - Lança muitas exceções raw, falta Result<T>

---

## Anti-Patterns a Evitar

### ❌ Estado Global Mutável

```csharp
public static class BadService
{
    private static Dictionary<string, T> _cache = new(); // ❌ Global mutável
}
```

### ❌ Retornar Null

```csharp
public OperationMetadata? GetOperation(string name)
{
    return _operations.FirstOrDefault(op => op.Name == name); // ❌ Null
}
```

**Correto:**
```csharp
public Result<OperationMetadata> GetOperation(string name)
{
    var operation = _operations.FirstOrDefault(op => op.Name == name);
    return operation != null
        ? Result<OperationMetadata>.Success(operation)
        : Result<OperationMetadata>.Failure($"Operation '{name}' not found");
}
```

### ❌ Engolir Exceções

```csharp
catch (Exception ex)
{
    _logger.LogError(ex.Message);
    return context; // ❌ Continua como se nada tivesse acontecido
}
```

### ❌ Utilitários Estáticos com Dependências

```csharp
public static class FileHelper
{
    public static T ReadJson<T>(string path) // ❌ Não testável
    {
        return JsonSerializer.Deserialize<T>(File.ReadAllText(path));
    }
}
```

**Correto:**
```csharp
public interface IFileHelper
{
    Result<T> ReadJson<T>(string path);
}

public class FileHelper : IFileHelper
{
    private readonly ILogger _logger;

    public FileHelper(ILogger logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Result<T> ReadJson<T>(string path)
    {
        try
        {
            var content = File.ReadAllText(path);
            var result = JsonSerializer.Deserialize<T>(content);
            return Result<T>.Success(result!);
        }
        catch (Exception ex)
        {
            _logger.LogError($"Failed to read JSON from {path}: {ex.Message}", ex);
            return Result<T>.Failure($"Failed to read JSON: {ex.Message}");
        }
    }
}
```

---

## Referências

- `Core.Common.Result<T>` - Tipo de retorno para operações que podem falhar
- `Core.Logging.ILogger` - Interface de logging
- `Core.Events.IEventBus` - Sistema de eventos
- `arquitetura-engine.md` - Visão geral da arquitetura
