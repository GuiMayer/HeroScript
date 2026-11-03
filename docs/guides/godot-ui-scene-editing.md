# Edição das cenas de interface da Godot

Revisão: 2026-11-03. Projeto: `examples/godot-engine-showcase`.

## Começar pelo catálogo

Abra `scenes/ui/workbenches/screen_catalog_workbench.tscn` (ou `scenes/ui/workbenches/ui_catalog_workbench.tscn` para componentes isolados) e execute F6. O catálogo de telas oferece 21 composições reais, incluindo combate, mapa, atividades, diálogo, replay, menus e laboratórios. O segundo seletor oferece fixtures `populated`, `empty`, `locked`, `loading`, `error`, `modal` e `long_text` (estresse de quebra de linha e paginação), conforme o componente. Esses estados são exemplos de apresentação com cópias defensivas (`.duplicate(true)`); não mutam recursos compartilhados nem iniciam conteúdo jogável.

O catálogo não configura controllers, inicia servidor, envia comandos ou grava preferências. Os shells dos laboratórios de mão e inscrições são carregados sem executar seus adapters; seus componentes recebem fixtures locais. O laboratório de loadout unificado mantém apenas seu draft visual. A cena de produção não recebe esses exemplos.

## Onde editar

Os caminhos abaixo são relativos ao projeto Godot. `scripts/ui/scene_registry.gd` concentra as rotas da aplicação, e `scripts/ui/gameplay_route_resolver.gd` resolve a rota ativa da run (`combat`, `dialogue`, `map`, `activity`).

| Tela | Cena de composição | Componentes compartilhados |
| --- | --- | --- |
| Shell e pausa | `main.tscn`, `scenes/ui/application_shell.tscn`, `scenes/ui/overlays/pause_menu.tscn` | hosts de cenário/HUD, confirmação, toast, histórico de conversa |
| Menu principal | `scenes/ui/screens/main_menu.tscn` | seletor de setting, `MenuVisualStyle` (`data/themes/ivory_menu_style.tres`) |
| Configurações | `scenes/ui/screens/settings.tscn` | páginas persistentes, linhas e controles de remapeamento |
| Loadout | `scenes/ui/screens/unified_loadout.tscn` | `components/loadout/loadout_page.tscn`, inscrições, deck, atributos, companions (`support_workbench.tscn`), resumos |
| Combate | `scenes/ui/screens/combat_screen.tscn` | `components/combat_view.tscn`, battlefield, mão, atores, dock, preview e pilhas |
| Mapa | `scenes/ui/screens/map_screen.tscn` | `components/map_view.tscn`, route, nós e inspector flutuante |
| Atividades/loja/recompensas | `scenes/ui/screens/activity_screen.tscn` | journey, ofertas de carta/itens, confirmação Before/After |
| Diálogo | `scenes/ui/screens/dialogue_screen.tscn`, `scenes/ui/dialogue_scene.tscn` | painel, retrato, paginação e item de escolha |
| Histórico | `scenes/ui/screens/run_history.tscn` | ItemList e controles nativos |
| Timeline | `scenes/ui/screens/timeline.tscn` | Tree, scrubber, inspector causal e histórico visual |
| Replay | `scenes/ui/screens/run_replay.tscn` | `components/run_replay_view.tscn`, historical beat/combat e feedback |
| Sandbox/codex | `scenes/ui/screens/sandbox.tscn`, `scenes/ui/screens/codex.tscn` | templates de modo/aba, editor e listas nativas |
| Laboratórios | `scenes/*_lab.tscn`, `scenes/ui/overlays/art_effect_lab.tscn` | os mesmos componentes e executores do jogo |

`scenes/run_loadout.tscn` é uma cena herdada do loadout canônico com a aba inicial de inscrições; não é uma segunda implementação.

Para editar uma face de carta, abra `scenes/ui/components/card_view.tscn`. Todas as mãos, ofertas, coleções, comparações e ghosts utilizam essa cena. Para atores, recursos e statuses, abra `actor_panel.tscn`, `resource_view.tscn` e `status_badge.tscn` na mesma pasta.

