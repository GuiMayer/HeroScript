# Repaginação visual — Marfim / circuito impresso

Data: 2026-10-11.

Status: etapa 0 executada; tema Marfim ainda não aplicado. Registro, capturas, testes e limites da base em [interface-baseline/2026-10-11](../design/interface-baseline/2026-10-11/README.md). Etapas 1–10 pendentes.

Direção escolhida pelo autor: [conceito 02 — Marfim](../design/interface-concepts/2026-10-11/02-ivory.png). Os conceitos e seus prompts permanecem como referência; o mockup não é uma especificação literal de regras ou layout.

Dependência de arquitetura adicionada após a revisão do frontend: [reestruturação da UI para estilos substituíveis](GODOT_UI_ARCHITECTURE_REFACTOR_PLAN.md). Executar primeiro os contratos/componentização desse plano e compartilhar sua infraestrutura de tema; não criar sistemas paralelos em ambos. A seção 7 do novo plano define a ordem combinada, preservando esta direção artística.

## 1. Objetivo e limites

Transformar a apresentação do jogo em uma interface de papel marfim, tinta escura e vermelhão, identificada por feixes finos de linhas paralelas com mudanças de direção de 45°. O resultado deve parecer uma linguagem visual deliberada e consistente, não apenas o tema atual com novas cores.

A repaginação cobre combate, cartas, jornada, ofertas, diálogos, menu principal, pausa, configurações, histórico, replay, timeline, códice e ferramentas. Deve funcionar com todos os settings e com conteúdo desconhecido que use os contratos existentes.

Preservar:

- Engine como autoridade de regras, cálculos, ações legais, recursos, progressão e replay.
- Separação entre comunicação, aplicação, projeção de dados e interface.
- Mão compartilhada, interação por identidade e geometria adaptativa.
- Preview contextual separado da descrição da carta.
- Sidebar flutuante; recolhida, apenas sua pequena alça com seta permanece.
- Inglês como fallback e chaves de tradução; suporte a português.
- Áudio, remapeamento, preferências, resoluções e progressão separada por setting.

Não inclui balanceamento, novas mecânicas, mudanças de endpoints, nova lore ou arte definitiva para cada carta/inimigo. O novo sistema de placeholders deve permitir substituir essas artes depois, sem refazer componentes.

## 2. Diagnóstico do cliente atual

A base está em `examples/godot-engine-showcase/`. Há componentes reutilizáveis suficientes para fazer a mudança sem criar uma segunda interface concorrente.

| Área | Situação observada | Implicação para a mudança |
| --- | --- | --- |
| Tema | `AppTheme` concentra helpers, mas usa paleta escura, sombras de texto e caixas arredondadas. Várias telas também possuem cores locais. | Trocar apenas suas constantes não resolve o conjunto; migrar overrides por função visual. |
| Alto contraste | `apply_view_preferences()` escurece fundos explicitamente. | Tornar a variante consciente do novo tema; não reintroduzir caixas escuras aleatórias. |
| Preferências visuais | Fontes e estilos têm metadados de base para reaplicação. | Invalidar bases quando o tema muda, sem acumular escala ou exibir uma fonte intermediária. |
| Fundo | `EmberBackground` desenha gradientes, montanhas e partículas, com atualização contínua quando habilitada. | Substituir a direção visual, mantendo movimento reduzido e evitando redesenho permanente de decoração estática. |
| Cartas | `CardView`, `CardDetailsPanel` e `CardVisualStyle` compartilham anatomia e política semântica. | Reestilizar os componentes existentes, incluindo laboratório, cópias passivas, ofertas e inspeção. |
| Mão | `HandVisualStyle`, layout e interação são separados das regras das pilhas. | Não trocar o sistema da mão durante o redesign; ajustar apenas apresentação onde necessário. |
| Artes | `ArtCatalog`, `ArtSlot` e `art_manifest.json` já oferecem slots, fallback e placeholders. | Usar esse caminho, sem embutir textos de interface nas imagens. |
| Configurações | Navegação por categorias, página, rolagem e rodapé já existem. | Manter a organização; modernizar a aparência e todos os controles nativos. |
| Pausa | Modal usa canvas separado para ficar acima das cartas. | Preservar essa autoridade de camada, bloqueio de input e restauração de foco. |
| Atualização de telas | Combate e atividades preservam contexto e reconciliam dados. | Não reconstruir telas a cada resposta ou animação para aplicar o tema. |

