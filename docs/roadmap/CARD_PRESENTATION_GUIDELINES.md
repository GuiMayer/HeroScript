# Diretrizes de apresentação das cartas

Data: 2026-10-07.

## Objetivo

A carta deve comunicar o que custa, o que exige e o que faz. A organização deve continuar funcionando quando o setting trocar os recursos, os tipos de carta e os efeitos. A Godot apresenta essas informações; a engine continua responsável pelas regras, pelas condições e pelos cálculos.

Este documento consolida as diretrizes discutidas na conversa e as decisões de raridade e valores. Complementa o [laboratório e a pesquisa de design](CARD_HAND_DESIGN_LAB_AND_RESEARCH.md). Não modifica os documentos de game design nem define novas regras de gameplay.

## Anatomia da carta

| Informação | Lugar | Diretriz |
| --- | --- | --- |
| Nome | Cabeçalho | Posição estável. Nome longo não comprime custos nem vira texto vertical. O nome completo fica nos detalhes. |
| Upgrade permanente | Indicador perto do nome | Usar um indicador separado. Não confundir upgrade da carta com status do personagem ou influência temporária. |
| Custo de uso | Faixa própria abaixo do nome | Um token por recurso, com símbolo e quantidade. `+` significa pagamento conjunto; `OR` significa opções alternativas publicadas pela engine. |
| Arte | Área central reservada | Usar `ArtSlot`, com placeholder identificável pelo nome enquanto não houver arte. Substituir a arte sem refazer o layout. |
| Tipo e afinidade | Linha abaixo da arte | Mostrar tipo e tags com significado para o jogador. Tags técnicas ficam nos detalhes. Os rótulos são configuráveis na apresentação. |
| Raridade | Borda externa e texto discreto | A borda da carta, do tooltip e do painel de inspeção deve usar a mesma cor. O nome da raridade continua disponível, evitando depender só da cor. |
| Condição para usar a carta | Faixa antes dos efeitos | Mostrar o requisito e, quando publicado, se ele foi atendido. Não avaliar expressões na Godot. |
| Efeitos | Corpo principal | Estrutura de leitura: verbo, valor, recurso/status e alvo. Efeitos de recursos não presumem que dano significa reduzir vida. |
| Condição de um efeito | Junto do efeito correspondente | Não apresentar uma condição interna como impedimento para jogar a carta inteira. Condições complexas sem descrição recebem indicação para inspecionar. |
| Chance, duração e momento de execução | Junto do efeito | Não misturar esses qualificadores com custo ou raridade. Efeitos encadeados indicam antes/depois do impacto. |
| Repetição, orçamento, condensação e continuação | Palavras-chave de comportamento | Mostrar os parâmetros importantes; explicar a operação completa nos detalhes. Condensar stacks não significa obrigatoriamente causar dano. |
| Alterações contextuais | Faixa separada de alterações | Mostrar que existem influências e suas origens. Não somar alterações de parâmetros diferentes para decidir se a carta foi “buffada”. |
| Disponibilidade | Rodapé | Mostrar seleção ou motivo de indisponibilidade. Não substituir o custo pela palavra “indisponível”. |
| Fórmulas, buckets e contribuições | Inspeção | Informações para entender a origem dos valores. Não ocupar a leitura principal com identificadores técnicos. |
| Preço de loja e recompensa de decomposição | Contexto de aquisição / inspeção | Não são custo de uso. Uma oferta pode mostrar um preço; isso não transforma esse preço em custo de jogar a carta. |
| Slots, bundles, capacidades e histórico de transformações | Inspeção e telas de transformação | São informações de composição, não precisam ocupar permanentemente a face da carta. |
| Trigger | Inspeção técnica | O tipo existe no contrato, mas o validador atual rejeita triggers de carta sem ciclo executável. Não apresentar como funcionalidade disponível. |

## Cores dos valores dos efeitos

Estas cores foram definidas para o projeto, não são regras universais de cardgames:

| Comparação | Cor padrão | Significado |
| --- | --- | --- |
| Atual igual à base | Branco `#ffffff` | Sem alteração numérica. |
| Atual menor que a base | Vermelho `#ff7373` | Valor reduzido. |
| Atual maior que a base | Azul `#72b7ff` | Valor aumentado. |
| Sem comparação confiável | Branco | Não inventar uma mudança. Indicar valores base ou cálculo dependente da engine. |

Aplicar a cor somente ao número do efeito, não ao parágrafo inteiro. Dois números da mesma carta podem ter cores diferentes. A política serve para quantidade de recurso, stacks e outros parâmetros numéricos; não depende de existir um recurso chamado `health`.

Azul significa **maior**, não necessariamente melhor. Vermelho significa **menor**, não necessariamente pior. Custos usam a identidade visual do recurso, não essa escala de comparação. A borda indica raridade, não elemento, custo ou disponibilidade.

### O que significa “base”

