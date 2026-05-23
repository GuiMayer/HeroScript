# HeroScript API - Endpoints Atuais

Referencia dos endpoints REST expostos pelo projeto atual. A API e headless e data-driven: a regra de jogo deve vir de JSON (`ActionDefinition`, `EffectDefinition`, recursos, entidades, status, pipelines); o codigo expõe interpretadores e simuladores.

## Base URL

```text
http://localhost:5260/api
```

Swagger UI fica disponivel na raiz da aplicacao em ambiente de desenvolvimento.

## Estado da API

- Contrato principal de acoes: `effects[]` e `costs`.
- Campo `baseDamage`: mantido apenas como valor derivado/compatibilidade.
- Execucao de combate: prefira `actionId` em vez de montar manualmente `ActionType`/`PowerId`.
- Execucao generica de acontecimentos: use `/api/effect/apply`.
- Dano isolado: `/api/damage/calculate` e `/api/damage/simulate` sao diagnosticos, nao caminho canonico de execucao.
- Status: rota canonica `/api/status`; rotas antigas `/api/StatusEffect` e aliases de combate continuam para compatibilidade.
- Modifiers e Gambits: rotas canonicas `/api/modifiers` e `/api/gambits`.

---

## Health

### GET `/api/health`

Retorna o estado basico da API.

---

## Actions

Controla definicoes de acoes carregadas pelo `ActionManager`.

### GET `/api/action`

Lista acoes resumidas.

Resposta exemplo:

```json
[
  {
    "actionId": "basic_attack",
    "displayName": "Basic Attack",
    "actionType": "BASIC_ATTACK",
    "cooldown": 0,
    "requiresTarget": true,
    "multiTarget": false,
    "baseDamage": 10,
    "effectCount": 2,
    "tags": ["physical", "attack"],
    "costOptionsCount": 0
  }
]
```

### GET `/api/action/{actionId}`

Retorna a definicao completa da acao, incluindo custos e `effects[]`.

Resposta exemplo:

```json
{
  "actionId": "fireball",
  "displayName": "Fireball",
  "description": "Deal fire damage",
  "actionType": "POWER",
  "cooldown": 0,
  "requiresTarget": true,
  "multiTarget": false,
  "baseDamage": 30,
  "tags": ["spell", "fire"],
  "costs": {
    "resourceCosts": [
      { "resourceId": "energy", "amount": 3 }
    ],
    "alternativeCosts": []
  },
  "effects": [
    {
      "effectId": "fireball_damage",
      "type": "DAMAGE",
      "target": "TARGET",
      "timing": "IMMEDIATE",
      "flatValue": 30,
      "formulaValue": null,
      "targetResource": "health",
      "chance": 1,
      "repeat": 1,
      "tags": ["fire"]
    }
  ]
}
```

### GET `/api/action/by-type/{actionType}`

Filtra acoes por `ActionType`.

Tipos implementados no Core atual incluem `BASIC_ATTACK`, `POWER`, `PASS`, `END_TURN` e tipos de fase/stack TCG.

### GET `/api/action/by-tag/{tag}`

Filtra acoes por tag. Tag vazia retorna `400 Bad Request`.

### POST `/api/action/validate`

Valida uma `ActionDefinition` recebida pela API.

Request exemplo:

```json
{
  "definition": {
    "actionId": "custom_spell",
    "displayName": "Custom Spell",
    "actionType": "POWER",
    "requiresTarget": true,
    "multiTarget": false,
    "cooldown": 0,
    "tags": ["magic"],
    "effects": [
      {
        "effectId": "custom_damage",
        "type": "DAMAGE",
        "target": "TARGET",
        "timing": "IMMEDIATE",
        "flatValue": 12,
        "targetResource": "health"
      }
    ]
  }
}
```

### POST `/api/action/reload?configName=default`

Recarrega definicoes de acao quando reload estiver habilitado.

---

## Combat

Controla instancias de combate em memoria.

### POST `/api/combat/start`

Inicia um combate.

Request exemplo:

```json
{
  "heroId": "player_warrior",
  "enemies": ["enemy_orc_warrior"],
  "initialEnergy": 3
}
```

Quando IDs existem no loader de entidades, o Core cria combatentes a partir das definicoes JSON.

### POST `/api/combat/{combatId}/action`

Executa uma acao. O contrato preferido e `actionId`; `actionType`/`powerId` continuam como compatibilidade.

Request recomendado:

```json
{
  "actionId": "fireball",
  "targetId": "enemy-1",
  "costOptionId": null
}
```

Request legado ainda aceito:

```json
{
  "actionType": "POWER",
  "powerId": "fireball",
  "targetId": "enemy-1"
}
```

### GET `/api/combat/{combatId}/state`

Retorna o estado atual do combate.

