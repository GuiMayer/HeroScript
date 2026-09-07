# Sistema de recursos

**Status:** implementado e autoritativo

**Atualizado em:** 2026-09-05

## Objetivo

O sistema de recursos representa qualquer quantidade numérica limitada pertencente
a uma entidade: vida, energia, mana, escudo, ações, postura, moral ou uma mecânica
criada por um mod. O nome e a categoria do recurso são metadados. Eles não ativam
regras implícitas na engine.

Os princípios obrigatórios são:

- a definição vem do conteúdo JSON;
- cada run fixa uma revisão imutável desse conteúdo;
- cada alteração retorna um novo snapshot;
- lotes são atômicos: ou todas as alterações são aceitas, ou nenhuma é aplicada;
- dano, cura, custos e regeneração usam o mesmo redutor numérico;
- derrota é consequência de uma política declarada no recurso;
- a ordem de avaliação e os desempates são estáveis.

## Modelo

| Tipo | Responsabilidade |
| --- | --- |
| `ResourceDefinition` | Metadados e regras de um recurso carregados do JSON. |
| `ResourcePool` | Valor atual, mínimo, máximo e definição fixada. É imutável. |
| `ResourceSet` | Dicionário imutável dos recursos de um proprietário. |
| `ResolvedResourceMutation` | Alteração já resolvida, sem conhecer carta, dano ou turno. |
| `ResourceMutationReducer` | Valida e aplica um lote de alterações de forma pura e atômica. |
| `ResourceMutationRecord` | Registro do valor anterior e posterior de uma alteração aceita. |
| `ResourceThresholdPolicy` | Condição configurável e consequência associada a um limite. |

`ResourcePool.Materialize` é a única fronteira de construção de pools em produção.
Ela valida a definição, aplica valores específicos da entidade e normaliza o valor
inicial conforme `canBeNegative` e `canExceedMax`.

## Definição em JSON

As definições são descobertas dinamicamente em
`Resources/resources/*.json`. Não existe lista de IDs conhecida pelo código.

```json
{
  "resourceId": "stability",
  "displayName": "Stability",
  "shortName": "STB",
  "category": "SPECIAL",
  "defaultMin": 0,
  "defaultMax": 12,
  "defaultCurrent": 12,
  "canBeNegative": false,
  "canExceedMax": false,
  "regeneration": {
    "enabled": true,
    "amountPerTurn": 1,
    "formula": null,
    "timing": "START_TURN"
  },
  "costMultiplier": 1.0,
  "tags": ["combat", "primary"],
  "thresholdPolicies": [
    {
      "policyId": "defeat_when_depleted",
      "comparison": "LessThanOrEqual",
      "thresholdSource": "Minimum",
      "consequence": "DefeatOwner",
      "priority": 100
    }
  ]
}
```

`category` serve para organização, filtros e apresentação. `VITAL` não significa
"derrotar ao chegar a zero". Essa regra só existe quando uma
`thresholdPolicy` a declara.

Uma definição de entidade escolhe quais recursos possui e pode sobrescrever os
valores iniciais:

```json
{
  "entityId": "player_alchemist",
  "resources": {
    "resources": {
      "stability": { "current": 8, "max": 10 },
      "action_points": { "current": 2, "max": 2 }
    }
  }
}
```

O recurso precisa existir na revisão de conteúdo. Referências ausentes causam erro;
não são ignoradas silenciosamente.

## Pipeline de alteração

```text
regra configurada
  -> valor calculado
  -> ResolvedResourceMutation
  -> ResourceMutationReducer (lote atômico)
  -> novo ResourceSet
  -> EffectApplicationRecord / journal
  -> commit do agregado
  -> publicação de eventos
```

Uma mutação escolhe explicitamente:

- `resourceId`;
- `field`: `Current`, `Minimum` ou `Maximum`;
- `operation`: `Add`, `Subtract` ou `Set`;
- um valor finito;
- um `mutationId` estável para auditoria.

O redutor trabalha sobre uma cópia imutável. Se uma mutação do lote for inválida,
o estado original permanece intacto. Ao alterar mínimo ou máximo, o valor atual é
normalizado novamente com as regras da definição.

## Efeitos, custos e dano

`DAMAGE`, `HEAL` e `MODIFY_RESOURCE` são formas de autoria. Todos exigem
`targetResource` e convergem para a mesma mutação:

- `DAMAGE`: `Subtract`;
- `HEAL`: `Add`;
- `MODIFY_RESOURCE`: operação e campo explícitos.

Logo, dano em `mana`, cura de `stability` ou redução do máximo de `health` não
precisam de caminhos especiais. O efeito não infere um recurso pelo seu tipo.

Custos de cartas, ações, lojas, preparação e seleções usam
`ResourceCostTransitions`. Todas as parcelas de uma opção de custo são validadas
antes da aplicação e consumidas em uma única transação.

## Fórmulas

O vocabulário genérico de recursos é:

```text
source.resources.<resourceId>.current
source.resources.<resourceId>.minimum
source.resources.<resourceId>.maximum
source.resources.<resourceId>.percent

target.resources.<resourceId>.current
target.resources.<resourceId>.minimum
target.resources.<resourceId>.maximum
target.resources.<resourceId>.percent
```

