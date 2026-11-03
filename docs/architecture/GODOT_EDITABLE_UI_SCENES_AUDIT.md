# UI editável da Godot: arquitetura e estado atual

Revisão: 2026-11-03. Projeto: `examples/godot-engine-showcase`.

## Resultado

As etapas 0–20 do plano de centralização de cenas e as etapas 0–12 do [plano de desacoplamento e editabilidade](../plans/GODOT_UI_DECOUPLING_AND_EDITABILITY_PLAN.md) estabeleceram uma fronteira explícita de autoridade visual, contexto de tela (`ScreenContext`), contratos de apresentação (`ViewModels` / `PresentationBoundary`) e colaboradores de responsabilidade única para combate, mapa, mão e sessão. A certificação final aprovou 75 suítes offline completas, 5 suítes de integração REST online (524 verificações REST em API/stores isolados) e 74 verificações geométricas e de movimento contra baseline determinístico (`baseline.before.json` vs `baseline.after.json`). O [registro de desacoplamento](../plans/GODOT_UI_DECOUPLING_PROGRESS.md) e o [registro de centralização](../plans/GODOT_SCREEN_SCENE_CENTRALIZATION_PROGRESS.md) detalham commits, regressões e limites; o [guia de edição](../guides/godot-ui-scene-editing.md) indica os arquivos de cada tela.

A cena possui a árvore estática. Scripts vinculam nodes, preenchem modelos, conectam sinais e instanciam templates para coleções variáveis. Controllers, presenters, workspaces, gateways e políticas de animação permanecem nos papéis existentes. Não foram alteradas regras, endpoints, conteúdo de gameplay, durações ou autoridade dos dados.

## Composição canônica

| Área | Implementação atual |
| --- | --- |
| Entrada/router | `main.tscn`, `scripts/ui/scene_registry.gd`, `scripts/ui/gameplay_route_resolver.gd` e `scripts/ui/context/screen_context.gd` |
| Contexto/portas | `NavigationPort`, `SessionPort`, `FeedbackPort` e `PlaybackPort` em `scripts/ui/context/`; todas as telas operam com ou sem `main` |
| Fronteira de modelos | `scripts/presentation/view_models.gd` e `scripts/contracts/presentation_boundary.gd` (`StateKeeper` para reconciliação sem resetar foco/scroll) |
| Shell/pause | ApplicationShell, pause, toast, confirmações e histórico de conversa autorados |
| Cartas/atores | CardView, ActorPanel, ResourceView e StatusBadge compartilhados; métricas responsivas em `CardVisualStyle` e `ActorPanel` |
| Menus | MainMenuView (`MenuVisualStyle`) e Settings completos, incluindo páginas persistentes e controles de remapeamento |
| Loadout | Shell, inscrições, slots, inventário, deck, compartimentos, companions (`support_workbench.tscn`), fontes de atributos e resumos autorados |
| Combate | `CombatView`, `CombatLayoutProfile` + `CombatLayoutMetrics`, `CombatBeatOrchestrator` e lease de cenário via `NavigationPort` |
| Mão de cartas | `CardHandView` apoiado por `HandLayout`, `HandInteraction`, `HandCardReconciler` e `HandPointerCoordinator` |
| Mapa | `MapView`, `RouteView`, `MapLayoutProfile` + `MapLayout`, nós, ações e inspector flutuante |
| Sessão | `GameSession` apoiado por `SessionSnapshotStore`, `SessionInspectionService`, `SessionReplayService` e `SessionCommandDispatcher` |
| Atividades | Header, resumo, encerramento, ofertas de carta/itens, opções e confirmação Before/After sem acesso a privados entre componentes |
| Diálogo | Tela, retrato, texto, paginação, recuperação e escolhas reconciliadas por identidade; estado de leitura gerenciado com LRU em `NavigationPort` |
| Histórico/timeline/replay | Composições completas, listas/árvore/scrubber nativos e inspectores históricos/causais compartilhados |
| Sandbox/codex | Shells, editor JSON, seletores e templates de modos/abas; operações continuam REST |
| Laboratórios/workbenches | Mão, inscrições, loadout, compartimentos, eventos, efeitos e catálogos (`populated`, `empty`, `locked`, `loading`, `error`, `modal`, `long_text`) |

