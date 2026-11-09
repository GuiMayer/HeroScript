# Composição MVP — etapa 3: pilhas em coluna à esquerda

Data: 2026-11-09. **Etapas 1 e 2 aprovadas. Etapa 3 implementada, aguardando aprovação visual. Etapas 4–7 não iniciadas.**

Guia: [plano de composição](../plans/MVP_COMBAT_REFERENCE_HUD_REBUILD_PLAN.md). Evidências: [testes, arquivos protegidos e capturas](mvp-reference-stage3.json).

## Entrega visual

As pilhas saíram do conjunto provisório à direita (`TemporaryControls`) e agora ocupam uma coluna dedicada à esquerda (`CanvasHost/ReferenceCanvas/PileRail`) em `combat_mvp_view.tscn`, instanciando o componente existente `card_zone_controls.tscn` (`CardZoneControlsView` + `pile_button.tscn`).

- **Ordem vertical na coluna:** `Draw` (`draw` / `draw_pile`), `Exile` (`banish` / `exile` / `exile_pile`), `Discard` (`discard` / `discard_pile`) e `Owned Deck` (`owned` / `owned_deck`), seguidas de quaisquer outras zonas auxiliares publicadas em sua ordem relativa original.
- **Reuso visual:** os botões preservam o `CombatChrome`, os ícones existentes (`draw.svg`, `exile.svg`, `discard.svg`, `pile.svg`), os contadores e a tipografia atual, sem criação de novo tema, shader ou skin.
- **Contratos de zona e contrato de `Owned Deck`:** cada botão mantém seu endereço completo (`zoneId`, `ownerId`, `scopeId`), ordem autorizada (`orderVisible`) e visibilidade (`contentsVisible`). No `prototype-test`, `CardZoneReadModel.Project(run)` publica durante o combate a zona `"owned"` (`ownerId: "$run"`, `scopeId: null`, `presentation: {"displayName": "Collection / reserve"}`) contendo apenas as cartas mantidas na reserva da run fora do encontro ativo; a API não publica durante o combate uma lista consolidada somando `draw + hand + discard + banish + owned`. Conforme a regra de fronteira do plano, o cliente **não fabrica inventário pela soma das zonas**: o botão exibe exatamente a zona `"owned"` publicada pela engine (rótulo curto `Deck` / `Baralho` no botão e título/tooltip `Collection / reserve · <count>`).
- **Modal de coleção e foco:** clicar em qualquer pilha continua abrindo `CardZoneDialog` com instâncias reais de `CardView` (ou o placeholder de conteúdo oculto quando `contentsVisible == false`) e restaura o foco de teclado para o respectivo botão em `PileRail` ao fechar.
- **Âncoras de movimento e compra:** `CombatBeatOrchestrator.collect_zone_anchors_and_roles` e `CombatScreen._zone_anchor` passam a resolver automaticamente os centros dos botões em `PileRail` à esquerda, fazendo a compra (`draw`) e transferências partirem/chegarem na nova coluna sem alterar durações de animação.
- **Não interferência do drawer lateral:** o gatilho de abertura por hover na borda esquerda do `CombatAttributesDrawer` ignora movimentos do ponteiro na faixa vertical ocupada pelo `PileRail` quando o drawer está fechado, evitando abertura acidental ou interceptação de cliques nas pilhas (mantendo a abertura por hover no topo esquerdo acima de `PileRail` e pelo atalho `I`).

## Validação

Godot 4.7.2, cópia de QA isolada. Foram executados **26 scripts de validação, totalizando 1.373 verificações aprovadas**, todas com `exit_code = 0`, além de uma execução adicional de captura em português com mais 23 verificações aprovadas.

O novo teste `mvp_reference_piles` cobre 153 verificações:
- posicionamento da coluna `PileRail` à esquerda da mão e abaixo do cabeçalho em `1920×1080`, `1280×720`, `1280×800` e `2560×1080`, em inglês e português (`en` e `pt_BR`), com escalas de texto `1.0` e `1.6`;
- ocultação dos containers legados à direita na variante MVP;
- ordem vertical estrita (`Draw`, `Exile`, `Discard`, `Owned`), contadores publicados sem soma artificial, ícone de exílio para `banish` (`slot: "exile_pile"`) e suporte a `presentation.displayName`;
- preservação de endereços completos (`zoneId`, `ownerId`, `scopeId`);
- ocultação limpa da coluna quando nenhuma zona auxiliar é publicada;
- abertura de `CardZoneDialog` com `CardView` reais, preservação de `orderVisible = true`, catálogo neutro para `orderVisible = false`, ocultação de faces para `contentsVisible = false` e restauração de foco ao botão de origem ao fechar;
- não abertura acidental do `CombatAttributesDrawer` ao passar o mouse na borda esquerda sobre o `PileRail`;
- resolução das âncoras de transferência e compra (`draw`) a partir do botão `Draw` na coluna esquerda, mantendo a duração configurada em `MotionPolicy`.

Os 11 arquivos protegidos de carta, mão e movimento permanecem 100% inalterados (hashes SHA-256 idênticos aos das etapas 1 e 2).

Capturas renderizadas em `1920×1080`, `1280×800` e `2560×1080` foram geradas em `.runtime/mvp-reference-stage3/after/` (EN) e `.runtime/mvp-reference-stage3/pt-BR/` (PT-BR), com inspeção visual direta das imagens `1920×1080`.

## Reprodução e próximo gate

Executar `tools/test-godot-ui.ps1 -Tests mvp_reference_piles,mvp_reference_header,mvp_reference_base,card_collections,card_zone_motion,card_draw_pose,combat_mvp_layout` para a bateria focada da etapa 3, acrescentando `-VisualTest combat_mvp_preview -VisualOutputDirectory <pasta>` para regerar capturas.

Aguardar aprovação visual do usuário sobre a coluna de pilhas à esquerda antes de iniciar a Etapa 4 (Campo de batalha e arte separada das informações).
