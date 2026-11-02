# Edição das cenas de interface da Godot

Revisão: 2026-11-02. Projeto: `examples/godot-engine-showcase`.

## Começar pelo catálogo

Abra `scenes/ui/workbenches/screen_catalog_workbench.tscn` e execute F6. O catálogo oferece 21 composições reais, incluindo combate, mapa, atividades, diálogo, replay, menus e laboratórios. O segundo seletor oferece fixtures cheias, vazias, bloqueadas, loading, erro e modal, conforme o componente. Esses estados são exemplos de apresentação; não são conteúdo jogável nem uma simulação de regras.

O catálogo não configura controllers, inicia servidor, envia comandos ou grava preferências. Os shells dos laboratórios de mão e inscrições são carregados sem executar seus adapters; seus componentes recebem fixtures locais. O laboratório de loadout unificado mantém apenas seu draft visual. A cena de produção não recebe esses exemplos.

## Onde editar

Os caminhos abaixo são relativos ao projeto Godot. `scripts/ui/scene_registry.gd` concentra as rotas da aplicação.

| Tela | Cena de composição | Componentes compartilhados |
| --- | --- | --- |
| Shell e pausa | `main.tscn`, `scenes/ui/application_shell.tscn`, `scenes/ui/overlays/pause_menu.tscn` | hosts de cenário/HUD, confirmação, toast, histórico de conversa |
| Menu principal | `scenes/ui/screens/main_menu.tscn` | seletor de setting, estilos do menu |
| Configurações | `scenes/ui/screens/settings.tscn` | páginas persistentes, linhas e controles de remapeamento |
| Loadout | `scenes/ui/screens/unified_loadout.tscn` | `components/loadout/loadout_page.tscn`, inscrições, deck, atributos, resumos |
| Combate | `scenes/ui/screens/combat_screen.tscn` | `components/combat_view.tscn`, battlefield, mão, atores, dock, preview e pilhas |
| Mapa | `scenes/ui/screens/map_screen.tscn` | `components/map_view.tscn`, route, nós e inspector flutuante |
| Atividades/loja/recompensas | `scenes/ui/screens/activity_screen.tscn` | journey, ofertas de carta/itens, confirmação Before/After |
| Diálogo | `scenes/ui/screens/dialogue_screen.tscn`, `scenes/ui/dialogue_scene.tscn` | painel, retrato, paginação e item de escolha |
| Histórico | `scenes/ui/screens/run_history.tscn` | ItemList e controles nativos |
| Timeline | `scenes/ui/screens/timeline.tscn` | Tree, scrubber, inspector causal e histórico visual |
| Replay | `scenes/ui/screens/run_replay.tscn` | `components/run_replay_view.tscn`, historical beat/combat e feedback |
| Sandbox/codex | `scenes/ui/screens/sandbox.tscn`, `scenes/ui/screens/codex.tscn` | templates de modo/aba, editor e listas nativas |
| Laboratórios | `scenes/*_lab.tscn`, `scenes/ui/overlays/art_effect_lab.tscn` | os mesmos componentes e executores do jogo |

`scenes/run_loadout.tscn` é uma cena herdada do loadout canônico com a aba inicial de inscrições; não é uma segunda implementação. Companions permanece um placeholder.

Para editar uma face de carta, abra `scenes/ui/components/card_view.tscn`. Todas as mãos, ofertas, coleções, comparações e ghosts utilizam essa cena. Para atores, recursos e statuses, abra `actor_panel.tscn`, `resource_view.tscn` e `status_badge.tscn` na mesma pasta.

## Quem controla cada propriedade

| Propriedade | Autoridade |
| --- | --- |
| Hierarquia, anchors, flags, gaps e margens estáticas | `.tscn` da composição/componente |
| Cores semânticas, contraste e tipografia | Theme, VisualContext e perfis em `data/themes` |
| Dimensões globais de cartas | CardVisualStyle e `use_profile_metrics`; desligar essa opção permite experimentar métricas locais |
| Geometria responsiva de combate/mapa | CombatLayoutProfile/MapLayoutProfile e seus algoritmos existentes |
| Fan, hover, drag, hit testing e reorganização | HandLayout/HandInteraction e executores existentes |
| Durações, trajetórias, pause e cancelamento | políticas de movimento capturadas e executores compartilhados |
| Texto, disponibilidade, valores e conteúdo | presenter/read model publicado pela engine |
| Comandos, consequências, persistência e progressão | engine REST e controllers existentes |

Containers calculam a posição dos filhos: ajuste mínimo, flags, alinhamento e separações em vez de arrastar filhos gerenciados. Preserve os nomes usados por `get_node` e `%Nome`; renomear exige atualizar bindings e testes. Não serialize caches de contexto/tema de runtime na cena.

Quantidades variáveis continuam dinâmicas: cartas, atores, escolhas, slots, ofertas e nós instanciam templates e são reconciliados por identidade. Itens de Tree/ItemList usam a API nativa. Desenho de grafos, keylines e shaders permanecem procedurais em nodes próprios.

## Scripts e validação

A view vincula nodes, preenche dados e emite intenções. Adapters de aplicação continuam recebendo sessão/controller pelo router; para preview sem sessão, use o catálogo ou componentes passivos. Um script de preview não deve chamar `setup` de um adapter que inicia leituras ou comandos.

Após editar, use o runner isolado na raiz do repositório:

```powershell
./tools/test-godot-ui.ps1 -Tests screen_catalog_full,editable_ui_nodes,scene_preview_profiles
```

Inclua os testes da área alterada: `card_hand_lab`, `card_draw_pose`, `pause_layers`, `editable_map_scenes`, `dialogue_reading`, `unified_loadout_autosave`, `replay_lifecycle` e outros enumerados no plano. Os testes online da certificação exigem API/store próprios; nunca aponte testes de escrita para os saves do jogador.

Evidências e limites: [progresso da migração](../plans/GODOT_SCREEN_SCENE_CENTRALIZATION_PROGRESS.md), [auditoria](../architecture/GODOT_EDITABLE_UI_SCENES_AUDIT.md) e [plano completo](../plans/GODOT_SCREEN_SCENE_CENTRALIZATION_PLAN.md).
