# Laboratório de cartas e mão — pesquisa e direção de design

Data: 2026-10-07.

## Objetivo

Refinar a apresentação das cartas e da mão sem depender de uma run, de inimigos ou de uma configuração específica da engine. A cena inicial mantém o desenho de produção para permitir comparação. Ela não é um simulador de regras.

## Cena implementada

Abra `examples/godot-engine-showcase/scenes/card_hand_lab.tscn` no projeto Godot da demo e pressione **F6**. Não precisa iniciar a API. F5 continua abrindo o jogo normal.

Também pode iniciar pela raiz do repositório:

```powershell
godot --path examples/godot-engine-showcase res://scenes/card_hand_lab.tscn
```

O laboratório oferece:

- 17 modelos visuais em quatro grupos: cartas familiares, transformações voláteis, custos/scaling e conteúdo personalizado.
- Mãos de 0 a 40 cartas. Esse limite pertence ao controle do laboratório, não é uma regra da engine.
- Custos únicos e múltiplos, recursos desconhecidos, valores fracionários, custo zero e valores grandes.
- Nomes longos, muitos efeitos, cartas indisponíveis, tipos personalizados e ausência de arte.
- Upgrades, buffs, debuffs e modificações simultâneas, com identificação das fontes.
- Textos das cartas em português e inglês, escala de texto, alto contraste e movimento reduzido.
- Seleção, remoção local de amostras e inspeção do texto completo.

Os grupos lembram diferentes settings, mas são **fixtures de apresentação**. Seus valores não são resultados de cálculos da engine nem validam as regras desses settings. Os modelos podem ser editados em `data/labs/card_hand_lab.json`, dentro do projeto Godot.

As opções visuais são temporárias. A cena não salva preferências, não inicia runs e não altera snapshots da sessão.

## Componentes e fronteiras

| Componente | Responsabilidade |
| --- | --- |
| `CardView` | Renderizar o modelo de apresentação: nome, custos, arte, efeitos, modificações e disponibilidade. É o mesmo componente do combate. |
| `CardHandView` | Organizar os controles de cartas, apresentar mão vazia e permitir rolagem horizontal. Foi extraído do combate e agora é compartilhado. |
| `CardHandLab` | Criar modelos de teste a partir do JSON local e oferecer controles de inspeção. |
| Presenters do combate | Continuam convertendo dados autoritativos da engine em modelos visuais no jogo normal. Não são substituídos por fixtures. |

A mão não conhece energia, mana, dano, decks ou condições de uso. Ela recebe controles. A carta recebe um modelo visual e não executa componentes de gameplay. A mesma definição de carta pode gerar modelos diferentes conforme o estado autoritativo recebido da engine.