`scenes/run_loadout.tscn` herda a cena unificada e apenas seleciona a aba inicial de inscrições. O antigo script de tela de loadout foi removido. O painel procedural antigo de personagem, ainda referenciado somente por um teste, também foi retirado; esse teste agora exercita o drawer e o componente compartilhado de atributos.

Os sete SVGs de peças táticas já usados pela apresentação foram incluídos como dependências visuais; a cópia limpa expôs que ainda não estavam versionados. A limpeza de contadores em cartas emprestadas também foi versionada para conservar a correção de cancelamento que já existia no workspace.

## Edição e políticas

Abra `scenes/ui/workbenches/screen_catalog_workbench.tscn` para os 21 previews canônicos ou `scenes/ui/workbenches/ui_catalog_workbench.tscn` para o catálogo de componentes. O catálogo usa fixtures de apresentação com cópias defensivas (`.duplicate(true)`), não configura controllers de aplicação nem inicia servidor, HTTP ou gravações. Oferece estados `populated`, `empty`, `locked`, `loading`, `error`, `modal` e `long_text` conforme a view. Adapters de laboratórios com opções de persistência não são executados pelo catálogo.

Preserve nomes usados pelos bindings (`%UniqueName` e caminhos documentados em [GODOT_SCENE_OWNERSHIP_CONTRACT.md](GODOT_SCENE_OWNERSHIP_CONTRACT.md)). Containers controlam seus filhos: ajuste mínimos, flags, gaps, margens e alinhamento. Cores semânticas, contraste e tipografia vêm do contexto e dos recursos em `data/themes`. `CardVisualStyle` dirige métricas quando `use_profile_metrics=true`; métricas locais exigem desligar essa política. `CombatLayoutProfile`, `MapLayoutProfile` e `MenuVisualStyle` continuam autoridades das variantes responsivas.

Quantidades de cartas, atores, escolhas, ofertas e nós continuam dinâmicas pelos 25 templates canônicos. `Tree`/`ItemList` mantêm itens nativos. Fan, hit testing, drag, reordenação, câmera, keylines, shaders e tweens continuam em comportamentos próprios. Recursos, controllers e algoritmos podem continuar usando construtores; a migração não consiste em remover todo `.new()`.

## Validação e limites

A certificação usa cópias isoladas do cliente e instâncias dedicâncias da API nas portas `61987` e `61989` com stores próprios, sem abrir saves do jogador. Os fluxos reais cobrem equipamento/autosave de inscrições, compartimentos e companions, combate e retorno ao mapa, animação em três settings, histórico, timeline e replay. Verificações de replay semântico confirmam a integridade das runs de teste; modelos retidos antes de comandos permanecem inalterados.

As comparações geométricas e de movimento estão em `docs/architecture/baselines/godot-ui-decoupling/` (`baseline.before.json` e `baseline.after.json`) e `baselines/godot-scene-centralization/`. Métricas de refresh são síncronas e sintéticas: não representam latência REST nem tempo de GPU. Suítes focadas cobrem idiomas, escalas, pause, durações zero, cancelamento, drag/reorder, respostas tardias e falhas de leitura.

Não se declara cobertura do produto cartesiano de todas as configurações de resolução, locale, escala e conteúdo de mods. Uma tela de aplicação usa `ScreenContext` para receber portas de navegação/sessão/feedback/playback; para inspeção sem gameplay, use o catálogo ou `ScreenContext.isolated()`. Fixtures não autorizam ferramentas ou consequências que a engine não publicou.
