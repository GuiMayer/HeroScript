
Trilha 4 — Logging e Observabilidade

Por que: 32 ocorrências de Console.WriteLine em 5 arquivos core (DeltaMerger.cs, ResourcePathResolver.cs, ConfigValidator.cs, FormulaLoader.cs, ResourceProviderFactory.cs). EntityDefinitionLoader.cs linha 38 faz fallback new ConsoleLogger(...), bypassando o adapter. Zero correlation IDs. Logs usam interpolação de string em vez de template estruturado.
Passo 4.1 — Substituir Console.WriteLine por ILogger nos 5 arquivos

Para cada arquivo: injetar (ou usar o já existente) ILogger; substituir cada Console.WriteLine pelo nível adequado (LogDebug, LogWarning, LogError). Nível por contexto: path resolution = Debug; merge warnings = Warning; validation errors = Warning/Error.
Passo 4.2 — Corrigir fallback de logger em EntityDefinitionLoader (linha 38)

Mudar _logger = logger ?? new ConsoleLogger("EntityDefinitionLoader") para _logger = logger ?? NullLogger.Instance (já existe em Core.Logging). Ou melhor: tornar o parâmetro obrigatório — o DI sempre injeta com o adapter correto.
Passo 4.3 — Converter interpolações para templates estruturados

// De (não estruturado):
logger.LogInformation($"Effect {effect.InstanceId} resolved");

// Para (estruturado, indexável por log aggregator):
logger.LogInformation("Effect {EffectId} resolved", effect.InstanceId);

Varrer todos os LogInformation, LogWarning, LogError na codebase.
Passo 4.4 — Criar CorrelationIdMiddleware

src/API/Middleware/CorrelationIdMiddleware.cs:

    Lê X-Correlation-ID do request header; se ausente, gera Guid.NewGuid().
    Adiciona ao response header.
    Usa ILogger.BeginScope(new { CorrelationId = id }) para incluir em todos os logs do request.
    Registrar em Program.cs antes de app.MapControllers().

Passo 4.5 — Corrigir appsettings.Development.json para habilitar Debug

{
  "Logging": {
    "LogLevel": {
      "Default": "Debug",
      "Microsoft.AspNetcore": "Information"
    }
  }
}

Passo 4.6 — OpenTelemetry (opcional para MVP, recomendado para produção)

Adicionar AddOpenTelemetry() com exportador configurável via OTEL_EXPORTER_OTLP_ENDPOINT. Fallback para console exporter em Development. ActivitySource no EventBus.Publish.

Critério de done: grep -rn "Console.WriteLine" src/ retorna 0. grep -rn "new ConsoleLogger" src/ retorna 0 em código de produção. Todo request tem X-Correlation-ID no response.
Trilha 5 — Persistência: Event Store + Run State

Por que: zero persistência no código-fonte — confirmado por grep. RunManager usa Dictionary<Guid, RunState> em memória. EventBus usa List<IEvent></ievent> em memória. StatusEffectManager e ScriptModifierManager são in-memory. Restart do processo = perda total de estado.
Passo 5.1 — Definir interfaces em Core.Abstractions.Persistence/

IEventStore           // AppendAsync(IEvent), GetEventsAsync(filter), GetBySequenceAsync(from, to)
ISnapshotStore        // SaveAsync<T></t>(id, snapshot), LoadAsync<T></t>(id)
IRunStateRepository   // SaveAsync(RunState), LoadAsync(RunId), DeleteAsync(RunId), ExistsAsync(RunId)

Passo 5.2 — Implementar JsonFileEventStore

src/Core/Infrastructure/Persistence/JsonFileEventStore.cs:

    Persiste eventos como JSON line-delimited (.jsonl) em diretório configurável.
    AppendAsync: abre em append mode, serializa, fecha.
    GetEventsAsync: lê e filtra por eventType, runId, afterSequence.
    Caminho via appsettings: "Persistence": { "EventStorePath": "data/events/" }.

Passo 5.3 — Implementar JsonFileRunStateRepository

src/Core/Infrastructure/Persistence/JsonFileRunStateRepository.cs:

    Persiste cada RunState como {runId}.json.
    SaveAsync: write atômico via temp file + rename (evitar arquivo corrompido em crash).
    LoadAsync: deserializa do arquivo.

Passo 5.4 — Modificar EventBus para dual write

Aceitar IEventStore? no construtor (opcional para compatibilidade com testes). Em Publish<T></t>(): após adicionar ao histórico em memória, chamar _eventStore?.AppendAsync(event) de forma fire-and-forget com tratamento de erro isolado — falha no store não propaga para o Publish.
Passo 5.5 — Modificar RunManager para persistência

