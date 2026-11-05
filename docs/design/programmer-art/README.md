# Programmer art / marco do laboratório

Data: 2026-10-23. Registro das etapas 1–4 do [plano](../../plans/PROGRAMMER_ART_VISUAL_REVAMP_PLAN.md) e da aplicação posterior do visual aprovado ao jogo.

## Inventário inicial e propriedade visual

| Aparência | Dono atual | Troca nesta entrega |
| --- | --- | --- |
| Cores e controles nativos | VisualThemeProfile / ViewStyleFactory | Perfil isolado derivado do marfim do laboratório |
| Anatomia, raridade, valores e custo da carta | CardView / CardVisualStyle / CardCostSymbol | Só tinta/acabamento; métricas e símbolos de custo preservados |
| Arte de cartas e objetos | ArtSlot / ArtCatalog / manifestos JSON | Novo catálogo vetorial opt-in |
| Fundo e atores da run | SceneryVisualProfile / CombatSceneryView | Inventariados; substituição prevista na etapa 5 |
| Ornamentação do laboratório | CircuitTrace / CardView.InkKeyline | Mesmos controles e rects; acabamento passivo |
| Mão e animações | CardHandView / CardChoreography / MotionPolicy / CardMotionLab | Congelados |
| Mapa e ícones de nós | MapView / RouteView / MapNodeGlyph | Sem alteração; etapa 6 |
| Diálogo, loja, recompensas, loadout | Views existentes e catálogos específicos | Sem alteração; etapa 6 |
| Menus, configurações, pausa e histórico | Perfis atuais / MenuVisualStyle | Sem alteração; etapa 7 |

`ivory_theme.tres` é a origem do laboratório; `ivory_run.tres` é a origem da run. Fontes Cinzel/Source Sans 3, medidas dos estilos, fonte dos títulos e conteúdo textual permanecem iguais. O catálogo da run já contém SVGs; o problema observado é inconsistência de acabamento e reutilização das mesmas composições.

## Baseline

`tests/programmer_art_lab.gd` instancia a cena real do laboratório, com os componentes reais de mão e carta. Registra 40 casos: três resoluções (1280×720, 1920×1080, 2560×1080), EN/PT, escalas 1 e 1,2, mãos 0/2/5 e quatro presets completos. Cada digest inclui a árvore ordenada de controles, dimensões, mínimos, transformação global, visibilidade, filtro de mouse e foco. Também registra hashes dos executores e configurações de movimento.

O fixture gerado está em `data/labs/programmer_art_baseline.json` no projeto Godot. A captura de referência está em [baseline/lab-1920x1080.png](baseline/lab-1920x1080.png). São dados da árvore local antes do revamp, incluindo correções locais anteriores; não representam uma comparação com outro commit.

Captura inicial renderizada: 42 verificações aprovadas, mais 10 verificações de contrato de temas. O laboratório não enviou requests nem alterou snapshots. O movimento reduzido foi usado somente durante a medição para eliminar entradas transitórias. O código e as configurações de animação de produção não foram alterados.

Como esta entrega fica restrita ao laboratório, as capturas e baselines das telas da run e dos menus serão produzidos antes de suas respectivas etapas. Não se certifica aqui um revamp dessas telas.

## Execução reproduzível

Na raiz do repositório:

```powershell
./tools/test-godot-ui.ps1 -Tests programmer_art_lab,theme_contracts,ivory_lab,card_animation_parity
```

Para capturas, adicionar `-VisualTest programmer_art_lab -VisualOutputDirectory .runtime/programmer-art/after`. A opção `-VisualPhase baseline` é exclusiva da geração de referência antes de mudanças; não deve ser usada para aprovar regressões.

## Diretrizes de produção

SVGs estáticos, cores planas e formas geométricas, sem trilhas decorativas nas artes das cartas. Mapeamentos explícitos por ID e fallback neutro; sem sorteio, API ou interpretação de regras. O número de formas da ilustração é decorativo e não substitui os valores da carta.

Na validação inicial o perfil era opt-in no laboratório. Depois da aprovação, os perfis dos menus e da run passaram a consumir o mesmo catálogo e acabamento, preservando suas medidas e os assets de cenário existentes.

## Etapas 2 e 3

`ProgrammerArtProfile.create(base)` deriva um perfil sem duplicar fontes ou alterar métricas. A paleta fica no perfil, e `CardVisualStyle.keyline_style` seleciona entre a moldura anterior e marcas de registro. `CardView` continua sendo o único componente de carta: a variação muda somente o desenho passivo dentro do mesmo rect. Verificação do perfil: 161 checks de herança, mínimos dos estilos e preservação dos Resources de origem.

O catálogo, inicialmente isolado, agora fica em `data/programmer_art_manifest.json`; os SVGs em `assets/programmer-art`. São 22 famílias, cada uma com três arquivos de carta 320×150 e um símbolo quadrado 128×128 (88 arquivos), com 94 associações explícitas de cartas. As variantes foram unificadas após o pedido de retirar rotação e trilhas. As amostras conhecidas têm associação explícita; conteúdo de mod desconhecido recebe um documento com núcleo geométrico neutro. O nome de placeholder impresso abaixo da arte é preservado.

