# Primeiro marco Marfim — laboratório de cartas

2026-10-11. Implementação visual disponível para aprovação, não expansão do redesign para todas as telas.

## Abrir

Abra `examples/godot-engine-showcase/scenes/card_hand_lab.tscn` no editor Godot e pressione **F6**. F5 continua abrindo o jogo normal, com o tema preservado. A cena não precisa de API, não executa efeitos, não cria runs e não salva preferências automaticamente. O botão explícito **Aplicar animação ao combate** grava somente um perfil visual local.

Selecione um conjunto, altere o idioma e a escala, experimente seleção/hover, inspeção persistente, visão geral, modo de leitura e mão vazia. O campo permite 0–100 cartas apenas como teste visual; não define um limite de gameplay.

## Aparência entregue

- Papel `#F2EBDD`, superfície elevada `#FAF5EB`, tinta `#20201C`, vermelhão `#B83E25`.
- Cinzel 600 para títulos/nome da carta; Source Sans 3 500 para leitura. Fontes locais com licença OFL e glifos portugueses verificados. [Proveniência e licenças](../../examples/godot-engine-showcase/assets/fonts/README.md).
- Face clara com cantos pequenos, arte geométrica em tinta/vermelhão, nome do placeholder fora da ilustração e faixa de custos por recurso.
- Custos de energia usam um selo radiante; mana usa uma gota facetada. Cada símbolo tem somente um contorno em tinta, sem linhas internas ou detalhes vermelhos, e o número nativo centralizado, sem letras E/M misturadas ao valor. As silhuetas distinguem os recursos sem depender de cor. `+` e `OU` mantêm o significado dos pagamentos combinados/alternativos; nomes completos permanecem nos detalhes.
- Efeitos em superfície escura: prosa clara; números-base brancos, reduzidos vermelhos e aumentados azuis. As quatro cores atendem contraste de texto normal ≥4,5:1 sobre a faixa. Comparações continuam consumindo fatos publicados; a UI não calcula efeitos.
- Raridade permanece na borda externa e nos detalhes, acompanhada do nome. Filete interno de tinta garante leitura do contorno no papel claro; seleção/foco reforçam o filete sem substituir a raridade.
- Circuitos passivos de cinco linhas finas, com cotovelos de 45° e distância perpendicular constante. Redesenho somente quando necessário; não recebem input/foco. Alto contraste oculta os circuitos e preserva a superfície invertida dos efeitos.
- Tooltip continua resumo passivo sem scrollbar. Inspeção persistente e visão geral conservam navegação e rolagem próprias.

## Recursos e isolamento

### Transição de queima (Burn Edge)

Selecione uma carta e escolha **Uso da carta → Queima → Centro/Bordas** no seletor em cascata. Ajuste **Duração do ciclo (s)** e clique em **Reproduzir animação**. **Aplicar animação ao combate** salva explicitamente o tempo/variante para as próximas capturas no jogo; somente reproduzir continua isolado. Sem seleção de carta, o teste usa a primeira. A transição consome apenas essa instância no cenário visual; **Restore preset** restaura as amostras. Não altera conteúdo, pilhas ou regras de nenhuma run.

A duração visível completa começa no tempo capturado do perfil de produção (por padrão, **0,5 s** para queima) e pode ser alterada entre **0 e 30 segundos**, em incrementos de 0,05 s. Zero, **Prévia instantânea** e **Reduced motion** aplicam o estado final imediatamente, sem alocar a captura. Não há voo, espera ou cauda adicional na queima. O preparo da textura requer um frame renderizado; a face original permanece visível durante esse preparo. **Manual animation steps → Next** inicia o efeito; **Skip presentation** conclui sem aguardar. Pause congela o progresso.

