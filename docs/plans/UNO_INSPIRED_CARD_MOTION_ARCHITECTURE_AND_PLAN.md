# Animações de cartas: análise do UNO Clash e plano para a Godot

Data: 26/10/2026. Estado: proposta; nenhuma mudança de comportamento implementada por este documento.

## 1. Objetivo e limites

Adaptar o ritmo, a resposta ao ponteiro e a continuidade visual de `examples/uno-clash-web` para `examples/godot-engine-showcase`, preservando os executores compartilhados pelo laboratório e pelo combate.

O objetivo é melhorar a sensação de manipular cartas físicas, sem trocar o layout, o estilo marfim/preto/vermelho, as regras de gameplay ou a autoridade da REST API. A implementação deve continuar respeitando determinismo, imutabilidade, visibilidade de conteúdo e o ciclo de turnos publicado pela engine.

Esta análise foi feita no código JavaScript/CSS e GDScript. As durações abaixo foram lidas dos arquivos; não representam medições de fluidez em execução. A aprovação visual deve acontecer no laboratório antes de habilitar o acabamento no combate.

## 2. O que a referência faz

O UNO Clash é uma aplicação estática, sem framework de animação: JavaScript, CSS e Web Animations API. Sua sensação de dinamismo vem da composição de poucos recursos, não de uma biblioteca de física.

| Recurso | Implementação encontrada | O que aproveitar |
|---|---|---|
| Voo de carta | `app.js`, `animateCardFlight`, linha 247: três poses, ponto intermediário elevado 48 px, escala de 0,95 → 1,12 → 1,02 e rotação de −8° → 6° → 0° | Variar posição, rotação e escala no mesmo gesto, com aceleração inicial e chegada suave |
| Ritmo do voo | Padrão de 360 ms; uso por clique com 340 ms e após arraste com 185 ms | O arraste já percorreu parte do caminho: a continuação não deve reiniciar o gesto |
| Curva temporal | `cubic-bezier(0.22, 1, 0.36, 1)` | Começar com decisão e desacelerar na chegada, sem velocidade constante |
| Mão em leque | `app.js`, renderização da mão, aproximadamente linhas 620–667: arco, rotação e sobreposição dependem da quantidade | Movimento coerente com a pose final da mão, inclusive com duas cartas |
| Entrada escalonada | Atraso de 25 ms por índice e `cardDealIn` de 320 ms | Diferenciar as cartas de uma compra múltipla sem transformar a compra numa sequência longa |
| Hover | `styles.css`, aproximadamente linhas 1242–1290: elevação, escala de 1,11, rotação para zero e aumento de profundidade | Evidenciar imediatamente a carta sob o ponteiro |
| Arraste | `app.js`, `attachCardDragAndClick`, aproximadamente linhas 737–862: limiar de 7 px, preservação do ponto de pega e proxy visual | A carta não salta para centralizar no cursor quando o arraste começa |
| Resposta de alvo | A escala do proxy aumenta sobre uma área válida; o destino também recebe realce | Mostrar onde soltar sem exigir tentativa e erro |
| Retorno inválido | Retorno de 200–210 ms com desaceleração | Soltar fora de um alvo deve produzir uma recuperação visual clara |
| Acomodação | `styles.css`, `popLand`, aproximadamente linha 953: escala 1,26 → 0,96 → 1 e rotação −6° → 2° → 0° em 380 ms | Uma acomodação pequena ajuda a transmitir contato e peso |
| Painéis | `modalEnter` de 280 ms, opacidade e deslocamento/escala | Entrada discreta sem deslocar o restante da interface |

Os três quadros do voo são poses interpoladas. A curva CSS acima controla o tempo; ela não transforma, por si só, a trajetória espacial em uma Bézier contínua nem implementa uma mola física.

### Limitações que não devemos copiar

