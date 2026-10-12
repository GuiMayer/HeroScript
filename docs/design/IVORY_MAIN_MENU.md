# Menu principal — Marfim

2026-10-11. Primeira tela de produção migrada após a aprovação do laboratório. A ordem foi ajustada por solicitação do autor: menu principal antes do combate.

## Apresentação

- Papel marfim, tinta escura, Cinzel nos títulos e Source Sans 3 na leitura.
- Composição editorial em duas colunas: setting/jornada à esquerda; histórico, laboratório, códice, configurações e sair à direita.
- Nova jornada é a ação principal em vermelhão. Continuar mantém a disponibilidade publicada/resolvida para o setting selecionado.
- Circuitos finos com curvas de 45° ficam nos cantos, protegidos das áreas de texto. Não recebem input e não têm processamento por frame. Alto contraste ou layout compacto removem a decoração.
- Sem painel central escuro, sombras pesadas ou fundo de partículas em movimento no menu.
- Nomes longos têm título limitado a duas linhas e seleção truncada, preservando texto completo/metadados. Janela curta tem scroll que acompanha foco; em janela compacta as colunas se empilham.
- Português/inglês, escala de texto e navegação por teclado/controle usam os serviços existentes. Incluídas traduções das descrições dos settings Ascendant e Volatile que antes apareciam em inglês.

## Fronteiras de código

`MainMenuView` recebe apenas `VisualContext` e modelo visual defensivo. Emite intenção de ação ou seleção; não acessa sessão, HTTP, regras ou persistência. `main.gd` continua responsável pelos mesmos callbacks canônicos: nova run, resolução/abertura da continuação, recuperação, seleção do setting e navegação.

`MenuVisualStyle`, em `data/themes/ivory_menu_style.tres`, concentra largura, espaçamento, tipografia, altura de ações e geometria responsiva. `AppTheme` define as variações nativas `PrimaryAction` e `MenuLink`. Os controles não mantêm outro sistema de temas.

O router ativa `ivory_theme.tres` somente na rota `menu`. Ao abrir outras telas restaura o perfil de gameplay; ao voltar reativa Marfim. O fundo anterior fica oculto e sem processamento no menu. Notificações do shell recebem o perfil ativo e não conservam uma caixa escura com texto escuro. Destruir o router restaura seu perfil de entrada.

O laboratório usa o mesmo recurso Marfim já existente; seu ID histórico continua `ivory_lab`. Não é um setting, modo de jogo ou identidade determinística. Não existe seletor público de skins nem duas skins simultâneas na mesma árvore.

## Verificação

```powershell
./tools/test-godot-ui.ps1 -Tests ivory_menu,theme_contracts,setting_progression,view_replacement,pause_layers,ivory_lab,card_cost_symbols -VisualTest ivory_menu_visual -VisualOutputDirectory .runtime/ivory-menu
```

O runner usa projeto/user directory isolados. Capturas usam stretch de produção, 1440×900, `canvas_items/expand`, nas resoluções 1280×720, 1280×800, 1920×1080 e 2560×1080. Conferidos idiomas EN/PT e texto 100/120%, nomes extensos, estado offline, alto contraste, layout compacto, ações, continuidade por setting e retorno de configurações. As fixtures não iniciam runs nem fazem HTTP real ou alterações nas preferências do jogador.

[Capturas e relatório](ivory-menu/2026-10-11/README.md).

## Limite desta entrega

Este documento registra o primeiro passo no menu principal. A expansão posterior já inclui [menus secundários e pausa](IVORY_MENUS_AND_PAUSE.md). Combate, jornada, timeline e replay aberto pelo resultado da run conservam o perfil anterior; o redesign global segue pendente.
