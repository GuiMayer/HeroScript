# Plano de Melhoria de Usabilidade e Interface da Godot (`examples/godot-engine-showcase`)

Data: 2026-11-03  
Escopo: `examples/godot-engine-showcase` (cenas `.tscn`, componentes visuais, temas e adaptadores de apresentação)  
Pré-requisito concluído: [GODOT_UI_DECOUPLING_AND_EDITABILITY_PLAN.md](GODOT_UI_DECOUPLING_AND_EDITABILITY_PLAN.md) (Etapas 0–12)

---

## 1. Diagnóstico das Cenas Atuais

A auditoria direta das cenas em `scenes/ui/` e seus scripts correspondentes em `scripts/ui/` revelou que a arquitetura agora está limpa e desacoplada, mas a experiência de uso e a interface apresentam pontos concretos de fricção:

### 1.1. Navegação Global, Foco, Atalhos e Feedback (`application_shell.tscn`, `toast.tscn`, `pause_menu.tscn`)
- **Indicadores de foco invisíveis em botões de barra:** Em `scenes/ui/components/combat_header.tscn` e `scenes/ui/components/turn_controls.tscn`, os `StyleBoxFlat` atribuídos ao estado `focus` têm `bg_color = Color(1, 1, 1, 0)` e `border_color = Color(1, 1, 1, 0)`, tornando invisível o foco de teclado/gamepad nos controles de turno, timeline, ferramentas e pausa.
- **Ausência de `ui_cancel` (`Esc`) nas telas secundárias:** Enquanto `unified_loadout_screen.gd` trata `ui_cancel` para voltar, `codex_screen.gd`, `sandbox_screen.gd`, `run_history_screen.gd`, `timeline_screen.gd` e `run_replay_screen.gd` não respondem a `Esc`, exigindo clique manual no botão do cabeçalho.
- **Foco inicial e restauração pós-modal:** Apenas `main_menu.tscn` define `metadata/initial_focus = true`. Ao abrir `codex.tscn`, `run_history.tscn`, `timeline.tscn`, `map_view.tscn` ou fechar diálogos (`attribute_sources.tscn`, `confirmation_dialog.tscn`, `hand_overview_dialog.tscn`), o foco do teclado se perde.
- **Limitações do `toast.tscn`:** O nó `Toast` (`scenes/ui/overlays/toast.tscn`) é um `Label` estático (`520x52px`, sem `autowrap_mode`, sem distinção estrutural entre informação, aviso e erro além de cor) que pode truncar mensagens longas ou em escala `1.6x`.

### 1.2. Combate e Mão de Cartas (`combat_view.tscn`, `combat_header.tscn`, `turn_controls.tscn`, `actor_panel.tscn`, `action_preview.tscn`, `combat_attributes_drawer.tscn`)
- **Área de clique restrita nos atores (`actor_panel.tscn`):** O botão `ActorTarget` (`%ActorTarget`) cobre apenas a metade inferior do painel (a arte do ator, `min_size = Vector2(0, 136)`). Clicar no nome, intenção ou barras de vida do inimigo não seleciona o alvo quando uma carta está aguardando seleção.
- **Baixa proeminência do estado de mira/seleção (`action_preview.tscn` e `Hint`):** Quando o jogador seleciona uma carta que exige alvo ou escolha em `CandidatePanel`, a instrução em `ActionPreviewView` (`normal_font_size = 14`) e `HandSection/Hint` não destaca visualmente o modo de seleção nem informa como cancelar (`Esc` / clique direito).
- **Botões de cabeçalho sem legenda/atalho (`combat_header.tscn`):** `TimelineButton`, `ToolsButton` e `PauseButton` são ícones puros `44x44` sem indicação de tecla de atalho no tooltip e sem rótulo opcional em resoluções largas (`>= 1920px`).
- **Alvo de toque/clique estreito no drawer (`combat_attributes_drawer.tscn`):** O botão `CharacterSidebarToggle` (`×`) possui `custom_minimum_size = Vector2(36, 48)`, abaixo do padrão mínimo de `44x48px` usado no restante da interface.

