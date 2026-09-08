# Sistema de combate

**Status:** implementado no fluxo canônico de run

**Atualizado em:** 2026-09-06

## Papel arquitetural

O combate é um subagregado imutável de uma run. A engine recebe comandos,
valida regras, calcula toda a consequência imediatamente e persiste o novo
snapshot. A Godot envia intenção e renderiza o estado e a fila de resolução;
ela não calcula custos, dano, alvos, turnos ou vitória.

Não existe uma segunda API autoritativa de combate. Criação e resolução do
encontro atravessam o gateway da run; ações do encontro atravessam o gateway de
combate e são registradas na mesma run.

## Invariantes

- Uma run fixa seed, versão da engine, modo e revisão de conteúdo.
- Todo comando possui ID, sequência e step esperados.
- Repetir o mesmo comando e payload devolve o recibo persistido.
- Um comando aceito produz um novo snapshot; snapshots anteriores não mudam.
- Alterações compostas são atômicas.
- Iterações e desempates relevantes usam ordem estável.
- O journal contém informação suficiente para replay semântico e auditoria.
- Eventos são projeções posteriores ao commit, não fonte concorrente de verdade.

## Estado

`CombatState` contém:

- identidade do combate e da run;
- contexto determinístico e turno atual;
- herói e inimigos materializados;
- recursos genéricos de cada participante;
- status ativos;
- board, fases, ativação e dados da estratégia de ordem de turno;
- histórico de ações.

`CombatActorState` não possui HP ou energia especializados. Seu estado numérico é um
`ResourceSet`. `IsAlive` é derivado somente das políticas de limite configuradas
nas definições dos recursos.

## Início de encontro

O encontro é iniciado com `START_ENCOUNTER`:

```http
POST /api/v1/runs/{runId}/commands
Content-Type: application/json
```

```json
{
  "commandId": "54dc2f17-37cb-4e77-91ca-8464367f49e6",
  "expectedSequence": 1,
  "expectedStep": 1,
  "type": "START_ENCOUNTER",
  "payload": {
    "participants": [
      {
        "instanceId": "player", "definitionId": "player_warrior",
        "sideId": "player", "controllerBinding": { "kind": "Player" }
      },
      {
        "instanceId": "enemy_1", "definitionId": "enemy_goblin",
        "sideId": "opposition", "controllerBinding": { "kind": "AI", "policyId": "gambit" }
      }
    ],
    "initialResourceValues": {
      "player": { "energy": 3 }
    }
  }
}
```

`instanceId` é a identidade única usada durante esse combate. `definitionId` aponta
para a entidade na revisão de conteúdo da run. Isso permite criar `enemy_1` e
`enemy_2` a partir de `enemy_goblin` sem duplicar JSON.

`initialResourceValues` é opcional, aceita qualquer recurso existente na
definição e é indexado pelo alias. Participante, definição ou recurso desconhecido
rejeita a transação inteira. Não existe `initialEnergy` ou fallback de entidade.

O journal registra os participantes materializados; replay não depende do estado
atual dos arquivos locais.

## Comandos durante o encontro

```http
POST /api/v1/combats/{combatId}/commands
Content-Type: application/json
```

Tipos aceitos:

| Tipo | Uso |
| --- | --- |
| `PLAY_CARD` | Joga uma instância de carta da run. |
| `EXECUTE_ACTION` | Executa uma habilidade configurada do ator. |
| `END_TURN` | Encerra a ativação/turno conforme o modo. |

Exemplo:

```json
{
  "commandId": "28beec35-b909-47de-876d-f11dccd59a23",
  "expectedSequence": 3,
  "expectedStep": 6,
  "type": "PLAY_CARD",
  "payload": {
    "cardInstanceId": "35fc8101-753e-4e5f-bf9e-2b91933d9290",
    "actorId": "player",
    "targetIds": ["enemy_1"],
    "costOptionId": "energy"
  }
}
```

O cliente nunca envia a definição ou o valor calculado da carta. A engine carrega
o container, aplica upgrades, status, relíquias e influências da revisão fixada,
valida custo/alvos/fase/orçamento e executa a transação.

