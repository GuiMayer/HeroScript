# Loadouts de inscrições (experimental)

Disponível somente para modos que habilitam `loadoutPolicyId`. Contratos de conteúdo e unidades: [inscrições](../content/inscriptions.md). Inscrições, relíquias e itens modificadores de cartas têm identidades e inventários distintos.

- `GET /api/v1/runs/{runId}/loadout?actorId={persistentActorId}`: coleção acessível ao personagem, equipamento, política, janelas e versão. No escopo Actor não retorna as peças de outro inventário; no escopo Run informa qual personagem está usando cada peça. A sessão compacta leva só um resumo, sem copiar todo inventário/traces.
- `POST /api/v1/runs/{runId}/loadout/preview`: rascunho completo, com `actorId`, `assignments` (objeto slot → GUID da instância), `expectedSequence` e `expectedStep`. Retorna `before` e `after` vinculados à versão consultada; não cria instâncias, não grava journal e não avança RNG. Versão obsoleta retorna 409 Problem Details.
- `POST /api/v1/runs/{runId}/commands`: confirmação pelo gateway existente. Tipo `APPLY_LOADOUT`; payload contém somente `actorId` e `assignments`. O envelope inclui `commandId`, `expectedSequence` e `expectedStep` da consulta/preview. Posições omitidas ficam vazias: não é um patch parcial.

```json
{
  "commandId":"5bc5347a-6009-4db3-b27c-81350cf1ec25",
  "type":"APPLY_LOADOUT",
  "expectedSequence":7,
  "expectedStep":12,
  "payload":{"actorId":"hero-1","assignments":{"one":"6fa16543-181b-428d-8a4b-9285cba11c78"}}
}
```

Custos, posse, tags, duplicatas, uso por outro personagem, capacidade e janelas são resolvidos pelo mesmo planner puro usado no preview. Não enviar valores de atributos, definições ou custos do cliente. Trocas usam o estado final da transação. Reutilizar o mesmo commandId/envelope no retry retorna o recibo existente; usar uma versão antiga com outro ID retorna conflito.

O gateway persiste o sucessor e o recibo atomicamente antes de publicá-los. Replay reexecuta o mesmo comando; branches preservam instâncias e equipamento, reatribuindo somente a identidade do inventário compartilhado à run filha. A coleção da branch não modifica a original.

O contrato OpenAPI vivo inclui `x-heroscript-command-payloads`, gerado a partir do registro canônico de comandos. O artefato estático inclui os endpoints e tipos de loadout, sem substituir os metadados curados das rotas anteriores.

`attributes` é a projeção canônica do personagem, com base/efetivo e traces de origem, tanto na consulta quanto no before/after do preview. Usa o mesmo provider que os cálculos de gameplay; nenhuma fórmula é executada na Godot. Inscrições só alteram cálculos elegíveis pelo contrato de canal/bucket/tags do conteúdo. Um atributo projetado não concede por si uma ação, uma rolagem de esquiva ou uma regra de limite de recurso: esses consumidores também precisam ser configurados pelo setting.

Estado desta etapa: equipamento/query/preview, provider de atributos e persistência funcionam; nenhum setting publicado foi habilitado automaticamente. Regras excepcionais, aquisição no mapa e integração Godot continuam etapas posteriores. Hot reload em runs com inscrições permanece conservador até a etapa de migração: não troca o escopo nem a revisão silenciosamente.

Versão determinística 32: a correção de owner fora de combate altera resultados que antes omitiam relíquias/modificadores. Runs de versões anteriores não são migradas ou apagadas; execução/replay semântico exige a versão executável correspondente. Não remover a guarda de versão para forçar compatibilidade.