### 1.3. Mapa e Inspector de Rota (`map_view.tscn`, `map_inspector.tscn`, `route_view.tscn`)
- **Hierarquia tipográfica plana no `map_inspector.tscn`:** Os nós `Kind`, `Risk`, `Benefit`, `Description` e `Status` dentro de `Margin/Stack/Scroll/Body/Information` são cinco `Label`s consecutivos com variação `Caption` (`15px`–`17px`), dificultando a leitura rápida de **Tipo de Encontro**, **Risco** e **Recompensa** antes de viajar.
- **Falta de controle de centralização e legenda no `map_view.tscn`:** Quando o jogador arrasta o `MapScroll` em rotas grandes, não há botão no `Header` (nem tecla `Home`/`C`) para recentralizar no nó atual do jogador, e o botão `MapInspectorToggle` (`›`) não possui `tooltip_text` nem `accessibility_name`.

### 1.4. Atividades, Recompensas e Diálogo (`journey_view.tscn`, `activity_header.tscn`, `activity_options.tscn`, `dialogue_scene.tscn`, `dialogue_panel.tscn`)
- **Botão técnico no header principal (`activity_header.tscn`):** O botão `Verify` (`VERIFY REPLAY`, `185x48px`) ocupa espaço nobre ao lado de `MAP`, `LOADOUT` e `PAUSE` em todas as telas de atividade/loja/recompensa, competindo com a navegação principal.
- **Proporção vertical e atalhos no diálogo (`dialogue_panel.tscn`):** A seção `Stage` (`Portrait` `160x150` + `Title`) usa `size_flags_vertical = 3` (expand), comprimindo a narrativa (`SpeechPanel`) e a lista de escolhas (`ChoiceScroll`, `min_size = Vector2(0, 150)`) em `1280x720` ou escala de texto `1.6x`. Além disso, as escolhas de diálogo e o botão `DialogueReadNext` não exibem dicas de atalho numérico (`[1]`, `[2]`... / `[Espaço]`).

### 1.5. Loadout Unificado (`loadout_page.tscn`, `inscriptions_editorial.tscn`, `deck_module.tscn`, `support_workbench.tscn`)
- **Falta de indicadores de resumo nas abas (`loadout_page.tscn` e `deck_module.tscn`):** Os botões de módulo (`Overview`, `Inscriptions`, `Deck`, `Relics`, `Companions`) e as sub-abas de deck (`Cards`, `Compartments`, `Card modifiers`, `Build`) não informam contagens ou capacidade (ex.: `Inscriptions (6/12)`, `Deck (15)`).
- **Filtros sem ação de limpeza e estado vazio (`deck_module.tscn` e `inscriptions_editorial.tscn`):** `Body/CardBody/Tools` (`Search`, `Tags`, `Sort`, `Resources`) não tem botão `"Limpar filtros"` e não exibe estado vazio orientativo quando nenhuma carta corresponde à busca.
- **Visibilidade de salvamento automático (`loadout_page.tscn`):** O rodapé (`Footer/Feedback`) exibe mensagens de validação/salvamento apenas como `Caption` discreto, sem badge de estado (`Sincronizado` / `Salvando…` / `Atenção`).

### 1.6. Telas de Inspeção, Histórico, Sandbox e Codex (`codex.tscn`, `sandbox.tscn`, `run_history.tscn`, `timeline.tscn`, `run_replay.tscn`, `settings.tscn`)
- **Codex exibe apenas JSON cru (`codex.tscn` / `codex_screen.gd`):** A tela `ENGINE CODEX` renderiza `JSON.stringify(current_items[index], "  ")` em um `TextEdit`, não possui barra de busca (`LineEdit`) para filtrar itens da categoria, não marca visualmente a aba ativa (`button_pressed`) e não aproveita `CardView` ou um resumo estruturado para cartas/entidades/relíquias.
- **Hierarquia de ações em `run_history.tscn`:** Os botões `WatchReplayButton`, `VerifyButton` e `ContinueButton` têm todos o mesmo estilo (`SecondaryAction`) empilhados verticalmente, sem destacar a ação principal (`CONTINUE JOURNEY` para runs ativas ou `WATCH REPLAY` para runs concluídas).
- **Legibilidade da Timeline (`timeline.tscn`):** A lista de comandos exibe o identificador técnico cru (`PLAY_CARD`, `START_ENCOUNTER`) sem tradução amigável, não há botão `PREVIOUS FRAME` ao lado de `NEXT FRAME`, e os controles de branch/simulação ficam misturados aos de reprodução.

