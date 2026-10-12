# Marfim na run, mapa de escolhas e diálogo RPG/visual novel

Data: 2026-10-12. Status: **plano; não implementado nesta entrega**.

Base: [arquitetura visual da run](GODOT_RUN_GRAPHICS_ARCHITECTURE_PLAN.md), [progresso da implementação](GODOT_RUN_GRAPHICS_IMPLEMENTATION_PROGRESS.md), [direção Marfim](GODOT_IVORY_VISUAL_REDESIGN_PLAN.md) e [contratos atuais](../design/GODOT_RUN_GRAPHICS_CONTRACTS.md). Os documentos anteriores permanecem como referência; este plano organiza a próxima entrega.

## 1. Objetivo e decisões propostas

Aplicar a linguagem Marfim à experiência dentro da run, melhorar escolhas e leitura, e separar completamente a conversa da tela de mapa/atividades.

Decisões recomendadas para a primeira implementação:

- **Mapa dedicado, ocupando a área principal**, em vez de uma coluna lateral estreita permanente. Inspector contextual ao lado; compacto/recolhível em resoluções menores.
- Selecionar um nó apenas inspeciona; **Viajar** confirma uma opção publicada. Não exigir duplo clique. Movimento da câmera, hover, filtros e foco nunca enviam comandos.
- Combate mantém campo aberto, mão inferior compartilhada e sidebar flutuante. Mudar linguagem visual, não a anatomia funcional aprovada.
- **Diálogo em cena própria**, com ambiente/retratos acima e caixa de fala abaixo, seguida das escolhas. Mapa e outras atividades não permanecem interativos por trás.
- Texto completo é o padrão inicial. Revelação progressiva é uma preferência opcional; leitura não exige timer. Auto/skip de decisões fica fora da primeira entrega.
- A conclusão da conversa volta à apresentação do mapa; consequências e destinos dependem exclusivamente da engine.
- Marfim é a aparência do cliente, não uma regra do setting. Recursos, tipos de atividades, equipes e artes configurados continuam extensíveis.

Essas são propostas de UX, não fatos extraídos das referências. Em particular, mapa amplo e confirmação em duas etapas são escolhas deliberadas para este projeto.

## 2. Diagnóstico atual

| Área | Base existente | Lacuna para esta entrega |
| --- | --- | --- |
| Estilo | Marfim funciona no laboratório, menus e pausa; VisualContext/Theme locais já existem. | Run ainda preserva o estilo escuro, com fundos/ilustrações antigos. |
| Combate | CombatView, componentes de mão/HUD, cenário separado e cursor único. | Aplicar papel/tinta, limpar hierarquia de informações e adaptar placeholders sem alterar input/regras. |
| Jornada | JourneyView e ActivityOptionsView, reconciliação regional. | Mapa ainda ocupa coluna lateral e divide tela com a atividade. Falta separar exploração, atividade e conversa como apresentações próprias. |
| Grafo | MapLayout puro, DAG por camadas, cache, ciclos/desconexões e scroll/foco. | Falta câmera/inspector de mapa, nós compactos, legenda e leitura de rotas futuras. Resíduos cíclicos ficam numa linha única; mapas complexos precisam de refinamento. |
| Diálogo cliente | DialoguePanel passivo, retrato, fala, opções e histórico. | É montado dentro de ActivityOptionsView; não há cena narrativa dedicada. |
| Projeção de diálogo | DialoguePresenter localiza texto e associa escolhas aos comandos publicados. | Descarta dialogueInstanceId, dialogueId, nodeId, contentRevision e activityNodeId. Associação por choiceId apenas é insuficiente para identidade contextual robusta. |
| Diálogo engine | Definições JSON, ramificações, condições, opções ocultas/desabilitadas, custos, efeitos, flags, histórico e comandos de início/escolha. | Não distingue semanticamente uma continuação narrativa de uma escolha única significativa na projeção pública. Não há composição visual multi-retrato/ambiente dedicada. |
| Navegação | open_game escolhe combate ativo ou ActivityScreen. | Não reconhece conversa como rota própria; retorno ao mapa precisa ser coordenado sem pular escolhas/atividades. |

Arquivos centrais: `ui/main.gd`, `ui/journey_view.gd`, `ui/components/activity_options_view.gd`, `ui/dialogue_panel.gd`, `presentation/dialogue_presenter.gd`, `presentation/map_layout.gd`, `ui/route_view.gd` e `Core/Run/Dialogue/*`.