1. A referência limpa e recria toda a mão a cada renderização. Isso pode repetir a animação de entrada e perder continuidade de identidade. A Godot deve manter as instâncias existentes.
2. O hover altera margens, podendo reorganizar a mão. Na Godot, hover deve atuar na transformação visual e preservar a geometria lógica dos slots.
3. A inclinação do arraste usa deslocamento por evento de ponteiro, sem normalização pelo tempo. O efeito varia conforme a frequência dos eventos; usar velocidade em pixels lógicos por segundo, limitada e filtrada.
4. O retorno do proxy termina com rotação zero, mesmo que a carta pertença a um leque. Retornar à pose completa atual do slot.
5. `handlePlayCard` inicia o voo em paralelo ao POST, antes da aceitação. Isso é responsivo, mas a referência não oferece uma reconciliação visual completa para rejeição ou resposta perdida. Não antecipar descarte, dano ou pagamento dessa maneira.
6. O fim de turno percorre vários frames, mas conserva apenas um identificador de ação da IA para parte da apresentação. A Godot deve continuar apresentando todos os fatos em ordem causal.
7. Há identificação de poderes por nomes específicos de UNO. A Godot deve consumir os fatos e as pistas de apresentação genéricas já existentes.
8. Não foi encontrada uma política de movimento reduzido no CSS examinado. A Godot já tem essa política e deve preservá-la.
9. Brilhos intensos, pulsos contínuos e cores de UNO não pertencem à direção de arte atual. Aproveitar movimento e legibilidade, sem importar esses acabamentos.

## 3. Situação atual da Godot

Não recomendo substituir a arquitetura atual pela arquitetura do exemplo. A Godot já resolve problemas mais complexos: replay, cancelamento, cartas ocultas, eventos configuráveis, duração ajustável e transferência de uma mesma carta entre vários passos.

| Área | Estado encontrado | Lacuna ou oportunidade |
|---|---|---|
| Política central | `scripts/presentation/motion_policy.gd` e `data/motion_manifest.json` capturam duração, velocidade, acessibilidade e curvas | Curvas disponíveis são famílias de Tween; não há equivalência exata com a Bézier CSS da referência |
| Perfil do laboratório | `scripts/presentation/card_animation_profile.gd` guarda overrides locais e divide o ciclo entre suas partes | Todo novo segmento deve participar do orçamento existente, sem aumentar silenciosamente a duração total |
| Compra em curva | `scripts/ui/card_motion_pose.gd` e `card_zone_motion.gd` já usam trajetória cúbica e a pose final do leque | Refinar ritmo, rotação e acomodação; não reintroduzir compra até o centro do container |
| Identidade visual | `scripts/ui/card_transit_registry.gd` mantém um visual por instância e pode emprestar a própria carta da mão | Estender a continuidade ao gesto de arraste, sem criar uma segunda autoridade de movimento |
| Uso e retenção | `scripts/ui/card_use_motion.gd` usa interpolação espacial linear para o estágio e retorno; o retorno trata centro e escala, sem restaurar explicitamente toda a pose | Unificar captura/aplicação da pose e dar continuidade à curva e à orientação final |
| Arraste | `card_hand_view.gd`, `_drag_visual`, cria uma cópia e a centraliza no ponteiro; `_clear_ghost` a remove imediatamente | Preservar ponto de pega, inclinação leve, retorno animado e passagem contínua para o executor de uso |
| Hover e reorganização | `card_hand_view.gd` possui tweens separados e cancela/recria movimentos ao mudar o destino | Reorientar o movimento sem saltos ou reinício brusco de velocidade; evitar conflito entre hover, slot e trânsito |
| Antes da resposta | `combat_screen.gd`, `_play_candidate`, chama `prepare_use(id, false)`: congela a face sem deslocamento antecipado | Dar uma confirmação local reversível e curta, sem simular aceitação |
| Após aceitação | `combat_screen.gd`, `_on_receipt_received`, já começa a saída antes de terminar toda a sincronização; a apresentação posterior reaproveita esse movimento | Preservar essa otimização e eliminar descontinuidades de pose/tempo na passagem para o envelope completo |
| Ordenação | `presentation_planner.gd` e `ui/controllers/presentation_director.gd` usam beats e handles | Manter ordem causal e conclusão por trabalho real; não adicionar timers independentes de espera |
| Efeitos | `effect_motion.gd` distingue animações concretas e cosméticos que não precisam bloquear | Sincronizar contato, som, barra e reação; evitar que texto flutuante prolongue o bloqueio |

Há testes existentes para política, paridade laboratório/combate, mão, transferências, ciclo de turno, limites e responsividade. A implementação deve ampliá-los, não criar uma suíte paralela desconectada.