Não foram encontrados arquivos `.ttf`/`.otf` dentro da demo na inspeção atual. A tipografia proposta exige escolher, licenciar e incorporar fontes locais, não depender da instalação do computador.

Há mudanças locais anteriores, incluindo a separação do preview e seus testes. Elas são a base funcional a preservar, não arquivos a descartar para começar a repaginação.

## 3. Direção visual

### 3.1. Identidade

- Marfim como superfície dominante; tinta escura para texto, silhuetas e estrutura.
- Vermelhão como acento reservado a ações importantes e pontos de atenção.
- Formas predominantemente retas, com pequenos chanfros quando úteis. Evitar cápsulas em todos os controles.
- Espaço vazio como parte da composição: campo de batalha aberto, agrupamentos claros e pouca moldura.
- Sem gradientes roxos, brilho genérico, sombras pesadas ou circuitos atravessando textos.
- Fundo de cenário com contraste contido; atores, intenções, cartas e comandos devem dominar a leitura.
- Motivo de circuito em cantos, divisores e assinaturas visuais, não em cada centímetro da tela.

O mockup sugere a direção, mas suas palavras, custos, pips, números e bordas não substituem dados publicados pela engine. Não criar indicadores de progresso ou estados que o contrato não forneça.

### 3.2. Tokens iniciais

Valores abaixo são candidatos para prototipagem, não paleta certificada. Validar contraste nos pares reais, incluindo estados desabilitados e escala de texto.

| Papel | Cor inicial | Uso |
| --- | --- | --- |
| `paper` | `#F2EBDD` | Fundo predominante. |
| `paper_raised` | `#FAF5EB` | Cartas, menus e painéis de leitura. |
| `ink` | `#20201C` | Texto principal, silhuetas, divisores fortes. |
| `ink_secondary` | `#5B574F` | Texto secundário ainda legível. |
| `line` | `#827B70` | Estrutura discreta; não única pista de estado. |
| `accent` | `#B83E25` | Ação principal e assinatura vermelhão. |
| `accent_pressed` | `#98311D` | Estado pressionado da ação principal. |
| `inverse_surface` | `#20201C` | Áreas pequenas de leitura invertida. |
| `inverse_text` | `#FFFFFF` | Texto em superfícies invertidas. |

Sucesso, aviso, erro, foco, recursos e raridade têm papéis próprios. Não transformar todas as cores semânticas em vermelhão. Recursos configurados continuam identificados por símbolo, nome e cor de apresentação, com fallback para recursos desconhecidos.

### 3.3. Resolver branco sobre marfim sem mudar a regra das cartas

A diretriz existente é: número-base branco `#FFFFFF`, reduzido vermelho `#FF7373`, aumentado azul `#72B7FF`. Branco diretamente sobre papel claro não é legível.

Solução proposta: carta marfim com um **painel compacto de efeitos em tinta escura**, como uma faixa de impressão invertida. Apenas os segmentos numéricos recebem as três cores; a prosa permanece neutra e clara. Tooltip e inspeção usam a mesma superfície atrás de seus efeitos, sem tornar toda a janela escura.

- Preservar a comparação publicada; maior não significa necessariamente melhor.
- Não recolorir o parágrafo inteiro nem inferir mudanças de valores pelo texto.
- Manter custo por recurso fora dessa escala de comparação.
- Não remover as cores com um filtro de desabilitado sobre a carta inteira.
- Usar `CardVisualStyle` para as variáveis de valores e raridade; não duplicar essa política no tema global.

Essa é uma adaptação consciente do mockup para respeitar a decisão anterior. Alterar o neutro para preto seria outra opção, mas depende de decisão explícita do autor; não faz parte deste plano.

Raridade continua na borda da carta, tooltip e inspeção, acompanhada do nome. A paleta atual deve ser testada sobre marfim antes de propor ajustes: pode-se usar um filete de tinta adjacente para contraste sem substituir a cor da raridade. Foco e seleção não devem sobrescrever essa borda com vermelho.