Foi ativado `follow_focus` no componente compartilhado: navegar até uma carta fora da área visível faz a mão rolar até ela. Esse comportamento é fornecido pelo próprio [ScrollContainer da Godot](https://docs.godotengine.org/en/stable/classes/class_scrollcontainer.html), sem cálculos ou regras de jogo adicionais.

## Limites encontrados no desenho atual

Inspeção do código e da captura renderizada do grupo de stress:

1. **Nome e custos disputam a mesma linha.** Vários recursos deixam pouco espaço para identificar a carta.
2. **O efeito tem limite de três linhas.** Condições importantes podem desaparecer da face sem um indicador explícito de continuação.
3. **Muitas etiquetas ultrapassam a altura prevista.** O grupo com quatro modificações evidencia overflow na parte inferior.
4. **Texto secundário é pequeno.** Tipo, raridade e etiquetas usam fontes lógicas de 9–10; regras usam 13. A avaliação final deve considerar o tamanho efetivamente renderizado, não apenas o número configurado na Godot.
5. **Hover continua limitado pelo recorte do scroll.** A ampliação atual é pequena e ocorre dentro da mão; não substitui uma leitura ampliada independente.
6. **O layout é uma linha com scroll, não um leque adaptativo.** Isso serve como baseline confiável, mas ainda não resolve a experiência desejada para mãos grandes.

Esses limites estão deliberadamente expostos, não escondidos por fontes menores ou versões especiais da carta usadas apenas no laboratório.

## Pesquisa

### Magic: hierarquia e espaço útil

Na análise da mudança dos frames, Mark Rosewater explica problemas de leitura dos nomes, distribuição do espaço e excesso de destaque da moldura. A revisão priorizou funcionalidade e espaço para arte e informação. Jogadores novos dependem mais do nome; jogadores experientes também reconhecem a arte. Fonte: [Wizards — Frames of Reference](https://magic.wizards.com/en/news/making-magic/frames-reference-2003-01-27).

**Aplicação proposta:** anatomia previsível e separação entre identidade, custo e efeito. A moldura deve ajudar a identificar a carta sem competir com a leitura. A faixa de custos não deve esmagar o nome. Arte substituível continua tendo um slot estável, com placeholder identificável.

### Hearthstone: efeitos visíveis e posições consistentes

A Blizzard alterou as cartas Signature para apresentar texto diretamente na face e alinhá-lo à posição usada nas outras cartas. A justificativa foi facilitar leitura e comparação na mão; também foram adicionadas preferências para priorizar texto ou arte. Fonte: [Blizzard — Signature Card Features, patch 34.2](https://hearthstone.blizzard.com/en-us/news/24244424/34-2-patch-notes-events-and-features).

**Aplicação proposta:** o efeito necessário para decidir uma jogada deve aparecer na carta, não apenas no tooltip. Inspeção amplia e explica; não deve ser a única forma de descobrir o efeito básico. Modificações não devem deslocar os campos principais a cada atualização.

### Slay the Spire: testar combinações, não apenas cartas isoladas

Na entrevista com Anthony Giovannetti, da Mega Crit, o processo descrito envolve iteração, testes e seleção de cartas que produzam impacto, sinergias e estratégias diferentes. Isso não determina um layout de mão específico. Fonte: [Game Developer — entrevista com o designer de Slay the Spire](https://www.gamedeveloper.com/game-platforms/road-to-the-igf-mega-crit-games-i-slay-the-spire-i-).

**Aplicação proposta:** validar leitura de combinações como múltiplos procs, condensação, transferência condicional e modificações simultâneas. Uma carta simples e bonita não prova que o componente suporta as cartas compostas do core. Os fixtures exercitam a apresentação; testes da engine continuam responsáveis pelo comportamento.

### Acessibilidade: preservar leitura e redundância

A Microsoft recomenda tamanho e apresentação de texto configuráveis. A XAG 101 apresenta referência mínima de 18 pixels visíveis para PC em 1080p e orientação de ampliação até 200%. A medição considera o texto realmente renderizado, não o valor nominal da fonte. Fonte: [Microsoft — XAG 101, Text display](https://learn.microsoft.com/en-us/xbox/accessibility/xbox-accessibility-guidelines/101).

**Aplicação proposta:** não encolher a fonte conforme aumenta a mão. Ajustar espaço, sobreposição, rolagem ou leitura ampliada. O intervalo atual de 90–120% do laboratório é uma ferramenta de comparação, não uma implementação completa dessas recomendações.

A XAG 103 recomenda não transmitir informação apenas por cor, usando texto, símbolos e outros canais complementares. Fonte: [Microsoft — XAG 103, Additional channels](https://learn.microsoft.com/en-us/xbox/accessibility/xbox-accessibility-guidelines/103).

**Aplicação proposta:** recursos com símbolo, quantidade e nome acessível; alterações com ícone e rótulo. Verde/vermelho não deve ser a única diferença entre buff e debuff. A identidade de uma tag de efeito não deve ser confundida com o recurso usado para pagar seu custo.

A XAG 112 orienta navegação consistente e ordem de foco compreensível para diferentes formas de input. Fonte: [Microsoft — XAG 112, UI navigation](https://learn.microsoft.com/en-us/xbox/accessibility/xbox-accessibility-guidelines/112).

**Aplicação proposta:** manter ordem e seleção estáveis, revelar a carta focada e permitir inspeção por teclado/controle, sem depender exclusivamente de hover.

## Direção recomendada para o próximo refinamento

As recomendações abaixo são decisões propostas para este projeto, não regras universais extraídas das fontes.

1. **Fixar a anatomia da carta.** Nome; faixa própria de custos; arte; identidade/tag; efeito; alterações e disponibilidade. A quantidade de recursos não deve alterar o espaço do título.
2. **Separar leitura rápida e leitura completa.** Resumo fiel na mão e uma carta ampliada fora do recorte do scroll, com efeitos completos e fontes das alterações. Indicar explicitamente quando existe informação adicional. Não inventar resumos que mudem condições ou ordem dos efeitos.
3. **Resolver crescimento de conteúdo.** Custos, tags e alterações precisam de política de overflow. Para conteúdo arbitrariamente longo, usar expansão/inspeção; nenhum retângulo finito comporta todo JSON sem limites de apresentação.
4. **Comparar linha adaptativa e leque.** Primeiro reduzir espaçamento, depois avaliar sobreposição controlada. Manter scroll como fallback para mãos grandes. Não impor um limite de cartas por decisão visual e não diminuir as fontes para caber.
5. **Estabilizar a interação.** Carta selecionada acima das demais, área clicável previsível, foco preservado e alterações incrementais. Comprar, remover ou enriquecer a inspeção não deve reconstruir visualmente todas as cartas sem necessidade.
6. **Configurar apresentação, não duplicar regras.** Dimensões, arte, símbolos, cores e políticas de layout podem ser descritos por configuração visual. Custo, resultado dos efeitos, mudanças e disponibilidade continuam vindo da engine/presenter no jogo normal.

Começar pelo layout da carta e só depois pela distribuição da mão. Caso contrário, o leque pode esconder problemas que continuam existindo dentro de cada carta.

## Validação desta entrega

```powershell
godot --headless --path examples/godot-engine-showcase --script res://tests/card_hand_lab.gd
godot --headless --path examples/godot-engine-showcase --script res://tests/resolutions.gd -- --layout-smoke
godot --headless --path examples/godot-engine-showcase --script res://tests/layers.gd
```

O teste do laboratório confirma compartilhamento dos componentes com o combate, layouts em 1280×720, 1920×1080 e 2560×1080, português/inglês, escalas de 100%/120%, mão vazia e até 40 cartas, foco com rolagem, estabilidade das fontes ao selecionar e ausência de alteração de sessão ou preferências persistidas.

Uma captura com renderização real foi inspecionada em 2560×1080. Os testes de layout não afirmam que todo efeito cabe na face atual; essa limitação está registrada acima. O encerramento headless ainda emite o aviso preexistente de duas instâncias ObjectDB, associado ao áudio da demo; não foram feitas mudanças no áudio nesta entrega.

## Atualização — refinamento da mão implementado

O diagnóstico acima registra a baseline da criação do laboratório. A mão deixou
de ser uma linha reconstruída a cada atualização: agora reconcilia instâncias e
oferece leque suave, leitura sem sobreposição, overflow, foco por ordem, visão
geral persistente e drag opcional. A anatomia, cores e `CardView` permanecem
compartilhados. Destaques no recorte são cópias passivas, não novas autoridades
de input ou gameplay.

Os testes locais cobrem até 100 cartas; o combate real foi verificado em
`default` e `ascendant`, PT/EN, 100/120% e quatro resoluções. O relatório de
[implementação e validação](../plans/CARD_HAND_UX_IMPLEMENTATION_PLAN.md#9-implementação-e-verificação)
contém componentes, comandos de teste, medições e a ressalva externa de branches.
Os dialogs usam a tematização de [AcceptDialog](https://docs.godotengine.org/en/stable/classes/class_acceptdialog.html#theme-property-descriptions)
e [Window](https://docs.godotengine.org/en/stable/classes/class_window.html#theme-property-descriptions)
da Godot para manter a apresentação coerente com a demo.
