# Recursos do personagem entre encontros

Disponível a partir da engine version 21. O recurso continua genérico: seu nome não define dano, derrota, regeneração ou tempo de vida.

A definição de run informa `playerDefinitionId`. Seus componentes `stats` e `resources` materializam `RunState.PlayerEntity`. `RunState.ResourceState` é outra autoridade: a carteira da run. Mesmo que ambas contenham um recurso com o mesmo ID, seus valores não se misturam.

Participantes de encontro podem declarar `identityBinding: RunPlayer`; a engine resolve `instanceId` para `run.PlayerEntityId` antes de materializar o ator, inclusive quando o jogador possui ID personalizado. `Literal` mantém a identidade declarada. IDs/aliases duplicados ou colisões após o binding são rejeitados; overrides de recursos usam o mesmo remapeamento. O vínculo é explícito e não inferido do controller.

O modo pode referenciar `actorResourceLifecyclePolicyId`. A definição fica no catálogo `actor-resource-lifecycle-policies` e é exposta pelos endpoints genéricos de conteúdo. A política resolvida acompanha o snapshot da run e sua revisão.

```json
{
  "world_one_actor_resources": {
    "actorResourceLifecyclePolicyId": "world_one_actor_resources",
    "rules": [
      {
        "resourceId": "health",
        "entry": "PreserveCurrent",
        "victory": "PreserveCurrent",
        "defeat": "PreserveCurrent",
        "draw": "PreserveCurrent",
        "abandoned": "PreserveCurrent",
        "retry": "EncounterOnly",
        "missingResource": "Error"
      },
      {
        "resourceId": "energy",
        "entry": "ResetToMaximum",
        "victory": "EncounterOnly",
        "defeat": "EncounterOnly",
        "draw": "EncounterOnly",
        "abandoned": "EncounterOnly",
        "retry": "EncounterOnly",
        "missingResource": "Error"
      }
    ]
  }
}
```

## Semântica

- `PreserveCurrent`: copia o valor atual da origem para o destino.
- `ResetToMaximum`: restaura o máximo persistente, não o máximo alterado por buffs de combate.
- `ResetToConfiguredValue`: usa `configuredValue`, finito, limitado pela definição do recurso e seus bounds persistentes.
- `EncounterOnly`: não importa nem exporta esse recurso naquela fronteira; a fábrica inicializa o encontro e seu resultado não modifica a base persistente.
- Recursos não listados não são transportados. Sem política, os encontros continuam independentes.
- Todas as ações de entrada, resultado e retry são obrigatórias. Recursos ausentes geram erro salvo `missingResource: Ignore`. Não existe fallback implícito para vida/energia.
- Apenas `Current` é transportado. `Minimum`, `Maximum` e definição seguem a base persistente. Overflow/valores negativos obedecem à definição do recurso; não há promoção de buff temporário.

Dentro do encontro, o ator é a autoridade. A resolução promove o resultado e fecha o encontro na mesma transação, antes dos efeitos de saída. Falhar no último efeito de saída preserva o encontro ainda não resolvido e a base anterior. Comandos duplicados retornam o receipt, sem segunda promoção.

## Recuperação fora do combate

Cada conjunto de efeitos de atividade escolhe um proprietário. A escolha é independente de `SELF`/`TARGET`, que continua sendo interpretado pelo processador universal:

- Nós do mapa: `entryEffectOwner` e `exitEffectOwner`.
- Opções de preparação: `effectOwner`.
- Diálogo: `entryEffectOwner` no nó, `effectOwner` na escolha.
- Valores aceitos: `RunWallet` (carteira) ou `PlayerEntity` (personagem persistente).

`PlayerEntity` exige um componente de recursos persistentes e nenhum encontro ativo. Não existe endpoint normal de sobrescrita desses valores. O conjunto tem um proprietário único; para pagar carteira e recuperar personagem use `costs` da opção e `effectOwner: PlayerEntity`. O custo e todos os efeitos entram no mesmo candidato e commit.

```json
{
  "optionId": "recover",
  "costs": [{ "resourceId": "gold", "amount": 20 }],
  "effectOwner": "PlayerEntity",
  "effects": [{
    "effectId": "recover.health",
    "type": "HEAL",
    "target": "SELF",
    "targetResource": "health",
    "parameters": [{
      "parameter": "Amount",
      "flatValue": 20,
      "unitId": "points",
      "channel": "recovery",
      "pipelineId": "recovery"
    }]
  }]
}
```

A pipeline `recovery` deve existir e estar habilitada no modo. Cura, modificadores e bounds continuam usando os mesmos cálculos/processador de recursos; não há fórmula paralela em Godot ou no handler de preparação. O mesmo ID pode aparecer nas duas carteiras sem ambiguidade de proprietário.

As consequências de derrota declaradas no próprio recurso também são avaliadas no commit fora do encontro e tornam a run `Failed`, sem depender do nome do recurso. Durante um encontro, o baseline persistente não é reavaliado como cópia da vida do ator.

## Saves e conteúdo

Novos estados incluem o componente de recursos do personagem. Saves com versão anterior são preservados em disco, mas não executados por um runtime de semântica diferente. A engine reporta a versão indisponível; não migra nem apaga saves automaticamente.

Hotreload preserva os valores e os limites de runtime, atualiza as definições e valida schema/proprietário. Trocar IDs de componente ou recursos é incompatível. A carta e a carteira não ganham cópias alternativas desses valores.