`card_burn_view.gd` compõe o mesmo `CardView` em `SubViewport` transparente, com `UPDATE_ONCE` e sobreamostragem 2×. `TextureRect` exibe a `ViewportTexture` diretamente na GPU; não existe captura de tela, leitura de pixels pela CPU ou geração de imagem em cada frame. `shaders/card_burn.gdshader` usa distância, ruído fixo, carbonização e uma borda incandescente fina; `progress = 0/1` garante os extremos íntegro/transparente. Material e progresso são independentes por transição; shader e ruído são compartilhados.

`card_burn_motion.gd` usa o `PresentationHandle` e o `CardTransitRegistry` existentes, com limpeza no fim, skip ou fechamento. A versão controlável permanece no laboratório; o combate também reutiliza o executor para [queima pós-uso de cartas exiladas](CARD_EXILE_ANIMATION.md), configurada em 0,5 s no manifesto de produção. `tests/card_burn_lab.gd` verifica direções, duração, seleção, pause, modo manual, skip, acessibilidade e descarte dos nodes; a certificação visual deve usar renderização nativa, não apenas headless.

### Catálogo de animações e duração do ciclo

`data/labs/card_animation_catalog.json` define categorias/subcategorias recursivas, variantes, IDs estáveis e a seleção inicial. `lab_animation_picker.gd` monta um `MenuButton` com `PopupMenu` nativo em cascata, conserva a seleção por ID e traduz o caminho completo. As variantes usam `scenarioId` para referenciar os cenários existentes em `card_motion_lab.json`, ou um objeto `scenario` para uma nova definição. Adicionar um item para um executor disponível não exige outro botão; um novo efeito visual continua exigindo seu executor.

Há um único botão de reprodução. O campo de segundos vale para o **ciclo ativo completo**, não para cada etapa: compra em lote inclui stagger; usar e descartar divide o tempo entre as duas animações; usar uma carta retida inclui ida e volta. `CardZoneMotion.schedule()` centraliza a programação de transferências e é consumido tanto pelo executor quanto pela previsão do laboratório, preservando o comportamento do combate. O orçamento e o ponto de passagem são definidos em `card_choreography.gd`, compartilhado com o combate. Editar o campo somente ajusta a prévia; **Aplicar animação ao combate** salva `user://card_animation_profile.json`, lido nas próximas capturas de ambas as cenas, sem editar regras ou `motion_manifest.json`. Uso/retorno compartilham o tempo da família de uso. Movimentos da mão/hover não são desacelerados pelo campo.

Pausa, preparação da captura e espera de avanço manual não entram no orçamento ativo. A duração/modo são congelados no início da reprodução; editar controles afeta apenas a próxima. Fatos sem animação visível nunca ganham timers para preencher o ciclo. Durações longas elevam somente o watchdog local. `tests/lab_animation_controls.gd` cobre seleção, tradução, extensão por JSON, duração composta, zero, edição durante execução e ausência de esperas artificiais.

Os controles de animação ficam fora da área rolável de amostras/detalhes. O menu em cascata aceita navegação por teclado e marca a variante selecionada; escolher uma variante ou editar os segundos não inicia reprodução.

Validação do seletor/ciclo em 2026-10-14: **502 verificações aprovadas** — 410 headless em nove suites (`lab_animation_controls`, `card_burn_lab`, `card_zone_motion`, `semantic_cues`, `card_hand_lab`, `ivory_lab`, `card_play_selection`, `reference_hand_geometry`, `motion_policy`) e 92 renderizadas (`lab_animation_controls`: 56; `card_burn_lab`: 36), sem diagnósticos de encerramento. Os testes exercitam entrada numérica real, categorias/subcategorias por teclado, EN/PT, duração composta de 0,4 s, zero, skip, pause e controles acessíveis em 1280×720/2560×1080. Capturas locais: `.runtime/lab-animation-controls/` e `.runtime/card-burn-lab/latest/`.