### 3.4. Tipografia e ícones

- Uma fonte de títulos com caráter editorial; uma fonte de leitura para textos, custos e controles.
- Incorporar fontes licenciadas e seus avisos, com acentos portugueses, pontuação, numerais e fallback de glifos.
- Não usar caixa alta em descrições extensas. Nomes longos quebram ou abreviam com acesso ao nome completo nos detalhes.
- Definir níveis de título, nome de carta, corpo, legenda e número; não escolher tamanhos independentemente em cada tela.
- Ícones próprios simples, em tinta, com espessura consistente. Significado importante também aparece por texto/tooltip acessível.
- Não reduzir fontes para acomodar mãos grandes nem animar tamanho de fonte no hover.

### 3.5. Circuitos de 45°

Criar um componente decorativo reutilizável, `CircuitTrace`, desenhado por geometria da Godot ou assets vetoriais pequenos. Não usar o mockup completo como textura de interface.

Parâmetros visuais: quantidade de trilhas, espessura, distância, cor, orientação, extensão e terminal. Ponto de partida na resolução de referência: 4–6 trilhas, 1–1,5 px de espessura e 3–4 px entre trilhas; calibrar por escala de exibição, mantendo linhas visíveis.

- Diagonais com deslocamento horizontal e vertical igual, mantendo direção de 45°.
- Distância constante entre trilhas nos cotovelos; evitar simples cópias deslocadas que se cruzem nas curvas.
- Terminais discretos, como ponto ou quadrado, sem sugerir interatividade.
- Recorte dentro dos limites decorativos e margem protegida em volta do conteúdo.
- Sem captura de mouse, foco, influência sobre tamanho mínimo ou atualização por frame quando estático.
- Variante de alto contraste simplifica ou remove a decoração. Movimento reduzido não perde informação.

## 4. Arquitetura da apresentação

Manter o fluxo existente:

```text
REST / EngineGateway → GameSession → presenters → componentes de UI
                                                    ↑
                                tema + estilos locais + ArtCatalog
```

### 4.1. Tema global

Introduzir um recurso visual, por exemplo `VisualThemeProfile`, com uma instância `data/themes/ivory_theme.tres`. Ele possui cores por papel, fontes, espaçamentos, estilos de controles e configuração decorativa compartilhada.

`AppTheme` continua como fachada para construir o `Theme` da Godot e os helpers. Migrar seus consumidores gradualmente para papéis semânticos. Não basta trocar `INK` de branco para preto: alguns controles precisam de texto invertido, recursos têm identidade própria e há argumentos/defaults e overrides locais.

O perfil é do cliente, não do ruleset da engine. Aplicar Marfim como direção padrão aos settings existentes. Não adicionar um seletor público de vários temas nem manter permanentemente duas árvores de UI para esta tarefa.

### 4.2. Componentes especializados

- `CardVisualStyle`: raridade, valores, anatomia e tipografia da carta; incluir superfícies da área de efeitos sem duplicar políticas numéricas.
- `HandVisualStyle`: arco, espaçamento e elevação; continuar sem regras de capacidade ou custo.
- `CircuitTrace`: desenho passivo, sem conhecer cartas, recursos ou comandos.
- `ArtCatalog`/`ArtSlot`: associação de IDs a artes; reutilizar o manifesto e os fallbacks existentes.
- `AppTheme`: aparência de botões, painéis, inputs, barras, popups e estados.
- Presenters: continuar projetando fatos e textos; não receber responsabilidades de desenho, chamadas HTTP ou novos cálculos.

Revisar os metadados de base de estilos/fontes ao aplicar preferências, para que o perfil ativo seja a fonte da escala. Não introduzir uma nova infraestrutura global de temas além do necessário para essa mudança.

## 5. Tradução da direção para cada tela

### 5.1. Carta, tooltip e inspeção

Usar o mesmo `CardView` em combate, laboratório, ofertas, códice, histórico e ferramentas.

