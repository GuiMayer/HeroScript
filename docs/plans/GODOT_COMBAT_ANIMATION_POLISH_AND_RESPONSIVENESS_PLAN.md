# Combate: animações básicas, acabamento e resposta imediata

Data: 2026-10-13. Estado: oito etapas implementadas.
Base inspecionada: `5925db3`.

Implementação e evidências: [relatório de calibração](GODOT_COMBAT_ANIMATION_POLISH_VALIDATION.md).

## 1. Objetivo

Refinar o sistema de apresentação existente para que usar, comprar, descartar e
reorganizar cartas pareça contínuo e responsivo. Um registro que não produz uma
animação executável deve acrescentar **zero espera intencional** à ação.

Preservar envelope contextual, planner causal, Playback, director, handles,
permissões, snapshots imutáveis e a autoridade da engine. Este trabalho evolui
esses componentes; não cria uma segunda fila de animações.

## 2. Diagnóstico do código atual

| Evidência | Consequência | Mudança necessária |
| --- | --- | --- |
| `combat_screen.gd::_execute_beat` e `historical_beat_view.gd::_execute` criam `tween_interval(impact)` quando `hasVisibleFacts` é verdadeiro; default 0,12 s | Aplicação registrada pode consumir tempo mesmo sem alvo visual, delta visível ou efeito executado | Bloqueio deve vir das operações realmente iniciadas |
| `presentation_planner.gd` define `hasVisibleFacts` pela existência de aplicações/steps | Ter fatos não garante animação disponível | Separar fato canônico de operação visual resolvida |
| `presentation_director.gd::_process` despacha no máximo um beat por frame; executor ausente só conclui no próximo tick | Beats instantâneos ainda acumulam frames de espera | Consumir imediatamente a sequência instantânea até encontrar trabalho assíncrono real |
| `Playback.next_is_passive()` depende de strings de `transitionType` | O modo manual pode pedir confirmação para eventos sem apresentação | Decidir pela capacidade visual resolvida, não pelo nome do evento |
| `CardView.fly_to` combina trajeto reto, escala até 0,45 e fade durante todo o voo | A carta perde legibilidade e desaparece antes de comunicar claramente seu uso | Saída curta, área de uso e chegada/destino coordenados |
| `card_zone_motion.gd` usa praticamente o mesmo ghost a 30% e fade para todos os transfers | Compra, descarte, criação e destruição ficam pouco distinguíveis | Coreografias orientadas aos papéis visuais das zonas e às operações publicadas |
| `_zone_anchor` procura a carta por ID antes de verificar o address solicitado | Uma carta presente pode produzir a mesma âncora para origem e destino | Resolver por address, instante observado e papel da âncora |
| Executor de zonas dispara todos os ghosts juntos; a mão tem seu próprio fade e relayout | Falta coordenação entre trânsito, chegada e ocupação do leque | Um dono de movimento por instância/propriedade e slots de chegada estáveis |
| Uso possui beat `card_departure`, mas o fluxo de zonas também pode descrever a mesma instância | Há risco de duas saídas visuais para uma só carta | Correlacionar as duas descrições em uma única trajetória |
| `handle.hit()`/áudio são disparados antes de resolver todas as operações visuais do beat | Impacto pode não coincidir com a chegada de uma transferência | Emitir milestones no executor apropriado |
| `_refresh` aguarda run, projeções, ações legais e avaliação da mão antes do envelope | O clique pode passar bastante tempo sem movimento significativo | Feedback local imediato e redução medida do caminho de leitura |

Os pontos acima são de inspeção estática. O risco de duplicação de trajetórias e
a disputa de apresentação entre mão/ghost precisam de reprodução dedicada antes
de serem tratados como falhas presentes em todos os fluxos.

O relatório anterior mediu 852–1.014 ms até sincronizar, seguidos de 229–239 ms de
apresentação, na segunda execução de três comandos em modo rápido. São amostras
anteriores, não uma nova medição desta análise. A percepção de lag envolve tanto
a espera da API quanto a cadência visual. Não basta reduzir uma constante global.

## 3. Regras de execução

### 3.1 Sem animação, sem espera

Cada beat mantém seus fatos, mas a preparação visual retorna uma das situações:

- **Instantâneo:** atualiza projeção/log/contador e termina na mesma passagem;
- **Cosmético:** dispara áudio, texto flutuante ou outra cauda que não bloqueia;
- **Bloqueante:** há uma animação visível necessária à leitura, com handle real.