### GET `/api/combat/{combatId}/history`

Retorna historico de acoes do combate.

### GET `/api/combat/{combatId}/available-actions`

Lista acoes disponiveis no estado atual, incluindo custos, alvo e resumo de effects.

### GET `/api/combat/{combatId}/actions/{actionId}/cost-options`

Lista opcoes de custo normal/alternativo para a acao.

### POST `/api/combat/{combatId}/actions/{actionId}/can-afford`

Valida se uma acao pode ser paga com os recursos atuais.

### POST `/api/combat/{combatId}/end`

Encerra uma instancia de combate.

---

## Effects

Endpoint central para acontecimentos data-driven.

### GET `/api/effect/types`

Lista `EffectType` disponiveis.

### GET `/api/effect/targets`

Lista `EffectTarget` disponiveis.

### GET `/api/effect/scopes`

Lista scopes suportados, como `COMBAT` e `RUN`.

### POST `/api/effect/apply`

Aplica um efeito em um contexto. Hoje suporta `COMBAT` e `RUN`; outros dominios devem entrar conforme `Run/Deck/Shop` forem implementados.

Request de combate:

```json
{
  "scope": "COMBAT",
  "combatId": "00000000-0000-0000-0000-000000000001",
  "sourceEntityId": "hero",
  "targetEntityId": "enemy-1",
  "sourceActionId": "fireball",
  "effect": {
    "effectId": "fireball_damage",
    "type": "DAMAGE",
    "target": "TARGET",
    "timing": "IMMEDIATE",
    "flatValue": 30,
    "targetResource": "health"
  }
}
```

Request de run/economia:

```json
{
  "scope": "RUN",
  "runId": "run-001",
  "sourceEntityId": "system",
  "targetEntityId": "player",
  "effect": {
    "effectId": "reward_gold",
    "type": "GAIN_GOLD",
    "target": "SELF",
    "timing": "IMMEDIATE",
    "flatValue": 25
  }
}
```

---

## Status

Rota canonica: `/api/status`. Rota antiga `/api/StatusEffect` continua disponivel.

### POST `/api/status/apply`

Aplica um status em uma entidade.

```json
{
  "targetId": "00000000-0000-0000-0000-000000000002",
  "statusId": "burning",
  "stacks": 2,
  "duration": 3,
  "sourceId": "00000000-0000-0000-0000-000000000001"
}
```

### GET `/api/status/{targetId}/active`

Lista status ativos do alvo.

### DELETE `/api/status/remove`

Remove uma instancia de status por `targetId` e `instanceId`.

### DELETE `/api/status/{targetId}/status/{statusId}`

Remove status por ID de definicao.

### POST `/api/status/add-stacks`

Adiciona stacks a uma instancia.

### POST `/api/status/remove-stacks`

Remove stacks de uma instancia.

### PUT `/api/status/{targetId}/status/{instanceId}/duration`

Atualiza duracao.

### POST `/api/status/process`

Processa ticks por timing.

### POST `/api/status/{targetId}/tick`

Reduz duracoes e expira status.

### GET `/api/status/{targetId}/modifiers`

Retorna modificadores de pipeline ativos.

### Aliases de combate

Tambem existem aliases como:

- `POST /api/combat/{combatId}/entities/{targetId}/status`
- `GET /api/combat/{combatId}/entities/{targetId}/status`
- `POST /api/combat/{combatId}/entities/{targetId}/status/{instanceId}/add-stacks`
- `POST /api/combat/{combatId}/entities/{targetId}/status/{instanceId}/refresh`
- `DELETE /api/combat/{combatId}/entities/{targetId}/status`

---

## Script Modifiers

Controla definicoes e instancias de modificadores data-driven.

### GET `/api/modifiers`

Lista modificadores disponiveis.

### GET `/api/modifiers/{modifierId}`

Retorna uma definicao especifica.

### POST `/api/modifiers/reload?configName=default`

Recarrega definicoes quando reload estiver habilitado.

### POST `/api/modifiers/apply`

Aplica um modificador a um owner/action.

Request exemplo:

```json
{
  "ownerId": "hero-1",
  "modifierId": "GO_AGAIN",
  "sourceId": "preparation",
  "actionId": "fireball"
}
```

### GET `/api/modifiers/active/{ownerId}`

Lista instancias ativas de um owner.

### GET `/api/modifiers/active/{ownerId}/pipeline?tags=offensive&tags=fire`

Retorna modificadores aplicaveis ao pipeline filtrado por tags.

### POST `/api/modifiers/active/{ownerId}/tick`

Processa duracao/tick das instancias ativas.

### DELETE `/api/modifiers/active/{ownerId}/{instanceId}`

Remove uma instancia ativa.

---

## Gambits