## Fluxo de uma ação

```text
CommandEnvelope
  -> controle de concorrência e idempotência
  -> compilação da carta/ação
  -> validação de legalidade e alvos
  -> cálculo por buckets configurados
  -> pagamento atômico de custos
  -> efeitos fonte-agnósticos
  -> novo CombatState
  -> fila de resolução visual + journal
  -> commit atômico da run
```

Efeitos numéricos convergem para o redutor genérico de recursos. `DAMAGE` não
significa "reduzir health"; significa subtrair do `targetResource` declarado.
Derrota é reavaliada pelas políticas de limite após as transições.

## Turnos e fases

O modo de jogo escolhe a política de ativação, orçamento de ações, estratégia de
ordem e fases. O mínimo operacional é começo, meio e fim do turno, mas fases
adicionais podem ser configuradas.

Regenerações de `START_TURN` e `END_TURN`, ticks de status e gatilhos usam limites
de ciclo de vida nomeados. A engine resolve toda a cadeia imediatamente. A fila de
resolução permite que a Godot anime cada item no próprio ritmo sem pausar a regra.

Estado necessário a estratégias, como medidores de iniciativa, vive no snapshot
do combate. Serviços singleton não armazenam progresso de gameplay.

## Leituras para a Godot

| Objetivo | Endpoint |
| --- | --- |
| Encontro atual da run | `GET /api/v1/runs/{runId}/encounters/current` |
| Estado do combate | `GET /api/v1/combats/{combatId}` |
| Avaliar toda a mão | `GET /api/v1/combats/{combatId}/cards/evaluations` |
| Inspecionar uma carta | `GET /api/v1/combats/{combatId}/cards/{cardInstanceId}/evaluation` |
| Retomar fila visual | `GET /api/v1/combats/{combatId}/resolutions/{commandId}` |
| Histórico | `GET /api/v1/combats/{combatId}/history` |
| Journal | `GET /api/v1/combats/{combatId}/journal` |
| Timeline | `GET /api/v1/combats/{combatId}/timeline` |
| Estado histórico | `GET /api/v1/combats/{combatId}/timeline/{sequence}/state` |
| Criar branch | `POST /api/v1/combats/{combatId}/timeline/{sequence}/branches` |
| Verificar replay | `POST /api/v1/combats/{combatId}/verify` |

Os read models expõem `resources` como dicionário. A UI escolhe representação com
base em definições, tags e regras de apresentação; não deve esperar campos
duplicados `currentHp`, `maxHp` ou `energy`.

Resoluções incluem hashes inicial/final, fingerprint e, em cada frame, os passos
de efeito, cálculos e aplicações tipados. A inicialização do encontro também é
uma resolução durável. Itens da timeline apontam para ela por
`resolutionCommandId`, inclusive quando um comando produz várias transições.

## Timeline, branches e replay

Cada comando aceito forma um ponto da timeline. O sandbox pode:

- consultar o estado em uma sequência anterior;
- criar uma branch derivada daquele ponto;
- comparar branches sem alterar a original;
- verificar o journal reexecutando comandos contra a mesma revisão.

Branches têm identidade própria e preservam proveniência. Voltar no tempo não
reescreve o histórico da run original.

## Contrato com o cliente

A Godot deve:

1. guardar `runId`, `combatId`, `sequence`, `step` e o último hash;
2. criar um novo `commandId` para uma nova intenção;
3. reutilizar o mesmo request apenas em retry da mesma intenção;
4. tratar `409` como necessidade de recarregar o estado;
5. renderizar a fila de resolução sem recalcular regras;
6. atualizar a UI exclusivamente a partir da resposta aceita/read model.

## Referências

- [Runs, combates e replay](../../api/runs-and-combat.md)
- [Sistema de recursos](../resources/resource-system.md)
- [Sistema de efeitos](../effects/effect-system.md)
- [Arquitetura e plano do sandbox](../../plans/COMBAT_SANDBOX_ARCHITECTURE_AND_PLAN.md)
