# HeroScript — Plano de redesign funcional de interface

**Data:** 22/11/2026 · **Escopo:** MVP (`prototype-test`) e Alisyum, quatro classes · **Referência do código:** `ece48337` + alterações locais de HUD e cartas já existentes no checkout.

**Objetivo:** fazer o jogo parecer legível, previsível e confortável de operar mesmo quando se retiram todas as ilustrações. A arte de personagens, cenários, cartas, texturas e decoração fica para depois. O sistema de builds, runas de sobrescrita, resolução em cadeia e decisões do jogador deve ser o centro da interface.

## 1. Pesquisa e método

O [Game UI Database](https://www.gameuidatabase.com/) é a referência solicitada. Seu criador, [Edd Coates](https://www.eddcoatesdesign.com/), descreve o acervo como uma ferramenta de referência para designers: tipos de tela, componentes, controles, análise visual e vídeos permitem comparar interfaces por função, em vez de selecionar apenas jogos com estilo semelhante. A [descrição do lançamento da versão 2.0](https://www.gamedeveloper.com/design/game-ui-database-relaunches-with-new-features-video-support-and-over-55-000-screenshots) confirma esse método.

**Limitação da consulta:** o domínio bloqueou o indexador (`robots.txt`) e a tentativa de navegação normal pelo navegador não abriu devido a falha do ambiente de captura. Portanto, não foi possível consultar diretamente as fichas individuais do Game UI Database. Não atribuo ao banco capturas ou resultados de buscas que não consegui acessar. Para executar a comparação, consultei o acervo acessível [Interface In Game](https://interfaceingame.com/), mídias de jogos, páginas de desenvolvedores e diretrizes de acessibilidade. Os links abaixo distinguem páginas de capturas de referências primárias e recomendações próprias.

### 1.1 Perguntas que orientam a comparação

1. Em dois segundos, o jogador entende o objetivo da tela, o estado da partida e a ação principal?
2. Em cinco segundos, consegue comparar duas cartas ou duas runas sem abrir uma sequência de pop-ups?
3. Consegue reconhecer uma alternativa indisponível e entender por quê, sem realizar uma tentativa que falhe?
4. A saída, o cancelamento e a recuperação permanecem acessíveis independentemente da quantidade de conteúdo?
5. A UI revela regras e sinergias reais sem inventar números, custos ou informações ocultas?
6. A experiência continua utilizável com teclado, textos maiores, português e uma janela pequena?

Esses tempos são **metas experimentais de playtest**, não medições já obtidas. Capturas permitem avaliar evidências de geometria, informação e contraste; não provam diversão, preferência estética nem tempo humano de leitura.

### 1.2 Referências por função

| Referência e telas | Padrão funcional de interesse | Aplicação no HeroScript |
| --- | --- | --- |
| **Slay the Spire** — [combate](https://interfaceingame.com/screenshots/slay-the-spire-attack-enemy/), [informação inimiga](https://interfaceingame.com/screenshots/slay-the-spire-enemy-stats/), [mapa](https://interfaceingame.com/screenshots/slay-the-spire-map/), [escolha de carta](https://interfaceingame.com/screenshots/slay-the-spire-choose-a-card/), [personagem](https://interfaceingame.com/screenshots/slay-the-spire-choose-your-character/) | Recursos e intenções próximos à decisão; mão distinguível; legenda de mapa; poucas alternativas de recompensa, com saída visível. | Organizar a interface em zonas funcionais, tornar custo e efeito imediatamente escaneáveis, apresentar escolhas de jornada comparáveis. Não copiar a aparência de pergaminho ou introduzir a ação Pular quando a regra não a publica. |
| **Monster Train** — [página oficial](https://store.steampowered.com/app/1102190/Monster_Train/), [captura da loja de melhorias](https://boilingsteam.com/monster-train/), [exemplo de interface de aplicação](https://cdn.akamai.steamstatic.com/steam/apps/1102190/ss_13f43b3b31bff7891d899172799dc0dbf3a2c0c7.1920x1080.jpg) | Pedras de melhoria identificadas pelo efeito e preço; serviços da loja agrupados separadamente; seleção explícita de carta a melhorar. | Runa como objeto funcional, catálogo separado por tipo, diferença entre aquisição e vínculo e serviço de renovação fora da grade de itens. |
| **Wildfrost** — [site oficial](https://www.wildfrostgame.com/), [press kit](https://www.wildfrostgame.com/presskit/), [trailer explicativo da editora](https://www.youtube.com/watch?v=7gD8nbyN3ps) | Contadores ajudam a planejar o próximo evento; charms personalizam cartas e sua informação deve acompanhar a carta. | Calor da Runa e estados como contadores, identidade das runas equipadas, distinção entre efeito atual, preparação e disparo posterior. Não transformar contadores em moedas de ação. |
| **Into the Breach** — [combate](https://interfaceingame.com/screenshots/into-the-breach-combat/), [galeria](https://interfaceingame.com/games/into-the-breach/), [página oficial](https://store.steampowered.com/app/590380/Into_the_Breach/) | Ameaças antecipadas e espaço de atuação compreensível; consequência da próxima ação importa mais que detalhes técnicos. | Intenção inimiga compacta, prévia de alvo, efeito e alterações previstas do estado. Se houver incerteza, comunicar a incerteza. Não criar simulador paralelo no cliente. |
| **Hades** — [escolha de bênção](https://interfaceingame.com/screenshots/hades-boons-of-aphrodite/), [loja](https://interfaceingame.com/screenshots/hades-well-of-charon/), [equipamentos](https://interfaceingame.com/screenshots/hades-keepsakes/) | Alternativas comparáveis: categoria, valor e efeito na mesma posição; explicação contextual de palavras-chave. | Comparador de transformação e lista de runas com benefício, condição, duração e alteração; glossário contextual ao lado, não tipografia decorativa. |
| **Hearthstone** — [galeria de interfaces](https://interfaceingame.com/games/hearthstone-heroes-of-warcraft/) | Carta ampliada para leitura e interação; exploração de detalhes separada da decisão de jogar. | Uma carta pode ter representação compacta, normal e inspeção ampliada derivadas dos mesmos fatos. Evitar exigir um clique a mais para ler o que é necessário em combate. |

O benchmark não é ranking visual nem uma lista de interfaces a clonar. Esses jogos utilizam arte, mas os padrões escolhidos continuam válidos em wireframes de alto contraste.

#### Roteiro para complementação manual no Game UI Database

Quando o acesso estiver disponível, usar a busca por títulos acima e por telas (os nomes dos filtros podem variar): `HUD / combat`, `status / buffs`, `card detail`, `inventory / loadout`, `shop`, `reward / upgrade`, `map / level selection`, `character selection`, `pause`, `dialogue` e `error / confirmation`. Restringir a desktop, paisagem e controle mouse/teclado quando possível. Procurar os estados **normal**, **focado**, **indisponível**, **selecionado**, **confirmando**, **erro** e **recuperado**, não só a primeira captura de cada jogo. Usar inspetor de fonte, grade e visualizadores de contraste/cor para ler os padrões. Registrar jogo, tela, plataforma, versão/período se disponível, link individual e uma decisão transferível. Nenhum asset de terceiros será incorporado ao projeto.

### 1.3 Critérios externos

As [Xbox Accessibility Guidelines](https://learn.microsoft.com/en-us/xbox/accessibility/guidelines) servem como metas técnicas de usabilidade, não como declaração de certificação. [XAG 101](https://learn.microsoft.com/en-us/gaming/accessibility/xbox-accessibility-guidelines/101): medir a altura realmente renderizada do texto; referência de 18 px a 1080p para PC, com opção de ampliação. [XAG 102](https://learn.microsoft.com/en-us/gaming/accessibility/xbox-accessibility-guidelines/102): contraste de 4,5:1 para texto comum relevante, 3:1 para elementos grandes/inativos e 7:1 no modo de alto contraste. [XAG 112](https://learn.microsoft.com/en-us/gaming/accessibility/xbox-accessibility-guidelines/112) e [113](https://learn.microsoft.com/en-us/gaming/accessibility/xbox-accessibility-guidelines/113): navegação e foco previsíveis. [XAG 115](https://learn.microsoft.com/en-us/gaming/accessibility/xbox-accessibility-guidelines/115): erros compreensíveis, prevenção e revisão de decisões irreversíveis.

No Godot, priorizar [navegação de teclado/controle](https://docs.godotengine.org/en/stable/tutorials/ui/gui_navigation.html), [layouts responsivos](https://docs.godotengine.org/en/stable/tutorials/rendering/multiple_resolutions.html) e os recursos de [ScrollContainer](https://docs.godotengine.org/en/stable/classes/class_scrollcontainer.html) para manter foco e elementos acessíveis. O projeto já usa `Control`, cenas autoradas e contextos visuais: não é necessário substituir a UI por uma biblioteca externa.

## 2. Auditoria atual: evidências, não preferência estética

Capturas reais no renderer do Godot v4.7.2 e leitura do jogo aberto sem escolher uma nova ação. Foram analisados componentes compartilhados e fixtures controladas; o pacote de Alisyum e o MVP não são tratados como se tivessem as mesmas regras. Capturas anteriores da loja correspondem ao momento antes da última correção da interface (commit `ece48337`), então servem para geometria e fluxo; o estado de ofertas clicáveis sem saldo **já foi corrigido no código**, não deve ser reintroduzido.

| ID | Evidência consultada (caminho relativo ao projeto) | Natureza |
| --- | --- | --- |
| E1 | `.runtime/ui-legibility-audit/combat/mvp-1920x1080.png` e `mvp-1280x720.png` | Renderer da cena de combate MVP com 5 cartas, fatos de teste incompletos. |
| E2 | `.runtime/ui-legibility-audit/combat/mvp-10-cards.png`, `mvp-12-cards.png` | Stress de mão grande no mesmo componente, não gameplay completo. |
| E3 | `.runtime/ui-legibility-audit/hud/statuses.png` | Componente de ator com seis status em inimigos; silhuetas deliberadamente sem arte. |
| E4 | `.runtime/ui-legibility-audit/live/alisyum-map-completed.jpg` | Mapa real da campanha, bazar concluído; somente observação. |
| E5 | `.runtime/ui-legibility-audit/live/alisyum-shop-top.jpg`, `alisyum-shop-bottom.jpg` | Capturas da loja real: cinco cartas, doze runas e ações ao final da rolagem. |
| E6 | `.runtime/rune-workbench-validation/02-forge-dragging.png`, `04-forge-comparison.png` | Forja em fixture, gesto nativo e comparação de Fragmentação. |
| E7 | `.runtime/ui-legibility-audit/loadout/inscriptions-pt_BR-1280x720.png`, `deck-pt_BR-1280x720.png`, `inscriptions-pt_BR-1920x1080.png` | Laboratório dos mesmos componentes de loadout, usado só para auditar estrutura; textos de laboratório não são, por si, defeitos da jornada real. |
| E8 | `.runtime/rune-workbench-validation/menus/main/alisyum-continue-pt_BR-1920x1080.png` | Menu de campanha com resumo de retomada em fixture. |
| E9 | `.runtime/ui-legibility-audit/journey/pt_BR-ivory-reward-1280x720.png`, `pt_BR-ivory-dialogue-1280x720.png`, `pt_BR-ivory-pause-1280x720.png` | Componentes de jornada e pausa em perfil compartilhado. A recompensa genérica não é prova de fluxo de recompensa completa de Alisyum. |

**Nota de QA:** os runners `combat_mvp_preview`, `unified_loadout_layout` e `ivory_run_visual` completaram suas verificações existentes, mas os testes atuais não certificam legibilidade. Por exemplo, o cabeçalho e a mão relatados pelo runner de combate têm as mesmas dimensões em unidades lógicas entre resoluções, e os arquivos chamados `mvp-1280x800.png` e `mvp-2560x1080.png` foram efetivamente gravados em 1280×720 e 1920×1080. O mesmo ocorreu com algumas imagens ultrawide de jornada. Já os arquivos de loadout foram confirmados em 2560×1080. Todo gate futuro deve confrontar a resolução solicitada com as dimensões efetivas do PNG, e falhar se não corresponderem. Isso é diferente de haver espaço lógico suficiente na cena.

### 2.1 Avaliação de telas

Prioridades: **P0** bloqueia ou arrisca progresso/leitura indispensável; **P1** reduz fortemente clareza e qualidade das decisões; **P2** melhora aprendizado e eficiência.

| Interface | Problema observado e consequência | Prioridade | Padrão a adotar |
| --- | --- | --- | --- |
| Combate, mão | E1/2: mão em arco visualmente agradável com poucos cartões, mas cartas se encobrem; com 10 há nomes, custos e efeitos parcialmente ocultos, scroll horizontal e competição com pilhas. Uma carta não inspecionada precisa ser reconhecível. | P0 | Faixa de mão com largura reservada e adaptação discreta. Cartas vizinhas expostas; hover/foco amplia sem cobrir o rodapé, nenhum controle fixo dentro da área rolável. |
| Carta | Nome duplicado no slot de ilustração e vários rótulos técnicos; custo múltiplo recebe pouco espaço. O texto de efeito fica no bloco escuro e pode ser genérico se fatos não foram fornecidos. No estilo atual `title_min_two_line_font_size = 8`, `inline_font_size = 11`, `change_badge_font_size = 10`. | P0 | Hierarquia nome → custo → ação → número → condição/duração; nenhuma redução tipográfica arbitrária. Inspeção permite ler todo o efeito. |
| Vida e intenções | E1/3: números, barra fina, escudo e intenção aparecem afastados em linhas diferentes. O contorno da peça não explica a relação entre elas, e o alvo da intenção pode exigir leitura adicional. 5 status inimigos já aparecem em E3; não reduzir essa capacidade. | P1 | Unidade + identidade + vida/bloqueio + faixa de status fixa + intenção sintética, agrupados por proximidade. |
| Calor da Runa | E4/5: nas telas de jornada ainda aparece misturado à lista de energia e ouro; a loja não depende de Calor. | P1 | Contador contextual no herói/inspeção, não moeda de pagamento ou barra de recurso global. Valor publicado e tooltip; diferenciar contadores de custos. |
| Resolução em cadeia | Já existe banner causal não bloqueante, mas o jogador precisa relacionar o nome da runa, a carta de origem e a consequência. Uma cadeia longa não deve disputar o espaço da mão. | P1 | Resumo causal compacto do evento real; log expandido e referência à peça. |
| Bazar | E5: 17 ofertas em uma mistura vertical de cartas e runas. Saldo é um fragmento de texto pequeno no cabeçalho; Renovar e Concluir estão no fundo do scroll. O usuário já vivenciou falha de compra e perda de sincronização. | P0 | Cabeçalho de saldo + categorias + grade própria e **rodapé persistente**. Reaproveitar a recuperação implementada no último commit. |
| Recompensas | E9: título genérico repetido, três cartas à esquerda e muito espaço sem função; texto de diagnóstico (`PRNG`, `journal`) não ajuda a escolher. | P1 | Enunciado e opções grandes centralizadas/agrupadas, destino e efeito da escolha explícitos. Não inventar uma opção de pular. |
| Forja | E6: gesto direto está funcional, mas bandeja ocupa altura desproporcional à quantidade de runas; comparar lado a lado com carta completa duplica muitas áreas vazias, mudança precisa ser encontrada no texto. | P0 | Bandeja compacta, área de atenção na carta compatível; resumo de **linha alterada** antes/depois; confirmação permanente sem ambiguidade. |
| Loadout | E7: inventário de inscrições usa tiles muito altos para um ícone/nome; espaço de atributos estreito, rolagens independentes e no deck as cartas extensas escondem parte do efeito. O layout passa nos testes de contenção, mas exige muito movimento para comparar. | P1 | Tabulação clara, catálogo denso, detalhe da seleção, comparação de atributo e build. Usar nomes dos slots da política publicada, nunca genéricos quando a engine publicar identidade. |
| Mapa | E4: grafo comprimido, margens largas, nomes truncados (`Clandestine...`, `Broken Shrine...`). A etapa completada ainda domina um painel grande sem próxima instrução, embora três destinos estejam liberados. | P0 | Caminhos ramificados em área maior, legenda, estados redundantes e painel de próxima escolha. |
| Menu / continuar | E8: continuar já tem destaque e mostra save. Escolha de campanha em dropdown parece configuração, não decisão de jogador; menu e códice têm peso visual próximo. | P2 | Ação de jogar/continuar mais proeminente, cartões de campanha e seleção de identidade com informações realmente publicadas. |
| Diálogo, pausa e modais | E9: diálogo alonga um parágrafo curto na largura toda; opção pode ficar fora enquanto texto revela. Pausa repete informações sobre a engine. Em validação anterior, comparação de runas renderizou 628×567 numa janela de 960×540, maior que sua altura. | P0 para modal; P2 para copy | Área de leitura limitada, ação fixa, rolagem interna, foco e retorno preservados. |
| Estado e localização | UI alterna termos EN/PT mesmo em captura PT; o mapa/loja exibem nomes ingleses para descrições portuguesas. Erro temporário não pode ser única indicação do problema. | P0 para estado; P1 para tradução | Copy de jogador, glossário consistente e estados persistentes de pendência, rejeição, recuperação. |

### 2.2 A paleta não é o maior problema

Calculados pela luminância relativa a partir do recurso `data/themes/shared_palette.tres` (valores sólidos, não medição de pixels antialias/estado final):

| Combinação atual | Contraste aproximado |
| --- | ---: |
| Texto principal `#202124` / branco | 16,10:1 |
| Texto secundário `#60646c` / branco | 5,94:1 |
| Texto desativado `#70757d` / `#f3f4f5` | 4,21:1 |
| Branco / ação principal `#b83e25` | 5,59:1 |
| Foco `#303c63` / branco | 10,76:1 |
| Borda de controle `#9ba1a9` / branco | 2,60:1 |
| Borda de painel `#d4d8dd` / branco | 1,43:1 |
| Vida aliada / trilho | 4,93:1 |
| Vida inimiga / trilho | 4,75:1 |

Conclusão: manter base marfim e tinta escura. A maior dificuldade é **organização, densidade, texto pequeno e áreas de clique pouco distintas**, não a ausência de arte. Bordas muito claras servem para divisores decorativos; se forem o único sinal que delimita um controle interativo, reforçar contraste ou superfície. Não depender exclusivamente de cor para dano, raridade, foco, seleção, legalidade e estado de erro.

## 3. Sistema visual funcional (sem arte)

Adotar uma gramática que funcione até em escala de cinza e seja reutilizável no MVP e em Alisyum. Manter a fonte atual (Source Sans 3 para leitura; Cinzel apenas em títulos curtos onde não prejudica a identificação). Não ampliar títulos enquanto efeitos ficam menores.

### 3.1 Contrato de hierarquia

| Nível | Leitura desejada | Tokens iniciais no canvas de design 1440×900 |
| --- | --- | --- |
| A — decisão | Custo disponível, vida, dano previsto, ação principal | 22–28 unidades de fonte, semibold/bold, números alinhados e sinal específico. |
| B — conteúdo | Efeito da carta, nome de runa, escolhas, texto de diálogo | 18–22, regular/semibold, alinhado à esquerda. |
| C — contexto | Categoria, duração, destino, raridade, contador secundário | 16–18; se essencial à decisão, elevar a B. |
| D — detalhe | IDs, origem, raw traces ou metadados opcionais | 14–16 apenas em inspeção; não usar como informação imprescindível. |

Esses valores são pontos de partida lógicos e não garantem altura física de glifo. Medir os pixels efetivos a 1080p contra a referência de 18 px da XAG 101, e testar 100%, 120%, 150% e 200% de escala. Hoje `Preferences` limita a persistência da escala a 0,9–1,2; expandir a opção **junto** de layouts alternativos, sem fazer o texto vazar. Em 200%, permitir leitura paginada/inspeção com todo o conteúdo e ações acessíveis, em vez de prometer que a mesma grade caberá inteira.

- Espaçamento baseado em 4/8/12/16/24/32, usar grupos separados pelo espaço e não por muitas molduras.
- Alvo principal de clique recomendado mínimo de 44×44 unidades lógicas; controle secundário de ícone não operar com hitbox menor que o necessário. Testar clique real e foco em todos os breakpoints.
- Normal, hover, foco, selecionado, válido, incompatível, sem saldo, processando, concluído e erro têm tratamento explícito. Foco é um contorno próprio; seleção não é sinônimo de hover. Usar ícone/forma e texto além de cor.
- Ação primária única por contexto: `Encerrar turno`, `Concluir etapa`, `Confirmar gravação`, `Viajar`, `Continuar`. O resto é opção, operação reversível ou detalhe.
- Não usar mudança de tamanho que reordene conteúdo apenas para indicar estado. Feedback por contorno, modulação, overlay, texto curto; motion reduzido preserva informação.

### 3.2 Cabeçalho e rodapé comuns

Os contextos normais de jornada (mapa, preparo, recompensa, loja, forja, loadout) compartilham uma barra curta: `local atual | vida | ouro | fragmentos`, mostrando somente recursos pertinentes e publicados. Ações de navegação numa faixa previsível. `Pausa/Configurações` em local consistente; `Voltar` devolve a tela anterior; `Mapa` nunca escolhe o próximo nó. Logs, verificação de replay, conexão e diagnóstico ficam em área secundária apropriada.

O rodapé fica fora da região rolável. Se a atividade possui ação de conclusão publicada, ela permanece aparente em todas as posições de scroll. Se não possui, explicar requisito real e manter uma saída de inspeção possível onde a regra autorizar. Nenhum botão falsamente ativo.

## 4. Redesign proposto por fluxo

### 4.1 Carta, informação e custo (fundação)

A carta é o objeto central deste jogo e a peça que muda de função. Manter `CardView` como componente compartilhado e `CardFactsPresenter`/`CardSectionPresenter` como fonte de composição de texto; criar variantes de apresentação **somente** por contexto (`hand`, `offer`, `collection`, `compare`, `inspect`). Não usar outro modelo de regras.

Estrutura em todos os contextos: cabeçalho com nome e custo de jogar grande; categoria curta e símbolo funcional já existente; efeitos por linhas; condições/duração/consumo; runas equipadas com símbolo e tooltip. Eliminar o nome repetido sob o ícone e rótulos que desperdiçam uma linha inteira. O espaço de ilustração futura pode existir como pequena área de identidade quando não houver ilustração. Símbolos existentes e texto bastam.

Exemplos de representação (apenas wireframe, valores ilustrativos não alteram conteúdo):

- `Corte` · `1 energia` · `Cause 6 de dano ao alvo.`
- `Fragmentação` · `Cause 6 de dano total em 3 impactos.` · detalhe: orçamento compartilhado.
- `Ressonância` · `Enquanto na mão: ao aplicar um residual, ganhe 2 de Bloqueio.` · `1× por ação`.

Preservar semântica de `Base`, `ComposedBase`, valores de fonte e de impacto. Se o efeito depende de personagem, alvo, crítico ou contexto, exibir valor composto autorizado, `variável`/`prévia necessária` com motivo conciso, ou intervalo apenas quando a engine fornecer seus limites. Não transformar `Calculated` em número fictício, não tratar 6 / 3 impactos como 18 de dano e não calcular potencial de sinergia no Godot. Destacar número modificado com indicação `+/-` e não só azul/vermelho.

Na loja exibir **preço de compra** separado de **custo para jogar**; na recompensa, destino e efeito terminal da escolha fora da face. Na comparação de transformações mostrar `+ adicionado`, `− removido`, `↔ substituído` e valor anterior → novo quando os fatos permitirem. Nome longo ocupa duas linhas e, se ainda maior, a inspeção mostra o nome completo; não reduzir o título a 8 unidades.

Definição de concluído: as mesmas instâncias têm mesma descrição em combate, loja, forja e coleção no mesmo contexto; nenhum valor ausente vira zero falso; texto essencial da carta selecionada é legível sem depender de ícone ou arte.

### 4.2 Combate

**Zonas fixas:** cabeçalho compacto com turno/rodada no centro geométrico; campo com dois lados e HUD ancorada ao ator; energia centralizada imediatamente acima da mão; mão e controles do rodapé. Não restaurar a linha de resumo abaixo da energia, que o usuário já solicitou remover. Ajustar espaço útil por agrupamento, não por expansão de caixas.

```text
┌──────────────────── Cabeçalho: jogador • RODADA / TURNO • log / pausa ────────────────────┐
│     Aliado                                        Inimigo A             Inimigo B        │
│     identificação                                 identificação         identificação    │
│     VIDA ██ 67/90  BLOCK 8                        VIDA ██ 31/55        VIDA ██ 12/48      │
│     [calor][efeito][efeito]                        [5 badge + mais]      [5 badge + mais] │
│                                                   intenção 12 → você      defender 8      │
│                        [resumo causal pequeno, temporário e não interativo]              │
│                                        ENERGIA 3/3                                       │
├── Pilha Compra ──────── mão: carta selecionada / faixa adaptativa ────── Descarte/Exílio ──┤
│                                                             ENCERRAR TURNO (sempre visível)│
└───────────────────────────────────────────────────────────────────────────────────────────┘
```

**Vida e status:** número de vida e barra fazem um grupo reconhecível; bloco ao lado com escudo e número, inclusive 0 quando contextual. Definir dimensão fixa por slot da HUD e do combatente, medir `Rect2` de arte, HUD e hitbox antes/depois de 0/1/5/10 status, remoção, stack 999 e duração. Os primeiros cinco status continuam legíveis e estáveis, overflow `+N` abre uma lista acessível por clique e teclado, sem reflow da peça. Ordenação determinística: manter os status já visíveis, inserir novos nos lugares disponíveis; se houver prioridade mecânica publicada (por exemplo, expiração), reservá-la de forma previsível. Não mover nomes e ícones a cada tick. O mesmo vale para o herói e quaisquer companheiros elegíveis; o Calor da Runa é um contador desse grupo, não energia, conforme decisão do projeto.

**Intenções:** expressão imediata como `⚔ 12 × 2 → você` ou `Escudo 8`, com nome e explicação detalhada no tooltip/painel. Não substituir uma intenção publicamente desconhecida por dano calculado: `? · efeito incerto` permanece honesto. Alvo válido, selecionado, apontado e foco são estados diferentes. Confirmar jogada só pelo comando anunciado.

**Mão:** 1–5 cartas podem conservar arco moderado; 6–8 reduzir ângulo e espaçamento móvel, preservando faixa de custo e título; 9+ usar paginação/scroll com indicação `7–12 de 12` ou leque que desliza para a seleção. O jogador deve conseguir navegar até cada instância e ler por foco sem zoom da arena. A carta inspecionada pode ser elevada em overlay que não encobre `Encerrar turno`, pilhas ou HUD. Não estender a mão sobre áreas de clique dos inimigos. `Esc` cancela; controle por setas/Tab e Enter; pressionamento repetido durante animação não duplica ação. Nenhuma carta acessível apenas por mouse.

**Cadeia:** a apresentação continua assíncrona, baseada nos registros reais da engine. Mostrar origem e contribuição (`Brasa: +Queimadura → Ressonância: +Bloqueio → Catalisador: compra`). Agrupar repetição de impactos sem esconder números; deixar consultar log com ordem causal e instâncias. Resumo ocupa posição estável e não bloqueia hitbox; movimento reduzido troca efeitos animados por estados legíveis. Botão de avançar/pular permanece disponível quando a apresentação permitir. Não sugerir cadeia já resolvida se não houve registro correspondente.

Definição de concluído: jogador sabe quais cartas tem, energia, ameaças, alvos e saída; consegue ler pelo menos cinco status, executar/inspecionar cadeias, concluir turnos com mão vazia ou cheia, sem alterar escala de ator/peça nem mudar geometria de controle por efeito novo.

### 4.3 Loja e recompensas — decisão sem rolagem obrigatória para sair

Problema P0 principal fora de combate. O `ActionScroll` contém a lista primária e secundária no layout atual. Separar controle de jornada da listagem: `Header / Categorias / Scroll das ofertas / Footer fixo`. O rodapé não deve ser filho do scroll. Reutilizar `ActivityOptionsView`, `ActivityPresenter` e referências de opções publicadas; não implementar compras locais.

```text
┌ BAZAR DAS CINZAS DE ABAR                  Ouro 77             Baralho 12 · Equipamento ┐
│ [Cartas 5]    [Runas 12]     [Todas / Compráveis]      [buscar apenas se útil]          │
│ ┌────────┐ ┌────────┐ ┌────────┐ ┌────────┐                 Detalhe da seleção         │
│ │ carta  │ │ carta  │ │ carta  │ │ carta  │                 Preço / efeito / custo     │
│ │ 40 ouro│ │ 60 ouro│ │ 40 ouro│ │ 25 ouro│                 Comprar ou inspecionar     │
│ └────────┘ └────────┘ └────────┘ └────────┘                                            │
│                 [área rolável da categoria, nunca o rodapé]                           │
├───────────────────────────────────────────────────────────────────────────────────────┤
│ Renovar estoque · 15 ouro    • 1 compra realizada       CONCLUIR ETAPA →               │
└───────────────────────────────────────────────────────────────────────────────────────┘
```

- `Cartas` e `Runas` têm suas próprias grades e contagem; itens adquiridos exibem `Comprado` ou permanecem fora da lista comprável com estado explícito. Distinguir runa de carta de inscrição de equipamento por tipo. Mostrar preço junto a cada opção, com a unidade (ouro ou item autorizado).
- Oferecer filtro `Compráveis` como opção, sem esconder o catálogo por padrão. Estado `Sem ouro · faltam 13` só quando valores publicados permitirem calcular, sem desabilitar a inspeção; a regra de compra é da engine. O commit `ece48337` já bloqueia ofertas e reroll sem saldo e recupera após rejeição; manter essa proteção, inclusive cliques repetidos, pending idempotente e saldo real após confirmar.
- `Concluir etapa` e `Renovar` visíveis de qualquer posição. Renovação paga tem custo e confirmação, não se confunde com saída. Concluir não compra nada; se disponível, uma ação direta. Saída de uma decisão obrigatória nunca inventa progresso.
- Mensagens de compra mostram resultado concreto: `Carta adquirida → Reserva`, `Runa adquirida → Inventário`, `Saldo 17`. A próxima ação explícita `Equipar agora` oferece retorno à loja e mantém posição da listagem. Só mostrar quando o comando publicado permitir.
- Recompensa: enunciado `Escolha 1`, ofertas em grade centralizada com grande área para regras em vez de cards espremidos à esquerda. Botões `Guardar`/`Decompor` associados à carta; quantidade de material, destino e término de escolha apresentados. `Pular`/`Concluir` apenas se autorizado. Não sacrificar a regressão de uma linha de três ofertas sem scroll já coberta por `reward_choice.gd`.

Definição de concluído: o jogador abandona o Bazar sem comprar após 0, 1 ou várias compras; os controles ficam dentro da janela pequena e após rolar para o último item; uma compra rejeitada não trava nem consome ouro; há reconciliação e opção visível de recuperação quando a sessão não está sincronizada.

### 4.4 Forja, inventário de runas e comparação

O arraste nativo existente é a interação principal, já validada com duas cartas de mesma definição. A biblioteca de runas deve parecer parte do baralho, não uma tela de seleção textual. Aproveitar `rune_drag.gd`, `rune_ui_manifest.json`, `pattern_workshop_inventory.gd` e `card_modifier_inventory_view.gd`.

- Bandeja compacta à esquerda ou acima em janela estreita, item com símbolo único, nome, quantidade e resumo `Afinidade`, `Forma`, `Vínculo` conforme dados de apresentação. Destino mostra a carta ou `motivo de incompatibilidade`, inclusive slot ocupado. Não inferir compatibilidade de tags no cliente.
- Cards do baralho com custo/runa/effect keywords visíveis; filtro opcional `Compatíveis (N)`, mantendo ação para ver todos. Destaque de drop permitido sem redimensionar grid. Cursor carrega símbolo e nome; drop fora/Esc/falha não altera carta.
- Painel de comparação real: efeito anterior, removido/substituído, novo efeito; impacto no custo e quantidade de impactos, quando publicados. Duas cartas completas são opcionais/expansíveis, a tabela de diferenças fica acima. Não reclamar um ganho numérico fora do estado/composição auditável.
- Confirmação permanente `Gravar [nome] em [carta]` + aviso de vínculo definitivo; `Cancelar` tem foco de retorno. Preview assíncrono debounced e versionado; não aceitar resposta velha ou duas aplicações. `Aplicar depois` mantém unidade disponível; reencaixe/swap continua fora do escopo.
- Nas telas de largura curta, mudar comparação para sequência vertical com rodapé fixo; scroll interno permite ler tudo a 960×540/200%. Não usar `popup_centered_clamped` como única defesa se `minimum_size` do conteúdo excede janela.

Definição de concluído: adquirir e gravar primeira runa em até três etapas de decisão, sem alternar entre menus que não ajudam. Soltar numa carta incompatível não emite comando, e a comparação permite entender o efeito trocado sem ler os dois cards inteiros.

### 4.5 Mapa e navegação de jornada

- Usar melhor a largura do mapa sem mudar a topologia; espaçamento entre profundidades, tamanho previsível de ícones e nome curto de atividade. Exibir nome completo no painel/tooltip/foco, sem exigir hover para descobrir a categoria.
- Legenda pequena por símbolo e texto: combate/elite, evento, loja, forja, recompensa, descanso e chefe (somente tipos existentes). Para cada nó distinguir `visitado`, `atual`, `concluído`, `disponível`, `bloqueado`, `selecionado` por sinais redundantes, nunca só cor. A linha da rota completada forte e caminhos bloqueados mais fracos, mas ainda legíveis.
- Ao concluir etapa, o painel mostra `Escolha o próximo destino` e as alternativas legais visíveis; manter contexto do destino atual. Duas fases `selecionar` → `viajar` para bifurcação salvo alternativa inequívoca publicada. Ação de Travel nunca dispara por passar o mouse, abrir mapa ou refocar janela.
- Detalhe de nó: tipo, efeito/recompensa **conhecida/publicada**, rota acessível, custo se houver e ação; não revelar resultado aleatório, chefe oculto ou número inventado. Se existem três saídas, comparação breve da função: `Descanso — recuperar`, `Forja — runas`, `Armaria — equipamento`; textos devem vir do conteúdo real.
- Pan/zoom/clique, foco entre nós e `Centralizar` com feedback. Não prender rolagem na cena em telas pequenas. Ao retornar do loadout, preservar scroll, seleção e nó atual, sem auto avançar.

Definição de concluído: dado um nó concluído com três ramos, jogador identifica os três tipos e escolhe um com teclado ou mouse, sem clicar em destino inválido e sem depender de truncamento ou decifrar ícones.

### 4.6 Preparação, loadout, baralho, atributos

- Organizar em blocos: `Visão geral`, `Equipamentos/Inscrições`, `Baralho`, `Runas de cartas`, `Relíquias`, `Companheiros` conforme disponibilidade real. Se a aba não é suportada pelo pacote, ocultar ou justificar. Não renomear conceito mecânico de inscrição para runa de carta. MVP mantém regras simples.
- No inventário, substituir tiles enormes e genéricos por cartões compactos/lista filtrável com nome, categoria, efeito curto, custo/capacidade e estado `equipado/livre`. Mostrar detalhes e motivo de incompatibilidade da seleção de forma contextual. Alternar modo lista/grade, mantendo filtro e foco.
- Área de slots usa nomes reais e desocupado/ocupado, admitindo clique e drag. Layout de Alisyum precisa representar claramente identidade, arma, armadura e slots efetivos publicados. Atualizar capacidade e atributos antes/depois apenas quando a engine retornar preview.
- Deck: separar baralho ativo (quantidade e limites publicados) e reserva com títulos consistentes; exibir cartas compactas de fácil comparação e uma inspeção persistente. Poder mover/selecionar sem depender de scroll em duas colunas estreitas. Mostrar runas equipadas por carta, nomes de instância e cópias distintas quando relevante.
- Drawer de atributos agrupado em sobrevivência, ataque, afinidade, utilidade e `Origem dos atributos`. Mostrar valores relevantes à carta/slot escolhido; sem despejar toda a ficha. Layout de 3 painéis em janela larga e detalhe/drawer alternável na estreita. Manter salvamento automático e indicar `Salvando`, `Equipado`, `Não foi possível salvar — tentar novamente`; nunca mostrar sucesso antes de snapshot/recibo.
- Botões destrutivos como limpar/desequipar tudo são operações secundárias, explicam consequência, confirmam ou desfazem somente se o contrato permitir. Retorno não descarta rascunho silenciosamente.

Definição de concluído: montar uma carta com runa e equipar inscrição é encontrável em menus distintos, selecionar uma peça e identificar qual slot/estatística muda é óbvio; retorno de um painel/modal preserva o foco e o estado.

### 4.7 Menu principal, seleção, diálogo e fim

- Menu principal concentra `Continuar jornada` (se há save) ou `Nova jornada`. Mostrar resumo de save apenas com dados publicados (campanha, herói, capítulo, etapa). `Nova jornada` indica que cria outra partida; não sobrescrever/abandonar sem confirmação explícita.
- Apresentar seletores de campanha e identidade como opções de jogo legíveis, em vez de um dropdown técnico. Cartão funcional com nome, recurso principal, estilo de build e acesso à classe. Usar modos/identidades realmente publicados; se a configuração atual não anuncia todas as identidades, expor seleção autorizada via contrato em etapa separada, sem IDs hard-coded em edição de save.
- Pausa: `Retomar` primeiro, configurações, retorno ao menu com estado de save; abandono separado e confirmado onde existir. Remover explicações internas da engine do caminho do jogador.
- Diálogo: nome do interlocutor/título, região de leitura de largura limitada (meta 50–75 caracteres por linha numa coluna comum), parágrafo e opções. Espaço revela texto/avança apenas no contexto correto; movimento reduzido permite apresentação instantânea, teclado alcança todas as escolhas. Não revelar consequências que o jogo não publicou.
- Derrota e encerramento: resultado do encontro, o que foi preservado, `Preparar/Retentar` se publicado, `Continuar/Escolher` e, no fim de capítulo, resumo de build/runas e `Nova jornada`. Verificação de hash permanece ferramenta, não botão central de jogador.

## 5. Contrato de interação e estados

Para todas as telas, tratar estado da UI separadamente do layout. Ordem canônica: `snapshot bem-sucedido → opção publicada → gesto confirmável → comando versionado → recibo → reconciliação`. A engine decide custo, compatibilidade, alvo, modalidade, transformação e progresso. UI pode sinalizar falta de saldo a partir de preço e balanço publicados, nunca assumir autoridade nem mascarar rejeição.

| Estado | Interface deve mostrar | Ações |
| --- | --- | --- |
| Disponível | Informação e CTA habilitadas | enviar uma escolha publicada |
| Sem saldo / incompatível | motivo textual junto ao controle; outros destinos permanecem ativos | inspeção, escolher outro, sair se permitido |
| Processando | feedback imediato e escopo da operação; não apagar todo o contexto | impedir duplicação, permitir ações que o contrato não bloqueia |
| Rejeição definitiva | motivo compreensível, estado anterior preservado | recuperar sessão sem reenviar compra |
| Resultado incerto | `Verificando operação`, não comprar de novo com outro ID | recuperação idempotente pelo comando original; botão visível |
| Desconectado | contexto visível porém leitura somente, reconexão persistente | reobter snapshot e foco |
| Confirmado | resultado real e destino; feedback breve | continuar pelo novo conjunto de opções publicadas |

Preservar comportamento corrigido da loja, `GameSession`, `ActivityController`, `GameplayRouteResolver`, refresh por snapshot, scopes/versionamento do drag, replay determinístico e testes. Separar diagnóstico de erro de apresentação: não mostrar mensagem de engine crua como única ação; incluir razão em português/inglês e um próximo passo. `Esc` volta/cancela o contexto mais interno, não abandona a partida.

## 6. Roadmap de implementação (um commit por etapa)

O trabalho deve ser incremental, com comparação antes/depois em mesma condição. Não incluir os arquivos modificados anteriores de HUD/gambits/run ou a planilha temporária. Se uma questão exigir alterar a engine, isolar num commit de contrato com teste, não adaptar um cálculo na UI.

| Etapa | Entregáveis e caminhos principais | Gate e commit sugerido |
| --- | --- | --- |
| 0 — Baseline reproduzível | Runners de captura em projeto isolado; metadados da resolução efetiva, escala, estado, locale e revisão. Lista de casos `P0` e matriz por campanha. | Nenhuma mutação de save ou UI. `test(ui): establish legibility and interaction baseline` |
| 1 — Hierarquia e acessibilidade | Tokens/tamanhos reutilizáveis em `data/themes`, `visual_theme_profile.gd`, `card_visual_style.gd`, `app_theme.gd`, `preferences.gd`, localização. Estados de foco, alto contraste e escala com reflow. | Medição e teste de contraste, fontes e navegação. `feat(ui): establish legible typography and semantic states` |
| 2 — Carta canônica e detalhes | `card_view.tscn`, `card_view.gd`, `card_facts_presenter.gd`, `card_section_presenter.gd`, painel de detalhes e comparação de fatos. Card compact/full e diferenças. | Custos/efeitos verdadeiros; sem regressão de instância, hover, custo alternativo, carta modificada. `feat(ui): simplify card faces and authoritative details` |
| 3 — Combate e mão | `combat_mvp_view.gd/tscn`, `card_hand.tscn`, atores, `combat_header_view.gd`, pilhas e badges, `rune_chain_presenter.gd`. Centro geométrico, 5+ status, intenção, layout mão longa. | Hand 0–20, atores fixos, hitboxes, preview e término do turno, replay. `feat(combat-ui): improve combat decision hierarchy` |
| 4 — Loja, recompensa e feedback | `journey_view.tscn`, `activity_options.tscn`, `activity_screen.gd`, `activity_presenter.gd`, `activity_options_view.gd`; categorias, footer, detalhe/compra, estados. | 5 cartas+12 runas, rolagem longa, 0/1/N compras, rejeição/retry, Complete. `feat(ui): make shops and rewards decision-first` |
| 5 — Bancada e vínculo | `pattern_workshop_inventory.gd`, `pattern_workshop_view.gd`, `rune_drag.gd`, `card_modifier_inventory_view.gd`, `transformation_confirmation.tscn`. Layout responsivo, diferenças e inventário. | Arraste real em ambas as rotas, cancelamento, alvo inválido, versão antiga, confirmação e fechamento de modal em 960×540. `feat(ui): clarify rune binding and comparison` |
| 6 — Jornada e loadout | `map_view.gd`, `map_view.tscn`, `map_inspector.tscn`, `map_node_glyph.gd`, `unified_loadout_screen.gd`, componentes `loadout/`, controller e foco. | Ramificação real, retorno, deck ativo/reserva, slots e autosave. `feat(ui): improve journey navigation and build workspace` |
| 7 — Entrada, diálogo e diálogos finais | `main_menu_view.gd`, `main_menu.tscn`, `dialogue_panel.tscn`, `pause_menu.tscn`, `activity_ending.tscn`, overlays e traduções. | Continuar/nova jornada, quatro identidades existentes, decisões e retorno sem exposição técnica. `feat(ui): streamline campaign menus and contextual dialogs` |
| 8 — Certificação | Matriz de testes, capturas de cada etapa, métricas e playtest de duas builds. | Todos os gates críticos documentados, sem afirmar feeling validado sem jogador. `test(ui): certify readable MVP and Alisyum flow` |

Ordem de produto: **etapa 1 → carta → combinação combate/loja/forja → navegação**. Loja tem prioridade de urgência pela falha vivenciada, podendo antecipar apenas a mudança de footer e recuperação sem depender da revisão de carta. O redesign não é aprovado por screenshots isolados: cada etapa tem uma tarefa real que o usuário consegue realizar.

## 7. Folha de aceitação e validação

### 7.1 Matriz de testes obrigatória

Executar com janela real e captura do viewport (PNG de dimensões verificadas), 960×540, 1280×720, 1280×800, 1440×900, 1920×1080 e 2560×1080; escala 100/120/150/200% conforme implementação; inglês e PT-BR. No mínimo base nas seis resoluções, cenários críticos em 960/1280/1920, 200% em alternativa de leitura. Se um monitor limitar a janela, usar viewport offscreen/ferramenta de teste que comprove a dimensão; não aceitar arquivo nomeado com resolução que não tem.

| Cenário | Verificações |
| --- | --- |
| Combate | 1 herói, 1–4 unidades, chefe, nomes longos, 0/1/5/10 efeitos, stacks grandes, alvo incerto, mão 0/1/5/8/10/20, resize, rodapé fixo. Identidade, HUD e hitbox não mudam por status. |
| Carta | Custo 0, múltiplos custos, custo alternativo, desconhecido, impacto distribuído, expressão longa, status aplicado, reações na mão, duas cópias transformadas de forma diferente; custo de uso ≠ preço. |
| Loja | 17+ ofertas, scroll/filtro, saldo exato e insuficiente, item comprado, renovar com/sem ouro, cancelamento, falha simulada após compra, reconexão e concluir sem compras. Nenhum gasto ao sair e sem reenvio indevido. |
| Forja | aquisição grátis, com custo, runa disponível/vinculada, alvo inválido, comparação em janela curta, drag nativo real, teclado alternativo, revisão antiga, cancelar, aplicar uma vez. |
| Mapa | início, nó concluído com ramificação, retorno do loadout, inspeção de etapa incompleta, próximo destino legal e bloqueado, foco/zoom e nomes longos. |
| Loadout | ativos e reserva, slots publicados, filtro e item selecionado, autosave com rejeição, atributo base/composto, retorno e detecção de novo contexto. |
| Menus | sem save, com save, seleção autorizada de identidade, diálogo lento/instantâneo, pausa, configuração e retorno, derrota e resultado; nenhum botão principal fora da tela. |

### 7.2 Testes existentes a manter e ampliados

- Combate: `combat_mvp_preview.gd`, `combat_hud_reference.gd`, `combat_mvp_layout.gd`, `mvp_reference_*`, `reference_hand_geometry.gd`, `card_motion_polish.gd`, `card_presentation.gd`, `rune_chain_presentation.gd`, `rune_presentation.gd`.
- Economia/jornada: `shop_activity_recovery.gd`, `activity_reconciliation.gd`, `activity_map_completion.gd`, `reward_choice.gd`, `map_navigation.gd`, `map_world_one.gd`, `map_pattern_workshop.gd`, `pattern_workshop_screen.gd`, `pattern_workshop_inventory.gd`, `card_modifier_items.gd`, `rune_workbench_drag.gd`.
- Loadout: `unified_loadout_layout.gd`, `unified_loadout_autosave.gd`, `unified_loadout_recovery.gd`, `loadout_input.gd`, `run_loadout.gd`.
- Menus: `ivory_menu.gd`, `ivory_menus.gd`, `dialogue_contracts.gd`, `dialogue_reading.gd`, `settings_contracts.gd`, `pause_layers.gd`.
- Engine/API: `AlisyumShopTests`, `AlisyumRuneCompositionTests`, testes de composição, elegibilidade, inventário, replay e quatro classes. Gate por conteúdo revisado e comandos REST, com sandbox; nunca usar a partida real para testes de compra, retry ou projeção experimental.

Adicionar `ui_readability_contracts.gd` (tokens, altura efetiva de texto, overflow e hitbox), `ui_viewport_capture_contract.gd` (metadados e PNG), `shop_persistent_footer.gd` (rodapé/scroll/foco), `hand_long_readability.gd`, `rune_comparison_small_window.gd` e `build_workspace_navigation.gd`. Os nomes são propostas para implementação, não arquivos que já existem. Testes de layout detectam erros geométricos; não equivalem a avaliação humana de leitura. `tools/test-godot-ui.ps1`, Godot MCP e HeroScript MCP podem apoiar a validação isolada. Em qualquer falha de conector, usar os testes/renderer locais e documentar a falha.

### 7.3 Critérios mensuráveis de aceite

| Métrica | Meta |
| --- | --- |
| Ações primárias acessíveis | 100% dos estados e resoluções suportados; zero requisito de rolar até o fim para sair da loja. |
| Elementos essenciais | Nenhum texto cortado sem mecanismo de inspeção; custo, energia, vida, intenção, destino e saldo não dependem só de cor ou de tooltip. |
| Contraste | Alvo 4,5:1 conteúdo textual normal, 3:1 estados/inativos grandes, 7:1 no alto contraste para elementos pertinentes. Medir também em hover, disabled e foco. |
| Tipografia | Medir altura de glifos no renderer; 18 px referência PC/1080p. Escala maior mantém conteúdo e funcionalidade, mesmo com paginação/scroll. |
| Mão | Todas as 0–20 instâncias acessíveis por mouse/teclado, com nome, custo e efeito integral ao inspecionar; não sobrepor controles fixos. |
| HUD | Cinco status simultâneos por combatente e overflow; `Rect2` estável antes/depois de status, diferenças intencionais apenas por resize ou composição da arena. |
| Navegação | Zero armadilha de foco; cancelamento restaura o originador; enter não compra ou viaja sem seleção. Todos os fluxos normais testados sem mouse. |
| Erros | 0 comandos duplicados, gastos involuntários, perda de snapshot ou rota bloqueada após rejeição; estados recuperáveis têm ação disponível. |
| Eficiência experimental | Medir tempo para reconhecer ameaça, selecionar carta jogável, adquirir/enxertar runa, concluir loja e comparar rota. Obter baseline humano e buscar redução de 25% no tempo mediano ou menos telas/cliques sem reduzir compreensão. |

Não contar como sucesso a UI que abre menus mais rapidamente mas torna incerto o modo como uma carta foi modificada. Em playtest, pedir ao jogador para explicar **qual regra a runa substituiu, por que a cadeia disparou e como aproveitará a próxima escolha**. Registrar acertos de entendimento, não apenas preferência de cor.

## 8. Fora do escopo e próximos passos

- Arte final, ilustrações de cartas/personagens, cenários, texturas, partículas ornamentais, animações de alta produção, novos packs de áudio, mudanças de lore ou branding.
- Balanceamento, novos mods/cartas/runas, novas classes, reescrita do sistema de combate, permitir reencaixe/swap ou alterar regras de compra. Essas mudanças são outras decisões de produto.
- Redesign de laboratórios e telas técnicas fora do fluxo normal, salvo uso de laboratório como fixture dos componentes compartilhados.

**Resultado esperado:** um jogo que pode ser jogado com símbolos e tipografia simples, cuja profundidade de build é visível e explorável. A arte será adicionada apenas quando as tarefas de ler ameaça, selecionar carta, entender alteração, comparar runa, comprar e seguir a jornada já forem rápidas e inequívocas.
