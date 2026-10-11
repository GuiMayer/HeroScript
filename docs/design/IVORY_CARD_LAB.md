# Primeiro marco Marfim — laboratório de cartas

2026-10-11. Implementação visual disponível para aprovação, não expansão do redesign para todas as telas.

## Abrir

Abra `examples/godot-engine-showcase/scenes/card_hand_lab.tscn` no editor Godot e pressione **F6**. F5 continua abrindo o jogo normal, com o tema preservado. A cena não precisa de API, não executa efeitos, não cria runs e não salva preferências.

Selecione um conjunto, altere o idioma e a escala, experimente seleção/hover, inspeção persistente, visão geral, modo de leitura e mão vazia. O campo permite 0–100 cartas apenas como teste visual; não define um limite de gameplay.

## Aparência entregue

- Papel `#F2EBDD`, superfície elevada `#FAF5EB`, tinta `#20201C`, vermelhão `#B83E25`.
- Cinzel 600 para títulos/nome da carta; Source Sans 3 500 para leitura. Fontes locais com licença OFL e glifos portugueses verificados. [Proveniência e licenças](../../examples/godot-engine-showcase/assets/fonts/README.md).
- Face clara com cantos pequenos, arte geométrica em tinta/vermelhão, nome do placeholder fora da ilustração e faixa de custos por recurso.
- Efeitos em superfície escura: prosa clara; números-base brancos, reduzidos vermelhos e aumentados azuis. As quatro cores atendem contraste de texto normal ≥4,5:1 sobre a faixa. Comparações continuam consumindo fatos publicados; a UI não calcula efeitos.
- Raridade permanece na borda externa e nos detalhes, acompanhada do nome. Filete interno de tinta garante leitura do contorno no papel claro; seleção/foco reforçam o filete sem substituir a raridade.
- Circuitos passivos de cinco linhas finas, com cotovelos de 45° e distância perpendicular constante. Redesenho somente quando necessário; não recebem input/foco. Alto contraste oculta os circuitos e preserva a superfície invertida dos efeitos.
- Tooltip continua resumo passivo sem scrollbar. Inspeção persistente e visão geral conservam navegação e rolagem próprias.

## Recursos e isolamento

`data/themes/ivory_theme.tres` seleciona paleta, fontes, manifesto e `ivory_card_style.tres`. O laboratório aplica/restaura o perfil em seu ciclo de vida; nenhum setting da engine foi modificado e nenhum seletor de skins foi adicionado ao jogo.

`CardView`, `CardEffectText`, `CardDetailsPanel`, `InspectionDialog` e `CardHandView` são os mesmos usados no jogo. A política de impressão é opcional em `CardVisualStyle`; o perfil preservado mantém a apresentação anterior. Custos, requisitos, comportamentos e badges continuam vindo dos mesmos modelos.

`assets/ivory/*.svg` contém seis placeholders vetoriais originais. O manifesto visual do laboratório resolve IDs conhecidos e desconhecidos pelos slots existentes. Textos traduzidos não ficam embutidos nas imagens. Esta entrega não inclui arte definitiva, atores ou cenário do combate.

## Verificação reproduzível

```powershell
./tools/test-godot-ui.ps1 -Tests ivory_lab,card_hand_lab,card_presentation,card_preview_separation,theme_contracts,hand_ux,component_context,view_replacement,pause_layers,ui_lifecycle -VisualTest ivory_lab_visual -VisualOutputDirectory docs/design/ivory-lab/2026-10-11
```

O runner importa uma cópia isolada com user directory exclusivo. `ivory_lab` cobre fonte/glifos, contraste, raridade, detalhe/tooltip, circuitos, mãos de 0/1/5/12/100, EN/PT, escala 1,0/1,2 e restauração de perfil/preferências sem rede. As capturas renderizadas usam stretch de produção em 1280×720 e 2560×1080; português também exercita texto a 120%.

Resultado final: 380 verificações em dez suites offline e 39 verificações na captura renderizada, todas aprovadas. Foram salvos 18 PNGs. O teste confirma o peso no cache nativo da fonte, não apenas sua propriedade declarada; chips de custo/badges preservam contraste no modo acessível.

[Capturas e relatório](ivory-lab/2026-10-11/README.md). Não confundem fixtures com teste de balanceamento ou gameplay. Os diagnósticos de encerramento headless continuam registrados, conforme a [certificação arquitetural](interface-refactor/2026-10-11/README.md).

## Aprovação e próximos passos

O primeiro marco implementa no laboratório os recursos e tratamentos das etapas 1–5 do [plano Marfim](../plans/GODOT_IVORY_VISUAL_REDESIGN_PLAN.md). Não declara concluída a migração global de controles, artes de atores/cenário ou outras telas. Após aprovação da direção, expandir primeiro para o combate, preservando as mesmas autoridades de input, REST e playback.