1. Cabeçalho marfim: nome e marcador de upgrade.
2. Faixa estável de custos: um token por recurso; pagamentos conjuntos e alternativos continuam distintos.
3. Arte central: composição em tinta, com acento controlado e placeholder de nome quando aplicável.
4. Linha de tipo/afinidade e requisito de uso, quando presentes.
5. Área invertida de efeitos: verbo, quantidade e categoria de alvo, sem nome de ator.
6. Comportamentos e influências: distinção entre upgrade, origem temporária e operação de efeito.
7. Rodapé: disponibilidade e acesso aos detalhes.

Circuitos ficam nos cantos da arte ou divisores; não competem com a borda de raridade. Conteúdo extenso permanece na inspeção persistente. Tooltip continua resumo passivo, sem scrollbar ou controles impossíveis de focar.

Hover, foco e seleção usam os mecanismos existentes. Não mudar dimensões, padding ou fontes entre estados; a elevação não desloca as outras cartas. A mão vazia mantém altura e uma mensagem horizontal legível.

### 5.2. Combate

- Campo aberto, atores com silhuetas fortes e linha visual de apoio; remover aparência de coleção de painéis escuros.
- Intenções próximas ao respectivo inimigo, com verbo/ícone, quantidade e qualificadores publicados. Nunca escondê-las atrás da decoração ou sidebar.
- Vida e demais recursos com rótulos e valores; recursos desconhecidos continuam funcionais.
- Status e modificadores em agrupamento compacto, com caminho para informação completa.
- Cartas flutuantes na faixa inferior, sem um grande container preenchido delimitando a mão.
- Preview contextual em uma área própria, visualmente separada da mão e do texto de regras. Reservar espaço estável e limitar overflow com leitura acessível.
- Encerrar turno como ação primária em vermelhão; ações secundárias discretas e estáveis.
- Sidebar clara flutuante e sobreposta, não coluna que reduz a arena. Fechada, só a seta; aberta, não cobrir os comandos essenciais.
- Pausa e inspeção acima das cartas elevadas, sem vazamento de clique ou animação decorativa.

Projetar para números variáveis de aliados/inimigos; não copiar a composição fixa de um herói e dois inimigos do mockup. Em telas largas, ampliar respiro/cenário, não fontes e cartas indefinidamente. Em telas menores, priorizar leitura, rolagem explícita e acesso aos controles.

Aplicar a mesma linguagem ao combate histórico, sem acrescentar comandos executáveis ao replay.

### 5.3. Jornada, ofertas e diálogos

- Jornada como mapa impresso: nós, ligações e estados claros; disponível, visitado e bloqueado distinguidos por forma, marca e texto, além da cor.
- Quando houver rotas ramificadas, desenhar as ligações publicadas; não inventar caminhos ou calcular disponibilidade na Godot. Preservar uma apresentação legível/rolável para grafos extensos ou formatos que não caibam no layout principal.
- Painel da atividade como página editorial, com título, conteúdo e decisões agrupados.
- Recompensas e crafting com carta selecionada, oferta e confirmação claramente separadas. Preço de compra não ocupa a faixa de custo de uso da carta.
- Lojas com hierarquia entre item, preço, recursos disponíveis e disponibilidade, sem reconstrução visual completa a cada compra.
- Diálogo como caixa de leitura clara, retrato opcional e escolhas consistentes com os outros botões.

### 5.4. Menus e ferramentas

- Principal: composição editorial, título do setting selecionado e ações hierarquizadas. Indicadores técnicos detalhados vão para informação/debug, sem eliminar o estado de conexão e erros necessários.
- Pausa: folha clara compacta sobre escurecimento neutro; continuar, configurações e ações destrutivas separados.
- Configurações: preservar categorias e navegação atuais; padronizar inputs, sliders, toggles, combos, bindings, mensagens e estados salvos.
- Histórico: lista de registros legível, com setting, resultado e ações disponíveis; evitar estética de dashboard com caixas repetidas.
- Timeline/replay: comandos e branches identificáveis; seleção, sequência e modo histórico distinguíveis. Não fazer as trilhas decorativas parecerem ligações reais da timeline.
- Códice: grade/lista e inspeção usando os mesmos componentes das cartas.
- Sandbox/debug: mesma família visual, mas informação técnica completa e, quando útil, fonte monoespaçada. Não esconder ferramentas autorizadas para tornar a tela mais limpa.

