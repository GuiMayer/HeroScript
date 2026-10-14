# Cartas: apresentação, cálculo e itens modificadores

Engine `30`. A Godot não calcula efeitos ou compatibilidade; consome fatos revisionados e envia comandos.

## Camadas do valor

- `original`: definição autorada.
- `composedBase`: definição com transformações próprias da carta. É o valor mostrado na oferta e na coleção.
- `source`: cálculo das etapas de origem da pipeline, no snapshot anterior à ação. É o valor contextual da face no combate, mesmo sem energia ou sem um alvo selecionado.
- `impacts`: resultados de um preview executável. Ficam separados; a validade informa quando representam um caminho amostrado, não uma garantia de crítico.

Fórmulas dependentes de alvo, quantidades transportadas ou defaults não resolvidos permanecem desconhecidas. Não substituir desconhecido por zero. Custos dinâmicos também devem ser identificados como contextuais quando não resolvidos.

Ofertas não possuem `cardInstanceId`. Usam identidade `Offer`, referência concreta da seleção/loja e sua geração. Não criar uma instância fictícia para ler uma carta. A composição mostrada deve corresponder à carta adquirida.

Os efeitos possuem `effectPath` e `parentEffectPath`: a lista já é achatada. Não renderizar novamente `chainedEffects` ao consumir essa lista. A camada Source compara com ComposedBase; ComposedBase compara com Original. A paleta do componente permanece configurável: neutro/branco, menor/vermelho e maior/azul.

## Consultas

| Consulta | Resultado |
| --- | --- |
| `GET /api/v1/runs/{runId}/cards/presentations` | Instâncias visíveis e ofertas atuais, em base composta; lote máximo 512, sem resposta parcial silenciosa. |
| `GET /api/v1/combats/{combatId}/cards/evaluations` | Legalidade separada da apresentação, com origem e impactos quando disponíveis. |
| `GET /api/v1/runs/{runId}/card-modifier-items` | Catálogo revisionado, inventário e compatibilidade com cartas visíveis. |
| `GET /api/v1/runs/{runId}/card-modifier-items/{itemInstanceId}/preview/{cardInstanceId}` | Antes/depois em base composta, versão esperada e identidades selecionadas. Não retorna nem aplica um candidato de run. |

A sessão publica um conjunto completo depois de validar run, sequência e revisão. Inspeção em combate também valida combate e step. Ler esses endpoints não consome RNG nem altera snapshots. Caches são derivados, limitados e separados por hash de estado/revisão; nunca são autoridade de gameplay.

## Conteúdo de um item

Registre o arquivo no `package.json`, com kind e diretório canônico `card-modifier-items`:

```json
{
  "split_matrix": {
    "itemDefinitionId": "split_matrix",
    "upgradeId": "configured_multihit",
    "use": "Bind",
    "presentation": {
      "name": "Split matrix",
      "description": "Transforms a compatible card into multiple hits.",
      "artId": "flurry"
    }
  }
}
```

`upgradeId` referencia o compositor existente: patches, bundles, predicados, slots e categorias. Não existe outro processador de efeitos para itens. `use` é obrigatório: `Consume` utiliza a unidade; `Bind` mantém vínculo à carta e transformação. As duas políticas preservam proveniência no inventário. Não há remoção, devolução, transferência ou reaplicação gratuita implícita.

Um item pode existir sem carta escolhida. Cada unidade guarda identidade determinística, revisão de aquisição, origem e sequência. Ao aplicar, a engine resolve a definição na revisão de aquisição e valida a composição resultante contra a revisão atual da run. Atualizar conteúdo não transforma silenciosamente um item já possuído.

## Aquisição e aplicação

Na loja, declare `modifierItemDefinitionId` em vez de `cardId`, com `costs`. Uma oferta não pode entregar simultaneamente carta e item. Lojas com pool de cartas podem incluir itens modificadores explícitos; o reroll continua obedecendo às regras da loja.

Uma atividade `CardUpgrade` pode oferecer `modifierItemIds`. Os `upgradeIds` continuam representando upgrades diretos. O Mundo 1 usa upgrades diretos apenas para Base; afinidades/comportamentos são itens independentes. Adquirir pela atividade consome sua operação/custos e não altera deck.

Envie pelo gateway de run, sempre com `commandId`, `expectedSequence` e `expectedStep`:

```json
{ "type": "ACQUIRE_CARD_MODIFIER_ITEM", "payload": { "itemDefinitionId": "split_matrix" } }
```

```json
{ "type": "APPLY_CARD_MODIFIER_ITEM", "payload": { "itemInstanceId": "uuid", "cardInstanceId": "uuid" } }
```

Comprar usa `BUY_SHOP_ITEM`, não um comando de aplicação. Aplicação exige run ativa fora de combate e unidade disponível. O mesmo planner atende compatibilidade, preview e comando. Carta e unidade entram no mesmo candidato/commit: rejeição não perde item, retry não duplica aplicação. Transformações de origem item não podem ser removidas/substituídas pelo comando de upgrade.

## Interface

`card_facts_presenter.gd` formata as cartas compartilhadas; `card_calculation_presenter.gd` formata números já publicados. A face não usa nomes de alvos ou resultados críticos. O tooltip explica camadas/atributos/fontes pertinentes; o inspector permite leitura persistente. Traces invisíveis/versões/impactos não entram na chave de reconstrução da face.

MODIFIERS no mapa/jornada abre a coleção: item → cartas com compatibilidade publicada → comparação antes/depois → confirmação. Slots, preços e condições não são recalculados pela Godot. Arte de item usa categoria `modifier_items` e slot substituível no catálogo visual; não embutir regras na arte/flavor.

## Compatibilidade executável

Não há migração silenciosa de runs antigas. Para jogar as regras novas, reinicie a API pelo launcher e crie uma run na versão `30`/revisão nova. Históricos de outra versão exigem o runtime executável correspondente para replay semântico.