### Três tempos diferentes

- **Resposta local:** confirmação de que o clique ou arraste foi recebido. Pode acontecer no próximo frame, sem consultar a engine.
- **Aceitação e sincronização:** tempo do comando REST e das leituras necessárias para publicar o estado consistente. Melhorar animação não reduz automaticamente esse tempo.
- **Apresentação:** tempo ocupado pelo movimento e pelos efeitos depois de autorizados. Pode sobrepor trabalho de sincronização seguro, mas deve respeitar causalidade e preferências de reprodução.

Medir esses tempos separadamente evita atribuir à animação uma demora que pertence ao servidor ou compensá-la com efeitos artificiais.

## 4. Decisões de arquitetura

### 4.1. A engine continua sendo a autoridade

Nenhuma alteração no cálculo de combate, RNG, sequência de comandos, custos, zonas ou snapshots é necessária para este plano. As mudanças ficam na apresentação e no tratamento do input da Godot.

Antes da aceitação, permitir apenas feedback reversível: leve elevação/compressão, estado visual de envio e manutenção da carta na mão ou no ponto de soltura. Não revelar novas cartas, escolher destino final, reduzir recursos, mostrar dano ou emitir um impacto de sucesso.

O recibo aceito autoriza a saída neutra já existente. O envelope sincronizado decide os efeitos e o destino. Para cartas cujo fluxo publicado pede queima, conservar o fluxo próprio, sem inserir um voo adicional.

### 4.2. Um proprietário da transformação

Usar o registro de trânsito existente para coordenar a passagem entre mão, arraste, preparação, voo e transferência. Não criar outro gerenciador global de animações.

Cada carta deve ter um único escritor final de posição, rotação e escala por frame. O slot fornece a pose-base; hover fornece um deslocamento de ênfase; um compositor aplica a pose resultante. Durante trânsito, o executor assume essa escrita e a mão reserva o slot sem competir com ele.

Representar a pose com centro global, rotação global, escala vetorial, tamanho e pivô. Usar `card_motion_pose.gd` como contrato compartilhado. Passar a pose atual entre etapas sem reconstruí-la com `global_position + size / 2`, que não descreve corretamente todos os casos com escala e rotação.

### 4.3. Movimento amostrado em progresso normalizado

Separar trajetória espacial, orientação/escala e curva temporal. Os executores continuam usando handles e o ciclo configurado; uma função compartilhada recebe progresso de 0 a 1 e produz a pose.

Adicionar poucos presets nomeados: saída rápida com desaceleração, retorno suave e acomodação discreta. Para reproduzir a Bézier do UNO, implementar/amostrar uma curva temporal validada; não tratar os parâmetros CSS como pontos da trajetória nem chamar o Tween cúbico padrão de equivalente exato.

Não é necessário introduzir física de corpos ou um sistema de molas para todos os voos. Um filtro dependente de delta para a inclinação do arraste e curvas de duração limitada são suficientes para o primeiro marco.

### 4.4. Duração é um contrato

Preservar o perfil configurado pelo usuário: ciclos de 1 segundo e queima central de 0,5 segundo onde atualmente configurados. A saída após arraste pode consumir apenas a parte ainda não percorrida, mas o significado dessa duração precisa ser explícito no laboratório: ciclo completo versus continuação do gesto.

Antecipação, voo e acomodação devem caber no ciclo, por exemplo 10% / 75% / 15%, sem acrescentar três durações completas. A preparação enquanto aguarda a rede não pode repetir ou prolongar o ciclo autorizado.

Não mudar os padrões para os 185–380 ms da referência neste trabalho. Um perfil opcional mais rápido pode ser avaliado depois da validação. Duração zero, modo instantâneo, movimento reduzido e ausência de animação visível continuam concluindo sem espera artificial.

### 4.5. Estados de interação explícitos

Fluxo visual sugerido:

`repouso → hover → pressionada → arraste → envio pendente → saída aceita → efeitos/transferência → repouso`

Alternativas: `arraste → retorno ao slot`, `envio → rejeição → retorno` e `qualquer estado → cancelamento de apresentação`.

Esses estados descrevem somente a interface. Não duplicam estados ou decisões de combate. Pausa, navegação e troca de geração da apresentação devem encerrar ou suspender o trabalho pelo mecanismo existente, sem deixar cartas invisíveis ou handles ativos.