## 6. Etapas de implementação sequencial

Cada etapa termina com verificação e um commit próprio quando a implementação for autorizada. Os commits abaixo são sugestões; os resultados executados ficam registrados no progresso.

### Etapa 0 — Preservar a base e registrar referências

Executada em 2026-10-11. Alterações funcionais anteriores preservadas em `d0d42cb`; baseline reproduzível e inventário registrados no documento vinculado acima. Capturas usam componentes reais com fixtures offline, não runs REST. A validação online permanece explicitamente pendente de uma API ativa.

- Inspecionar o diff pendente e separar mudanças funcionais anteriores do redesign.
- Registrar estado dos testes, capturas reais e avisos preexistentes; não chamar o mockup de baseline funcional.
- Inventariar telas, modais, controles e overrides locais de cor/fonte.
- Capturar combate com mão vazia/cheia, preview, sidebar aberta/fechada, pausa, jornada, oferta, configurações e replay.
- Registrar tempos de atualização visual e número de requisições nos mesmos fluxos para comparação posterior.

Saída: baseline verificável e lista de migração. Commit da base anterior apenas após revisar seu conteúdo, sem misturá-la ao tema.

### Etapa 1 — Especificação visual e recursos

- Fixar tokens, tipografia, escala de espaçamentos, anatomia da carta e regras de circuitos.
- Selecionar fontes locais, verificar licença e glifos EN/PT.
- Criar o perfil Marfim e estender os recursos especializados estritamente necessários.
- Documentar a área invertida de efeitos e a preservação das cores/raridade.

Arquivos centrais: novo perfil visual, `data/themes/`, `card_visual_style.gd`, recurso padrão e documentação.

Verificação: fixtures de tipografia e cores, comparações numéricas e recurso desconhecido.

Commit sugerido: `feat(ui): define ivory visual profile and typography`.

### Etapa 2 — Migrar o tema e controles básicos

- Atualizar `app_theme.gd` e remover cores locais substituíveis por papéis do perfil.
- Cobrir Label, RichTextLabel, Button, LineEdit, TextEdit, barras, sliders, toggles, OptionButton, PopupMenu, scrollbars e janelas usadas pela demo.
- Definir normal, hover, pressionado, foco, selecionado, indisponível e erro sem variação de tamanho.
- Refatorar alto contraste e reaplicação de escala para respeitar superfícies claras/invertidas.

Verificação: galeria de controles, navegação teclado/controle, popups sem resíduos do tema antigo e preferências aplicadas sem escala acumulada.

Commit sugerido: `refactor(ui): apply semantic ivory theme to shared controls`.

### Etapa 3 — Circuitos e fundo

- Implementar `CircuitTrace` passivo e testar curvas, espaçamento, recorte e redimensionamento.
- Substituir a apresentação de `EmberBackground` por fundo marfim contido, preservando seu papel e preferências pertinentes.
- Cachear geometria estática; redesenhar por mudança de tamanho/estilo, não por passagem de tempo.
- Aplicar os primeiros motivos em uma tela de demonstração, não automaticamente em todos os controles.

Verificação: diagonais, ausência de interseções, nenhuma captura de input, modo reduzido e alto contraste.

Commit sugerido: `feat(ui): add printed circuit ornament and ivory background`.

### Etapa 4 — Artes e placeholders coerentes

- Criar uma família inicial de SVGs de tinta para cartas, atores, atividades e cenário.
- Reservar áreas de arte com recorte/preenchimento consistente e silhuetas identificáveis.
- Atualizar `art_manifest.json` pelos slots existentes; manter fallback de cada categoria.
- Preservar nomes traduzidos como UI, nunca texto embutido no SVG.
- Não recolorir à força artes finais futuras; a compatibilidade deve vir dos slots e molduras.

Verificação: IDs conhecidos/desconhecidos, cenário recortado em ultrawide e substituição de placeholder por arte sem mudança de layout.

Commit sugerido: `feat(ui): introduce ink placeholder art through existing slots`.

### Etapa 5 — Cartas, detalhes e laboratório