## Quem controla cada propriedade

| Propriedade | Autoridade |
| --- | --- |
| Hierarquia, anchors, flags, gaps e margens estáticas | `.tscn` da composição/componente |
| Cores semânticas, contraste e tipografia | Theme, VisualContext e perfis em `data/themes` |
| Dimensões globais de cartas e limiares responsivos | `CardVisualStyle` e `use_profile_metrics`; desligar essa opção permite experimentar métricas locais |
| Geometria responsiva de combate/mapa/menu | `CombatLayoutProfile` + `CombatLayoutMetrics`, `MapLayoutProfile` + `MapLayout`, `MenuVisualStyle` |
| Fan, hover, drag, hit testing e reorganização | `HandLayout`, `HandInteraction`, `HandCardReconciler` e `HandPointerCoordinator` sob `CardHandView` |
| Durações, trajetórias, pause e cancelamento | políticas de movimento capturadas, `CombatBeatOrchestrator` e executores compartilhados |
| Texto, disponibilidade, valores e conteúdo | `ViewModels` / `PresentationBoundary` e presenters publicados pela engine |
| Navegação, feedback, sessão e leitura local | `ScreenContext` (`NavigationPort`, `SessionPort`, `FeedbackPort`, `PlaybackPort`) |
| Comandos, consequências, persistência e progressão | engine REST, `GameSession` (`SessionSnapshotStore`, `SessionInspectionService`, `SessionReplayService`, `SessionCommandDispatcher`) e controllers |

Containers calculam a posição dos filhos: ajuste mínimo, flags, alinhamento e separações em vez de arrastar filhos gerenciados. Preserve os nomes usados por `get_node` e `%Nome`; renomear exige atualizar bindings e testes. Não serialize caches de contexto/tema de runtime na cena.

Quantidades variáveis continuam dinâmicas: cartas, atores, escolhas, slots, ofertas e nós instanciam os 25 templates canônicos documentados em [GODOT_SCENE_OWNERSHIP_CONTRACT.md](../architecture/GODOT_SCENE_OWNERSHIP_CONTRACT.md) e são reconciliados por identidade (`StateKeeper` / reconciliadores dedicados). Itens de Tree/ItemList usam a API nativa. Desenho de grafos, keylines e shaders permanecem procedurais em nodes próprios.

## Scripts, contexto de tela e validação

A view vincula nodes, preenche dados e emite intenções sem chamar métodos privados de componentes irmãos (`_`-prefixed). Adapters de tela operam através de `ScreenContext` (`scripts/ui/context/screen_context.gd`), podendo ser montados pelo router (`ScreenContext.from_router(main)`) ou isoladamente em testes/workbenches (`ScreenContext.isolated()`).

Após editar, use o runner isolado na raiz do repositório:

```powershell
./tools/test-godot-ui.ps1 -Tests screen_catalog_full,editable_ui_nodes,scene_preview_profiles,visual_authority_contract,screen_context_pilot,presentation_boundaries,ui_decoupling_baseline
```

Inclua os testes da área alterada: `card_hand_lab`, `card_draw_pose`, `pause_layers`, `editable_map_scenes`, `dialogue_reading`, `unified_loadout_autosave`, `replay_lifecycle` e outros enumerados no plano. Os testes online da certificação exigem API/store próprios; nunca aponte testes de escrita para os saves do jogador.

Evidências e limites: [progresso do desacoplamento](../plans/GODOT_UI_DECOUPLING_PROGRESS.md), [contrato de propriedade visual](../architecture/GODOT_SCENE_OWNERSHIP_CONTRACT.md), [inventário](../architecture/GODOT_UI_DECOUPLING_INVENTORY.md), [auditoria](../architecture/GODOT_EDITABLE_UI_SCENES_AUDIT.md) e [plano de desacoplamento](../plans/GODOT_UI_DECOUPLING_AND_EDITABILITY_PLAN.md).
