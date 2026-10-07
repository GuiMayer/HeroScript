# Mão de cartas — pesquisa, direção de UX e plano de implementação

Data: 2026-10-07. Status: etapas 1–8 implementadas; validação da mão concluída na etapa 9, com uma ressalva externa sobre branches descrita ao final.

## 1. Objetivo e escopo

Transformar a linha de cartas em uma mão confortável de explorar, comparar e jogar. O componente deve funcionar em combate e no laboratório, independentemente dos recursos, tipos de carta, quantidades e zonas definidos pelo setting.

O foco é apresentação e interação na Godot. A engine continua decidindo ações legais, custos, alvos, efeitos e ordem autoritativa. Não criar limites de mão, ordenação de gameplay, seleção automática de custos ou regras de descarte para resolver problemas visuais.

Complementa [as diretrizes da carta](../roadmap/CARD_PRESENTATION_GUIDELINES.md) e [o laboratório e pesquisa inicial](../roadmap/CARD_HAND_DESIGN_LAB_AND_RESEARCH.md). Não modifica o GDD nem a anatomia consolidada das cartas.

## 2. Diagnóstico inicial, antes da implementação

| Evidência | Consequência | Direção |
| --- | --- | --- |
| `CardHandView` herda `ScrollContainer` e contém um `HBoxContainer`, com separação de 12. | Todas as cartas ocupam sua largura completa. Mãos médias rapidamente exigem rolagem. | Distribuição adaptativa, com leque suave e sobreposição limitada. |
| `CardView._emphasize()` amplia para 1,035 dentro do recorte da mão. | O destaque é pequeno e pode ser cortado; ampliar não garante leitura. | Elevação e endireitamento com área reservada e solução de destaque fora do recorte. |
| `set_cards()` remove e recria todos os controles. | A atualização não preserva identidade, foco, rolagem nem animações em andamento. | Reconciliação por ID da instância. |
| `CombatScreen._render()` limpa a tela e a seleção, e reconstrói a mão. | Corrigir somente `set_cards()` não estabiliza o fluxo real. | Manter o contêiner de mão durante atualizações do mesmo combate. |
| `_load_inspection()` chama `CardView.configure()` em cada carta; esse método reconstrói a face. | Novos detalhes podem repetir trabalho e afetar geometria. | Atualização idempotente de conteúdo, separada de layout e interação. |
| `FocusNavigation.wire()` calcula vizinhos pela posição dos controles. | Rotação e elevação podem mudar a navegação em um leque. | Vizinhos explícitos pela ordem da mão, sem depender do hover. |
| Ações legais e indisponibilidade já vêm do presenter; custos alternativos permanecem explícitos. | Existe uma boa fronteira de autoridade a preservar. | Reutilizar candidatos; não validar regras dentro da mão. |
| Tooltip agora é resumo passivo; inspeção é persistente e rolável. | A leitura completa já tem caminho utilizável. | Manter essa distinção; não reintroduzir tooltip interativo. |

`hand_row` expõe atualmente a implementação interna da mão ao combate. Essa dependência deve desaparecer durante a extração da API pública do componente.

O laboratório já oferece 0–40 cartas e testes em português/inglês, três resoluções, alto contraste e movimento reduzido. Quarenta é limite do controle de teste, não da engine. As fixtures não comprovam as regras de gameplay.

## 3. Pesquisa e aplicação ao projeto

### Cardgames: organização e comparação