- Reestilizar `card_view.gd`, `card_effect_text.gd`, `card_details_panel.gd` e inspeção.
- Usar o painel de efeitos invertido; conservar cores numéricas e bordas de raridade compartilhadas.
- Revisar custos múltiplos, condições, comportamentos, alterações e textos extensos.
- Atualizar o laboratório com fixtures do novo estilo; conservar mão adaptativa, leitura e cópias passivas.

Verificação: `card_presentation.gd`, `card_preview_separation.gd`, `card_hand_lab.gd` e `hand_ux.gd`. Validar 0–100 cartas, sem encolher fontes ou transformar essa quantidade em limite de gameplay.

Gate visual: aprovar uma carta e uma mão reais antes de espalhar a direção para todas as telas.

Commit sugerido: `feat(ui): restyle shared cards and inspection in ivory`.

### Etapa 6 — Combate completo

- Migrar `combat_screen.gd`, `actor_panel.gd`, retratos, `resource_view.gd`, HUD flutuante e combate histórico.
- Ajustar escala do campo, intenções, barras, status e rodapé mantendo fontes de dados e hitboxes.
- Integrar mão, preview separado e sidebar; conferir comportamento em atualização assíncrona.
- Aplicar o estilo ao modal de pausa suficiente para validar o conjunto de camadas.

Verificação: `combat_stage_layout.gd`, `pause_layers.gd`, `card_play_selection.gd` e fixtures históricas. Com API disponível, testar jogar carta, trocar alvo, terminar turno, vitória/derrota e retorno da pausa.

Gate visual: combate real EN/PT, 1280×720 e 2560×1080, sem oscilações de layout.

Commit sugerido: `feat(ui): redesign combat stage with floating ivory HUD`.

### Etapa 7 — Jornada e atividades

- Migrar `activity_screen.gd`, `route_view.gd`, ofertas, serviços e `dialogue_panel.gd`.
- Desenhar estado e ligações do mapa existentes sem alterar fluxo ou seleção de ações.
- Padronizar compra, recusa, escolha de recompensa, crafting e continuação automática existente.
- Manter foco, scroll e posições durante respostas da API.

Verificação: rotas lineares/ramificadas, oferta vazia, item indisponível, descrição longa e decisões de diálogo; repetir compras/escolhas sem movimentos da tela.

Commit sugerido: `feat(ui): restyle journey and activity decisions as printed pages`.

### Etapa 8 — Principal, pausa e configurações

- Finalizar `main.gd` e `settings_screen.gd`, incluindo toasts, confirmação destrutiva e conexão indisponível.
- Manter seleção de setting e continuidade separada, sem mudar saves ou preferências.
- Conferir todas as categorias, resolução 2560×1080, fullscreen, áudio, bindings e acessibilidade.
- Remover mensagens promocionais/técnicas redundantes da composição principal, mantendo informação útil acessível.

Verificação: `resolutions.gd`, `usability.gd`, pausa acima da mão, foco restaurado e configurações persistentes após reinício.

Commit sugerido: `feat(ui): refresh main pause and settings menus in ivory`.

### Etapa 9 — Histórico, timeline, replay, códice e ferramentas

- Migrar as telas restantes e os diálogos de visão geral/pilhas/ferramentas.
- Compartilhar estilos do combate e das cartas; não manter versões antigas apenas no replay.
- Distinguir contexto histórico, branch selecionada e permissões publicadas.
- Preservar legibilidade de dados técnicos e grandes listas.

Verificação: consultas históricas, alternância de branches permitida, reprodução, filtros, inspeção e restrições dos modos; nenhuma ação de gameplay surge por aplicação de tema.

Commit sugerido: `feat(ui): unify history replay and tools with ivory presentation`.

### Etapa 10 — Auditoria final e documentação

- Revisar cores/fundos/fontes locais restantes; exceptions precisam de papel semântico documentado.
- Ajustar contraste, truncamento, alinhamento e densidade com conteúdo real, não somente fixtures curtas.
- Comparar tempos de atualização, requisições e estabilidade com a etapa 0.
- Executar testes de fronteira e regressão, incluindo `layers.gd`, além dos testes de UI relevantes.
- Com API disponível, validar gameplay/replay e isolamento entre settings; comparar os mesmos comandos/entradas sem incluir metadados visuais na identidade determinística.
- Atualizar `ARCHITECTURE.md`, `ART_SLOTS.md`, diretrizes de cartas e capturas de referência após validação.