Timeout com resultado desconhecido não equivale a rejeição: manter a reconciliação do comando existente, sem reenviar um novo comando ou apresentar sucesso duas vezes.

## 5. Direção de movimento por ação

| Ação | Comportamento proposto |
|---|---|
| Hover | Elevação rápida, pequena ampliação e profundidade maior; manter o slot e a superfície de seleção coerentes. Som apenas na entrada física do ponteiro, sem repetição por tween ou refresh |
| Troca rápida de hover | Partir da pose atual e redirecionar a ênfase; não retornar primeiro ao repouso nem disparar entrada de carta |
| Arraste | Preservar o ponto de pega; posição acompanha o ponteiro sem atraso perceptível. Inclinação limitada e suavizada pelo tempo, sem oscilar continuamente quando parado |
| Alvo válido | Realçar alvo e carta de forma discreta. Usar apenas candidatos existentes da engine; a animação não decide legalidade |
| Soltura inválida | Retorno curto em arco até a pose completa atual do slot, inclusive em mão com duas cartas, resize ou reorganização |
| Uso por clique | Confirmação local reversível; após aceitação, arco moderado para o estágio, acomodação breve e continuidade para o fluxo publicado |
| Uso por arraste | Continuar do ponto real de soltura, conservando orientação/escala; não reaparecer na mão e não reproduzir uma saída completa de novo |
| Compra | Partir da pilha correta e terminar exatamente na pose reservada do leque. Cartas existentes apenas se reorganizam; não recebem animação de compra |
| Compra múltipla | Pequeno escalonamento opcional, inspirado nos 25 ms da referência, limitado por lote e incluído no orçamento total; não multiplicar a duração por quantidade |
| Carta retida | Retornar à orientação, escala e centro do slot corrente; evitar encaixe abrupto no último frame |
| Descarte/exílio | Consumir os fatos das zonas e as pistas configuradas. Queima central conserva 0,5 s quando configurada; sem voo ou pausa redundante antes dela |
| Impacto | Reação pequena da peça/barra, som e alteração visual no mesmo marco de contato. Número flutuante pode terminar depois sem bloquear o próximo beat |
| Fim de turno | Animar apenas saídas, entradas e efeitos publicados para o ator elegível; não fazer toda a mão sair e voltar por uma atualização de tela |
| Painéis e pilhas | Entrada/saída discreta por transformação e opacidade, sem redimensionar o combate. Esta extensão vem depois da aprovação das cartas |

Os valores de lift, inclinação e escala devem ser configuráveis em unidades lógicas/proporções da carta, sem copiar pixels CSS diretamente. A implementação inicial não altera o espaçamento ou a disposição dos elementos existentes.

## 6. Etapas sequenciais de implementação

### Etapa 1 — Baseline e cenários comparáveis no laboratório

**Trabalho:** registrar os marcos de input, envio, recibo, envelope, primeiro movimento, contato e conclusão; reaproveitar as métricas existentes. Criar cenários reproduzíveis de hover, arraste, uso, rejeição visual, retorno, compra e retenção, com 2, 5 e 10 cartas.

**Arquivos:** laboratório e testes de `animation_responsiveness`, `card_motion_polish`, `card_animation_parity`; ampliar os pontos de medição já usados por `combat_screen.gd`.

**Saída:** capturas e tempos antes da mudança, com perfil, resolução e velocidade registrados. Latência simulada deve atuar no transporte de teste, sem inserir sleeps na produção.

**Commit:** `test(motion): baseline card interaction latency and shared lab scenarios`.

### Etapa 2 — Pose completa e posse de transformação

**Trabalho:** consolidar as operações de leitura/aplicação da pose; corrigir o uso e retorno retido para usar esse contrato; coordenar mão, proxy e trânsito pelo registro existente. Garantir transferência de propriedade sem um frame duplicado ou invisível.

**Arquivos:** `card_motion_pose.gd`, `card_transit_registry.gd`, `card_use_motion.gd`, `card_hand_view.gd`.

**Saída:** ausência de salto na troca mão/overlay/slot, inclusive com carta inclinada, UI escalada e pais transformados. Os resultados visuais podem continuar iguais nesta etapa.