Controla regras data-driven para companions/IA. O endpoint atual decide a proxima acao; execucao automatica do turno de IA ainda pertence a Fase 3.

### GET `/api/gambits`

Lista definicoes de gambit.

### GET `/api/gambits/{gambitId}`

Retorna uma definicao especifica.

### POST `/api/gambits/reload?configName=default`

Recarrega regras quando reload estiver habilitado.

### POST `/api/gambits/decide`

Escolhe a melhor acao para uma entidade em combate com base na ordem/prioridade dos `gambitIds`.

Request exemplo:

```json
{
  "combatId": "00000000-0000-0000-0000-000000000001",
  "entityId": "companion-1",
  "gambitIds": ["heal_low_hp", "attack_weakest"]
}
```

Observacao: este endpoint decide a acao; ele nao executa automaticamente todos os turnos de IA. A automacao deve entrar com endpoint futuro como `POST /api/combat/{combatId}/process-ai-turns`.

---

## Damage

API diagnostica para simular dano e inspecionar pipeline.

### POST `/api/damage/calculate`

### POST `/api/damage/simulate`

Ambos executam a mesma simulacao. Use `/api/combat/{combatId}/action` ou `/api/effect/apply` para execucao real.

### GET `/api/damage/pipeline/config`

Retorna configuracao atual do pipeline.

### POST `/api/damage/pipeline/reload`

Recarrega pipeline quando habilitado.

---

## Game Resources

### GET `/api/game-resources`

Lista recursos de jogo.

### GET `/api/game-resources/{resourceId}`

Retorna recurso por ID.

### GET `/api/game-resources/by-category/{category}`

Filtra por categoria.

### GET `/api/game-resources/by-tag/{tag}`

Filtra por tag. Tag vazia retorna `400 Bad Request`.

### POST `/api/game-resources/validate`

Valida uma definicao de recurso.

### POST `/api/game-resources/reload?configName=default`

Recarrega recursos quando habilitado.

### POST `/api/game-resources/create-pool`

Cria `ResourcePool` a partir de uma definicao.

### POST `/api/game-resources/validate-cost`

Valida custo contra quantidade atual.

---

## Resource Diagnostics

### GET `/api/resource/origins?resourcePath=Pipelines/MathFormulas.json`

Retorna origens de formulas/recursos rastreados.

### POST `/api/resource/reload`

Recarrega recurso diagnostico quando habilitado.

### GET `/api/resource/stats`

Ainda retorna `501 Not Implemented`.

---

## Config

### GET `/api/config`

Lista configs disponiveis.

### GET `/api/config/current`

Retorna config atual e cadeia de heranca.

### GET `/api/config/{name}`

Retorna metadados de uma config.

### GET `/api/config/{name}/chain`

Retorna cadeia de heranca.

### POST `/api/config/{name}/validate`

Valida uma config.

### POST `/api/config/{name}/load`

Carrega config quando reload estiver habilitado.

---

## Entity

### GET `/api/entity/definitions`

Lista definicoes de entidade.

### GET `/api/entity/definitions/{definitionId}`

Retorna definicao especifica.

### POST `/api/entity/create`

Cria entidade a partir de definicao.

### POST `/api/entity/definitions/validate`

Valida definicao de entidade.

---

## Math, Formula e Operations

### GET `/api/formula`

Lista formulas.

### GET `/api/formula/{name}`

Retorna formula por nome.

### POST `/api/formula/evaluate`

Avalia formula.

### POST `/api/formula/reload`

Recarrega formulas quando habilitado.

### POST `/api/math/expression/evaluate`

Avalia uma expressao/operacao matematica.

### GET `/api/operation`

Lista operacoes matematicas.

### GET `/api/operation/{name}`

Retorna metadados de uma operacao.

### GET `/api/operation/categories`

Lista categorias de operacao.

---

## Events

### GET `/api/events`

Lista eventos em memoria.

### GET `/api/events/{eventId}`

Retorna evento especifico.

### GET `/api/events/categories`

Lista categorias.

### GET `/api/events/severities`

Lista severidades.

### DELETE `/api/events`

Limpa historico somente em ambiente de desenvolvimento.

---

## Contratos de Erro

Padrao atual:

- `400 Bad Request`: entrada invalida, enum invalido, tag vazia, custo negativo, contexto de effect invalido.
- `404 Not Found`: recurso/acao/config/combate inexistente.
- `403 Forbidden`: reload/clear bloqueado por ambiente/configuracao.
- `500 Internal Server Error`: falhas inesperadas.

## Observacoes de Teste

- `Core.Tests`: suite principal verde no momento da atualizacao.
- `API.Tests`: projeto compila; testes unitarios de `ConfigController` foram alinhados; o runner local ainda congela ao filtrar `ResourceControllerTests`, sem reportar falha de assercao.
