# Edição da interface na Godot

## Autoridade visual

Abra a cena da região e edite a árvore que será usada em execução. Ferramentas da mão, pilhas, controles de turno e painéis flutuantes agora têm pais estáticos. A criação em código permanece para quantidade variável de cartas, status, nós do grafo, opções e linhas de texto. A troca explícita de composição no laboratório ainda pode transferir grupos existentes; isso é uma operação deliberada do laboratório, não a montagem normal da tela.

## Tarefas frequentes

Os passos abaixo contam selecionar o recurso/nó, alterar e salvar; pressupõem que o projeto já esteja aberto.

| Tarefa | Abrir | Percurso |
|---|---|---|
| Fonte do custo no MVP | `data/themes/mvp_card_style.tres` | Selecionar o recurso, alterar `Cost Symbol Font Size`, salvar: 3 passos |
| Fonte do custo nos demais settings | `data/themes/ivory_run_card_style.tres` | Mesmo percurso: 3 passos |
| Ordem/espaço de Encerrar turno | `scenes/ui/components/turn_controls.tscn` | Selecionar `EndTurnButton` ou `EndTurnGap`, alterar ordem/mínimo, salvar: 3 passos |
| Posição do conjunto de ações | `scenes/ui/components/combat_view.tscn` ou `combat_mvp_view.tscn` | Editar `CombatBottomHUD/TurnControls` e seu container; o script não o transfere mais para outro pai |
| Novo ícone para um status existente/publicado | Asset + `scenes/ui/components/status_badge.tscn` | Importar asset, selecionar raiz, adicionar chave/Texture2D em `Icon Overrides`, salvar e conferir `Preview Status`: aproximadamente 5 passos |
| Layout do ator/inimigo no MVP | `scenes/ui/components/battlefield_actor.tscn` | Reordenar/redimensionar os filhos de `ActorContent` e salvar; o script do MVP não os reorganiza |
| Forma do nó da campanha | `scenes/ui/components/map_node.tscn` | Editar arte/glifo/legenda; a legenda usa âncoras relativas ao botão |
| Espaçamento do grafo | Perfil `Map Layout Profile` da `route_view.tscn` | Alterar dimensões, margens e desvio das arestas no Inspector |

Dimensões que dependem da quantidade de cartas, da extensão do grafo e do tamanho da janela continuam sendo calculadas. Alterações de significado do status ou regra de combate pertencem à engine; o override de ícone é apenas visual.

## F6 e prévia

`card_view.tscn`, `actor_panel.tscn`, `resource_view.tscn` e `status_badge.tscn` já possuíam exemplos exportados. Mão, combate genérico, MVP e mapa agora podem montar dados locais a partir de `data/labs/ui_editor_preview.json`, selecionável pelo campo `Preview Document`.

Execute isoladamente `card_hand.tscn`, `combat_view.tscn`, `combat_mvp_view.tscn`, `map_view.tscn` ou suas telas `combat_screen.tscn`, `combat_mvp.tscn`, `map_screen.tscn`. Os exemplos não substituem uma run no GameSession nem enviam comandos. Em execução normal, os dados reais continuam sendo apresentados pelo mesmo fluxo. As telas de combate e mapa são adaptadoras de runtime; para editar visualmente os exemplos no editor, abra os respectivos componentes de view.

`combat_hand.tscn` é uma variante montada dentro do combate e referencia as ferramentas externas pelo `Toolbar Path`; para F6 da mão independente, use `card_hand.tscn`. `hand_tools.tscn` reúne os controles reutilizados pela mão e pelo combate, evitando três cópias dos seus estilos.

## Sinais e arquivos

Conexões fixas de cabeçalho e controles de turno estão nos `.tscn` e aparecem na aba Node/Signals. Conexões para nós dinâmicos do mapa permanecem em código, com handlers nomeados e ID explícito. Integração com sessão e controladores continua nos adaptadores de tela.

Os seis templates removidos eram Labels/botão sem comportamento: `card_inline_label`, `card_detail_line`, `resource_counter`, `combat_button`, `activity_badge` e `attribute_source_trace`. Seu conteúdo é variável e suas fontes agora usam variantes do Theme ou a política de carta existente. O símbolo de custo, tooltip, carta, status e recurso continuam componentes porque têm comportamento ou reutilização próprios.

Não foram removidos laboratórios ou variantes de inscrições sem confirmação de sua finalidade.