**Commit:** `refactor(motion): share complete card poses and transform ownership`.

### Etapa 3 — Curvas e orçamento de ciclo compartilhados

**Trabalho:** acrescentar presets de curva e poses normalizadas; usar o orçamento de `card_choreography.gd` para distribuir saída, trajetória e acomodação. Expor os ajustes no laboratório sem criar um executor de demonstração separado.

**Arquivos:** `motion_policy.gd`, `motion_manifest.json`, `card_animation_profile.gd`, `card_choreography.gd` e auxiliares pequenos de amostragem, se necessários.

**Saída:** ciclos configurados mantêm a duração total; curva temporal tem extremos exatos e não produz NaN ou overshoot de posição acidental. Perfil antigo sem novas chaves permanece válido.

**Commit:** `feat(motion): add shared timing curves and bounded settling profiles`.

### Etapa 4 — Hover e reorganização contínuos

**Trabalho:** ajustar reorientação dos tweens, composição de ênfase e profundidade; manter as cartas persistentes e a superfície central de hit testing. Nenhum refresh deve refazer a entrada ou disparar som.

**Saída:** passar rapidamente por cartas sobrepostas não seleciona a vizinha, não muda layout, não acumula outline e não deixa cartas elevadas. Preservar a regra atual de desseleção por hover no mouse e navegação por controle/teclado.

**Commit:** `feat(hand): smooth hover retargeting without layout or selection shifts`.

### Etapa 5 — Arraste, ponto de pega e retorno

**Trabalho:** capturar offset e pose no início; adicionar inclinação leve baseada em velocidade com delta; realce de alvo; retorno animado até o destino completo. Substituir a destruição imediata do ghost por uma conclusão controlada quando houver retorno visível.

**Saída:** soltar/cancelar com Escape ou botão direito sempre libera o input; o primeiro frame do arraste coincide com a carta original. O retorno usa a posição atual do leque e não uma posição antiga.

**Commit:** `feat(hand): preserve drag grip and animate safe returns to fan slots`.

### Etapa 6 — Confirmação local e passagem para a aceitação REST

**Trabalho:** introduzir feedback pendente reversível no fluxo existente; passar a pose de soltura/seleção ao executor. Reaproveitar a saída antecipada por recibo de `combat_screen.gd`, correlacionando comando, sequência e geração. Não substituir esse mecanismo por um `Promise.all` otimista equivalente ao UNO.

**Saída:** comando rápido, lento, rejeitado ou supersedido não produz salto, duplicação de som, efeito antecipado ou desbloqueio incorreto. Resposta perdida utiliza a reconciliação existente. A carta não desaparece definitivamente antes da publicação autorizada.

**Commit:** `feat(combat-ui): connect reversible input feedback to accepted card motion`.

### Etapa 7 — Uso, acomodação e contato

**Trabalho:** trocar o trecho espacial linear por trajetória controlada; interpolar orientação/escala; aplicar acomodação pequena dentro do ciclo e passagem contínua ao próximo trecho. Sincronizar impacto e áudio ao marco do handle.

**Saída:** clique e arraste usam o mesmo executor com poses iniciais diferentes. Laboratório e combate exibem a mesma trajetória sob as mesmas entradas. Efeitos sem alvo visual não criam tempo morto; dano só aparece após autorização.

**Commit:** `feat(motion): polish accepted card arcs and synchronized impact feedback`.

### Etapa 8 — Compra e transferências em lote

**Trabalho:** refinar a curva já existente para a pose final do leque; adicionar escalonamento limitado e acomodação sem duplicar animações de entrada; coordenar reorganização dos slots com as chegadas. Manter anonimato de cartas ocultas e serialização de transferências da mesma instância.

**Saída:** 1, 2, 5 e 10 chegadas terminam nos slots corretos; duração do lote respeita o perfil. Descarte, retenção, destruição e queima seguem o fluxo real, sem simular ações de compra ou descarte que o setting não possui.

**Commit:** `feat(motion): refine curved hand arrivals and bounded batch timing`.

### Etapa 9 — Interrupções, replay e acabamento secundário

**Trabalho:** validar pausa no meio de cada trecho, skip, modo manual, replay, navegação e cancelamento; ajustar discretamente abertura de pilhas/painéis usando as mesmas políticas de acessibilidade. Não introduzir realces pulsantes permanentes ou tremor global.

