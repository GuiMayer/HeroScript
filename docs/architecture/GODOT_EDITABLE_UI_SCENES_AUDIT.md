# UI editável da Godot: arquitetura e estado atual

Revisão: 2026-11-02. Projeto: `examples/godot-engine-showcase`.

## Resultado

As etapas 0–20 do plano centralizaram as telas, overlays e shells dos laboratórios em cenas editáveis. A certificação final aprovou 604 verificações em 27 suítes e importação da exportação do índice Git, 492 verificações REST com dados exclusivos de teste e 20 capturas sem falhas. O [registro por etapa](../plans/GODOT_SCREEN_SCENE_CENTRALIZATION_PROGRESS.md) detalha commits, regressões e limites; o [guia de edição](../guides/godot-ui-scene-editing.md) indica os arquivos de cada tela.

A cena possui a árvore estática. Scripts vinculam nodes, preenchem modelos, conectam sinais e instanciam templates para coleções variáveis. Controllers, presenters, workspaces, gateways e políticas de animação permanecem nos papéis existentes. Não foram alteradas regras, endpoints, conteúdo de gameplay, durações ou autoridade dos dados.

## Composição canônica

| Área | Implementação atual |
| --- | --- |
| Entrada/router | `main.tscn` e `scripts/ui/scene_registry.gd`; cada rota instancia um PackedScene |
| Shell/pause | ApplicationShell, pause, toast, confirmações e histórico de conversa autorados |
| Cartas/atores | CardView, ActorPanel, ResourceView e StatusBadge compartilhados; não há face alternativa antiga |
| Menus | MainMenuView e Settings completos, incluindo páginas persistentes e controles de remapeamento |
| Loadout | Shell, inscrições, slots, inventário, deck, compartimentos, fontes de atributos e resumos autorados; companions continua placeholder |
| Combate | Header, battlefield, cenário, mão, dock, pilhas, preview, drawer de atributos, feedback e hosts autorados |
| Mapa | Shell, RouteView, nós, ações e inspector flutuante; layout do grafo/câmera continuam algoritmos |
| Atividades | Header, resumo, encerramento, ofertas de carta/itens, opções e confirmação Before/After |
| Diálogo | Tela, retrato, texto, paginação, recuperação e escolhas reconciliadas por identidade |
| Histórico/timeline/replay | Composições completas, listas/árvore/scrubber nativos e inspectores históricos/causais compartilhados |
| Sandbox/codex | Shells, editor JSON, seletores e templates de modos/abas; operações continuam REST |
| Laboratórios | Mão, inscrições, loadout, compartimentos, eventos e efeitos usam shells editáveis e componentes reais |

`scenes/run_loadout.tscn` herda a cena unificada e apenas seleciona a aba inicial de inscrições. O antigo script de tela de loadout foi removido. O painel procedural antigo de personagem, ainda referenciado somente por um teste, também foi retirado; esse teste agora exercita o drawer e o componente compartilhado de atributos.

Os sete SVGs de peças táticas já usados pela apresentação foram incluídos como dependências visuais; a cópia limpa expôs que ainda não estavam versionados. A limpeza de contadores em cartas emprestadas também foi versionada para conservar a correção de cancelamento que já existia no workspace.

## Edição e políticas

Abra `scenes/ui/workbenches/screen_catalog_workbench.tscn` para os 21 previews canônicos. O catálogo usa fixtures de apresentação, não configura controllers de aplicação nem inicia servidor, HTTP ou gravações. Oferece estados cheio, vazio, bloqueado, loading, erro e modal conforme a view. Adapters de laboratórios com opções de persistência não são executados pelo catálogo.

Preserve nomes usados pelos bindings. Containers controlam seus filhos: ajuste mínimos, flags, gaps, margens e alinhamento. Cores semânticas, contraste e tipografia vêm do contexto e dos recursos em `data/themes`. CardVisualStyle dirige métricas quando `use_profile_metrics=true`; métricas locais exigem desligar essa política. CombatLayoutProfile e MapLayoutProfile continuam autoridades das variantes responsivas.

Quantidades de cartas, atores, escolhas, ofertas e nós continuam dinâmicas por templates. Tree/ItemList mantêm itens nativos. Fan, hit testing, drag, reordenação, câmera, keylines, shaders e tweens continuam em comportamentos próprios. Recursos, controllers e algoritmos podem continuar usando construtores; a migração não consiste em remover todo `.new()`.

## Validação e limites

A certificação usa cópias isoladas do cliente e uma API na porta 61987 com stores próprios, sem abrir saves do jogador. Os fluxos reais cobrem equipamento/autosave, combate e retorno ao mapa, animação em três settings, histórico, timeline e replay. Verificações de replay semântico confirmam a integridade das runs de teste; modelos retidos antes de comandos permanecem inalterados.

As comparações geométricas por etapa estão em `baselines/godot-scene-centralization/`. A captura final contém 20 imagens em 1280×720 e 2560×1080. Métricas de refresh são síncronas e sintéticas: não representam latência REST nem tempo de GPU. Suítes focadas cobrem idiomas, escalas, pause, durações zero, cancelamento, drag/reorder, respostas tardias e falhas de leitura.

Não se declara cobertura do produto cartesiano de todas as configurações de resolução, locale, escala e conteúdo de mods. Uma tela de aplicação precisa do router para receber sessão/controller; para inspeção sem gameplay, use o catálogo ou uma view passiva. Fixtures não autorizam ferramentas ou consequências que a engine não publicou.
