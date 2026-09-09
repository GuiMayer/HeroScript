# Guia de testes de integração

**Atualizado em:** 2026-09-05

Os testes de integração exercitam a aplicação headless por HTTP, com persistência,
conteúdo, regras e serialização reais. Eles devem se comportar como um pequeno
cliente Godot: enviar intenções, observar versões e confiar somente nas respostas
da engine.

## Onde ficam

```text
tests/API.Tests/
├── Controllers/     contratos HTTP e erros
├── Integration/     fluxos completos de jogo
├── Helpers/         cliente de teste
└── TestWebApplicationFactory.cs
```

`TestWebApplicationFactory` inicializa uma API isolada. Cada teste deve criar sua
própria run e não depender da ordem de execução de outros testes.

## Executar

```powershell
dotnet test tests/API.Tests/API.Tests.csproj
```

Para investigar um cenário:

```powershell
dotnet test tests/API.Tests/API.Tests.csproj --filter "FullyQualifiedName~DeterministicPrototypeFlowTests"
```

Rode também o Core quando a mudança atravessar regras de domínio:

```powershell
dotnet test tests/Core.Tests/Core.Tests.csproj
dotnet test tests/API.Tests/API.Tests.csproj
```

## Cliente de teste

`GameEngineClientSimulator` encapsula o envelope determinístico e reduz boilerplate.
Ele não é uma API alternativa: internamente usa apenas as rotas v1 canônicas.

Operações centrais disponíveis:

```csharp
Task<Guid> StartRunAsync(
    string configName = "default",
    string runDefinitionId = "default_run",
    string playerEntityId = "player",
    ulong? seed = null);

Task<JsonElement> GetRunStateAsync(Guid runId);
Task<List<string>> DrawCardsAsync(Guid runId, int count = 1);
Task<JsonElement> DiscardCardsAsync(Guid runId, IEnumerable<string> cardIds);
Task<List<string>> GetHandAsync(Guid runId);

Task<Guid> StartCombatAsync(
    string playerActorId,
    IEnumerable<string> otherActorIds,
    IReadOnlyDictionary<string, float>? initialPlayerResourceValues = null,
    Guid? runId = null,
    string heroDefinitionId = "player_warrior",
    string enemyDefinitionId = "enemy_goblin");

Task<JsonElement> GetCombatStateAsync(Guid combatId);
Task<JsonElement> ExecuteActionAsync(
    Guid combatId,
    string actorId,
    string? targetId = null,
    string? powerId = null,
    string? cardId = null,
    Guid? runId = null);
Task<JsonElement> EndTurnAsync(Guid combatId, Guid? runId = null);
```

O parâmetro de recursos iniciais é genérico; os nomes do helper de teste não
definem papéis no contrato da engine:

```csharp
var overrides = new Dictionary<string, float>
{
    ["energy"] = 3,
    ["stability"] = 8
};

var combatId = await Client.StartCombatAsync(
    "player",
    new[] { "enemy_1" },
    overrides,
    runId);
```

Não adicione novamente parâmetros como `initialEnergy`. O helper deve refletir o
modelo de recursos da API.

## Estrutura recomendada de um teste

```csharp
public sealed class ExampleFlowTests : GameEngineIntegrationTestBase
{
    public ExampleFlowTests(TestWebApplicationFactory factory)
        : base(factory)
    {
    }

    [Fact]
    public async Task FixedSeed_RunCanBeVerified()
    {
        var runId = await Client.StartRunAsync(seed: 424242);
        var state = await Client.GetRunStateAsync(runId);
        Assert.Equal((ulong)424242, state.GetProperty("seed").GetUInt64());

        using var response = await RawClient.PostAsync(
            $"/api/v1/runs/{runId}/verify",
            content: null);
        response.EnsureSuccessStatusCode();

        var verification = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(verification.GetProperty("isValid").GetBoolean());
    }
}
```

Use Arrange, Act e Assert, mas prefira um fluxo completo pequeno a mocks internos.
Quando a regra é puramente local, cubra-a no Core e deixe no teste de integração
apenas a prova de que HTTP, conteúdo e persistência estão conectados corretamente.

## Cenário mínimo de combate

1. Crie a run com seed fixa.
2. Compre cartas pelo comando de run, se o modo exigir.
3. Inicie `START_ENCOUNTER` com aliases e definições explícitas.
4. Leia a mão e obtenha o `cardInstanceId`.
5. Consulte a avaliação da carta.
6. Envie `PLAY_CARD` com sequência e step observados.
7. Confirme recursos, zonas de carta e fila de resolução.
8. Leia o journal e execute a verificação semântica.
9. Repita o cenário do zero e compare hashes.

