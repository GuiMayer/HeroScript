# Combat sandbox para Godot

O `combat_sandbox` é um `GameMode` da engine, não uma segunda implementação
de combate. A Godot envia intenções HTTP, mantém somente a sessão da interface
e renderiza os read models retornados pela API.

## Divisão de responsabilidades

| Engine HeroScript | Cliente Godot |
| --- | --- |
| Seed, IDs de instância, embaralhamento e hashes | Cenas, animações, sons e layout |
| Regras de carta, dano, custos, status e turnos | Clique, seleção de alvo e feedback de input |
| Legalidade de ações e alvos | Habilitar opções devolvidas pela API |
| Journal, replay, timeline, branches e simulações | Navegar snapshots e escolher uma branch |
| Políticas do `GameMode` e revisões de conteúdo | Exibir ferramentas permitidas pelo modo |

Não calcule dano, custo, ordem da mão, consequência de status ou próxima fase
na Godot. Mesmo uma prévia visual deve vir de uma leitura/ação legal da engine.

## Organização sugerida

```text
CombatApiClient       transporte HTTP e ProblemDetails
CombatSession         runId, combatId, snapshot selecionado e branch atual
CombatDtos            objetos de leitura desserializados da API
Combat scenes/widgets apresentação e conversão de input em intenções
```

`CombatApiClient` não conhece controles de cena. `CombatSession` é a única
fonte local de IDs e versões observadas. Após uma mutação aceita, substitua o
estado visual pelo recibo/snapshot retornado. Após `409`, recarregue o snapshot
atual; após `422`, mostre o diagnóstico e não tente corrigir o estado local.

## Fluxo do sandbox

1. Opcionalmente valide um cenário em `POST /api/v1/sandbox/scenarios/validate`.
2. Inicie-o em `POST /api/v1/sandbox/runs`.
3. Guarde `run.runId` e `combat.combatId`. Repetir o mesmo cenário e
   `attemptKey` é idempotente; trocar só `attemptKey` abre outra tentativa.
   O `run.combatResolutions` inicial contém o frame `combat.initialized` para
   animar efeitos de abertura.
4. Leia `GET /api/v1/sandbox/runs/{runId}/snapshot` para montar a tela.
5. Envie intenção ao gateway canônico de combate usando as versões observadas.
6. Reproduza em ordem os frames de `state.resolution.frames`.
7. Atualize a tela pelo último frame ou releia o snapshot.

Exemplo de ação de carta:

```json
{
  "commandId": "6ba5a0e2-0a56-4d43-bdf7-4950e950eac1",
  "expectedSequence": 2,
  "expectedStep": 6,
  "type": "EXECUTE_ACTION",
  "payload": {
    "actorId": "hero",
    "actionId": "basic_attack",
    "cardId": "c104ff7d-3f3c-4874-a697-645f27424d1d",
    "targetId": "goblin_a"
  }
}
```

`cardId` é a instância em `snapshot.hand[].cardInstanceId`, não o ID da
definição. Assim cópias iguais podem ter upgrades, histórico e destino próprios.

`initialState.effects` pode declarar status iniciais para aliases do cenário
(por exemplo, `{"targetAlias":"goblin_a","statusId":"poison","stacks":2}`).
Eles são validados pela capability do modo e ficam no snapshot imutável de
combate, portanto também aparecem na timeline, branches e replay.

## Fila de animação durável

A engine resolve todo o comando imediatamente. A Godot não espera animações
para autorizar a transição seguinte da engine; ela consome a fila imutável
associada ao `commandId` no ritmo visual desejado.

| Política JSON | Conteúdo do frame | Uso no cliente |
| --- | --- | --- |
| `FullSnapshots` | `stateAfter` contém o combate após cada transição | Renderize diretamente; é o modo de `combat_sandbox`. |
| `CompactWithSnapshotLookup` | `stateAfter` é `null` e o frame contém `snapshotSequence` | Busque `/timeline/{snapshotSequence}/state`; é o modo de `combat_sandbox_fixed_actions`. |

`frameId`, ordem, payload, sequência e step são determinísticos. Se a conexão
cair após o comando ser aceito, repita o comando com o mesmo `commandId` ou
consulte `/resolutions/{commandId}`; não gere uma segunda intenção para tentar
recriar as animações.

Cada frame expõe `effectSteps`, `calculations` e `applications`. Use
`applications` para escolher a animação concreta, `effectSteps` para explicar
ordem, skips, chance e proveniência, e `calculations` para ferramentas de
theorycraft. Verifique `resolutionFingerprint` ao reutilizar uma fila em cache.

Os dois modos iniciais diferem também no orçamento de ação: `combat_sandbox`
usa custos configurados e energia; `combat_sandbox_fixed_actions` ignora esses
custos e encerra a ativação após a quantidade definida em JSON.

## Timeline e branches

Use `GET /api/v1/combats/{combatId}/timeline` para uma lista compacta de
comandos e `GET /api/v1/combats/{combatId}/timeline/{sequence}/state` para
mostrar um momento passado em modo somente-leitura.

Quando `resolutionCommandId` estiver presente em um item, ele pode ser usado
diretamente em `GET /api/v1/combats/{combatId}/resolutions/{resolutionCommandId}`.
Transições internas do mesmo comando compartilham o `rootCommandId`; a Godot
não precisa deduzir essa associação pelo tipo ou pela posição do item.

Para continuar do passado, crie uma filha:

```text
POST /api/v1/combats/{combatId}/timeline/{sequence}/branches
{ "branchKey": "try-fireball-first" }
```

A resposta contém uma `runId` filha e `activeEncounterId` novo. Mude ambos na
sessão antes de habilitar input. O pai permanece imutável. Use
`GET /api/v1/runs/{runId}/branch-tree` para desenhar o navegador de branches;
selecionar um nó só carrega seu snapshot.

## Simulações

`POST /api/v1/simulations` executa uma sequência em uma branch durável e
isolada. `EXECUTE_ACTION`, `END_TURN` e `RESOLVE_COMBAT` usam o mesmo
coordenador de combate da jogada manual. O `GameMode` decide se a ferramenta é
permitida e seus limites de comandos/branches.

O resultado contém `simulationId`, hash final e timeline compacta. Repetir a
mesma fonte, sequência e comandos normalizados retorna a mesma simulação. Use
`GET /api/v1/simulations/{simulationId}` para o resumo ou `/result` para o
estado final completo.

## Conteúdo em desenvolvimento

Uma run sempre fixa uma revisão de conteúdo. Se o modo permitir hot reload, a
troca acontece somente por `ACTIVATE_CONTENT_REVISION` no gateway de comandos
da run. Mostre esse controle apenas quando
`resolvedMode.capabilityPolicy.allowHotReloadActivation` for verdadeiro e
recarregue o snapshot após aceitá-lo. Não há reload silencioso de regras numa
run já iniciada.

## Checklist de cliente

- Gere um novo `commandId` para cada intenção; reutilize-o somente para retry.
- Termine de consumir `resolution.frames` antes de liberar o próximo input visual.
- Use `expectedSequence` da run e `expectedStep` do combate atual.
- Use IDs retornados, sobretudo `cardInstanceId`; não gere IDs no cliente.
- Habilite botões e alvos somente a partir das leituras legais da API.
- Bloqueie mutação ao inspecionar timeline histórica até criar/trocar de branch.
- Ao trocar de branch, descarte cache visual do antigo `runId` e `combatId`.