Delta zero, alvo/âncora ausente, cue desabilitado, asset sem fallback animado,
trajeto sem deslocamento e modo instantâneo não ganham duração mínima artificial.
Som e texto estático não obrigam a esperar. Um fallback neutro pode atualizar a
tela imediatamente; só bloqueia se ele próprio executar movimento relevante.

O director usa um pump iterativo, protegido por geração e contra reentrância.
Ao concluir sincronamente, segue para o próximo beat na mesma passagem. Para no
primeiro handle bloqueante, pause, espera manual significativa ou fim da fila.
Conclusões assíncronas retomam o pump sem um intervalo adicional obrigatório.

Filas excepcionalmente grandes podem ceder execução ao ultrapassar um orçamento
de CPU por passagem para preservar o frame. Isso é proteção contra travamento,
não um timer por evento. Instrumentar essas cessões. Zero espera significa zero
delay/tween/frame imposto por evento sem animação; processamento possui custo real.

No modo manual, a confirmação avança uma unidade visual significativa. Eventos
instantâneos ao redor dela são drenados automaticamente, preservando seus logs.
O watchdog protege executores bloqueantes defeituosos e não cria a cadência normal.

### 3.2 Sincronização e input

O clique recebe feedback no próximo frame renderizado: realce/pequeno movimento
de preparação da carta e indicação discreta de envio. Esse estado local é
reversível; não mostra dano, pagamento, compra ou sucesso antes da confirmação.

Depois da sincronização completa, o mesmo objeto visual continua a coreografia.
Rejeição devolve a carta à posição anterior. Falha de leitura após aceitação usa
o fluxo de recuperação existente, sem reenviar a escrita confirmada.

Comandos continuam serializados. O próximo comando é liberado quando a sessão
está coerente e a parte visual indispensável terminou. Partículas, fades e texto
residual podem terminar depois, desde que não disputem interação ou propriedade
com a ação seguinte. Não adicionar uma fila de comandos baseada em ações antigas.

### 3.3 Uma trajetória por instância

Introduzir um pequeno registro de trânsito pertencente à apresentação ativa:
instance ID, address de origem/destino, slot/âncora, modelo autorizado, transform
inicial/final e handle. Seu ciclo de vida termina com a apresentação.

A mão oferece reserva de slot, posição final, suspensão de sua animação local
para a instância em trânsito e entrega de volta ao componente compartilhado.
Layout, hover, voo e chegada não escrevem a mesma propriedade simultaneamente.

O registro coordena `card_departure` com os passos canônicos da mesma carta, sem
eliminar movimentos legítimos posteriores. Uma instância pode realizar vários
transfers causais, mas nunca duas representações simultâneas da mesma passagem.
Resize recalcula âncoras a partir da posição atual, sem reiniciar a animação.

## 4. Direção visual e ritmo inicial

Movimentos curtos, curva limpa e chegada perceptível combinam com o tema marfim.
Manter texto legível, escala estável dos vizinhos e pouca oscilação. Estes tempos
são pontos de partida para playtest, não padrões universais nem limites da API.

| Ação | Coreografia proposta | Orçamento visual inicial em normal |
| --- | --- | --- |
| Hover/foco | elevação pequena, endireitamento e realce; sem mexer na fonte | 70–100 ms, sem bloqueio de comandos |
| Preparação do uso | deslocamento curto na direção da área de uso; pose estável enquanto envia | feedback até o próximo frame; transição 60–90 ms |
| Uso confirmado | carta deixa o slot e passa pela área de uso, preservando sua face; cue atinge o alvo quando aplicável | 150–280 ms até liberar a parte essencial de uma ação simples |
| Compra de uma carta | sai da zona de origem, cresce até seu slot no leque e revela a face quando autorizada | 160–220 ms |
| Compra em lote | voos sobrepostos, chegadas levemente espaçadas, posição final reservada | espaçamento 20–35 ms; 5 cartas em aproximadamente 250–360 ms |
| Descarte | carta segue uma curva curta até a âncora de destino; fade apenas próximo da chegada | 130–180 ms; lote sobreposto |
| Remoção/destruição | retração/fade breve no local ou trajetória até destino explícito | 100–160 ms |
| Carta retida | permanece no slot ou retorna suavemente da área de uso, conforme os fatos | 80–120 ms de retorno, sem falsa saída para pilha |
| Criação em zona visível | aparece no slot de destino com entrada curta | 120–180 ms |
| Reordenação | slots afetados se reposicionam; cartas restantes mantêm identidade e face | 90–130 ms |
| Impacto | reação local do alvo + alteração de barra/contador e som no milestone | 60–110 ms de leitura essencial |
| Múltiplos acertos | impactos separados com caudas sobrepostas, conservando ordem | espaçamento inicial 50–80 ms entre impactos visíveis |