`tools/build-programmer-art-assets.ps1` contém as receitas vetoriais; `-Check` verifica os arquivos produzidos. Não há geração em runtime, novas dependências ou consumo de RNG. As variações não codificam a quantidade real de acertos/stacks. Os ícones de objetos e atividades ficam preparados no catálogo, sem alterar os consumidores da run nesta etapa. Canvas e preenchimento dos símbolos de custo atuais permanecem intactos.

`programmer_art_catalog.gd`: 312 verificações aprovadas de importação, cobertura das fixtures, fallback, dimensões e preservação do catálogo de cenário. Atores, retratos e fundos continuam com as mesmas associações e arquivos da etapa anterior.

## Etapa 4: disponível para validação

Abra **Laboratório de cartas** pelo menu existente ou execute `scenes/card_hand_lab.tscn` na Godot. A aparência nova já é a aparência padrão dessa cena. Alterne os quatro presets no seletor existente; idioma, escala de texto, alto contraste, quantidade de cartas, inspeção e animações continuam nos controles atuais.

A cena deriva `ivory_theme.tres`, injeta o catálogo novo e desenha três trilhas no ornamento existente. Nenhum controle ou rótulo foi acrescentado, retirado ou reposicionado. `CardView`/`ArtSlot` são os componentes do combate. A run conserva seus perfis e catálogos; sair do laboratório restaura o perfil anterior.

Capturas renderizadas:

- [Mão inicial / depois](after/lab-1920x1080.png) e [antes](baseline/lab-1920x1080.png).
- [Transformações](after/volatile.png).
- [Conteúdo personalizado e textos longos](after/stress.png).

Os 40 digests de geometria coincidem com a referência original, incluindo matrizes de transformação do fan, mínimos nativos, foco e filtro de input. A geometria é verificada a 0,01 unidade da UI, depois de estabilizar a montagem. A medição ajusta tanto o tamanho da janela quanto `content_scale_size`, para exercitar resoluções lógicas distintas mesmo em monitor menor que ultrawide.

Certificação da rodada principal: dez suítes, 762 verificações distintas aprovadas; o relatório também registra uma segunda execução renderizada do teste de geometria (+42). Inclui 73 verificações do laboratório marfim, 22 de paridade de animações, 19 de seleção, 72 de geometria da mão e os testes dos shaders existentes. Não houve diagnósticos de script/shader nessa rodada. Evidência: [validation.json](validation.json).

Rodada final após normalizar hashes de texto entre LF/CRLF: 42 verificações de geometria em headless e 35 verificações renderizadas dos efeitos, incluindo impacto, contorno e sheen sobre a carta real, sem diagnósticos. Evidência: [rendered-validation.json](rendered-validation.json). Os SVGs também passaram na verificação de reprodução do gerador.

Limite da entrega inicial: as etapas 1–4 certificaram apenas o laboratório e os componentes compartilhados nos testes isolados. A aplicação posterior ao jogo está registrada abaixo.

## Refinamento solicitado: linhas padronizadas da carta

Após a validação do marco, foi solicitado reservar uma linha para os recursos e padronizar título/arte. `CardView` agora reserva uma área fixa para o título (altura configurável em `CardVisualStyle.title_line_height`), uma linha de custos alinhada à esquerda diretamente abaixo do nome e a altura de arte definida pelo estilo.

`CardCostLine` contém a linha horizontal de recursos e alternativas. Custos extensos se ajustam proporcionalmente à largura disponível, sem quebra de linha nem distorção dos símbolos. Isso impede que recursos extras empurrem a arte. O componente é compartilhado pelo laboratório e combate; nenhuma regra ou animação foi alterada.

O baseline original foi preservado. `programmer_art_rows_baseline.json` registra a nova anatomia autorizada; o teste de geometria continua comparando posição/tamanho externos da mão com a referência original em todos os 36 casos da matriz. [Captura com as linhas padronizadas](rows/lab-1920x1080.png).

`card_face_rows.gd` cobre todas as amostras, dois perfis marfim, EN/PT, texto normal/ampliado e atualização para custos alternativos (544 verificações aprovadas). O teste também confere alinhamento da arte, permanência do componente durante reconciliação, custo contido em uma única linha e preservação dos modelos de entrada.

Refinamento de arte: removida a rotação decorativa de ±8° aplicada pelo gerador e removidas as trilhas triplas das ilustrações. Os símbolos ficam orientados no espaço local da carta e acompanham somente a inclinação da mão. Os nomes dos arquivos e mapeamentos permanecem iguais; as variantes existentes passam a apontar para a mesma composição limpa por família. Circuitos da interface externa às artes permanecem intactos. `-AssetsOnly` permite regenerar os SVGs sem regravar o catálogo.