---

## 2. Princípios e Restrições do Plano

1. **Preservação dos Contratos Arquiteturais:** Manter rigorosamente a Matriz de Autoridade Visual ([GODOT_SCENE_OWNERSHIP_CONTRACT.md](../architecture/GODOT_SCENE_OWNERSHIP_CONTRACT.md)), as portas de `ScreenContext`, os validadores de `PresentationBoundary` e a autoridade exclusiva da engine REST sobre regras e estado.
2. **Edição Nativa na Godot:** Toda alteração estrutural de layout, margens, alinhamento, `focus_neighbor_*` e estilos estáticos deve ser feita nas cenas `.tscn` e recursos `.tres` (`data/themes/`), mantendo compatibilidade com os catálogos `screen_catalog_workbench.tscn` e `ui_catalog_workbench.tscn`.
3. **Validação Incremental:** Cada etapa atualiza os baselines geométricos quando o layout intencionalmente muda e executa as suítes correspondentes em `tools/test-godot-ui.ps1`.

---

## 3. Etapas de Implementação

### Etapa 1 — Acessibilidade de Foco, Atalhos Globais (`Esc`) e Sistema de Toast
**Objetivo:** Garantir navegação completa por teclado/gamepad em todas as telas e melhorar o feedback visual de notificações.

- **Arquivos a modificar:**
  - `scenes/ui/components/combat_header.tscn` e `scenes/ui/components/turn_controls.tscn`
  - `scenes/ui/overlays/toast.tscn` e `scripts/ui/shell/application_shell.gd`
  - `scripts/ui/codex_screen.gd`, `scripts/ui/sandbox_screen.gd`, `scripts/ui/run_history_screen.gd`, `scripts/ui/timeline_screen.gd`, `scripts/ui/run_replay_screen.gd`
- **Ações:**
  1. Corrigir os `StyleBoxFlat` de `focus` em `combat_header.tscn` e `turn_controls.tscn` para exibir anel de foco visível (`border_width = 2`, papel semântico `stroke.focus` / `accent.primary`).
  2. Adicionar tratamento consistente de `ui_cancel` (`Esc`) em `codex_screen.gd`, `sandbox_screen.gd`, `run_history_screen.gd`, `timeline_screen.gd` e `run_replay_screen.gd` para acionar a ação de retorno da tela quando nenhum modal estiver aberto.
  3. Definir foco inicial determinístico ao montar cada tela e restaurar o `Viewport.gui_get_focus_owner()` anterior ao fechar diálogos modais (`attribute_sources`, `confirmation_dialog`, `conversation_history`, `hand_overview_dialog`).
  4. Evoluir `toast.tscn` para um `PanelContainer` auto-ajustável com `autowrap_mode = 3`, largura máxima responsiva e distinção visual clara entre informativo e erro.
- **Validação:** `pause_layers`, `settings_interaction`, `screen_scene_registry`, `visual_authority_contract`.

---

### Etapa 2 — Usabilidade e Clareza Tática no Combate (`CombatView`, `ActorPanel`, `ActionPreview`)
**Objetivo:** Tornar imediata a leitura de intenções, seleção de alvos, dicas de ação e atalhos durante o combate.

- **Arquivos a modificar:**
  - `scenes/ui/components/actor_panel.tscn` e `scripts/ui/actor_panel.gd`
  - `scenes/ui/components/action_preview.tscn` e `scripts/ui/components/action_preview_view.gd`
  - `scenes/ui/components/combat_header.tscn` e `scripts/ui/components/combat_header_view.gd`
  - `scenes/ui/components/combat/combat_attributes_drawer.tscn`
- **Ações:**
  1. Expandir a área interativa de seleção de alvo em `actor_panel.tscn` (permitindo clicar em todo o corpo do cartão do ator quando `ActorTarget` estiver habilitado como alvo válido, preservando os tooltips dos badges de status).
  2. Destacar visualmente `ActionPreviewView` quando houver uma carta/ação selecionada aguardando alvo ou confirmação, exibindo dica explícita de cancelamento (`[Esc / Clique direito para cancelar]`).
  3. Enriquecer os tooltips de `TimelineButton`, `ToolsButton` e `PauseButton` em `combat_header.tscn` e dos botões de `turn_controls.tscn` com os respectivos atalhos mapeados em `Preferences`.
  4. Ajustar `CharacterSidebarToggle` em `combat_attributes_drawer.tscn` para `custom_minimum_size = Vector2(44, 48)` com `tooltip_text` e `accessibility_name`.
