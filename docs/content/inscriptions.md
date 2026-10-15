# Inscrições: contratos de conteúdo

Inscrições são equipamento de personagem. Não são componentes de carta, upgrades ou itens modificadores de cartas. O conteúdo é publicado pelos kinds canônicos `inscriptions` e `loadout-policies` e usa os mesmos JSONs revisionados dos outros módulos.

O schema tipado é definido em `Core.Run.Loadouts`. A publicação valida identidade, custo finito, atributos existentes, canais/buckets, fórmulas e referências. Não há conversão implícita de unidades: no exemplo abaixo o atributo usa probabilidade fracionária, logo `0.25` equivale a 25 pontos percentuais.

```json
{
  "calibrated_focus": {
    "inscriptionId": "calibrated_focus",
    "equipmentCost": 3,
    "tags": ["critical"],
    "attributeBonuses": [{
      "attributeId": "critical_chance",
      "influence": {"influenceId":"chance", "scope":"Actor", "channel":"critical_chance", "bucket":"flat", "value":0.25}
    }],
    "presentation": {"name":"Calibrated focus"}
  }
}
```

Arquivo separado em `Resources/loadout-policies`:

```json
{
  "hero_loadout": {
    "loadoutPolicyId":"hero_loadout",
    "inventoryScope":"Actor",
    "capacity":12,
    "allowDuplicateDefinitions":false,
    "maximumInventoryEntries":512,
    "inscriptionIds":["calibrated_focus"],
    "equipmentWindows":["OutsideCombat"],
    "slots":[{"slotId":"one","requiredTags":[]},{"slotId":"two","requiredTags":[]}]
  }
}
```

O modo habilita explicitamente `loadoutPolicyId`. Deve também habilitar os pipelines dos atributos utilizados, declarados na sua `effectResolutionPolicyId`. Um bônus não pode apontar para um pipeline de dano arbitrário fingindo ser atributo do personagem.

`inventoryScope: Actor` separa a posse por personagem persistente; `Run` compartilha a coleção, não os efeitos. Em ambos os casos os loadouts são individuais. `requiredTags` exige todas as tags indicadas, vazio aceita qualquer inscrição autorizada pela política. A capacidade limita o equipamento, não é energia de combate. `OutsideCombat`, `Preparation` e `Combat` são janelas explícitas; não há janela implícita quando a lista está vazia.

Os exemplos são contratos, não conteúdo já adicionado ao mundo 1. A disponibilidade rara de armadura continua sendo uma decisão do catálogo. Settings sem `loadoutPolicyId` permanecem opt-out e não ganham estado serializado de equipamento.

Regras excepcionais continuam separadas: o contrato de [reações de relíquias](relic-reactions.md) observa aplicações reais de recurso. Ele não pertence à definição de inscrição ou à carta.
