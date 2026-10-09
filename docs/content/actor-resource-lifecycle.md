# Recursos do personagem entre encontros

Disponível a partir da engine version 21. O recurso continua genérico: seu nome não define dano, derrota, regeneração ou tempo de vida.

A definição de run informa `playerDefinitionId`. Seus componentes `stats` e `resources` materializam `RunState.PlayerEntity`. `RunState.ResourceState` é outra autoridade: a carteira da run. Mesmo que ambas contenham um recurso com o mesmo ID, seus valores não se misturam.

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

Dentro do encontro, o ator é a autoridade. A resolução deve promover o resultado e fechar o encontro na mesma transação. Fora do encontro, efeitos de atividade usam o personagem persistente. Esses pontos de integração são entregues na etapa 3; a política e os planners são os fundamentos da etapa 2.

## Saves e conteúdo

Novos estados incluem o componente de recursos do personagem. Saves com versão anterior são preservados em disco, mas não executados por um runtime de semântica diferente. A engine reporta a versão indisponível; não migra nem apaga saves automaticamente.

Hotreload preserva os valores e os limites de runtime, atualiza as definições e valida schema/proprietário. Trocar IDs de componente ou recursos é incompatível. A carta e a carteira não ganham cópias alternativas desses valores.