- **Validação:** `combat_controller`, `combat_collaborators`, `combat_input_soak`, `combat_attributes_drawer`, `hand_ux`, `hand_selection`.

---

### Etapa 3 — Navegação Espacial e Leitura Rápida no Mapa (`MapView`, `MapFloatingInspector`)
**Objetivo:** Facilitar a tomada de decisão entre rotas e o controle de câmera em mapas extensos.

- **Arquivos a modificar:**
  - `scenes/ui/components/map_view.tscn` e `scripts/ui/map_view.gd`
  - `scenes/ui/components/map_inspector.tscn`
- **Ações:**
  1. Reestruturar `map_inspector.tscn` separando visualmente:
     - Cabeçalho do nó (`Title` + badge de `Kind` e `Status`),
     - Bloco estruturado de **Risco** e **Benefício** com cores semânticas (`accent.warning` / `accent.positive`),
     - `Description` com espaçamento confortável e `Actions` fixadas na base do inspector (`Margin/Stack`) fora da rolagem secundária para que o botão de viajar esteja sempre visível.
  2. Adicionar botão `"Centralizar"` (`Center on current node`) no `Header` de `map_view.tscn` ao lado de `UnifiedLoadoutButton` e adicionar tooltip/acessibilidade ao `MapInspectorToggle`.
- **Validação:** `map_selection`, `map_geometry`, `map_graph_view`, `map_camera_motion`, `editable_map_scenes`.

---

### Etapa 4 — Hierarquia de Atividades e Ergonomia de Diálogo (`JourneyView`, `DialoguePanel`)
**Objetivo:** Limpar distrações nas telas de atividade/recompensa e otimizar o espaço de leitura e escolha nos diálogos.

- **Arquivos a modificar:**
  - `scenes/ui/components/activity_header.tscn` e `scripts/ui/journey_view.gd`
  - `scenes/ui/components/activity_options.tscn` e `scripts/ui/components/activity_options_view.gd`
  - `scenes/ui/dialogue_scene.tscn`, `scenes/ui/components/dialogue_panel.tscn`, `scripts/ui/dialogue_panel.gd`
- **Ações:**
  1. Em `activity_header.tscn`, tornar `Verify` secundário/compacto (ou visível apenas quando o perfil de ferramentas permitir inspeção) para priorizar `MAP`, `LOADOUT` e `PAUSE`.
  2. Em `dialogue_panel.tscn`, remover `size_flags_vertical = 3` do `Stage` (fixando altura proporcional ao retrato e ocultando/compactando quando não houver arte específica) para dar prioridade vertical ao `SpeechPanel` e ao `ChoiceScroll` em `1280x720` e escala `1.6x`.
  3. Adicionar suporte a atalhos numéricos (`1`–`9`) para escolhas de diálogo visíveis e indicação clara de paginação (`1/N · Espaço para avançar`) em `DialogueReadNext`.
- **Validação:** `activity_reconciliation`, `activity_ingress`, `activity_map_completion`, `dialogue_contracts`, `dialogue_coordination`, `dialogue_reading`.

---

### Etapa 5 — Descoberta, Filtros e Feedback no Loadout Unificado (`LoadoutPage`, `DeckModule`, `Inscriptions`)
**Objetivo:** Dar visibilidade imediata ao estado da build, facilitar a filtragem de cartas/inscrições e explicitar o status de autosave.

- **Arquivos a modificar:**
  - `scenes/ui/components/loadout/loadout_page.tscn` e `scripts/ui/loadout/loadout_page_view.gd`
  - `scenes/ui/components/loadout/deck_module.tscn` e `scripts/ui/loadout/deck_module_view.gd`
  - `scenes/ui/components/loadout/inscriptions_editorial.tscn` e `scripts/ui/loadout/loadout_view.gd`
  - `scripts/ui/unified_loadout_screen.gd`