A certificação REST do plano arquitetural anterior ainda está pendente. Não tratá-la como concluída para iniciar a ativação definitiva do novo gameplay.

## 3. Pesquisa e implicações de design

### 3.1. Mapa como decisão estratégica

Anthony Giovannetti descreve a escolha de caminhos de Slay the Spire como uma camada adicional de risco/recompensa e planejamento, inspirada em FTL. A aplicação aqui é apresentar alternativas e informação útil para a build, sem um algoritmo local escolher a melhor rota. [Entrevista com o designer — Game Developer](https://www.gamedeveloper.com/game-platforms/road-to-the-igf-mega-crit-games-i-slay-the-spire-i-).

A Mega Crit relatou aumentar ícones/hitboxes e diferenciar tipos de sala no mapa de StS2; também mostrou desenho de anotações no mapa e uma barra de informações mais espaçosa. Para nós: alvos claros, informação de run acessível e marcações locais opcionais. Não copiar efeitos de raridade ou aumentar todos os elementos indiscriminadamente. [Mega Crit, Neowsletter de novembro de 2024](https://www.megacrit.com/news/2024-11-07-neowsletter-issue-4/).

### 3.2. Fala, escolha e histórico como componentes diferentes

Ren'Py separa a apresentação de fala — nome, texto e janela — da tela de escolhas. Essas escolhas têm validade contextual, não devem sobreviver como ações executáveis depois que a etapa acaba. A adaptação proposta é separar DialogueView, choices e controller, sem introduzir Ren'Py como dependência ou adotar sua execução de scripts. [Ren'Py — Special Screen Names](https://www.renpy.org/doc/html/screen_special.html).

Seu histórico de diálogo fornece uma referência útil para um backlog de leitura. No HeroScript, consultar esse histórico **não deve executar rollback**: o registro autoritativo continua na run, e mudanças de branch pertencem às ferramentas/timeline autorizadas. [Ren'Py — Dialogue History](https://www.renpy.org/doc/html/history.html).

### 3.3. Acessibilidade faz parte da qualidade de vida

Informação essencial não deve depender exclusivamente de cor: nós precisam de símbolo, texto e estado, não só círculos coloridos. [Game Accessibility Guidelines — cor](https://gameaccessibilityguidelines.com/ensure-no-essential-information-is-conveyed-by-a-fixed-colour-alone/).

O jogador deve controlar o ritmo de leitura e poder consultar mensagens anteriores. Portanto, fala não desaparece por prazo fixo; histórico não reposiciona o foco contra a vontade do leitor. [Game Accessibility Guidelines — ritmo de texto](https://gameaccessibilityguidelines.com/allow-players-to-progress-through-text-prompts-at-their-own-pace/).

Usar texto legível e respeitar escala de fonte, com layouts alternativos antes de diminuir indefinidamente os caracteres. Os tamanhos precisam ser testados nas resoluções reais do projeto. [Game Accessibility Guidelines — tamanho de texto](https://gameaccessibilityguidelines.com/use-an-easily-readable-default-font-size/).

## 4. Direção Marfim dentro da run

### 4.1. Papel, tinta e circuitos

Reutilizar paleta/fontes/componentes aprovados; criar um perfil de run derivado deles, sem copiar a factory de estilos.

- Papel marfim nas superfícies de leitura; tinta nas palavras, silhuetas e estrutura.
- Vermelhão para ação principal/atenção, não para toda informação semântica.
- Feixes finos e próximos, com mudanças de direção de 45°, em margens, divisores e assinaturas. Não cruzar texto, retratos, efeitos ou regiões de escolha.
- Arestas funcionais do mapa devem ser linhas claras. Não desenhar vários fios decorativos em cada conexão, tornando ramificações ambíguas.
- Arte placeholder em linguagem de impressão: contornos limpos, massas de tinta e poucas cores. Criar variantes SVG/manifestos próprios quando necessário; não aplicar filtro de cor sobre a tela inteira.
- Ambiente permanece propriedade do cenário; HUD usa seu próprio perfil. A troca da skin não recolore automaticamente uma arte definitiva fornecida por um setting/mod.

### 4.2. Preservar semântica das cartas

Reutilizar exatamente CardView/CardDetailsPanel/CardVisualStyle do laboratório, inclusive custos com símbolo e numeral central.

- Raridade: borda e indicação textual, incluindo tooltip/inspeção.
- Número-base branco, redução vermelha e aumento azul: manter superfície de tinta atrás dos efeitos, para legibilidade sobre papel claro.
- Custo identifica recursos; descrição da carta não contém o nome do alvo selecionado.
- Preview da ação permanece separado e contextual; não substitui a descrição estável.
- Indisponibilidade/seleção não devem dessaturar a carta inteira nem apagar cores semânticas.

### 4.3. Combate e demais atividades

Hierarquia de leitura: intenção/estado do alvo → cartas/possibilidades → preview → executar/encerrar. Recursos relevantes ficam acessíveis sem abrir várias janelas; não presumir health/energy/mana como lista fechada.

Recompensas, loja, preparação e transformações usam as mesmas superfícies/componentes. Preço de aquisição continua fora do custo de jogar a carta. Ofertas podem ser recusadas quando a engine publica essa opção. Diálogo deixa de ser uma oferta/painel da jornada.

## 5. Nova experiência de mapa

### 5.1. Composição

Mapa amplo no centro; cabeçalho compacto com setting/run, recursos publicados, deck e pausa. Inspector contextual lateral para nó selecionado: nome, tipo, estado, risco conhecido, descrição e ação publicada. Em 1280×720, inspector pode virar painel recolhível/parte inferior; em 2560×1080, usa espaço lateral sem esticar texto por toda a tela.

O mapa é seleção/planejamento; a atividade abre uma apresentação separada depois da ação aceita. Não iniciar combate/loja/conversa apenas ao passar o ponteiro sobre um nó.

### 5.2. Nós e arestas

Estados visuais independentes: atual, concluído, disponível para viajar, indisponível, selecionado e apenas inspecionado. Foco não substitui o estado atual; seleção não equivale a viagem.

- Ícone por tipo, nome curto e forma/filete de estado; legenda com texto.
- Nó indisponível continua inspecionável quando seus dados são visíveis. Apenas o comando Viajar fica indisponível. Não revelar nós que a engine não publicou.
- Arestas dirigidas; rotas candidatas têm destaque temporário ao inspecionar. Isso mostra conectividade, não promete que todos os passos futuros estarão habilitados.
- Risco/recompensas só aparecem se publicados; não calcular probabilidades, drops ou dificuldade pela composição dos inimigos.
- Filtros realçam tipos/estados, sem remover silenciosamente nós e distorcer a topologia.
- Tipos desconhecidos têm ícone genérico e nome legível. Ciclos/retornos continuam representados.

### 5.3. Controles e QoL

- Arrastar o espaço vazio para mover a câmera; wheel/controles equivalentes para navegação vertical/horizontal. Separar gesto de pan de seleção por limiar de movimento.
- Ações **Centralizar no atual**, **Mostrar próximas escolhas**, **Legenda** e, quando útil, **Visão geral**.
- Zoom/visão geral não reduz hitboxes/texto abaixo dos limites legíveis. Preferir detalhe compacto no mapa e texto completo no inspector a diminuir a UI inteira. Implementar zoom depois do mapa amplo, conforme a necessidade demonstrada nas fixtures.
- Teclado/controle percorre nós de modo estável; foco traz o nó para a viewport. Inspeção não depende de hover.
- Preservar seleção, foco, pan/scroll e nível de detalhe por run/branch/contexto. Ao voltar de atividade/conversa, restaurar a região de leitura; ao mudar setting/run, não reutilizar estado antigo.
- Marcação de rota pode ser um marcador local simples, removível e sem comandos. Desenho livre/colaboração fica para depois.

Viajar usa a referência atual publicada e bloqueia duplicação enquanto envia. Se a opção mudar, atualizar o inspector; não executar automaticamente uma alternativa com ID parecido. Confirmar escolhas destrutivas/custosas quando necessário, sem um modal para cada clique de leitura.

O avanço forçado existente continua separado da seleção manual. Ao concluir um diálogo, não deve encadear viagens antes de apresentar o retorno ao mapa: a coordenação deve estabelecer uma fronteira de retorno/continuação visível, respeitando o comportamento configurado do modo, sem criar permissão nova.

## 6. Diálogo como subsistema independente

### 6.1. Autoridades

| Camada | Sabe | Não sabe/faz |
| --- | --- | --- |
| DialogueView | Fala, speaker, retratos, opções visíveis, estado de leitura e refs opacas | Condições, flags, dano, loot, combate ou significado de uma escolha |
| DialoguePresenter | Projetar/localizar dados publicados, custos e justificativas | Calcular disponibilidade/consequências ou executar scripts |
| DialogueController | Contexto atual, referência escolhida, submissão única e recuperação | Interpretar “aceitar” como ganhar item, perder vida ou iniciar combate |
| Navigator/coordenador da run | Qual apresentação corresponde ao estado atual e onde restaurar a UI | Antecipar o resultado de uma opção pelo texto/ID |
| Engine/serviços de regras existentes | Condições, transições, custos, flags e efeitos declarados no JSON | Depender de resolução, animação, velocidade de leitura ou assets da Godot |

O módulo de sequência pode transportar definições declarativas; o processamento de efeitos continua indistinguível de outros efeitos do jogo. Não inserir branches de código como `if choiceId == accept: give_relic`. Reutilizar IRunActivityEffectExecutor, transições de recursos e journal existentes; não criar outro executor narrativo.

### 6.2. Fluxo canônico

Mapa → entrada de atividade de diálogo aceita pela engine → cena de diálogo → leitura → escolha publicada → atualização autoritativa → próxima fala ou conclusão → mapa.

Regras de coordenação:

1. Entrar na apresentação não reaplica START_DIALOGUE. Enviar esse comando apenas se ainda houver necessidade e ele estiver publicado; uma conversa já iniciada retoma o mesmo diálogo.
2. Ler/revelar/paginar o texto atual é local e não envia comando.
3. Avançar para outro nó narrativo é uma escolha/comando real, mesmo que pareça apenas “Continuar”. Pode ter efeitos de entrada: não tratar como mudança local de página.
4. Uma opção única não é automaticamente uma continuação; pode ser uma decisão significativa/custosa. Nunca inferir isso de quantidade, texto traduzido ou ID.
5. Após uma escolha, resolver a próxima apresentação pelo novo estado publicado, não por um nextNodeId interpretado na UI.
6. Completed não significa inventar ADVANCE_NODE. Quando RESOLVE_NODE for necessário e estiver publicado, a coordenação aplica o comando de conclusão; depois apresenta o mapa. Não escolher a próxima rota.
7. Mapa durante conversa pode ser consulta, sem abandonar/resolver a atividade. Escape abre pausa; não encerra diálogo como efeito implícito. Se saída/recusa for permitida, ela vem de uma opção publicada.
8. Voltar ao menu/salvar e continuar retoma o contexto da conversa. Não reaplicar efeitos de entrada ou escolhas já confirmadas.

### 6.3. Composição RPG/visual novel

- DialogueSceneryView: ambiente/ilustração e retratos em slots independentes.
- DialogueView: nome do interlocutor, caixa de fala Marfim, indicador de leitura/continuação e escolhas.
- DialogueHistoryView: backlog rolável/focável, com falas e respostas já registradas.
- Sem mapa, ofertas ou comandos de combate interativos na mesma cena.
- Narrador sem retrato/nome funciona sem fabricar identidade. Retrato ausente usa placeholder discreto.
- Textos longos recebem paginação ou área de leitura explícita; escolhas ficam acessíveis, não abaixo de uma altura infinita.
- O primeiro confirmar pode completar a revelação opcional; não também selecionar uma resposta no mesmo evento. Escolhas exigem uma nova ativação após a fala estar pronta.
- Opções indisponíveis mostram justificativa quando publicada; ocultação continua determinada pela engine. Uma escolha onerosa mostra custo publicado; confirmações adicionais são política de UX, não nova validação de regra.
- Pause/histórico/inspeção bloqueiam input e avanço. Históricos não viram botões para executar escolhas antigas.

Paginação usa texto/locale/escala da fala atual, não modifica o grafo narrativo. Se idioma/escala mudar, recompor a leitura sem submeter comandos ou reiniciar o diálogo. Guardar progresso visual por escopo; histórico autoritativo continua na engine.

### 6.4. Contratos mínimos

Preservar na projeção: runId, branchId, sequence/contexto de versão, dialogueInstanceId, dialogueId, activityNodeId, nodeId e contentRevision. Cada opção carrega choiceId, reference, texto, custo publicado, disponibilidade e motivo publicado. A associação verifica instância/nó/choice, não apenas choiceId.

Extensões propostas, a validar antes da implementação:

- `interactionKind` explícito para Choice/Continue, com Choice como comportamento conservador. Continue também usa um comando publicado; o campo nunca autoriza uma ação ausente.
- Mapping visual por dialogueId/nodeId/speakerId para ambiente, posições e variantes de retratos. Usar IDs existentes quando suficientes; identificador estável de speaker, se necessário, é metadata opcional, não interpretação do nome traduzido.
- Variante visual de layout e preferências de leitura em perfis do cliente. Não colocar a velocidade de letras nos inputs determinísticos da run.

Não é necessário criar endpoints de diálogo independentes só para abrir a cena: os comandos de run e as projeções atuais já são a integração canônica. Estender DTO/schema apenas para lacunas demonstradas, com validação/OpenAPI/testes; não inventar agora um contrato de cutscene dentro de combate, voz sincronizada ou scripts Godot embutidos em JSON.

## 7. Etapas sequenciais de implementação

### Etapa 0 — fechar base e registrar referência

Reexecutar a certificação arquitetural com API QA isolada; congelar capturas/contadores antes do redesign. Acrescentar fixtures de mapa ramificado grande/cíclico, texto longo e diálogos com continuação, várias opções, opção única custosa, indisponíveis e retomada. Não usar partidas pessoais nos testes mutantes.

**Aceite:** engine/cliente atuais têm fluxo de referência verificável; falhas anteriores são separadas de mudanças de estilo.

### Etapa 1 — contratos de diálogo e seleção

Corrigir a projeção para reter IDs/versão e matching contextual; especificar interação Choice/Continue sem heurística. Definir estado local de inspeção do nó separado de travel e estado local de leitura separado da progressão narrativa. Auditar as delegações do diálogo para os serviços genéricos de efeitos/recursos, corrigindo somente desvios encontrados.

**Aceite:** opções de contextos diferentes com mesmo choiceId não colidem; um diálogo genérico não conhece consequências; contrato atende mais de um setting.

### Etapa 2 — separar a cena de diálogo antes do redesign

Criar DialogueScreen como adaptador, DialogueController e uma cena/view própria reutilizando as partes de DialoguePanel. Acrescentar resolução de rota narrativa ao navigator; remover a montagem de diálogo em ActivityOptionsView e evitar dois caminhos operacionais. Mapa pode consultar/retomar conversa, sem executar resolução por navegação.

**Aceite:** mapa→diálogo→escolhas→mapa funciona ainda no visual preservado; save/continue retoma corretamente e nenhum efeito é executado duas vezes.

### Etapa 3 — ampliar a jornada para mapa dedicado

Separar apresentação de mapa da apresentação de atividade usando JourneyView/ActivityOptionsView existentes. Criar inspector e nós inspecionáveis por ID; conectar Viajar às referências publicadas. Guardar estado de leitura no proprietário da navegação, para sobreviver à troca de cenas.

**Aceite:** selecionar/arrastar/ler nunca envia comandos; confirmações enviam uma única opção atual; retorno preserva o ponto de leitura.

### Etapa 4 — fundação Marfim da run

Definir perfil de UI da run, variantes de componentes, perfil/manifesto de cenário e anatomia de placeholders. Reutilizar fontes, circuitos e carta do laboratório. Validar contraste de raridade/valores/custos e a paleta dos recursos desconhecidos. Não trocar a skin de menus/pausa já aprovados.

**Aceite:** fixtures simultâneas no tema preservado e Marfim não contaminam estilo/cenário; regras, seleções e cursor são idênticos.

### Etapa 5 — primeiro marco: mapa Marfim e escolhas legíveis

Aplicar papel/tinta ao mapa amplo, nós, legenda, inspector e cabeçalho. Diferenciar atual/visitado/disponível/foco sem só cor. Melhorar edge routing e, onde necessário, agrupamento por componentes fortemente conectados para ciclos; isso altera apenas layout, não os nós/arestas autoritativos. Aprovar aparência antes de expandir ao combate.

**Aceite:** ramos/retornos legíveis, alvos confortáveis, texto não vertical; nenhum nó se move por atualização de recursos e nenhuma informação secreta é revelada.

### Etapa 6 — navegação/QoL do mapa

Adicionar pan, centralização, próximas escolhas, navegação de teclado/controle e restauração. Medir necessidade de visão geral/zoom e implementar sem destruir legibilidade/hitboxes. Filtros apenas destacam; marcador local simples é opcional. Invalidar somente a geometria/estado afetados quando topologia/perfil mudar.

**Aceite:** mapa grande utilizável em 1280×720 e ultrawide; drag não ativa travel; foco seleciona sem hover; requests de inspeção/comandos não aumentam por mover a câmera.

### Etapa 7 — marco narrativo: cena Marfim RPG/VN

Aplicar ambiente/retratos/caixa de fala, opções e histórico. Implementar texto instantâneo, paginação, revelação opcional e bloqueio de input entre revelar e escolher. Remapear ações no sistema existente; preferências não remontam a cena ou voltam à categoria General.

**Aceite:** leitura longa e opções funcionam com EN/PT, fonte ampliada e arte ausente; histórico não dá rollback; fechar/retomar preserva o diálogo.

### Etapa 8 — Marfim no combate com QoL

Aplicar o estilo aprovado ao cenário/HUD/mão/preview/controles e sidebar. Realçar intenções, recursos, mudanças de estado e resultado sem flashes repetitivos. Manter preview fora da carta, alvo explícito e nenhuma seleção automática após jogar. Melhorar feedback de processamento/reconexão sem confundir com fase da regra.

**Aceite:** mão vazia/grande, alternativas de custo, multi-alvo, equipes neutras e modos manual/automático preservam o comportamento; pausa fica acima de tudo.

### Etapa 9 — ofertas, loja, preparação, transformação e resultado

Uniformizar layouts de ações/preços/recusa/detalhes/confirmar. Usar componentes genéricos onde anatomia for equivalente; apresentações especiais precisam de dados publicados, não branches por nome de setting. Ao terminar conversa, atividade ou run, navegar pela projeção atual sem presumir prêmio, próximo combate ou vitória pelo texto.

**Aceite:** diferentes settings e comandos desconhecidos continuam utilizáveis; não há cards com preço de compra no custo de jogar ou confirmação com efeitos calculados pela UI.

### Etapa 10 — estabilização e ativação

Executar matriz funcional/visual, comparar identidade/foco/scroll/reflows/leituras e remover componentes/templates narrativos antigos sem consumidores. Registrar contratos, capturas, preferências e extensões da API. Ativar Marfim como perfil da run após aprovação dos marcos, mantendo uma implementação única — não duas versões do gameplay.

**Aceite final:** fluxo completo real em QA isolado, estabilidade sem regressões e aparência coerente com os menus/laboratório.

## 8. Matriz de validação

- 1280×720, 1920×1080 e 2560×1080; EN/PT; escala de texto, alto contraste e movimento reduzido.
- default/ascendant/volatile e conteúdo desconhecido; recursos/atores/atividades fora das listas iniciais.
- Grafo linear, diamante, muitas raízes, 100 nós, ciclos/retorno, IDs inválidos, hotreload, ramo desabilitado e estado alterado sem relayout.
- Diálogo com narrador, troca de interlocutor, várias opções, única opção significativa, Continue declarado, custos, opções ocultas, texto longo e retrato ausente.
- Repetição de clique, perda de conexão, timeout com resultado incerto, resposta atrasada, mudança de setting/branch, pause/histórico e navegação durante await. Recuperar pelo protocolo existente; não repetir escolha por tentativa local cega.
- Retomada de conversa/atividade concluída; efeitos de entrada e custos aplicados exatamente uma vez no journal. Replay reproduz comandos/estado; velocidade de leitura não afeta hash.
- Igualdade dos payloads e resultados antes/depois do estilo, para mesmas escolhas/seed/revisão. Não esperar equivalência se extensões de conteúdo forem alteradas deliberadamente.
- Vinte refreshes iguais sem reconstrução da mão/mapa ou leitura adicional; medir refresh local separadamente de rede e animação.

## 9. Marcos e fora de escopo

**A:** contratos + cena narrativa separada + mapa amplo funcionais no estilo antigo. **B:** mapa Marfim e interação aprovados. **C:** diálogo RPG/VN aprovado. **D:** combate/atividades Marfim e certificação integral.

Não incluir nesta primeira entrega: dublagem, lip-sync, autoescolha, skip de decisões, rollback VN local, editor visual de roteiro, desenho colaborativo no mapa, cálculo/recomendação de melhor build/rota ou reescrita do sistema de regras.

O critério é diminuir fricção e tornar decisões claras. Mais partículas, mais caixas e mais controles não são, por si só, qualidade de vida.