**Saída:** todos os handles terminam ou cancelam corretamente; nenhum ghost sobra; nenhuma carta fica inutilizável. Instantâneo/movimento reduzido não esperam a duração de um efeito desabilitado. Replay conserva a ordem causal completa.

**Commit:** `fix(motion): settle interrupted gestures and preserve replay accessibility`.

### Etapa 10 — Certificação visual e integração final

**Trabalho:** executar a matriz abaixo, comparar baseline, validar com API real em saves isolados e documentar os presets aprovados. Ampliar testes existentes de mão, ciclo, paridade, responsividade e apresentação. Habilitar o acabamento validado no combate pelo mesmo componente do laboratório.

**Saída:** relatório com vídeos/capturas, medições e limitações observadas. Aprovação visual no laboratório precede a adoção final; nenhuma mudança de regra é necessária.

**Commit:** `test(motion): certify polished card cycles across lab combat and replay`.

## 7. Matriz de validação e critérios de aceite

| Dimensão | Casos obrigatórios |
|---|---|
| Quantidade | Mão vazia, 1, 2, 5, 10 e mão excedente/paginada |
| Input | Mouse, teclado e controle; clique, hover rápido, arraste e cancelamento |
| Estado | Carta válida/inválida, retorno, retenção, descarte, queima, compra múltipla e transferências consecutivas |
| Rede | Resposta rápida, 300 ms, 1,5 s; rejeição, conflito e perda de resposta; mocks para falhas e API real para o caminho válido |
| Reprodução | Ao vivo, replay, passo manual, velocidade normal/rápida/instantânea e movimento reduzido |
| Tela | 1280×720, 1920×1080 e 2560×1080; escala de interface e resize durante arraste/voo |
| Interrupção | Pausa, abrir menu, voltar ao mapa, sair da run e trocar geração durante cada fase |
| Renderização | Medições em 30/60/120 FPS quando disponíveis; avaliar movimento por tempo, sem dependência da quantidade de eventos do ponteiro |

Critérios:

1. Primeiro feedback local no próximo frame disponível; medir separadamente o primeiro voo autorizado e o tempo REST. Não prometer que a animação reduz toda ação de servidor para menos de um segundo.
2. Passagem entre executores sem salto perceptível: testar centro com tolerância de 1 pixel lógico, rotação de 0,5° e escala de 0,01, com tolerâncias de captura documentadas.
3. Ciclo ativo respeita a duração configurada com tolerância de até dois frames na frequência testada. Manter as diferenças explícitas entre ciclo ativo e espera da rede.
4. Nenhum timer de espera para beats sem trabalho visual; cosméticos não essenciais não bloqueiam a próxima ação.
5. Uma carta visível por instância em trânsito; zero overlays/handles órfãos ao terminar ou cancelar.
6. Sem reconfiguração completa de `CardView`, leitura de JSON ou reconstrução de toda a mão por frame de animação.
7. Um som por entrada física de hover; passagem entre etapas não repete o som de uso.
8. Final do ciclo coincide com o snapshot autoritativo; a comparação de comandos, resultados e fingerprints deve permanecer igual com animações ligadas, desligadas e em replay, nos mesmos pontos de sequência.
9. Como meta de desempenho visual, p95 do tempo de frame até 16,7 ms no equipamento de referência a 60 FPS; registrar hardware, resolução e custo incremental dos efeitos, sem tratar isso como garantia para qualquer máquina.
10. Sem mudanças no layout, no hit testing esperado, nos custos, na ordem de resolução ou na visibilidade de cartas.

## 8. Primeiro marco recomendado

Concluir as etapas 1–5 no laboratório: comparação instrumentada, pose unificada, curvas, hover e arraste/retorno. Isso permite validar a sensação de manipular cartas antes de integrar todos os casos de rede e resolução.

Depois, executar as etapas 6–10 sobre os mesmos componentes. O ganho esperado é uma interação mais imediata e contínua, principalmente ao arrastar, retornar e encadear movimentos. A extensão desse ganho precisa ser confirmada pela medição e pelo teste visual; o código da referência, sozinho, não comprova ausência de engasgos.