Validação de 2026-10-14: rodada final com três execuções nativas de 35 verificações cada, sem erros de shader ou diagnósticos de encerramento; mais 250 verificações de laboratório, seleção e geometria da mão. Na última execução, o tempo ativo medido foi 1,054 s no centro e 0,986 s nas bordas; o Tween é configurado para 1,000 s e a medição de parede varia nas fronteiras dos frames/pausa. O modo headless não gera o ruído gráfico; o teste aguarda a liberação do playback Ogg pela thread de áudio antes de encerrar. Capturas intermediárias/finais: `.runtime/card-burn-lab/visual/` (artefatos locais, não versionados).

`data/themes/ivory_theme.tres` seleciona paleta, fontes, manifesto e `ivory_card_style.tres`. O laboratório aplica/restaura o perfil em seu ciclo de vida; nenhum setting da engine foi modificado e nenhum seletor de skins foi adicionado ao jogo.

`CardView`, `CardEffectText`, `CardDetailsPanel`, `InspectionDialog` e `CardHandView` são os mesmos usados no jogo. A política de impressão é opcional em `CardVisualStyle`; o perfil preservado mantém a apresentação anterior. Custos, requisitos, comportamentos e badges continuam vindo dos mesmos modelos.

`assets/ivory/*.svg` contém seis placeholders vetoriais originais. O manifesto visual do laboratório resolve IDs conhecidos e desconhecidos pelos slots existentes. Textos traduzidos não ficam embutidos nas imagens. Esta entrega não inclui arte definitiva, atores ou cenário do combate.

Os três vetores adicionais `cost-energy.svg`, `cost-mana.svg` e `cost-neutral.svg` são símbolos de custo, não artes de efeito. `presentation.json`, em `resources.<id>.cost_icon`, escolhe o vetor; `CardCostSymbol` (script `card_cost_symbol.gd`) não conhece IDs ou regras de recursos. Um recurso customizado pode fornecer seu próprio vetor. Sem vetor válido, o fallback neutro mantém o número no centro e a sigla configurada no canto, fora do campo numérico. `CardVisualStyle.cost_symbol_size` e `cost_symbol_font_size` controlam dimensões/tipografia. A face Marfim usa altura-base 404 para acomodar os símbolos e o rodapé, sem modificar o perfil de produção.

## Verificação reproduzível

```powershell
./tools/test-godot-ui.ps1 -Tests ivory_lab,card_hand_lab,card_presentation,card_preview_separation,theme_contracts,hand_ux,component_context,view_replacement,pause_layers,ui_lifecycle -VisualTest ivory_lab_visual -VisualOutputDirectory docs/design/ivory-lab/2026-10-11
```

O runner importa uma cópia isolada com user directory exclusivo. `ivory_lab` cobre fonte/glifos, contraste, raridade, detalhe/tooltip, circuitos, mãos de 0/1/5/12/100, EN/PT, escala 1,0/1,2 e restauração de perfil/preferências sem rede. As capturas renderizadas usam stretch de produção em 1280×720 e 2560×1080; português também exercita texto a 120%.

Resultado final: 380 verificações em dez suites offline e 39 verificações na captura renderizada, todas aprovadas. Foram salvos 18 PNGs. O teste confirma o peso no cache nativo da fonte, não apenas sua propriedade declarada; chips de custo/badges preservam contraste no modo acessível.

[Capturas e relatório](ivory-lab/2026-10-11/README.md). Não confundem fixtures com teste de balanceamento ou gameplay. Os diagnósticos de encerramento headless continuam registrados, conforme a [certificação arquitetural](interface-refactor/2026-10-11/README.md).

Revisão posterior dos custos: [símbolos, capturas e validação](ivory-lab/2026-10-11-cost-symbols/README.md). As capturas anteriores ficam preservadas como comparação.

## Aprovação e próximos passos

O primeiro marco implementa no laboratório os recursos e tratamentos das etapas 1–5 do [plano Marfim](../plans/GODOT_IVORY_VISUAL_REDESIGN_PLAN.md). Não declara concluída a migração global de controles ou artes de atores/cenário. Por solicitação posterior do autor, a expansão começou pelo [menu principal](IVORY_MAIN_MENU.md), antes do combate; as autoridades de input, REST e playback permanecem as mesmas.