Aceitar IRunStateRepository? no construtor. Em cada mutação de RunState: chamar _repository?.SaveAsync(state). Em GetRun(runId): se não em memória, tentar _repository?.LoadAsync(runId).
Passo 5.6 — Adicionar endpoints de snapshot no RunController

    POST /api/run/{runId}/snapshot — força save.
    GET /api/run/{runId}/snapshots — lista snapshots.
    POST /api/run/{runId}/restore/{snapshotId} — restaura estado (suporte a time-travel/debug).

Passo 5.7 — Registrar no DI e adicionar testes

Em Program.cs: registrar JsonFileEventStore e JsonFileRunStateRepository em todos os environments.

Em tests/Core.Tests/Persistence/:

    JsonFileEventStoreTests.cs: append, read, filter, sequência.
    JsonFileRunStateRepositoryTests.cs: save, load, atomic write.
    Usar Path.GetTempPath() + cleanup no teardown.

Critério de done: restart do processo + GET /api/run/{runId} retorna estado anterior. GET /api/events retorna eventos de sessões anteriores.
Trilha 6 — Estabilidade de API.Tests

Por que: TestWebApplicationFactory.cs tem FindProjectRoot() com loop while(directory != null) sem limite de profundidade. Factory inicializa o Program.cs real, que chama LoadStatusDefinitions, LoadDefinitions, LoadGambitDefinitions no startup — se o working directory estiver errado, podem falhar ou travar. Sem timeout de startup configurado.
Passo 6.1 — Corrigir FindProjectRoot()

Substituir o loop por busca com máximo de 10 níveis. Alternativa mais robusta: usar Assembly.GetExecutingAssembly().Location como ponto de partida determinístico, ou aceitar variável de ambiente HERESCRIPT_REPO_ROOT que o CI seta explicitamente.
Passo 6.2 — Adicionar timeout de startup

Configurar CancellationToken de 30 segundos no CreateClient(). Se o TestServer não responder em 30s, falhar com mensagem clara em vez de congelar indefinidamente.
Passo 6.3 — Categorizar testes

Marcar testes que usam TestWebApplicationFactory/HttpClient com [Trait("Category", "Integration")]. Marcar testes unitários com mocks diretos com [Trait("Category", "Unit")].

Criar .runsettings na raiz:

<TestRunParameters>
  <Parameter name="TestTimeout" value="30000" />
</TestRunParameters>

Passo 6.4 — Isolar e corrigir o congelamento

    Executar dotnet test --filter "Category=Unit" para confirmar que unitários passam rápido.
    Executar --filter "Category=Integration" com timeout para isolar qual integration test congela.
    Suspeita principal: algum teste de SSE que não fecha a conexão.

Passo 6.5 — Criar CI pipeline

.github/workflows/ci.yml:

jobs:
  test:
    steps:
      - name: Core Tests
        run: dotnet test tests/Core.Tests/Core.Tests.csproj
      - name: API Unit Tests
        run: dotnet test tests/API.Tests/API.Tests.csproj --filter "Category=Unit"
      - name: API Integration Tests
        timeout-minutes: 5
        run: dotnet test tests/API.Tests/API.Tests.csproj --filter "Category=Integration"

Critério de done: dotnet test completo em menos de 5 minutos sem congelamento. CI verde em todos os steps.
Trilha 7 — Documentação sincronizada

Por que: README.md linha 29 diz "Fase 3: ainda não implementada". Mas RunManager.cs, RunController.cs, ShopController.cs, CardSelectionController.cs, PreparationController.cs já existem no código.
Passo 7.1 — Atualizar README.md

Remover afirmação de Fase 3 não implementada. Adicionar matriz de módulos com status real. Adicionar seção "Segurança" (configurar Admin.ApiKey antes de produção). Adicionar seção "Persistência" (Persistence.EventStorePath, Persistence.RunStatePath).
Passo 7.2 — Criar docs/security.md

Documentar: quais endpoints precisam de X-Admin-Key; como configurar via environment variable (não no appsettings.json commitado); fallback CORS e o risco atual; política de AllowedOrigins em produção.
Passo 7.3 — Criar docs/persistence.md

Documentar: configuração do JsonFileEventStore; formato .jsonl; caminho de migração para SQLite trocando implementação de IEventStore; política de retenção de eventos.
Passo 7.4 — Atualizar docs/roadmap/README.md

Marcar Fase 3 com o que está realmente implementado vs. o que é refinamento futuro. Adicionar entradas de changelog para cada trilha de hardening.
