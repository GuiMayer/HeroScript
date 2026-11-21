# Juice de interface com offset transforms (Godot 4.7)

Referência: [Godot-UI-Tutorial, Kobe](https://github.com/kobeDevStuff/Godot-UI-Tutorial). A inspiração é a transformação visual independente do layout, com feedback de hover/pressionamento e entrada escalonada de menus. A implementação utiliza os componentes e perfis existentes do HeroScript; não incorpora código ou assets externos.

## Integração

- `button_affordance.gd`: botões de ação principais, secundários, perigo e links nos fluxos de menu, mapa, preparação/atividade, diálogo, combate e configurações. Hover +2,5%, foco +1,2%, squash ao pressionar. Tween anterior sempre substituído; botões ocultos/desabilitados voltam à pose neutra. Sem animar as cartas ou botões dos atores.
- `counter_affordance.gd`: microconfirmação visual de mudanças de Round e Energy no combate; sem alterar o valor autoritativo, o layout da mão ou a geometria do campo de batalha.
- `panel_motion.gd` e `pause_menu_view.gd`: fade com deslocamento de 8 px nos painéis de inspeção/pilhas e pausa; botões da pausa entram com sequência sutil.
- `offset_transform_visual_only=true` mantém regiões de clique e retângulos de layout originais. As animações obedecem ao perfil de movimento, inclusive modo instantâneo e movimento reduzido.

## Regressão

`godot --headless --path examples/godot-engine-showcase --script res://tests/ui_offset_motion.gd`

O teste verifica transformação visual, invariância de layout, reset de ações desabilitadas, preferência de movimento reduzido, cancelamento, contadores e entrada da pausa. Também foi incluído no roteiro de testes Godot.

Em 2026-11-21, Godot MCP confirmou a presença dos helpers nos botões e contadores reais do combate Alisyum, ausência de erros de runtime e retângulos dos atores intactos. HeroScript MCP consultou a partida antes e depois de abrir/fechar a interface: sequência 99 e hash da run inalterados; nenhum comando de gameplay foi enviado.