Nicolas Kraj analisa Hearthstone, MTG Arena e Runeterra no redesign de Fairtravel Battle. O artigo destaca a disputa por espaço, agrupamento de informações relacionadas e leitura sem ocultar o campo. **Aplicação:** reservar uma região estável para a mão e destacar uma carta sem cobrir controles essenciais ou o alvo escolhido. O leque é nossa proposta, não uma conclusão universal do artigo. [GDKeys — The Card Games UI Design of Fairtravel Battle](https://gdkeys.com/the-card-games-ui-design-of-fairtravel-battle/).

A palestra de Derek Sakamoto documenta o processo de criação da interface de Hearthstone. Ela é uma referência de processo e iteração, não uma especificação de ângulos ou tamanhos. **Aplicação:** comparar alternativas no laboratório antes de adotar o layout no combate. [GDC — Hearthstone: How to Create an Immersive User Interface](https://www.gdcvault.com/play/1022036/).

### Hearthstone: não esconder o efeito básico

A Blizzard alterou as cartas Signature para colocar texto na face e manter sua posição consistente com outras cartas, facilitando a leitura na mão. **Aplicação:** sobreposição não deve tornar a inspeção obrigatória para toda decisão simples. Oferecer modo sem sobreposição e carta ativa inteiramente visível. [Blizzard — Signature Card Features, patch 34.2](https://news.blizzard.com/en-gb/article/24244424/34-2-patch-notes-events-and-features).

### Acessibilidade: leitura, foco e cancelamento

A XAG 101 recomenda escala de texto e formas de acessar conteúdo que ultrapassa a tela. **Aplicação:** nunca diminuir a fonte automaticamente para encaixar cartas; utilizar overflow e inspeção. O intervalo atual de 90–120% não equivale a atender sua recomendação de ampliação até 200%. [Microsoft — XAG 101](https://learn.microsoft.com/en-us/xbox/accessibility/xbox-accessibility-guidelines/101).

A XAG 112 orienta foco previsível e navegação consistente. **Aplicação:** esquerda/direita seguem a ordem visual, revelar a carta focada e devolver o foco após fechar inspeções. [Microsoft — XAG 112](https://learn.microsoft.com/en-us/xbox/accessibility/xbox-accessibility-guidelines/112).

A XAG 107 recomenda alternativas a gestos e cancelamento de ações de ponteiro antes da conclusão. **Aplicação:** arrastar nunca será obrigatório; confirmar no release e permitir cancelar sem enviar comando. [Microsoft — XAG 107](https://learn.microsoft.com/en-us/xbox/accessibility/xbox-accessibility-guidelines/107).

A XAG 103 recomenda canais adicionais à cor. **Aplicação:** disponibilidade, seleção e alterações terão texto ou sinais adicionais, preservando borda de raridade e cores numéricas. [Microsoft — XAG 103](https://learn.microsoft.com/en-us/xbox/accessibility/xbox-accessibility-guidelines/103).

### Godot: layout com uma única autoridade

Containers organizam seus filhos; um leque não deve disputar posição e rotação com o `HBoxContainer`. **Aplicação:** manter Containers para estruturar a tela, mas usar uma superfície própria para os slots da mão. A documentação também explica o uso de ScrollContainer para overflow. [Godot — Using Containers](https://docs.godotengine.org/en/stable/tutorials/ui/gui_containers.html).

## 4. Direção de design proposta

### 4.1. Três modos de apresentação

1. **Adaptativo, padrão:** poucas cartas ficam centradas e quase retas; conforme o espaço diminui, entram arco suave e sobreposição limitada. Ao atingir o mínimo de área exposta, ativar overflow.
2. **Leitura:** cartas retas, sem sobreposição, com rolagem horizontal. Permite comparar custos e textos sem hover constante.
3. **Visão geral da mão:** painel persistente de leitura, com as mesmas cartas/componentes, para consultar mãos muito grandes. Retorna à mesma instância e posição. Não é uma zona nova da engine.

Não definir “até dez cartas” como limite de funcionamento. A escolha usa largura disponível, dimensões efetivas, escala de texto e largura mínima exposta. Ao abrir a barra lateral, recalcular com o espaço restante.

Como nome e custos ficam na parte superior da carta, uma sobreposição horizontal esconde parte desses campos nas cartas inativas. **Esse é um trade-off real.** O leque serve para varredura e reconhecimento; o modo leitura resolve comparação simultânea. Não duplicar custos em uma moldura específica da mão nesta primeira versão.

### 4.2. Carta ativa e leitura

- Hover ou foco eleva e endireita a carta; seleção mantém um sinal persistente próprio.
- Apenas uma carta recebe o destaque principal por vez, conforme o último dispositivo usado. Hover não muda a carta selecionada para jogar.
- Elevar não desloca os slots vizinhos. Não animar fonte, largura dos campos nem altura da mão durante hover.
- Preservar arte, borda de raridade, cores numéricas e indicadores de alteração. Disponibilidade usa rótulo/símbolo separado.
- Reservar espaço vertical para arco e elevação. Quando o overflow exigir recorte, usar uma representação passiva de destaque fora dele, feita com o mesmo `CardView`; ela não envia comandos e não rouba input.
- Tooltip continua curto e passivo. Inspeção completa continua aberta por ação explícita. Suprimir consultas concorrentes durante drag, modal ou envio de comando.

### 4.3. Interação previsível

- Clique seleciona; o fluxo atual de alvos e alternativas de pagamento continua explícito.
- Não alterar inicialmente os casos em que o fluxo atual confirma uma ação sem alvo ou com um único candidato. Qualquer mudança nisso será uma decisão de UX separada.
- Escape/clique direito cancelam seleção antes de abrir pause. Inspeção primeiro fecha o próprio modal e devolve foco.
- Cartas indisponíveis continuam consultáveis; inspeção não significa permissão para jogar.
- Hover não dispara REST. Preview utiliza fatos já disponíveis, com loading/freshness explícitos quando necessário.
- Durante envio, congelar a identidade do candidato escolhido e impedir comandos duplicados; não inferir aceitação antes da resposta.
- Ordem visual padrão corresponde à ordem recebida. Ordenação manual e filtros ficam fora desta primeira entrega: podem esconder informação ou sugerir mudança da ordem autoritativa.

### 4.4. Mãos grandes

Manter passo mínimo entre cartas e aumentar a extensão horizontal, não comprimir indefinidamente. Oferecer scrollbar utilizável, botões anterior/próximo e revelação automática da carta focada. Mostrar contagem e indicador de conteúdo fora da área, sem presumir máximo de mão.

Rolagem do mouse atua só quando o ponteiro está na mão e nenhum modal está consumindo o evento. Hover não provoca rolagem automática contínua. A seleção preserva sua âncora após inserção/remoção; não manter apenas um deslocamento em pixels que aponta para outra carta.

## 5. Arquitetura proposta

Todos os nomes abaixo são propostas de novos componentes, exceto os arquivos já existentes.

| Componente | Responsabilidade |
| --- | --- |
| `HandVisualStyle` + recurso `.tres` | Política de arco, sobreposição, áreas mínimas, elevação e duração. Não contém limites ou regras do jogo. |
| `HandLayout` | Cálculo puro de posições, rotações, extensão e modo de overflow, a partir de dimensões e ordem visual. |
| `CardHandView` | Reconciliar instâncias e apresentar slots, destaque, overflow, estado vazio e foco. API pública, sem expor `HBoxContainer`. |
| Slot da mão | Separar posição de repouso da transformação visual da carta e armazenar sua identidade estável. |
| `HandInteraction` | Coordenar hover, foco, seleção visual, cancelamento e drag opcional. Emite intenções, não comandos REST. |
| `CardView` | Face compartilhada. Fora da mão pode manter seu destaque autônomo; dentro dela, delega transformações à mão. |
| `CombatScreen` | Adaptar intenções à seleção existente e aos candidatos publicados; chamar GameSession para comandos. |
| Presenters / GameSession | Continuar projetando fatos e comunicando com a engine. A mão não ganha autoridade sobre custos ou alvos. |

Evitar duas autoridades de tween: o `_emphasize()` atual da carta não pode disputar `rotation`, `scale` ou `z_index` com o gerenciador da mão.

Identidade é `cardInstanceId`, nunca `definitionId`: duas cartas iguais podem ter upgrades e estados diferentes. O laboratório utiliza `labId` como identidade local explícita. Uma mudança de run/branch/ator/zona deve mudar o escopo da reconciliação, impedindo reutilização indevida.

Separar estados ortogonais:

- interação: repouso, hover/foco, seleção e drag;
- sessão: pronta, enviando, playback, dessincronizada ou pausada;
- leitura: tooltip, inspeção e visão geral.

O estado de sessão existente continua autoritativo para bloqueios. Não copiá-lo para uma segunda máquina de gameplay dentro da mão.

### Cálculo de layout e hit testing

Para `n > 1`, o passo pode começar em `min(passo_preferido, (largura_útil - largura_da_carta) / (n - 1))`. Se ficar abaixo do passo mínimo, usar overflow. Zero e uma carta têm caminhos próprios, sem divisão por zero. São cálculos de UI, não cálculos de efeito.

A curva e a rotação serão limitadas e parametrizadas. Calcular bounds transformados, inclusive cartas das pontas e elevação, em vez de assumir que a largura não rotacionada basta. A altura reservada não muda com hover ou mão vazia.

Hit testing deve escolher uma única instância em regiões sobrepostas, usando zonas de repouso e uma margem de estabilidade. Trocar `z_index` ou elevar uma carta não pode iniciar um ciclo `entered/exited`. Ao clicar, vincular press e release à mesma identidade; se ela sair da mão ou o contexto mudar, cancelar a intenção. Testar isso com eventos reais de ponteiro, não só por geometria.

## 6. Etapas sequenciais

### Etapa 1 — Baseline e contrato de apresentação

- Registrar capturas atuais e preservar testes de mão vazia, fonte estável e componentes compartilhados.
- Criar `HandVisualStyle` com modo adaptativo/leitura, passo, ângulo, arco, elevação, margem exposta e duração configuráveis.
- Definir API pública: sincronizar modelos por ID/escopo, revelar instância, definir seleção e solicitar inspeção.
- Registrar que informações de jogo vêm do presenter e que mudanças visuais não emitem REST.

**Aceite:** alterar estilo no laboratório não muda snapshots; nenhuma dependência de nomes de recursos ou IDs de cartas no componente.

### Etapa 2 — Persistência e reconciliação antes do leque

- Migrar `set_cards()` para reconciliação por instância: manter existentes, inserir novas e remover ausentes.
- Manter a mão e seu estado visual durante refresh do mesmo combate. Adaptar `_render()`; não basta otimizar o componente filho.
- Separar primeira montagem de atualização de dados. Manter os outros contêineres principais quando necessário para não descartar a mão junto com o corpo.
- Preservar foco e âncora. Quando a instância desaparecer, usar o vizinho sobrevivente; encerrar seleção inválida.
- Tornar atualização da face idempotente, com comparação de campos relevantes e descarte de respostas antigas por sequência/contexto.
- Remover dependência externa de `hand.row`/`hand_row`.

**Aceite:** enriquecer uma carta não recria as demais; refresh sem mudança estrutural preserva objetos e posição; mudar branch/run não reaproveita estado antigo.

### Etapa 3 — Layout adaptativo e overflow

- Substituir a distribuição do `HBoxContainer` por superfície de slots; manter Containers na estrutura externa da tela.
- Implementar linha centrada, arco suave e passo mínimo com bounds reais.
- Implementar overflow horizontal e modo leitura com o mesmo conteúdo e identidade.
- Recalcular apenas por alteração de conteúdo geométrico, dimensões, estilo ou escala, não a cada frame.
- Reservar espaço para foco e impedir que a mão empurre End Turn, escolhas ou controles para fora da tela.

**Aceite:** todas as cartas permanecem alcançáveis em 0, 1, 2, 5, 8, 12 e 40 cartas, inclusive com a barra lateral aberta. Nenhuma fonte diminui para caber.

### Etapa 4 — Hover, seleção e zonas estáveis

- Aplicar elevação/endireitamento sem reorganizar vizinhos.
- Definir prioridade de hover/foco/seleção e uma única autoridade de transformação.
- Implementar hit testing estável, cancelamento press/release e prevenção de oscilação em sobreposição.
- Resolver destaque cortado pelo overflow com o mesmo componente em camada passiva, quando necessário.
- Manter tooltip e inspeção atuais; evitar dois grandes painéis de leitura simultâneos.

**Aceite:** mover o mouse lentamente ou rápido entre cartas não causa flicker; ponteiro parado não troca de carta sozinho; borda e valores preservam suas cores.

### Etapa 5 — Teclado, controle e QoL de navegação

- Fixar esquerda/direita pela ordem visual, sem recalcular vizinhos por cartas elevadas.
- Integrar com `FocusNavigation` sem deixar a passagem genérica sobrescrever os vizinhos internos da mão.
- Revelar foco fora da área e restaurá-lo após inspeção/pause.
- Adicionar ação de inspeção e visão geral à estrutura de remapeamento existente; atualizar os prompts com o binding real.
- Disponibilizar modo leitura nas preferências e navegação de overflow por botões. Atalhos posicionais podem ser opcionais, nunca a única forma de acessar cartas.

**Aceite:** selecionar, inspecionar, cancelar e jogar com candidatos legais funciona sem mouse; cartas indisponíveis continuam legíveis.

### Etapa 6 — Atualizações e animações incrementais

- Animar entrada só de instâncias novas, saída só da usada/removida e reposicionamento apenas quando a composição muda.
- Integração com `fly_to()`: confirmação vem da engine; evitar ghost duplicado ou replay da entrada de toda a mão.
- Uma operação rápida interrompe/retargeta o tween anterior com segurança, sem filas crescentes.
- Em movimento reduzido, aplicar o estado final sem arco animado/voo, mantendo os sinais de seleção.
- Bloquear interação obsoleta durante envio, playback, pause e reconexão, preservando leitura quando seguro.

**Aceite:** jogar carta não muda por um frame a fonte das restantes nem reinicia suas animações; erro REST não perde a carta visualmente.

### Etapa 7 — Visão geral e preferências

- Abrir painel persistente com as mesmas cartas, identificação por instância e navegação em um único eixo de leitura.
- Mostrar todas as cartas, não apenas as jogáveis; selecionar uma delas retorna ao mesmo fluxo de seleção, sem enviar comando.
- Configurar modo da mão e intensidade/duração do destaque; manter overrides de movimento reduzido e alto contraste.
- Expandir o laboratório para alternar layouts, adicionar/remover/enriquecer amostras e simular estados de sessão, sem API.

**Aceite:** mãos extensas e texto longo podem ser consultados sem depender de hover. Preferências temporárias do laboratório não são salvas.

### Etapa 8 — Drag opcional, após estabilizar clique

- Adicionar drag-to-play desativável e com limiar configurável que o distingue de clique/scroll.
- Reutilizar seleção e os mesmos candidatos do clique. Drop apenas escolhe uma intenção; não interpreta efeitos nem valida custos.
- Um drop ambíguo abre a escolha existente; multi-target e pagamentos alternativos não são resolvidos arbitrariamente.
- Cancelar fora de região válida, ao apertar Escape, abrir modal ou trocar snapshot/contexto. Nenhum comando é enviado nesses casos.
- Não incluir reordenação de gameplay nem gestos móveis específicos nesta etapa.

**Aceite:** clique e drag convergem para o mesmo candidato e resultado autoritativo; nunca ocorrem dois envios; tudo continua possível sem drag.

### Etapa 9 — Validação no combate e documentação

- Rodar laboratório, testes de apresentação, camadas, resoluções e smoke real com a API, em mais de um setting.
- Capturar modos adaptativo/leitura, cartas nas extremidades, overlay, mão vazia, overflow e inspeção.
- Atualizar as diretrizes e o README do laboratório, distinguindo comportamento entregue de opções futuras.
- Revisar alocação de controles, custo de relayout e chamadas REST. Não atribuir à mão melhorias de latência que exigem diagnóstico da engine.

**Aceite:** mesmo componente em produção e laboratório; nenhum fluxo duplicado de comando; resultados de gameplay/replay continuam os mesmos para a mesma sequência de comandos.

## 7. Matriz de testes e metas

| Área | Casos / meta |
| --- | --- |
| Resolução | 1280×720, 1280×800, 1920×1080 e 2560×1080; resize em execução e sidebar aberta/fechada. |
| Conteúdo | 0, 1, 2, 5, 8, 12, 40 cartas e stress maior, sem pressupor limite da engine; duplicatas com IDs diferentes. |
| Leitura | PT/EN, 90/100/120%, nomes/efeitos longos, recursos desconhecidos e custos alternativos. Escala até 200% é objetivo adicional que exige ampliar a infraestrutura global, não promessa desta primeira entrega. |
| Input | Ponteiro parado, troca rápida, press em uma/release em outra, navegação sem mouse, cancelamento e foco restaurado. |
| Estado | Remover carta focada, enriquecer preview, receber resposta antiga, enviar com erro, replay, pause, reconexão e troca de branch. |
| Estabilidade | Refresh idêntico: zero reconstruções e zero reinício de tween; hover: zero mudanças nos slots vizinhos ou fontes. |
| Desempenho | Meta inicial de relayout abaixo de 2 ms para 40 cartas na máquina de referência, medida após aquecimento; input visual no próximo frame. Registrar resultados, sem declarar garantia para todo hardware. |
| Comunicação | Hover, foco, scroll, modo leitura e inspeção visual já carregada não criam novas chamadas REST; gestos cancelados: zero comandos; confirmação: um comando. |
| Autoridade | Snapshots não são mutados. A sequência equivalente de comandos produz os mesmos hashes, independentemente do modo visual. Não confundir isso com garantir resultados iguais se o jogador escolhe comandos diferentes. |

Automatizar regressões de identidade, hit testing, foco, ausência de mutação e REST. Para layout e animações, combinar assertions com capturas reais: um teste geométrico aprovado não prova que a mão ficou boa de usar.

## 8. Ordem e corte recomendado

**Ordem:** contrato → reconciliação → layout → hover/hit testing → navegação → animações → visão geral → drag opcional → validação final.

As etapas 1–7 entregam a melhoria principal. A etapa 8 é um polimento opcional, separado para não atrasar a estabilização. Validar continuamente desde a etapa 1; a etapa 9 é a integração final, não o primeiro momento de teste.

Primeiros parâmetros experimentais: inclinação máxima de 6–10°, elevação de 32–48 unidades lógicas e transições de 100–160 ms. O passo mínimo deve ser ajustado pelos testes de seleção e leitura, não escolhido para forçar uma quantidade específica na tela. Esses números são hipóteses de protótipo deste projeto, não medidas prescritas pelas fontes.

Não começar por efeitos holográficos, balanço contínuo, física ou troca completa da arte. A prioridade é encontrar a carta, ler, selecionar e jogar sem movimentos inesperados.

## 9. Implementação e verificação

### Componentes entregues

- `HandVisualStyle` e `data/default_hand_visual_style.tres`: passo mínimo de 92, ângulo máximo de 6°, arco de 10, elevação de 32 e transição de 130 ms. São unidades de apresentação, não limites do jogo. A velocidade global de animação ajusta a duração; movimento reduzido aplica o estado final.
- `HandLayout`: geometria pura e regiões de hit testing independentes das transformações visuais. Quando o passo mínimo não cabe, aumenta a extensão rolável.
- `HandInteraction`: identidades de hover, foco, seleção e gesto, sem acesso à engine.
- `HandHitSurface`: superfície de input estável. Ela recebe press/release, wheel e captura de arraste; elevar a face ou mudar seu `z_index` não muda os vizinhos clicáveis.
- `CardHandView`: sincroniza por `cardInstanceId`/`labId` e escopo de run/branch/combate/ator/zonas, mantendo controles sobreviventes, foco e âncora. Expõe sincronização, seleção, revelação, inspeção e intenções; não interpreta regras. O combate injeta seu cabeçalho na toolbar compartilhada, sem conhecer os slots internos.
- `HandOverviewDialog`: leitura persistente sem sobreposição, usando os mesmos componentes. Escolher uma instância devolve a seleção à tela, sem jogar automaticamente. Inspeção abre um modal filho; fechar restaura o foco.

`CardView.configure()` é idempotente para modelo, escala, contraste e valores da política visual, inclusive alterações no mesmo recurso. Dentro da mão, apenas o gerenciador da mão transforma a carta. Compra/entrada anima apenas novas instâncias; uso confirmado mantém os sobreviventes e reutiliza o voo existente. A carta retida pela engine não desaparece como se tivesse sido descartada.

A inspeção mantém uma âncora de leitura por identidade: mover o foco da carta
para o botão da toolbar não perde a carta que será inspecionada. Os vizinhos
esquerda/direita são refeitos após alterações de composição, inclusive dentro
da visão geral, cujo atalho de inspeção abre o modal filho no próprio painel.

O destaque cortado horizontalmente pelo overflow usa uma cópia passiva de `CardView`, sem foco, tooltip ou input, dentro da região de leitura. Hover não provoca rolagem automática. Foco revela a instância original. Modais, pausa, troca de contexto e bloqueio de sessão cancelam gestos/transientes.

### Preferências e integração

Em **Configurações → Geral → Mão de cartas**, escolher modo adaptativo/leitura, intensidade do destaque e drag opcional (desligado por padrão). Inspeção e visão geral são ações remapeáveis: padrões `I`/`H`, ou `X`/botão esquerdo superior no controle. Os prompts mostram o binding atual. A duração permanece na política visual e na velocidade global de animação, não em um novo sistema paralelo.

O arraste converge para os candidatos já publicados. Soltar num alvo apenas escolhe essa intenção; pagamentos alternativos e conjuntos de alvos continuam explícitos. Não existe ordenação autoritativa, regra de custo ou comando REST no componente da mão.

A reserva de espaço foi conferida no combate real, não apenas no laboratório. Cabeçalho, zonas e ferramentas da mão compartilham a mesma faixa. Os atores usam uma composição compacta com retrato ao lado das informações; intents extensos têm resumo de duas linhas e tooltip completo. Isso mantém recursos, alvos e rodapé visíveis nos encontros iniciais testados. Conteúdo arbitrariamente extenso ou muitos atores continua usando a rolagem do campo; não há garantia de caber todo JSON simultaneamente.

### Resultados

| Verificação | Resultado |
| --- | --- |
| `hand_ux.gd` | Identidade, zero relayout em snapshot idêntico, hover estável com eventos reais, fontes preservadas, foco por ordem, cancelamento, drag sem clique duplicado, proxy passivo e visão geral de 100 cartas. |
| `card_hand_lab.gd` | Componentes iguais aos do combate; PT/EN, 100/120%, 1280×720, 1920×1080 e 2560×1080; mão vazia e 0–40 cartas; não altera sessão nem salva preferências do laboratório. |
| `card_presentation.gd`, `layers.gd`, `resolutions.gd` | Regressões de semântica visual, fronteira com a engine, localização e resolução. |
| `pause_layers.gd` | Pausa em canvas independente acima da mão; 1280×720, 1920×1080 e 2560×1080; clique em Retomar, restauração de foco e cancelamento de arraste. Sem headless, verifica também o escurecimento renderizado de um elemento no `z_index` máximo do gameplay. |
| `hand_online.gd` | API real, settings `default`/`ascendant`; preservação após refresh/comando real; PT/EN, 100/120%, quatro resoluções incluindo 1280×800 e ultrawide; controles e atores visíveis; replay válido nos dois settings. |
| Capturas renderizadas | Adaptativo, leitura a 120%, visão geral, mão vazia e combate real, inspecionadas visualmente. |
| CPU na máquina de teste | Aproximadamente 25–30 µs para a matemática de 40 cartas e 0,23–0,35 ms para atualizar seus slots/vizinhos em movimento reduzido. Não inclui GPU, passes de Containers posteriores ou tempo da API; não é garantia de frame/latência. |

Reproduzir os testes específicos da nova mão, a partir da raiz:

```powershell
godot --headless --path examples/godot-engine-showcase --script res://tests/hand_ux.gd
godot --headless --path examples/godot-engine-showcase --script res://tests/pause_layers.gd -- --layout-smoke
godot --headless --path examples/godot-engine-showcase --script res://tests/hand_online.gd -- --layout-smoke --api-url=http://127.0.0.1:5271
```

O teste online cria runs de QA na API indicada e restaura as preferências visuais ao terminar. As capturas ficam em `.runtime/card-lab/`, fora do versionamento. O laboratório aceita `--lab-reading`, `--lab-overview` e `--lab-scale=1.2` para reproduzir essas apresentações.

**Ressalva:** o smoke geral passou combate, seleção, pause, idiomas, replay e inspeção histórica, mas falhou em criar branch e consultar sua árvore na API existente. O log mostra `Unsupported run commit engine: 6` durante a reconstrução de `RunLineageIndex`, ao ler commits antigos do armazenamento. Isso não foi corrigido nem ocultado nesta mudança de mão; nenhum save foi apagado ou migrado. Portanto, não declarar o smoke geral inteiro aprovado nem a troca real de branch validada ponta a ponta. A separação de escopo entre branches está coberta localmente. Alguns testes também mantêm o aviso preexistente de duas instâncias ObjectDB no encerramento.