Fórmulas de ciclo de vida também recebem `resources.<resourceId>.<field>`. Na
regeneração do próprio pool existem ainda os aliases locais `current`, `minimum`,
`maximum` e `percent`.

Exemplo:

```json
{
  "enabled": true,
  "amountPerTurn": 0,
  "formula": "resources.focus.current * 0.25",
  "timing": "END_TURN"
}
```

Variáveis especializadas como `source_hp` não pertencem ao contrato canônico.

## Regeneração e ciclo de vida

`RegenerationConfig` define `enabled`, um valor fixo ou fórmula e um dos momentos:

- `START_TURN`;
- `END_TURN`;
- `OUT_OF_COMBAT`.

As regras compatíveis são avaliadas contra o mesmo snapshot, em ordem estável por
ID de recurso. No combate de uma run, `CombatResourceLifecycle` converte o
resultado em comandos `MODIFY_RESOURCE` e usa o processador de efeitos canônico.
Eventos só são publicados depois que a transação completa da run é persistida.

## Limites e derrota

Uma política combina:

- `comparison`: `LessThan`, `LessThanOrEqual`, `Equal`, `NotEqual`,
  `GreaterThanOrEqual` ou `GreaterThan`;
- `thresholdSource`: `Minimum`, `Maximum` ou `Constant`;
- `thresholdValue`, quando a origem for constante;
- `tolerance` para igualdade numérica;
- `consequence`: atualmente `None` ou `DefeatOwner`;
- `priority` e `policyId` para resolução determinística.

Somente a política alcançada de maior prioridade de cada recurso é autoritativa.
Empates usam `policyId` ordinal. `CombatEntity.IsAlive` consulta essas políticas;
não procura `health`, o primeiro recurso `VITAL` ou um limite igual a zero.

## Recursos como influência de cálculo

Um pipeline pode transformar um campo de um recurso em contribuição de bucket:

```json
{
  "pipelineId": "default_effect_amount",
  "channel": "effect_amount",
  "resourceInfluenceBindings": [
    {
      "bindingId": "actor.power.flat",
      "scope": "Actor",
      "resourceId": "power",
      "field": "Current",
      "channel": "effect_amount",
      "bucket": "flat",
      "scale": 1,
      "offset": 0,
      "priority": 0
    }
  ]
}
```

A publicação do conteúdo valida a existência do recurso, o canal, o bucket,
valores finitos e IDs duplicados. O pipeline pertence à revisão fixada na run;
portanto replay e avaliação de carta observam as mesmas regras.

## Revisões, hot reload e determinismo

- A run guarda `contentRevision` e usa apenas definições daquela revisão.
- Pools guardam a definição materializada; mudanças externas não alteram snapshots.
- Reload cria conteúdo candidato e publicação cria uma nova revisão imutável.
- A ativação de conteúdo novo em uma run existente é uma decisão explícita do modo,
  nunca consequência automática de editar um arquivo.
- Ordenação ordinal, JSON canônico, seed e fingerprints tornam divergências
  observáveis no journal e na verificação semântica.

## REST API

Definições podem ser lidas genericamente:

```http
GET /api/v1/content/resources
GET /api/v1/content/resources/{resourceId}
```

O read model de entidade expõe um dicionário `resources`. Não há campos paralelos
como `currentHp`, `maxHp` ou `energy` que possam divergir do estado autoritativo.

Ao iniciar um encontro, recursos iniciais opcionais são informados por alias de
participante:

```json
{
  "hero": { "entityId": "player", "definitionId": "player_warrior" },
  "enemies": [
    { "entityId": "enemy_1", "definitionId": "enemy_goblin" }
  ],
  "initialResourceValues": {
    "player": { "energy": 3 }
  }
}
```

`entityId` identifica a instância no combate; `definitionId` seleciona o JSON.
Vários participantes podem usar a mesma definição com aliases diferentes.

## Regras para extensões

Ao adicionar uma mecânica:

1. Não compare `resourceId` para escolher comportamento.
2. Não dê semântica executável a `ResourceCategory`.
3. Não crie um pool especializado para um recurso específico.
4. Resolva a regra em dados e gere mutações genéricas.
5. Aplique lotes pelo redutor e publique somente depois do commit.
6. Inclua o resultado no journal ou em `EffectApplicationRecord`.
7. Valide referências durante a publicação do conteúdo.
8. Teste com um ID arbitrário, não apenas `health` ou `energy`.

## Referências no código

- `src/Core/Resources/ResourceDefinition.cs`
- `src/Core/Resources/ResourcePool.cs`
- `src/Core/Resources/ResourceSet.cs`
- `src/Core/Resources/ResourceMutationReducer.cs`
- `src/Core/Resources/ResourceThresholdPolicy.cs`
- `src/Core/Effects/ImmutableEffectProcessor.cs`
- `src/Core/Combat/Flow/CombatResourceLifecycle.cs`
- `src/Core/Calculations/CalculationInfluenceProviders.cs`
