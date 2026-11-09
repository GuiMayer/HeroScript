# Composição MVP — etapa 4: campo de batalha e arte separada das informações

Data: 2026-11-09. **Etapas 1, 2 e 3 aprovadas. Etapa 4 implementada, aguardando aprovação visual. Etapas 5–7 não iniciadas.**

Guia: [plano de composição](../plans/MVP_COMBAT_REFERENCE_HUD_REBUILD_PLAN.md). Evidências: [testes, arquivos protegidos e capturas](mvp-reference-stage4.json).

## Entrega visual

O campo de batalha da variante MVP (`combat_mvp_view.tscn` e `battlefield_actor.tscn`) agora separa a área de arte livre de cada unidade da placa compacta de informações posicionada abaixo dela, mantendo aliados à esquerda (`Allies`) e oponentes à direita (`Opponents`) na arena central à direita de `PileRail`:

- **Separação entre arte livre e placa compacta (`battlefield_actor.tscn`):**
  - `%ActorTarget` (contendo `%ActorArtworkAnchor` e servindo de âncora para o retrato passivo em `CombatSceneryView`) ocupa o topo de `%ActorContent` sem fundo de cartão envolvendo a unidade inteira (`Surface` transparente).
  - `%ActorPlate` (`PanelContainer` abaixo de `%ActorTarget`) agrupa `%ActorTitle`, `%ActorResources`, `%ActorCounters`, `%ActorStatusBadges` e `%ActorIntentRow` dentro de uma placa compacta com superfície `surface.actor` e borda `stroke.panel` do tema atual, sem alterar a paleta ou criar nova skin.
  - A largura de `%ActorTarget` (`custom_minimum_size.x = 184`) e a largura de `%ActorPlate` (`custom_minimum_size.x = 228`) são independentes e editáveis diretamente no Inspector.
- **Posicionamento da arena (`combat_mvp_view.tscn` e `combat_mvp_view.gd`):**
  - `BattlefieldActors` fica delimitado à direita da coluna de pilhas (`PileRail`), abaixo de `CombatHeader` e acima das cartas da mão (`HandHost`), evitando sobreposição entre atores e pilhas laterais ou cartas elevadas em `1920×1080`, `1280×720`, `1280×800` e `2560×1080`.
  - `Allies` (lado esquerdo do campo) e `Opponents` (lado direito do campo) mantêm separação clara entre equipes e acomodam quantidades variáveis de unidades (herói solo, herói + aliado real, 1/2/4 oponentes).
- **Preservação de contratos e honestidade de dados (`actor_panel.gd`):**
  - Lookups (`resource_view()`, `resource_counter()`, `status_badge()`, `find_child("Resource_*")`), sinais de alvo (`target_selected`, `target_previewed`, `preview_left`), highlights de hover/foco/seleção, `present_resource()` e `scenery.bind_anchors(stage.targets)` permanecem intactos.
  - Quando apenas metadados de suporte (`run.supportCombat`) existem na run, nenhuma unidade ou barra de vida fictícia de companion é criada no campo; aliados no campo só aparecem quando publicados em `combat.actors`.
  - Aliados sem intenção publicada ocultam `%ActorIntentRow` na placa compacta (em vez de exibir preenchimento redundante `"YOUR SIDE"`), enquanto oponentes sem intenção revelada exibem honestamente `"No intent revealed"` sem inventar ação futura.
  - A cena genérica `actor_panel.tscn` e os demais settings fora do MVP permanecem inalterados.

## Validação

Godot 4.7.2, cópia de QA isolada. Foram executados **30 scripts de validação (26 na bateria principal + 4 suítes focadas de autoridade/editabilidade de ator), totalizando 1.818 verificações aprovadas**, todas com `exit_code = 0`, além de uma execução adicional de captura em português com mais 23 verificações aprovadas.

O novo teste `mvp_reference_battlefield` cobre 399 verificações:
- hierarquia estática em `battlefield_actor.tscn` (`%ActorTarget` livre acima de `%ActorPlate`, fundo externo transparente, superfície/borda do tema na placa e larguras independentes entre arte e placa);
- posicionamento do campo à direita de `PileRail`, abaixo de `CombatHeader` e acima das cartas em `HandHost`, com separação esquerda/direita entre `Allies` e `Opponents` e sem barra de rolagem vertical em `1920×1080`, `1280×720`, `1280×800` e `2560×1080`, nos idiomas `en` e `pt_BR` e escalas de texto `1.0` e `1.6`;
- quantidades variáveis de atores: herói solo vs. 1 oponente (`solo_vs_1`), herói + aliado real (`Water Elemental`) vs. 2 oponentes (`ally_vs_2`), e herói solo vs. 4 oponentes (`solo_vs_4`);
- honestidade de dados: ausência de companion fictício quando só existe `supportCombat`, ocultação de `%ActorIntentRow` para aliados sem intenção e estado honesto `"No intent revealed"` para inimigos sem intenção publicada;
- truncamento limpo com reticências e `tooltip_text` completo para nomes longos de atores;
- atualização de dano e cura em `ResourceView`, ciclo de vida de `StatusBadge` (criação e remoção) e adição/remoção de ator no snapshot;
- integração em `CombatScreen` com sincronização de retratos em `CombatSceneryView`, habilitação seletiva de alvos legais, preview de aplicação no hover e seleção de alvo sem mutação de estado da engine.

Os 11 arquivos protegidos de carta, mão e movimento permanecem 100% inalterados (hashes SHA-256 idênticos aos das etapas 1, 2 e 3).

Capturas renderizadas em `1920×1080`, `1280×800` e `2560×1080` foram geradas em `.runtime/mvp-reference-stage4/after/` (EN) e `.runtime/mvp-reference-stage4/pt-BR/` (PT-BR), com inspeção visual direta das imagens.

## Reprodução e próximo gate

Executar `tools/test-godot-ui.ps1 -Tests mvp_reference_battlefield,mvp_reference_piles,mvp_reference_header,mvp_reference_base,ui_editability,actor_panel_polish,editable_battlefield_hosts,combat_mvp_layout` para a bateria focada da etapa 4, acrescentando `-VisualTest combat_mvp_preview -VisualOutputDirectory <pasta>` para regerar capturas.

Aguardar aprovação visual do usuário sobre o campo de batalha e as placas compactas antes de iniciar a Etapa 5 (Energia central e Encerrar turno no canto inferior direito).