O uso não precisa lançar a carta inteira contra o inimigo. A carta comunica a
ação na área de uso; o cue comunica o alvo. Isso atende ataque, cura própria,
ação sem alvo e área sem forçar a mesma metáfora visual para tudo.

Compra não deve terminar com um verso que apenas desaparece no centro. A chegada
faz a transferência de responsabilidade ao CardView do slot. A face só é revelada
com os dados autorizados e completos, sem uma descrição provisória seguida de outra.

Os perfis visuais atribuem papéis como leque, pilha, coleção ou área de uso a
addresses apresentados. A engine continua desconhecendo esses papéis. Ordem
oculta não é deduzida; transfers ocultos usam versos/contagens permitidas.

## 5. Etapas de implementação

### Etapa 1 — Baseline de tempo morto e fixtures de movimento

Medir clique → primeiro feedback, envio → recibo, recibo → sincronização,
sincronização → cue, bloqueio visual e maior frame. Registrar motivo de cada
espera e quantidade de operações instantâneas/cosméticas/bloqueantes.

Adicionar ao laboratório compartilhado cenários reproduzíveis: uso simples,
uso retido, compra 1/5/10, compra seguida de descarte, criação/destruição,
reordenação, alvo ausente, efeito sem mudança e fila de lifecycle sem cues.
As fixtures reproduzem fatos publicados e não implementam regras.

Aceitação: identificar separadamente atraso de rede, espera imposta pela UI e
stutter de construção/layout; comparar com a versão anterior no mesmo ambiente.

### Etapa 2 — Execução instantânea e conclusão por operação real

Substituir o avanço por tick pelo pump do director. Resolver capacidade visual
antes de decidir bloqueio; retirar `tween_interval(impact)` genérico do combate e
do replay. Não promover `hasVisibleFacts` a prova de animação executada.

Preservar fatos e snapshots mesmo quando o beat é instantâneo. Enviar uma
notificação consolidada de HUD por drenagem, evitando um relayout para cada fato
que não aparecerá entre dois frames. Manter conclusão única e geração contextual.

Aceitação: uma fila comum de no-ops termina na mesma passagem, sem timer, sem
um frame por item e sem exigir cliques no modo manual. Adicionar 100 no-ops ao
redor de um impacto não adiciona cadência à ação. Fila patológica respeita o
orçamento de CPU com cessões observáveis e sem recursão crescente.

### Etapa 3 — Contrato de trânsito e âncoras confiáveis

Criar a coordenação transitória descrita na seção 3.3 e as operações mínimas da
mão. Resolver origem pelo snapshot anterior e destino pelo snapshot autorizado
apropriado, considerando address antes de instance ID. Reservar slots de chegada
sem apresentar antecipadamente cartas adquiridas. Suprimir o fade local da mão
quando a chegada já pertence a um transfer.

Usar posições/layout e IDs publicados para compor a apresentação; não executar
fluxos de zonas na Godot. Se os dados compactos não descrevem uma passagem,
reconciliar com fallback imediato em vez de inventar uma ordem intermediária.

Aceitação: origem e destino distintos quando publicados; uma representação por
passagem; cancelar/skip/destruir a view deixa slots, foco e nós transitórios limpos.

### Etapa 4 — Polir uso da carta e fechamento do leque

Adicionar preparação local imediata, confirmação, área de uso e retorno por
rejeição. Continuar a partir do transform corrente, preservando face, pivô e
rotação. Integrar destino canônico da carta com sua saída para evitar dois voos.

O leque fecha o espaço durante a saída da carta. Acomodação das vizinhas usa
posição/rotação, sem reconfigurar texto ou selecionar a primeira carta. Cura,
ações sem alvo e múltiplos alvos usam âncoras próprias de cue, configuráveis.

Aceitação: clique responde visualmente mesmo sob leitura atrasada; sucesso nunca
é antecipado; uma carta consumida sai uma vez; retida retorna corretamente;
rejeição e falha pós-aceitação mantêm os fluxos distintos existentes.

### Etapa 5 — Compra, descarte e demais operações de zonas

Implementar os perfis da tabela sobre `CardZoneMotion`, com interpolação de
trajeto, escala, rotação e opacity. Compra entra no slot reservado; descarte chega
à zona destino; criação/destruição/reordenação seguem sua operação factual.

Lotes usam sobreposição e stagger limitado. O tamanho do lote não multiplica a
duração integral de cada voo. Acima do limite visual, usar representação compacta
com contagem autorizada e acomodação final; conservar detalhes no log. Nenhum
stagger é cobrado de uma operação que foi resolvida como instantânea.

