# UI editável da Godot: arquitetura, edição e migrações restantes

Revisão: 2026-11-01. Projeto: `examples/godot-engine-showcase`.

## Escopo concluído

As cinco cenas recentes foram reconstruídas. A hierarquia estática agora pertence aos arquivos `.tscn`; atualizações preenchem os controles existentes. Isso não significa que toda a aplicação já foi migrada.

| Cena | Responsabilidade | Dados de preview no Inspector |
| --- | --- | --- |
| `scenes/ui/components/status_badge.tscn` | Ícone, glifo, stacks e superfície do status | `preview_status` |
| `scenes/ui/components/resource_view.tscn` | Nome, barra e valor do recurso | `preview_resource_id`, `preview_state` |
| `scenes/ui/components/actor_panel.tscn` | Título, intenção, retrato/alvo e outlets de recursos/status | `preview_actor`, `preview_hostile`, `preview_intent`, variantes compact/battlefield |
| `scenes/ui/components/card_view.tscn` | Cabeçalho, custo, arte, regras, condições e alterações | `preview_data`, `visual_style`, `use_profile_metrics` |
| `scenes/ui/workbenches/ui_catalog_workbench.tscn` | Composição de exemplos das quatro cenas reais | Dados dos filhos, `visual_profile`, escala e contraste |

A fábrica `create(context)` de CardView, ActorPanel e ResourceView instancia a cena correspondente. Combate, mão, coleção, laboratório, loadout e ghosts usam essa mesma implementação; não há uma segunda face procedural. Status, recursos e custos variáveis instanciam templates de itens. Os scripts mantêm sinais, bindings, acessibilidade e animações de apresentação; regras e comandos continuam na engine/API.

Templates auxiliares: `resource_counter.tscn`, `card_cost_symbol.tscn`, `card_cost_chip.tscn`, `card_inline_label.tscn`, `card_change_badge.tscn`. O contorno impresso é um nó próprio com desenho procedural (`card_keyline.gd`), sem reconstruir a árvore.

## Como editar e validar

1. Abra `ui_catalog_workbench.tscn` e pressione F6. Não precisa da API; as amostras não criam runs nem enviam comandos.
2. Abra a cena do componente para alterar hierarquia, âncoras, alinhamento, margens, separações, fontes e StyleBoxes locais. Instâncias podem usar overrides/Editable Children para ajustes específicos.
3. Preserve os nomes únicos `%...` usados pelos bindings. Remover ou renomear esses nós exige atualizar o script e os testes juntos.
4. Altere os dicionários `preview_*` para testar dados diferentes. O preview consome o formato de apresentação do cliente: custos são arrays de objetos com `resourceId` e `amount`; recursos usam `current`, `minimum` e `maximum`.
5. Após mudar dados de amostra, reabra a cena ou execute F6. Escala, contraste e perfil da bancada atualizam o contexto de preview; não é um editor de conteúdo de gameplay.
6. Execute os testes em uma cópia isolada antes de substituir a aparência do jogo.

### Propriedades que continuam dirigidas por políticas

Cores de raridade, valor, relação/hostilidade e recurso são semânticas: dados/contexto podem substituir as cores do Inspector. Para mudanças globais, edite o perfil e os recursos de estilo, não adicione cores por chamada.

Na carta, `use_profile_metrics=true` mantém dimensões, altura do título/arte e escala acessível atuais. Use `visual_style`/CardVisualStyle para editar essas métricas em conjunto. Para experimentar dimensões locais da cena, desligue `use_profile_metrics`; título continua com ajuste de fonte e custos conservam proporção 1:1. Padding da face e nós internos permanecem editáveis. O estilo também dirige raio, superfície de efeitos e tipografia: não são promessas de preservar qualquer override de Inspector contra uma política explícita.

Containers calculam o layout dos filhos: arrastar um filho gerenciado por Container não fixa sua posição. Ajuste mínimos, flags, separação, alinhamento e margens. HandView continua responsável pelo fan, drag, ordem e trajetória; não fixe manualmente as posições das cartas da mão.

## O que ainda precisa de cena própria

Prioridade considera frequência de edição visual, complexidade e reuso. Uma cena existente com apenas um host não representa uma migração completa.