- Para um parâmetro literal do efeito, comparar o valor atual publicado com o literal da definição compilada original, antes dos upgrades da instância. Sem preview executável, pode-se mostrar a base efetiva da instância, deixando explícito que não é um resultado de combate.
- Quando a base depende de fórmula, usar `baseValue` do cálculo publicado pela engine. Não avaliar a fórmula novamente na Godot. Essa base calculada pode já incluir entradas do personagem; não fingir que é uma versão da fórmula com o personagem zerado.
- Para orçamento distribuído, comparar o valor publicado do impacto com a base publicada desse cálculo. Não comparar uma fatia com o orçamento total e marcá-la como penalidade.
- Se diferentes impactos ou alvos publicarem valores diferentes, mostrar “Varies — inspect”, com os impactos na inspeção. Não escolher um deles como resultado universal.
- Para efeitos filhos sem vínculo inequívoco de preview, mostrar a definição, como valor base, e deixar os resultados executados na inspeção. Não usar o cálculo do pai para colorir o filho.
- Valores inválidos ou sem base comparável ficam neutros. Texto livre não é interpretado por expressão regular para descobrir números ou cálculos.

A comparação é numérica, mas o escopo do preview também importa: alvo selecionado, opção de pagamento e snapshot. O resultado calculado de um efeito não deve ser confundido com a variação final do recurso depois de limites ou regras de aplicação. Os detalhes mostram o cálculo e os fatos de aplicação separadamente.

## Raridade e estados de interação

Paleta inicial editável:

| Raridade | Cor |
| --- | --- |
| Common | `#c9cbd3` |
| Uncommon | `#79d58c` |
| Rare | `#62a7f1` |
| Legendary | `#e7b75d` |
| Desconhecida / sem informação | `#969aaa` |

Usar o identificador canônico da raridade, separado do rótulo traduzido. Uma raridade ainda não recebida não deve ser inventada como Common. Identificadores numéricos do contrato são normalizados na projeção.

Hover e seleção podem alterar o fundo. Foco usa uma borda mais grossa da mesma raridade. Indisponibilidade deve aparecer em texto: tingir a carta inteira mudaria as cores dos números. Alto contraste escurece o fundo sem apagar as cores semânticas.

## Leitura e detalhes

- Não diminuir a fonte para encaixar mais cartas na mão. Usar rolagem horizontal e manter o foco de teclado/controle visível.
- A mão adaptativa usa arco suave e sobreposição limitada; o modo leitura mantém cartas retas e separadas. Visão geral é persistente e usa as mesmas instâncias/modelos de apresentação. Hover eleva/endireita sem mover vizinhos nem animar fontes.
- No overflow, uma carta cortada pode ganhar uma cópia de leitura passiva. Ela não rouba input nem permite comandos; os hitboxes continuam vinculados aos slots de repouso. Inspeção/visão geral cancelam gestos e destaque transitório.
- O corpo da carta tem espaço limitado, com indicação explícita de texto completo nos detalhes. O tooltip usa a mesma apresentação de números. A inspeção aberta por botão permite rolagem, seleção de texto e acesso aos dados completos.
- A janela de inspeção é o caminho persistente para textos extensos; o tooltip é um resumo passivo, sem rolagem, seleção ou foco. Seu texto e suas linhas são limitados, com indicação explícita para abrir a inspeção. Não depender de conseguir mover o cursor para dentro de um tooltip transitório.
- Cabeçalhos e badges podem abreviar na face. A inspeção deve preservar nomes, fontes e descrições completos, com quebra de linha.
- Reaplicar escala de texto antes de apresentar uma carta enriquecida por resposta assíncrona, evitando um frame com fonte diferente.
- Manter contagem/altura da mão estáveis ao selecionar ou esvaziar a mão. Não trocar o componente visual nem animar sua entrada novamente só porque chegaram detalhes.
- Não depender apenas da cor: detalhes mostram `Base → Current`, nome da raridade, fontes de alteração e motivo de indisponibilidade. Isso é uma medida de acessibilidade, não uma declaração de conformidade completa.

## Componentes e configuração