Aceitação: 1/5/10 cartas chegam sem duplicação, flicker ou troca de fonte. Nenhum
transfer oculto revela identidade/ordem. Papéis visuais funcionam com nomes de
zona arbitrários. Reset/skip durante o lote produz exatamente a projeção final.

### Etapa 6 — Impactos, áudio e caudas cosméticas

Trocar a pausa fixa pelo milestone efetivo de cada executor. Barra, reação local
e som acompanham o impacto; pagamento atualiza o contador sem uma pausa própria.
Status/ganhos podem produzir feedback cosmético sem bloquear uma ação seguinte.

Sobrepor fades, acomodação e números flutuantes quando não disputarem a mesma
propriedade nem inverterem causalidade. Multihit continua legível por impacto;
condensação mantém seu proc e continuações preservam as relações publicadas.
Sons de compra seguem as chegadas, respeitando os limites/cooldowns existentes.

Aceitação: som/impacto não chegam antes do movimento que os representa; ausência
de som/asset/alvo não cria espera; mode instantâneo conclui na mesma drenagem;
pause e skip alcançam também os resíduos cosméticos.

### Etapa 7 — Reduzir o custo do caminho de interação

Com as medições da etapa 1, atacar os custos demonstrados: evitar construção
repetida de CardView durante o voo, cachear o manifesto por revisão, reutilizar
modelos já montados e consolidar atualizações de layout/HUD. Pooling só entra se
o custo de alocação continuar relevante no profiler.

Revisar o caminho run → projeções → ações → avaliação. Paralelizar apenas consultas
independentes e validar a mesma revisão antes da publicação atômica. A avaliação
atual usa alvos das ações legais; essa dependência não pode ser removida apenas
para iniciar requests mais cedo. Aproveitar recibos/projeções já disponíveis quando
seus contratos forem suficientes e não repetir leituras idênticas.

Se o gargalo exigir uma projeção composta da API, documentar o contrato e adicioná-la
como leitura canônica atômica, mantendo autorização e versionamento. Essa mudança
na engine é condicional à evidência; as etapas visuais não precisam esperar por ela.

Aceitação: publicação mantém descrições estáveis, não há input sobre dados antigos
e nenhum comando aceito é reenviado. Comparação antes/depois discrimina API e UI;
melhora de feedback não é reportada como redução da latência real do servidor.

### Etapa 8 — Calibração no laboratório, combate e replay

Expor timings/curvas/stagger/limites nos perfis existentes, com fallback e captura
por apresentação. Compartilhar as correções do director/executores com o replay;
leitores sem âncoras ou dados de mão usam fallback imediato, não uma espera fictícia.

Validar normal, rápido, instantâneo, manual e movimento reduzido; inglês/português;
1280×720, 1920×1080 e 2560×1080. Cobrir pause, resize, skip, troca de setting e
encerramento do combate durante trânsito. Usar o mesmo conteúdo e fixtures para
comparar o ritmo antes/depois, registrando vídeo/capturas e medidas.

Aceitação: ações básicas têm movimento contínuo, nenhuma espera sem executor,
mesmo resultado autoritativo sob todos os presets e ausência de regressões no
texto das cartas, na seleção e na autorização de histórico.

## 6. Critérios de entrega e sequência de commits

O primeiro marco é a etapa 2: remover tempo morto já deve produzir melhora
perceptível com os efeitos atuais. O segundo marco reúne etapas 3–5: uso e compra
no laboratório com os mesmos componentes do combate. Etapas 6–8 fecham acabamento,
desempenho medido e integração.

Um commit por etapa, com testes proporcionais ao risco. Priorizar verificações
de resultado e lifecycle: não testar apenas o valor de uma constante de duração.
Não incluir mudanças de regras/balanceamento ou novos pacotes de arte/áudio.

Metas de apresentação: feedback local até o próximo frame renderizado; zero
espera intencional sem operação visual; ação simples e compra de cinco dentro
dos orçamentos iniciais da tabela após sincronização. O orçamento de sincronização
de 1 s continua sendo medido separadamente. Registrar p50/p95 e pior caso somente
com amostra e condições explicitadas; uma passagem isolada não certifica o alvo.

Referências locais: [arquitetura atual](GODOT_COMBAT_ANIMATION_ARCHITECTURE_AND_PLAN.md),
[validação anterior](GODOT_COMBAT_ANIMATION_VALIDATION_REPORT.md) e
[autoria da apresentação](../design/COMBAT_PRESENTATION_AUTHORING.md).
