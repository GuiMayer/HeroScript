# Composição MVP — etapa 6: janelas e overlays alinhados à nova composição

Data: 2026-11-09. **Etapas 1, 2, 3, 4 e 5 aprovadas. Etapa 6 implementada, aguardando aprovação visual. Etapa 7 não iniciada.**

Guia: [plano de composição](../plans/MVP_COMBAT_REFERENCE_HUD_REBUILD_PLAN.md). Evidências: [testes, arquivos protegidos e capturas](mvp-reference-stage6.json).

## Entrega visual

Na variante MVP (`combat_mvp_view.gd` e `combat_mvp_view.tscn`), todas as janelas flutuantes, diálogos modais e avisos de sessão abrem sobre o `ReferenceCanvas` em posições coerentes e ajustadas ao canvas real, sem deslocar campo de batalha, coluna de pilhas, energia, mão ou controles de turno:

- **Painel de opções de custo/candidatos (`CandidatePanel` em `FloatingCombatHUD/FloatingHUDHost`):**
  - Centralizado horizontalmente sobre a mão (`HandHost`), respeitando os limites entre `PileRail` (esquerda) e `EndTurnAndContextActions` (direita), e posicionado imediatamente acima de `EnergyIndicator` (`minf(energy_top - 8.0, first_card_top - fitted.y - 8.0)`).
  - Não sobrepõe `PileRail`, `HandHost` nem `EndTurnAndContextActions` tanto na escolha de múltiplas opções de jogada quanto na seleção inline de pagamento com cartas (`inline_selection`).
- **Botão de reconexão (`Reconnect` em `FloatingCombatHUD/FloatingHUDHost`):**
  - Reposicionado do canto superior esquerdo (onde cobria o retrato/nome de `ActiveIdentity` em `CombatHeader`) para o topo direito da arena logo abaixo de `CombatHeader` (`offset_top = header_view.offset_bottom + 8.0`, alinhado à margem direita da arena).
  - Permanece oculto enquanto `synchronized == true` e, quando exibido em perda de sincronia, nunca cobre `CombatHeader`, `PileRail`, placas de atores, `HandHost` ou `EndTurnButton`.
- **Gaveta de atributos do personagem (`CombatAttributesDrawer` em `FloatingCombatHUD/FloatingHUDHost`):**
  - Mantém abertura pelo atalho `I` (`open_character`, remapeável) ou por aproximação do ponteiro na borda esquerda acima de `PileRail` (sem abrir ao passar o mouse sobre `PileRail`), ajustada a `gameplay_column.get_global_rect()`.
  - Restaura o foco ao controle anterior da partida ao fechar (via `Escape`, `I` ou botão de fechar da gaveta) quando o foco estava dentro da gaveta ou sem dono.
- **Inspeção de carta (`InspectionDialog`), coleções de zonas (`CardZoneDialog`) e registro de combate (`CombatLog`):**
  - Todos os diálogos de leitura usam `hand.track_reading_modal(dialog)`, bloqueando atalhos de fundo (`I`, `V`, `E`, `F`) e abertura por hover de borda enquanto abertos, e ajustam seus limites à viewport (`popup_centered` / `popup_centered_clamped`).
  - `CombatLog` continua usando o `AcceptDialog` atual (sem introduzir o journal estruturado do backlog), aplica o rótulo traduzido `CLOSE` e restaura o foco para `LogButton` (ou para o controle anterior) ao fechar.
  - `CardZoneDialog` preserva o estado local de busca/ordenação/rolagem ao reabrir a mesma pilha, abre inspeção detalhada aninhada ao clicar em uma carta da coleção e devolve o foco ao botão `Pile_*` correspondente ao fechar.
- **Hierarquia de camadas e prioridade de `Escape` (`ui_cancel`):**
  - `PauseCanvas` (`layer = 10`) permanece acima de `FloatingCombatHUD` (`layer = 5`) e do gameplay (`layer = 0`, incluindo cartas elevadas com `z_index = 100`), e `Notifications/Toast` opera na camada `20` no topo central sem deslocar controles.
  - Prioridade confirmada para `Escape`: fecha primeiro modais de leitura abertos (`InspectionDialog`, `CardZoneDialog`, `CombatLog`), cancela seleção ativa de carta/alvo/pagamento inline (`selected_card` / `inline_selection`) ou fecha `CombatAttributesDrawer` antes de abrir `Pause`.