Refinamento do nome: `CardTitle` conserva a área original de 24 unidades em todas as cartas, com fonte inicial 17, piso 12 para uma linha e piso 8 para duas linhas, configuráveis no estilo e acompanhando a escala de texto. Primeiro procura uma fonte que caiba em uma linha; se o piso ainda não couber, procura o maior tamanho que caiba em duas linhas dentro dos mesmos 24. A entrelinha é zero para permitir essa composição compacta. A medição usa o shaping nativo da Godot. Nomes excepcionalmente extensos mantêm reticências e leitura completa na inspeção. Não há processamento por frame: o ajuste responde à montagem, reconciliação, tema e largura disponíveis. [Captura com títulos adaptáveis](rows/adaptive-titles.png).

`card_titles.gd`: 157 verificações aprovadas para nomes curtos, intermediários, longos, palavras extensas, EN/PT, escalas 1/1,2/1,6 e troca do nome sem recriar o Label. Conferem explicitamente o cabeçalho original de 24 unidades (arredondado ao pixel na escala escolhida), área constante, piso/teto de fonte, retorno ao tamanho máximo e dois modos de ajuste.

Conferência final da correção compacta: 544 verificações de linhas da carta aprovadas. As execuções renderizada e headless produziram os mesmos digests nos 40 casos; a rodada final de comparação passou em 78 verificações, incluindo posição/tamanho externos da mão e fontes/configurações de movimento inalteradas. [Relatório de geometria](rows/geometry-validation.json).

## Aplicação ao jogo

Após aprovação, `ivory_theme.tres` e `ivory_run.tres` passaram a usar a paleta e o catálogo compartilhado de programmer art. Isso cobre o combate, coleções, atividades, loja, mapa, diálogos, menus, pausa, configurações, histórico, replay e loadout que usam esses contextos. Os dois estilos de carta usam as marcas de registro aprovadas. As medidas de cada perfil permanecem próprias: combate 248×360 / arte 64; laboratório 248×404 / arte 108. O título permanece nos 24 originais e o custo fica abaixo do nome.

O catálogo saiu de `data/labs` e passou a `data/programmer_art_manifest.json`, consumido igualmente pelo laboratório e produção. Os ícones de inscrições também usam a família geométrica, preservando overrides de ícone explicitamente publicados no item. Os dois ornamentos `MenuCircuit` foram removidos do menu principal, sem alterar containers ou navegação. Os circuitos de outros painéis não foram removidos por este pedido.

Atores, retratos e fundos mantêm suas associações e posições atuais; nenhuma nova composição de cenário foi criada nesta aplicação do visual aprovado. Regras, transporte, saves, animações e fluxo de ações permanecem com suas autoridades anteriores.

Validação da aplicação: [608 verificações offline](game/offline-validation.json) de menus/pausa/configurações, coleções, loadout, contexto visual, lifecycle e paridade das animações; [239 verificações de contratos/geométrica/renderização da run](game/run-validation.json); [52 verificações renderizadas do menu](game/menu-validation.json). Nenhum diagnóstico de script/shader nessas rodadas. Capturas usam gateways de fixture e diretório de preferências isolado, sem modificar saves pessoais ou comprovar uma campanha na API real.

Capturas finais: [menu sem circuitos](game/menu.png), [combate](game/combat.png), [mapa](game/map.png) e [diálogo](game/dialogue.png). A rodada renderizada da run cobre também pausa, recompensa e mão vazia em 1280×720, 1920×1080 e 2560×1080, em EN/PT. O jogo deve ser reaberto para carregar os Resources e o catálogo atualizados.


## Fundo geral compartilhado

A cor de fundo das páginas fica em `examples/godot-engine-showcase/data/themes/shared_palette.tres`, na chave `surface.canvas` (branco, `#ffffff`). Menu, run e laboratório herdam essa chave; o cenário mapeia `scenery.base` para ela. Para trocar o fundo geral, altere apenas esse valor. Os painéis, cartas, bordas e estados de interação mantêm seus próprios tokens semânticos.

O SVG `assets/ivory/battlefield.svg` tem fundo transparente para não encobrir a cor do tema. O editor de compartimentos usa o token de canvas, e a cor de limpeza do renderer acompanha o perfil ativo no ponto de composição da aplicação. Perfis alternativos e de teste continuam podendo declarar overrides locais. O teste `shared_canvas_theme.gd` cobre herança, branco inicial, propagação, invalidação dos caches e preservação dos Resources originais.


## Paleta branca minimalista

`shared_palette.tres` também centraliza os tokens de superfície, texto, bordas, interação e acentos usados pelos perfis do menu e da run. O laboratório herda essa paleta sem recolocar as antigas cores bege. Os perfis conservam fontes, medidas, arte associada e configurações de movimento.

Superfícies principais são brancas; áreas secundárias usam cinzas frios leves, bordas discretas e texto carvão. Hover e pressão usam dois níveis de cinza; foco e ação principal mantêm o vermelho. Recursos, raridades e valores modificados preservam suas cores semânticas. As descrições de cartas mantêm a superfície escura para preservar o contraste dos valores-base brancos. Recortes de papel dos SVGs ativos passaram a branco, sem alterar a geometria das ilustrações.