| Prioridade | Área / evidência atual | Próximas cenas sugeridas | Deve permanecer em código |
| --- | --- | --- | --- |
| Alta | `combat_view.gd` cria HUD, cabeçalho, mão, pilhas e controles; `combat_composition.tscn` contém somente CombatBody/CombatGameplay. `components/battlefield_view.gd` e `floating_hud.gd` montam partes internas. | Shell do combate, cabeçalho, controles de turno/pilhas/preview e drawer de atributos; templates de slots de atores. | Coordenador, read models, posicionamento responsivo e executor de animações. |
| Alta | `main_menu_view.gd` monta coluna, seletor, navegação e rodapé. `settings_screen.gd` monta páginas, linhas e controles. `shell/application_shell.gd` cria o pause e notificações. | Menu principal, pause, settings shell e linhas reutilizáveis de slider/toggle/seletor. | Binding de preferências, navegação, foco e callbacks. Não recriar a página ao alterar sliders. |
| Alta | `map_view.gd` cria header, workspace, scroll/canvas e inspector flutuante. | MapScreen, MapInspector e template de nó do grafo. | Topologia, câmera/interpolação, centralização, navegação e desenho das conexões. Inspector continua flutuante. |
| Alta | `card_details_panel.gd`, `inspection_dialog.gd`, `card_zone_dialog.gd`, `card_collection_view.gd` ainda constroem shells e seções. | CardDetails, InspectionDialog, CardZoneDialog, CardCollection e linhas de explicação. | Glossário/cálculo do presenter e coleções de CardView; tooltip passivo não ganha scroll inacessível. |
| Média | `unified_loadout_screen.gd` e `loadout/loadout_page_view.gd` montam módulos, abas, atributos e confirmações. | LoadoutPage, InscriptionsModule, DeckModule, AttributesPanel e templates de inscrição/slot. | Controllers, autosave e transações REST. Laboratório continua separado da run. |
| Média | `activity_screen.gd` e `components/activity_options_view.gd` montam escolhas e variações de loja/recompensa/upgrade. | ActivityShell e templates de ofertas/ações com componentes de carta compartilhados. | Opções disponíveis e consequências vêm da engine. |
| Média | `dialogue_scene.tscn` possui só o root com script; `dialogue_view.gd` cria header/painel e `dialogue_panel.gd` monta o conteúdo. | DialogueScene completa, DialoguePanel, ChoiceItem e histórico. | Leitura gradual, coordenação e comandos; a view não interpreta consequências. |
| Média | `timeline_screen.gd` e `run_history_screen.gd` montam listas e ferramentas de inspeção. | TimelineScreen, BranchItem, CommandRow, RunHistory e RunHistoryItem. | Paginação, seleção, replay e consulta dos dados publicados. |

Ordem recomendada: shells compartilhados/menu/pause/settings → combate → mapa → inspeção de cartas → loadout → atividades/diálogo → timeline/histórico. Cada migração deve preservar a composição atual e trocar a criação estática por bindings, sem redesenhar a interface no mesmo passo.

Não migrar algoritmos para cenas por obrigação: fan/hit testing/drag, câmera, desenho de grafos, shaders, tweens, transporte, presenters e controllers são comportamentos. Coleções variáveis precisam de templates de itens, não de um número fixo de filhos pré-criados. `application_shell.tscn` já fornece ScreenHost, mas ainda não cobre todo o chrome gerado pelo script.

## Critérios e evidências de validação

- Atualizações mantêm identidade dos controles estáticos e overrides de margens/tipografia cobertos por `tests/editable_ui_nodes.gd` (14 verificações).
- `tests/workbench_smoke.gd` verifica atores, afiliação/intenção, custos, efeitos, limites de recursos, seis badges, ausência de animação de entrada e estabilidade dos filhos (12 verificações).
- Geometria da carta antes/depois comparada em preserved/ivory, escalas 1.0/1.6: mínimos e retângulos de cabeçalho, nome, custo, arte e regras idênticos.
- Testes de apresentação, títulos, linhas, custos, seleção, movimento, compra curvilínea, ciclo de turno, contexto e animação passaram na cópia isolada do workspace. A bancada também foi renderizada pela Godot para inspeção visual.
- Certificação final no workspace isolado: bancada 12, nós editáveis 14, estabilidade da carta 34, reconciliação 4, colaboradores do combate 12, contextos da run 8, contrato de animações 8 e contador transitório 24. Atualização de arte/contexto passou em mais 12 verificações, incluindo troca de contexto sem refazer a face.
- Nenhuma definição JSON de gameplay, endpoint, comando ou estado da engine foi alterado por esta reconstrução. Os testes são de apresentação com modelos injetados; não equivalem a uma certificação end-to-end com API real.

A validação adicional de um export só de HEAD encontrou uma inconsistência anterior a este trabalho: `card_presentation.gd` referencia `card_glossary_presenter.gd` e `effect_duration_presenter.gd`, ainda não versionados, e pressupõe um tooltip alterado apenas no workspace. As referências e o teste já existiam no commit anterior à reconstrução. A suíte de apresentação passa com esses arquivos locais presentes; esse conjunto deve ser revisado/versionado em etapa própria, sem incluir silenciosamente as alterações pendentes no commit da migração visual.
