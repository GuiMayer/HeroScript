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

Os exemplos acima ilustram o contrato, não os valores do Ascendant: esse setting usa chance em pontos percentuais (`30` = +30 pp) e possui seu próprio catálogo em `Resources/inscriptions/world_one.json`. Sua política usa seis posições/12 pontos, inventário Actor e equipamento fora do combate; esses valores não são constantes da engine. Veja [aquisição](inscription-acquisition.md) e [migração](inscription-migrations.md). A disponibilidade rara de armadura continua sendo uma decisão do catálogo. Settings sem `loadoutPolicyId` permanecem opt-out e não ganham estado serializado de equipamento.

Regras excepcionais continuam separadas: o contrato de [reações de relíquias](relic-reactions.md) observa aplicações reais de recurso. Ele não pertence à definição de inscrição ou à carta.

## Identidade do personagem e posições comuns no MVP

O MVP usa `prototype_character_inscriptions`: uma posição `identity` e seis posições `capacity_1` a `capacity_6`. A posição de identidade exige as tags `identity` e `character:prototype_hero`; as demais exigem `common`. São convenções do conteúdo, interpretadas pelo contrato genérico `requiredTags`, não categorias especiais da engine.

Um mod de identidade pode ser trocado por outro compatível com o mesmo personagem. O campo opcional `allowedActorDefinitionIds` da inscrição restringe as definições de personagem que podem equipá-la. Exemplo:

```json
{
  "tags": ["identity", "character:prototype_hero"],
  "allowedActorDefinitionIds": ["prototype_hero"]
}
```

A publicação exige referências existentes, únicas e não vazias. O planner valida a definição real do ator no estado persistente do jogador ou no encontro ativo; se não puder identificá-la, recusa equipamento restrito. Sem esse campo, a inscrição conserva o comportamento anterior. O cliente não decide a compatibilidade e não é permitido mover um mod de identidade para uma posição comum, nem uma inscrição comum para a identidade.

O conteúdo inicial inclui duas identidades de teste, ambas de custo zero: `prototype_identity_focus` (+30 pontos percentuais de chance crítica) e `prototype_identity_ward` (+20% aumentado de geração de Block em efeitos elegíveis). Não implementam a futura mecânica de Forja de Brasas. A escolha é opcional e utiliza a mesma transação `APPLY_LOADOUT`; não há equipamento implícito ao abrir a tela.

As seis posições comuns continuam sujeitas ao orçamento de **6 pontos**. Quantidade de posições e orçamento são limites independentes. A troca continua permitida somente em `OutsideCombat`. A política anterior `prototype_inscriptions`, limitada apenas por capacidade, permanece disponível para settings que a utilizam.

Runs existentes mantêm sua revisão e sua política. A interface pode mostrar seis destinos comuns na política sem posições fixas, mas isso é uma conveniência visual: não altera saves nem adiciona uma identidade à revisão antiga. A identidade aparece como reservada quando não existe no conteúdo da run. Para usar o novo catálogo e a política de sete posições, crie uma run na revisão atual; não há migração automática dos saves.