Commit sugerido: `test(ui): validate and document ivory redesign across settings`.

## 7. Matriz de validação e conclusão

| Dimensão | Casos mínimos |
| --- | --- |
| Resolução | 1280×720, 1280×800, 1920×1080, 2560×1080. |
| Localização | Inglês e português; nomes longos, acentos, glifos e números formatados. |
| Texto | Escala normal e máximo atualmente exposto, 1,2; nenhum aumento acumulativo ao reaplicar. |
| Acessibilidade | Alto contraste, movimento reduzido, teclado, controle e remapeamento. |
| Carta | Sem custo, vários recursos, custo alternativo, requisitos, upgrade, buff/debuff, efeitos longos e comportamento composto. |
| Mão | Vazia, uma carta, quantidade comum, overflow; hover, seleção, uso, drag quando habilitado e inspeção. |
| Combate | Vários atores, status extensos, alvo inválido, fim de turno, playback, sidebar e pausa. |
| Estado de aplicação | Carregando, sem conexão, erro, oferta vazia, run concluída e retorno ao menu. |
| Settings | Pacotes existentes e fixtures com IDs/recursos desconhecidos; continuar permanece isolado por setting. |

Adicionar testes offline do perfil/estados e um roteiro de captura visual, por exemplo `tests/ivory_theme.gd` e `tests/ivory_visual_smoke.gd`. Capturas são complementares: diferenças de rasterização de fontes não devem virar falsos erros rígidos entre máquinas. Usar verificações de geometria, invariantes e revisão visual junto delas.

Critérios para concluir:

- Toda tela usa a mesma linguagem; controles nativos e modais não escapam para o tema antigo.
- Número-base branco, reduzido vermelho e aumentado azul continuam legíveis e semanticamente corretos.
- Raridade, recurso, foco, seleção e perigo não são confundidos nem comunicados apenas por cor.
- Descrição de carta continua genérica; alvo e resultado contextual continuam no preview separado.
- Não há texto vertical acidental, fontes piscando, vizinhos se deslocando ou conteúdo interativo por baixo da pausa.
- Sidebar recolhida não ocupa coluna; aberta, não oculta ações essenciais.
- A troca de dados/ações não recria desnecessariamente a árvore visual nem aumenta chamadas REST.
- Decoração não interfere no input, não exige atualização contínua quando estática e não piora a responsividade medida.
- Nenhuma regra, fórmula, chance ou recurso passa a ser calculado pelo cliente.

## 8. Riscos e controles

| Risco | Controle |
| --- | --- |
| Branco invisível sobre marfim | Painel invertido compartilhado para efeitos; aprovação no laboratório antes da expansão. |
| Cores claras de raridade pouco visíveis | Testar filete de tinta/contraste adjacente e nome; não alterar a política silenciosamente. |
| Mudança global de constantes quebra texto invertido | Migrar por papel e contexto; revisar todos os overrides e argumentos padrão. |
| Preferências restauram estilos antigos | Bases de fontes/estilos ligadas ao perfil ativo e reaplicação idempotente. |
| Circuitos viram ruído ou parecem caminhos reais | Densidade limitada, área protegida e separação entre ornamento e informação de mapa/timeline. |
| Redesign reintroduz regressões recentes | Manter testes de pausa, preview, seleção e estabilidade como gates de cada etapa. |
| Tema funciona apenas no Ascendant | Componentes genéricos, manifesto com fallback e fixtures de recursos/atores variáveis. |
| Escopo cresce para arte e gameplay novos | Primeira entrega com placeholders coerentes; arte definitiva e mecânicas ficam em trabalhos separados. |

## 9. Primeiro marco recomendado

Implementar as etapas 0–5 e apresentar uma captura real do laboratório de cartas com a mão no novo tema. Confirmada a direção, concluir o combate da etapa 6 e usá-lo como referência de qualidade para jornada e menus.

Essa ordem valida primeiro os elementos mais frequentes e os conflitos semânticos. A repaginação só é considerada completa após as telas restantes e a auditoria final, não ao terminar a primeira screenshot bonita.