| Arquivo da demo | Responsabilidade |
| --- | --- |
| `scripts/ui/card_visual_style.gd` | Variáveis exportadas de raridade, valores, dimensões e tipografia. Não contém regras de jogo. |
| `data/default_card_visual_style.tres` | Instância compartilhada da política visual padrão. |
| `scripts/ui/card_view.gd` | Anatomia da face, estados, custos e tooltip. Recebe um modelo de apresentação. |
| `scripts/ui/card_effect_text.gd` | Texto com segmentos numéricos tipados e comparação de cores. Não executa BBCode vindo de conteúdo. |
| `scripts/ui/card_details_panel.gd` | Dois modos explícitos: resumo passivo para tooltip e inspeção completa, persistente e rolável. Compartilham cores e texto numérico, não os controles de interação. |
| `scripts/presentation/card_section_presenter.gd` | Projeção dos componentes e fatos de preview da engine em seções legíveis. |
| `scripts/presentation/combat_presenter.gd` | Une snapshot, ações legais e inspeção da mesma sequência. Deduplica custos por pagamento, não por quantidade de alvos. |
| `data/presentation.json` | Símbolos e cores dos recursos, nomes de tags e descrições de fluxos/receitas. Não cria regras. |
| `scripts/ui/card_hand_view.gd` | Reconciliação por instância/escopo, slots persistentes, destaque, foco, overflow, estado vazio e intenções de seleção/drag. Não chama REST. |
| `scripts/ui/hand_visual_style.gd` + `data/default_hand_visual_style.tres` | Ângulo, arco, passo mínimo exposto, elevação, duração, padding e limiar de drag. Não configura capacidade da pilha. |
| `scripts/ui/hand_layout.gd` | Cálculo puro de posições, rotações, extensão e hitboxes estáveis. |
| `scripts/ui/hand_interaction.gd` + `hand_hit_surface.gd` | Estado local de interação e captura de input, independente das faces animadas. |
| `scripts/ui/hand_overview_dialog.gd` | Consulta persistente com a mesma mão em modo leitura, inspeção e retorno por identidade. |

Edite o recurso `.tres` no Inspector para configurar a política padrão, ou atribua outra instância a `CardView.visual_style`. `rarity_colors`, `unknown_rarity_color`, `base_value_color`, `lower_value_color` e `higher_value_color` são variáveis, não cores espalhadas por telas. Tooltip, inspeção e cópia animada da carta recebem a mesma instância.

`card_size`, `text_scale_height_margin` e `effective_size()` centralizam a reserva de dimensões; a mão também respeita o mínimo efetivo dos controles. Preferências de modo, intensidade e drag são locais ao jogador, em **Configurações → Geral → Mão de cartas**. `I` inspeciona e `H` abre a visão geral por padrão; ambas as ações são remapeáveis. O [plano de UX da mão](../plans/CARD_HAND_UX_IMPLEMENTATION_PLAN.md#9-implementação-e-verificação) registra a implementação e suas limitações de validação.

O resumo usa `tooltip_effect_rows`, `tooltip_effect_lines` e `tooltip_label_lines` para limitar o espaço, sem diminuir a fonte. `inspection_height` define a altura mínima do painel completo; apenas esse modo cria um `ScrollContainer` com foco de teclado. O tooltip não carrega o journal nem a descrição técnica extensa (`inspectionText`).

O modelo fornece `rarityId`, `rarity`, `costs`, `costOptions`, `requirements`, `effectRows`, `identityTags`, `behaviors`, `changeBadges`, `availability` e `inspectionText`. Um segmento numérico usa `text`, `value` e `baseValue`. Texto não estruturado continua legível, mas fica neutro: não é possível inferir com segurança seu valor base.

Opções de pagamento ainda não cotadas pela engine recebem indicação de custo alternativo pendente (`costsKnown = false`), nunca “0” por ausência de dados. A lista de ações pode oferecer vários alvos com o mesmo pagamento: isso não cria opções de custo adicionais.

Para novos settings, nomes de condições podem ser configurados por componente em `cards.<id>.conditions`; descrições de fluxos e condensação usam `card_flow_labels` e `condensation_labels`. Quando falta uma descrição, a interface aponta para os detalhes em vez de atribuir semântica arbitrária ao identificador.

## Referências e interpretação

A separação de nome, custo, arte, tipo e regras aproveita a anatomia documentada de [Magic: The Gathering](https://magic.wizards.com/en/news/feature/anatomy-magic-card-2006-10-21). A posição exata dos custos e a raridade na borda são decisões deste projeto.

A distinção entre requisitos, ativação e resolução segue o princípio de clareza explicado pela [Konami sobre texto de cartas](https://www.yugioh-card.com/en/play/psct/psct-3/), sem importar suas regras ou pontuação como contrato da HeroScript.

O uso de texto junto das cores segue a orientação de não transmitir informação apenas por cor da [Xbox Accessibility Guideline 103](https://learn.microsoft.com/en-us/xbox/accessibility/xbox-accessibility-guidelines/103). A Godot oferece [tooltips personalizados em Control](https://docs.godotengine.org/en/stable/classes/class_control.html#class-control-private-method-make-custom-tooltip); aqui a consulta rápida e a inspeção persistente usam modos distintos do componente visual.

## Verificação

```powershell
godot --headless --path examples/godot-engine-showcase --script res://tests/card_presentation.gd
godot --headless --path examples/godot-engine-showcase --script res://tests/card_hand_lab.gd
godot --headless --path examples/godot-engine-showcase --script res://tests/hand_ux.gd
```

O laboratório inclui valores iguais, reduzidos e aumentados, além de cartas com vários recursos, alterações, nomes extensos e texto longo. São fixtures visuais, não resultados reais de gameplay. O teste de apresentação cobre a correspondência com cálculos publicados, cores compartilhadas, sobrescrita da política, localização, estados de interação e inspeção.