- **Ações:**
  1. Exibir contadores contextuais nos rótulos das abas em `LoadoutPage` (ex.: `Inscriptions (2/4)`, `Deck (12)`, `Relics (3)`) e nas sub-abas de `DeckModule`.
  2. Adicionar botão `"Limpar filtros"` (`ClearFilters`) em `deck_module.tscn` (`Body/CardBody/Tools`) e mensagem de estado vazio amigável quando a busca/filtro não encontrar cartas ou inscrições.
  3. Aprimorar o `Footer/Feedback` de `loadout_page.tscn` para diferenciar visualmente os estados `Sincronizado`, `Validando/Salvando…` e `Erro/Recuperação pendente`.
- **Validação:** `unified_loadout_shell`, `unified_loadout_layout`, `unified_loadout_autosave`, `unified_loadout_recovery`, `loadout_editorial`, `loadout_input`, `support_loadout`.

---

### Etapa 6 — Inspeção Formatada no Codex, Histórico, Timeline e Replay (`codex.tscn`, `run_history.tscn`, `timeline.tscn`, `run_replay.tscn`)
**Objetivo:** Transformar o Codex e os inspetores de histórico/timeline de visualizadores técnicos crus em telas legíveis para jogadores e criadores de mods.

- **Arquivos a modificar:**
  - `scenes/ui/screens/codex.tscn` e `scripts/ui/codex_screen.gd`
  - `scenes/ui/screens/run_history.tscn` e `scripts/ui/run_history_screen.gd`
  - `scenes/ui/screens/timeline.tscn` e `scripts/ui/timeline_screen.gd`
  - `scenes/ui/components/run_replay_view.tscn`
- **Ações:**
  1. **Codex (`codex.tscn`):**
     - Adicionar campo de busca (`Search` `LineEdit`) acima de `Columns/List` e contador de itens encontrados.
     - Marcar a aba ativa (`toggle_mode = true`, `button_pressed = true`) em `TabsScroll/Tabs`.
     - Substituir a exibição exclusivamente em JSON puro por um painel de detalhes formatado (`RichTextLabel` + preview de `CardView` quando a categoria for `cards`), mantendo um botão toggle `"Mostrar JSON bruto"` para modders.
  2. **Histórico de Jornadas (`run_history.tscn`):**
     - Organizar os botões de ação em uma barra horizontal (`HFlowContainer`) e aplicar `PrimaryAction` à ação principal (`CONTINUE JOURNEY` quando ativa, `WATCH REPLAY` quando concluída).
  3. **Timeline (`timeline.tscn`):**
     - Formatar os itens da lista com nomes de ação traduzidos e legíveis (`#003 • Turno 1 • Jogar Carta`), adicionar botão `PREVIOUS FRAME` ao lado de `NEXT FRAME` e agrupar visualmente os controles de reprodução vs controles de branch/simulação.
- **Validação:** `screen_context_pilot`, `replay_lifecycle`, `replay_editable`, `historical_presentation`, `tools_editable`, `screen_catalog_full`, `ui_decoupling_baseline`.

---

## 4. Matriz de Priorização Recomendada

| Prioridade | Etapa | Impacto na Usabilidade | Risco Técnico |
| --- | --- | --- | --- |
| **P0 (Alta)** | **Etapa 1** (Foco, `Esc` global e Toast) | Elimina becos sem saída no teclado/gamepad e melhora feedback global | Baixo |
| **P0 (Alta)** | **Etapa 2** (Combate: alvo em todo o ActorPanel, preview de ação e atalhos) | Melhora diretamente o loop principal de gameplay tático | Baixo/Médio |
| **P1 (Média)** | **Etapa 3** (Mapa: hierarquia do inspector e botão centralizar) | Facilita leitura de risco/recompensa e navegação no mapa | Baixo |
| **P1 (Média)** | **Etapa 5** (Loadout: contadores nas abas, limpar filtros e status de save) | Reduz carga cognitiva ao montar deck e inscrições | Baixo |
| **P2 (Normal)** | **Etapa 4** (Atividades e Diálogo: proporção vertical e atalhos `1`–`9`) | Melhora leitura em `1280x720` / escala `1.6x` e fluidez narrativa | Baixo |
| **P2 (Normal)** | **Etapa 6** (Codex formatado com busca + melhorias em Histórico/Timeline) | Transforma telas utilitárias/JSON em interfaces completas para jogadores | Baixo |