## Validação

Godot 4.7.2, cópia de QA isolada. Foram executados **32 scripts de validação na bateria principal, totalizando 2.024 verificações aprovadas**, todas com `exit_code = 0`, além das execuções renderizadas em português (`combat_mvp_preview`, 23 verificações) e de captura de overlays (`mvp_reference_overlays`, 84 verificações).

O novo teste `mvp_reference_overlays` cobre 84 verificações:
- hierarquia de camadas (`FloatingCombatHUD` na camada `5`, `PauseCanvas` na camada `10`, `Notifications` na camada `20`) e estado inicial fechado;
- `CombatAttributesDrawer`: abertura via `I`, bloqueio de hover na faixa vertical de `PileRail`, abertura por hover na borda esquerda acima de `PileRail`, fechamento via `Escape` antes de `Pause`, restauração de foco e invariância geométrica;
- `InspectionDialog`: abertura pelo botão `InspectButton` do cabeçalho e pelo atalho `V`, ativação de `hand.reading_modal`, bloqueio do atalho `I` enquanto aberto, enquadramento na viewport e restauração de foco para a carta inspecionada;
- `CardZoneDialog`: abertura a partir de `PileRail`, filtragem local por busca, preservação do estado de busca ao reabrir a mesma pilha, abertura e fechamento de detalhe aninhado de carta, e restauração de foco ao botão da pilha;
- `CombatLog`: abertura pelo `LogButton` do cabeçalho, rastreamento em `hand.reading_modal`, exibição de eventos registrados, texto traduzido do botão de fechar e restauração de foco ao `LogButton`;
- `CandidatePanel` e pagamento inline (`inline_selection`): posicionamento acima de `EnergyIndicator` e `HandHost` sem sobrepor `PileRail` ou `EndTurnAndContextActions`, destaque de cartas elegíveis para pagamento na mão, seleção de opção de custo e cancelamento com `Escape` antes de abrir `Pause`;
- `Reconnect` e `Toast`: exibição em sessão dessincronizada abaixo de `CombatHeader` sem cobrir `ActiveIdentity`, pilhas, mão ou fim de turno, recuperação da sessão ao clicar em `Reconnect` e aviso `Toast` na camada `20`;
- verificação multi-resolução (`1920×1080`, `1280×720`, `1280×800` e `2560×1080`) com overlays simultâneos e superioridade do `PauseCanvas` sem mover nenhum elemento de gameplay.

Os 11 arquivos protegidos de carta, mão e movimento permanecem 100% inalterados.

Capturas renderizadas em `1920×1080`, `1280×800`, `2560×1080`, `0` cartas, `2` cartas e overlays ativos (`mvp-stage6-overlays-1920x1080.png`) foram geradas em `.runtime/mvp-reference-stage6/after/` (EN) e `.runtime/mvp-reference-stage6/pt-BR/` (PT-BR), com inspeção visual direta das imagens.

## Reprodução e próximo gate

Executar `tools/test-godot-ui.ps1 -Tests mvp_reference_overlays,pause_layers,combat_attributes_drawer,card_collections,inline_card_selection,card_payment,combat_mvp_preview,mvp_reference_energy_and_turn,mvp_reference_battlefield,mvp_reference_piles,mvp_reference_header,mvp_reference_base,editor_tool_scenes` para a bateria focada da etapa 6, acrescentando `-VisualTest mvp_reference_overlays -VisualOutputDirectory <pasta>` ou `-VisualTest combat_mvp_preview -VisualOutputDirectory <pasta>` para regerar capturas.

Aguardar aprovação visual do usuário sobre a integração das janelas e overlays antes de iniciar a Etapa 7 (Composição completa ajustada e certificada).