O teste não deve calcular o dano esperado com uma implementação paralela. Ele pode
afirmar um valor conhecido do fixture e deve comparar a execução com a prévia e o
journal produzidos pela própria engine.

## Testando recursos genéricos

Todo comportamento que pareça específico de vida ou energia deve ter ao menos um
caso com um ID arbitrário.

Casos mínimos:

- materializar `stability` a partir de JSON;
- sobrescrever seu valor no início do encontro;
- consumi-lo como custo;
- reduzi-lo com `DAMAGE`;
- restaurá-lo com `HEAL`;
- usá-lo como influência de um pipeline;
- alterar mínimo ou máximo;
- disparar `DefeatOwner` por política;
- provar que a categoria, sozinha, não derrota;
- falhar a segunda mutação de um lote e confirmar rollback total.

## Testando cartas

Use instâncias reais da mão. Uma definição como `basic_attack` não substitui um
`cardInstanceId` no comando de gameplay.

Valide:

- avaliação em lote e individual;
- custo escolhido e affordability;
- alvos válidos e inválidos;
- definição efetiva após upgrade;
- influências de status, relíquia e recurso;
- igualdade entre prévia e aplicação;
- transição de zona após sucesso;
- preservação da mão e dos recursos após falha.

## Concorrência e idempotência

Um teste de comando deve cobrir três situações:

1. request novo com versões atuais: aceito;
2. retry byte-equivalente com o mesmo `commandId`: mesmo recibo, sem efeito duplo;
3. comando novo com versões antigas: `409`, sem alteração.

Não gere um novo `commandId` ao simular retry. Isso representa uma nova intenção e
não testa idempotência.

## Timeline e branches

Para o sandbox:

- execute comandos suficientes para formar mais de um ponto;
- recupere o estado histórico por sequência;
- crie duas branches do mesmo ponto;
- aplique decisões diferentes;
- confirme que a origem permaneceu inalterada;
- verifique replay e hashes de cada branch.

## Eventos

Eventos são validados depois do commit. O teste deve primeiro confirmar o estado
persistido e depois consultar polling ou SSE. Nunca use a chegada do evento como
prova única de que a transação ocorreu.

Teste paginação/cursor, filtro por run ou combate e retomada sem duplicação. Para
recuperação do cliente, descarte a projeção local e reconstrua a tela pelo read
model antes de continuar.

## Teste de conteúdo revisionado

1. Crie uma run na revisão A.
2. Publique uma revisão B com uma regra numericamente diferente.
3. Confirme que a run A mantém o resultado antigo.
4. Crie uma run B e confirme a regra nova.
5. Verifique semanticamente ambas.

Se o modo permitir troca de revisão em runtime, a ativação deve ser um comando
explícito, persistido e testado; editar arquivo nunca basta para mudar uma run.

## Diagnóstico de falhas

### `409 VersionConflict`

O teste reutilizou sequência ou step antigos. Releia a run e o combate antes de
criar a próxima intenção.

### `422 RuleViolation`

Inspecione `ProblemDetails`, avaliação da carta, comandos disponíveis, ator ativo e
fase. Não altere o cliente para contornar a regra.

### `404`

Confirme se o ID é da run/combate atual e se o fixture publica a definição na mesma
revisão.

### resultado não determinístico

Compare seed, revisão, ordem dos comandos, payload canônico, sequence/step e
fingerprints dos cálculos. Evite relógio real, GUID aleatório de domínio e iteração
não ordenada dentro da engine.

## Boas práticas

- Use uma seed explícita em cenários reproduzíveis.
- Mantenha fixtures mínimos e legíveis.
- Não dependa de arquivos gerados por uma execução manual da API.
- Não use rotas removidas ou helpers que escondam uma mutação fora do gateway.
- Afirme atomicidade em todos os caminhos de falha relevantes.
- Compare hashes em testes de replay, não apenas campos escolhidos.
- Dê ao teste um nome que descreva regra e resultado.
- Preserve o corpo HTTP ao reportar uma falha de contrato.

## Referências

- [Quickstart](api/getting-started.md)
- [Contratos](api/contracts.md)
- [Runs, combates e replay](api/runs-and-combat.md)
- [Checklist de validação](VALIDATION_CHECKLIST.md)
