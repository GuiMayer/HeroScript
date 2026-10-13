# Apresentação de combate: arquitetura e plano de implementação

Data: 2026-10-13. Estado: planejado, não implementado.

## 1. Objetivo

Transformar o playback de frames em uma apresentação de combate confiável,
legível e configurável, adequada a um deckbuilder com múltiplos acertos,
críticos por níveis, efeitos residuais, condensação e transformações de cartas.

A prioridade é corrigir coordenação e causalidade antes de acrescentar efeitos
decorativos. Uma ação deve comunicar intenção, aceitação, origem, impacto e
consequência sem depender da duração de uma consulta REST.

Escopo: combate, mão, transferências de cartas, resources, status/modifiers,
fontes de efeitos, áudio, pause, transições pós-combate e apresentação histórica.
Mapa e diálogo só entram na fronteira de navegação; seus sistemas não serão
reescritos. Não inclui novas regras, balanceamento, novas artes finais ou música.

## 2. Diagnóstico utilizado

A auditoria foi feita sobre a cópia de trabalho atual, incluindo a correção da
publicação atômica das avaliações das cartas. Foram executados 232 checks
existentes e probes isolados com recibos sintéticos. Não houve certificação
online: a API local estava indisponível.

Falhas reproduzidas:

1. O cursor consome um frame e toca seu impacto antes de a atualização da
   sessão terminar; o voo e o som de uso da carta começam depois.
2. `has_frames()` fica falso ao despachar o último frame, não ao concluir sua
   apresentação. O input pode ser liberado durante o voo da carta.
3. Uma carta consumida permanece visível ao lado da cópia que está voando;
   `still_present` consulta a projeção antiga.
4. O flash da arte dos personagens continua no pause, embora a interface pare.
5. Agrupar aplicações intercaladas de condensação muda a ordem visual:
   `A1, B, A2` torna-se `A1, A2, B`.
6. Pagar energia aciona um flash de impacto no personagem.
7. Dois impactos no mesmo alvo criam textos flutuantes na mesma posição.

Outras lacunas identificadas por inspeção:

- A saída do combate considera seu estado final, sem uma barreira de conclusão
  da apresentação; pode cortar frames de vitória/morte.
- Frames recebem uma cadência fixa, independentemente de seu significado.
- Barras, status e pilhas são atualizados pelo snapshot final, sem coordenação
  geral com o impacto e as transferências publicadas.
- Identidades, relações entre procs, passos de zonas e resultados aleatórios
  publicados são pouco aproveitados pelo feedback.
- Durações e preferências estão distribuídas por vários componentes.
- Replay da run e timeline não reutilizam uma apresentação completa do combate.
- Não há limite explícito de textos flutuantes ou medição da conclusão visual.

## 3. Invariantes obrigatórios

1. A engine resolve integralmente cada comando, imediatamente, e continua sendo
   a única autoridade sobre estado, regras, custos, alvos, efeitos e RNG.
2. A Godot consome fatos publicados. Não executa condições, fórmulas, buckets,
   críticos, dissipação, condensação ou regras de entrada/saída das pilhas.
3. Ritmo, curvas, cores, ícones, sons, movimento e disposição são apresentação.
   Trocar um preset visual não muda comandos, seed, hashes ou conteúdo de regras.
4. Pause, skip, replay e término de uma animação não enviam comandos de gameplay.
5. Cada efeito possui identidade de apresentação contextual a endpoint, setting,
   run, branch, combate, comando, sequência e geração de navegação.
6. Estado publicado é imutável. Interpolação visual nunca escreve em
   `GameSession`, `RunState`, recibos, journal ou frames.
7. Uma apresentação termina exatamente uma vez: concluída, pulada, cancelada ou
   encerrada com fallback. Callbacks obsoletos não navegam nem liberam outra sessão.
8. Existe uma autoridade de execução visual por sessão de apresentação. Não
   criar um driver antigo e um novo executando simultaneamente os mesmos eventos.
9. Recursos e zonas são genéricos. A aparência não presume que todo dano reduz
   health nem que uma zona chamada discard tem uma regra particular.
10. Preservar permissões, visibilidade de zonas e isolamento entre settings.
    Animações e replay não podem revelar cartas ocultas ou estados não autorizados.
11. Preservar o carregamento progressivo do replay e os limites de cache já
    implementados. Não carregar a run inteira para iniciar uma animação.

## 4. Arquitetura-alvo

### 4.1 Responsabilidades

| Elemento | Responsabilidade | Não possui autoridade sobre |
| --- | --- | --- |
| Engine/journal | fatos canônicos e estado resultante | duração, tween, som ou layout |
| Gateway/sessão | transporte, escopo, snapshots completos e falhas | construção da UI ou regras locais |
| Envelope de apresentação | recibo aceito e referências aos snapshots inicial/final | novo estado de gameplay |
| Planner de apresentação | fatos → beats visuais ordenados, agrupamento causal | recálculo dos efeitos |
| Playback/director | cursor, execução, conclusão, pause, cancelamento e skip | HTTP, persistência ou regras |
| Adaptador de combate | entrega do envelope, ligação entre intenção, sessão e view | desenho de efeitos ou cálculo |
| Views/componentes | executar cues visuais e responder conclusão | escolher resultados ou emitir comandos |
| Perfis/manifestos | durações, movimentos, ícones, sons e mapeamentos | alterar efeitos/custos da carta |
| Loader histórico | buscar um comando autorizado sob demanda | enviar comandos de jogo |

O `Playback` existente deve evoluir/reduzir suas responsabilidades em conjunto
com o director. O `CombatPresentationDriver` será absorvido pelo fluxo canônico,
não mantido como temporizador paralelo. Um plano de beats referencia fatos;
não precisa duplicar todos os snapshots e cálculos em cada cue.

### 4.2 Beats e conclusão

Um beat é uma unidade de leitura, não necessariamente um frame do journal.
Exemplos: pagamento, transferência da carta, impacto, proc residual, ganho de
resource, aplicação de status, ativação de relíquia, encerramento de ativação.

Cada beat contém identidade, origem/alvos publicados, referências aos fatos,
cue visual e uma política de execução sequencial/paralela. O paralelismo só
ocorre entre elementos que podem ser apresentados juntos sem inverter causas.

Uma operação visual responde por um handle com milestones:

- `started`: elemento começou a ser apresentado;
- `impact`: instante visual que sincroniza recursos, reações e som;
- `completed`: terminou a parte bloqueante;
- resultado terminal único: completed, skipped, cancelled ou fallback.

Texto flutuante e outros resíduos podem continuar após `completed`. Isso não
autoriza disparar um novo impacto causal antes do milestone apropriado.

Estado do director:

`Idle → AwaitingEnvelope → Presenting → AwaitingStep/Paused → Settling → Idle`

Cancelamento é uma transição terminal de qualquer estado ativo. `AwaitingStep`
aguarda o jogador; não é falha. O bloqueio usa trabalho bloqueante pendente e
execução ativa, e não apenas quantidade de frames restantes.

### 4.3 Entrega entre comando e apresentação

1. Antes de enviar, capturar o escopo, a referência ao snapshot observado e o
   modelo da carta usada. Mostrar feedback local de seleção/envio, sem antecipar
   resultados ou cobrar resources na Godot.
2. Ao receber sucesso, registrar o recibo como aceito, sem iniciar automaticamente
   impactos. A sessão completa a leitura coordenada das projeções e avaliações.
3. Quando houver um envelope coerente, o adaptador inicia a apresentação:
   aceitação → saída da carta → impacto/efeitos → reconciliação final.
4. O director sinaliza conclusão bloqueante. Só então o adaptador libera a próxima
   intenção ou navega para resultado/mapa.

Falha após aceitação não significa que o comando foi rejeitado. Manter a
identidade do comando aceito, bloquear novos comandos, preservar a apresentação
anterior e permitir recuperação por leitura. Não reenviar uma escrita já
confirmada para conseguir outro recibo. Usar a recuperação existente quando o
resultado da escrita for realmente incerto.

### 4.4 Projeção visual e snapshots

`GameSession` mantém o snapshot final autoritativo. A view pode mostrar uma
projeção de apresentação separada durante a execução, nunca editável.

- Em `FullSnapshots`, consumir `StateAfter` publicado por frame quando necessário.
- Valores numéricos podem interpolar endpoints explicitamente publicados,
  `previousValue/currentValue`; isso não cria um estado reconstruído de regras.
- Em modo compacto, não tratar `SnapshotSequence` como identidade de um estado
  intermediário exato: vários frames podem apontar para o mesmo commit.
- Na ausência de um snapshot intermediário, apresentar fatos/overlays e reconciliar
  o snapshot final. Não inventar status, ordem de pilhas, mortes ou estados completos
  aplicando uma simulação de deltas na Godot.
- Se uma funcionalidade realmente exigir estado intermediário ausente, documentar
  essa lacuna e usar uma consulta/projeção canônica autorizada, ou o modo factual.

### 4.5 Configuração visual por setting

Adicionar um perfil reutilizável de movimento e um manifesto JSON de cues,
resolvidos pelo `VisualContext` e pela identificação do setting. Separar esses
arquivos dos JSON que definem regras do jogo.

O perfil define durações, easings, lift, deslocamentos, limites, política de
motion reduzido e presets. O manifesto associa IDs/tags/fatos publicados a
cue, arte, símbolo, som e ênfase. Um setting desconhecido usa fallback visual
em inglês, com chaves em inglês. Não depende de IDs particulares do mundo 1.

O significado de um resultado crítico vem de dados publicados/configurados.
A Godot não transforma chance >100% em tier nem repete os sorteios. Caso falte
um resultado factual necessário, a engine o publica como read model derivado.

## 5. Etapas sequenciais

Cada etapa deve ter testes próprios, documentação pertinente e um commit
separado após validação. Antes de começar, revisar e preservar as alterações
pendentes da correção das cartas; não misturá-las inadvertidamente ao primeiro
commit desta refatoração. Este documento não faz esses commits automaticamente.

### Etapa 1 — Baseline e regressões temporais

Implementação:

- Converter as reproduções da auditoria em testes mantidos no projeto.
- Criar fixtures com consulta atrasada, um último frame, carta consumida/retida,
  vitória com fila restante, múltiplos impactos, condensação intercalada e pause.
- Registrar tempo de envio, recebimento, sincronização, primeiro cue, impacto,
  término bloqueante e término decorativo separadamente.
- Adicionar matriz de escopos e testes de cancelamento durante cada await.

Aceitação: falhas conhecidas ficam demonstradas por assertions, não somente
prints; baseline distingue latência da API de duração da apresentação.

Commit sugerido: `test(animation): capture sequencing and lifecycle regressions`.

### Etapa 2 — Contratos de fatos e envelope contextual

Implementação:

- Definir envelope e identidades de apresentação, com cópias defensivas ou
  referências privadas imutáveis, sem novo ledger de gameplay.
- Verificar dados reais de `EffectExecutionStep`, aplicações, relações entre
  procs/impacts, `CardZoneFlowStepRecord` e snapshots disponíveis por modo.
- Validar escopo e coerência do recibo com as projeções finais; descartar respostas
  obsoletas e resolver o caso de avanço externo concorrente explicitamente.
- Auditar visibilidade dos passos de zonas e autorização das resoluções históricas.
- Estender DTOs/OpenAPI somente se faltar um fato indispensável. Toda extensão
  deve derivar do resultado persistido, sem novo sorteio ou execução de regras.

Aceitação: nenhum dado de outra run/branch/setting entra no envelope; nenhum
campo visual é necessário no comando; falta de dado produz fallback explícito.

Commit: `refactor(animation): define scoped presentation envelopes`.

### Etapa 3 — Planner causal de beats

Implementação:

- Extrair a transformação de frames para um planner puro e testável.
- Preservar a ordem canônica, a identidade de origem e a hierarquia de procs.
- Separar impactos repetidos e continuações quando necessário à leitura.
- Condensação continua sendo uma ativação visual com seus resultados relacionados;
  não agrupar ocorrências não contíguas atravessando outro acontecimento causal.
- Um frame sem cue relevante não impõe uma espera automática de 0,32 s.
- Não duplicar todos os cálculos/estados nos grupos derivados.

Aceitação: `A1,B,A2` mantém causalidade; nenhum fato é perdido, duplicado ou
mutado; multihit, cadeia, área e condensação têm ordenação verificável.

Commit: `refactor(animation): preserve causality in presentation beats`.

### Etapa 4 — Director e handles de conclusão

Implementação:

- Evoluir o Playback e absorver o temporizador antigo em um executor canônico.
- Implementar milestones e resultado terminal único para operações visuais.
- Expor `has_blocking_work`, beat ativo, estado e conclusão contextual.
- Sequenciar por conclusão/milestone, com pausas de leitura do perfil quando
  necessárias. Permitir resíduos cosméticos concorrentes e limitados.
- Implementar controle manual por beat, sem sobreposição ao apertar confirmar
  rapidamente. Não usar um timeout como conclusão normal de todos os efeitos.
- Adicionar watchdog para handles defeituosos, suspenso no pause e na espera
  manual; encerrar por fallback seguro com diagnóstico, sem liberar callbacks velhos.

Aceitação: despachar o último beat não libera input; conclusão, skip, cancelamento
e remoção da view resolvem cada handle exatamente uma vez.

Commit: `feat(animation): coordinate blocking playback completion`.

### Etapa 5 — Integração canônica com combate e mão

Implementação:

- Remover o início automático de impactos no sinal de recebimento do recibo.
- Usar a entrega contextual da etapa 2 para iniciar o director.
- Capturar a carta e origem antes do envio; consultar permanência na projeção
  final, não no presenter anterior.
- Animar uma única representação da carta em trânsito, mantendo origens e
  superfícies de interação sem duplicação.
- O controller bloqueia comandos pelo estado do director e da sessão.
- Preservar seleção neutra após uso e a publicação atômica das descrições.
- Rejeição e falha pós-aceitação possuem caminhos visuais distintos.

Aceitação: voo precede impacto; áudio acompanha seus milestones; não há duas
cartas visíveis representando o mesmo trânsito nem comandos sobre opções antigas.

Commit: `fix(combat): synchronize accepted actions and card presentation`.

### Etapa 6 — Pause, cancelamento, skip e pós-combate

Implementação:

- Tornar explícitos os modos de processamento da UI, cenário, efeitos e overlays.
- Pause congela director e efeitos de combate, mantendo o menu utilizável.
- Separar sons de menu de vozes de combate para que os últimos possam pausar
  junto aos impactos. Não pausar arbitrariamente o áudio inteiro da aplicação.
- Navegação, mudança de setting/branch, desconexão e destruição da view cancelam
  handles e callbacks pelo escopo/epoch.
- Adicionar barreira de apresentação para vitória, derrota e transição de encontro.
- Skip liquida apenas apresentação: cancela resíduos, mostra o estado final e
  resolve a barreira uma vez. Não envia END_TURN nem outro comando.

Aceitação: pause no meio do voo/flash mantém posição e tempo; retomar continua;
nenhum frame relevante é cortado automaticamente por vitória; skip não muda hashes.

Commit: `fix(animation): unify pause cancellation and combat exit`.

### Etapa 7 — Feedback semântico e leitura dos recursos

Implementação:

- Criar executores reutilizáveis para impacto, proteção, recuperação, pagamento,
  ganho/perda de resource, status/modifier, fonte ativada e continuação.
- Diferenciar pagamento de dano usando fatos/proveniência publicados, não o
  nome energy ou health.
- Consumir o resultado crítico publicado e mapear sua ênfase pelo manifesto.
- Coordenar barras, contadores, badges e som com o milestone de impacto.
- Organizar números por alvo e lanes, evitando sobreposição; preservar sinais e
  quantidades, incluindo alterações de minimum/maximum.
- Apresentar ativações de relíquias/status e efeito residual/condensação sem
  repetir seus cálculos. Dados desconhecidos usam cue factual neutro.

Aceitação: pagamento não parece ferimento; heal não parece ataque; dano em mana
continua suportado; vários impactos são legíveis; comparação não depende só de cor.

Commit: `feat(animation): render semantic effects and resource feedback`.

### Etapa 8 — Coreografia genérica de cartas e zonas

Implementação:

- Consumir source/target addresses, operações e instance IDs dos passos de zonas.
- Resolver âncoras visuais por endereço publicado e configuração de apresentação.
- Suportar transferência, criação, destruição, mudança de ordem e fluxo em lote,
  inclusive cartas retidas e destinos fora da mão.
- Reutilizar CardView, catálogo de artes e a mão, sem uma segunda implementação.
- Cartas ocultas usam verso/representação permitida; não aproveitar ordens ou
  detalhes brutos para revelar informação que o modo não disponibiliza.
- Unknown zone: cue neutro e reconciliação final, sem inferir propósito da pilha.

Aceitação: nomes arbitrários de zonas funcionam; comprar/descarte/exílio são
mapeamentos visuais; instâncias não duplicam, foco não migra automaticamente e
cartas criadas/destruídas deixam zero ghosts após conclusão/cancelamento.

Commit: `feat(animation): present generic card zone transitions`.

### Etapa 9 — Perfis, acessibilidade e áudio coordenado

Implementação:

- Centralizar constantes em perfil e manifesto versionados de apresentação.
- Resolver overrides por setting e validar limites, assets e IDs; hotreload visual
  adota a nova configuração em fronteira segura, sem reexecutar o comando ativo.
- Preservar preferências existentes; presets normal/rápido, modo instantâneo e
  passo a passo são apresentação, não game modes novos.
- Movimento reduzido desativa deslocamento/oscilações e flashes intensos, mantendo
  confirmação e informação; ações sem movimento não esperam o tempo de um voo.
- Separar duração cosmética de pausa de leitura. Acelerar não remove causas.
- Áudio é disparado por milestones; volumes/cooldowns/vozes permanecem limitados.
  Qualquer variação cosmética fica fora do RNG da run.

Valores iniciais de calibração, não padrões obrigatórios da indústria:
hover/reposicionamento 90–150 ms; trânsito 150–250 ms; reação de impacto
80–180 ms; uma ação simples, excluindo REST, aproximadamente 300–600 ms.
Validar esses números por playtest e ajustar no perfil, não nas regras.

Aceitação: os executores não mantêm constantes conflitantes; todos os modos
chegam à mesma projeção final; preferências e remapeamento continuam funcionais.

Commit: `feat(animation): configure motion accessibility and synchronized audio`.

### Etapa 10 — Reutilização em timeline e replay progressivo

Implementação:

- Compartilhar planner, director e executores; cada inspeção possui sua instância
  descartável e não consome o cursor do combate vivo.
- Manter loader paginado separado do executor visual. Buscar a resolução do
  comando selecionado sob demanda, com cancelamento e limite de payload/cache.
- Verificar se a consulta atual consegue resolver combates já encerrados e
  aplicar permissões históricas. Se faltar endereço estável, estender a consulta
  canônica por run/commit, reutilizando o mesmo serviço de leitura.
- Não usar uma rota de combate atual como atalho para driblar autorização histórica.
- Snapshot completo autorizado: visualização compatível com combate. Dados
  compactos/restritos: apresentação factual, sem inventar uma reconstituição exata.
- Autoplay espera a conclusão bloqueante; trocar comando/branch cancela a sessão
  antiga. Inspecionar não envia comandos nem ativa a run histórica.

Aceitação: replay reutiliza os cues disponíveis sem baixar toda a run; permissões
do modo normal continuam vigentes; endpoint/setting/branch não compartilham cache.

Commit: `feat(replay): reuse scoped combat presentation on demand`.

### Etapa 11 — Limites e desempenho

Implementação:

- Limitar textos flutuantes, efeitos simultâneos e vozes por perfil. Sob pressão,
  simplificar resíduos decorativos, nunca remover fatos do plano/log.
- Definir como resumir visualmente lotes grandes sem alterar ordem, proc único
  ou total publicado; diagnóstico completo continua consultável.
- Evitar deep copies repetidas de snapshots por cue e retenção de recibos antigos.
- Só adicionar pooling se o profiling mostrar benefício, com reset completo e
  cancelamento seguro de tweens/sinais antes da reutilização.
- Medir API, parsing, planner, primeiro feedback, conclusão e renderização de
  modo separado, incluindo mediana/p95 e pior caso observado.

Aceitação: nenhum crescimento de objetos/handles por repetição após settling;
ações simples não têm duração proporcional ao número de frames sem cue. Alvo
de menos de 1 s de sincronização local continua separado do tempo artístico.
O orçamento de renderização será medido para 60 FPS no hardware de referência;
não prometer p95 global com uma amostra pequena.

Commit: `perf(animation): bound effects and instrument presentation latency`.

### Etapa 12 — Certificação, documentação e remoção do fluxo antigo

Implementação:

- Executar testes de contratos, planner, lifecycle, combate real, UI e replay.
- Cobrir default, ascendant e volatile-core, além de fixture com resources e
  zonas arbitrários e assets ausentes.
- Capturar sequências visuais e comparar timings em 1280×720, 1920×1080 e
  2560×1080, inglês/português, escala de texto e alto contraste.
- Verificar replay semântico e ausência de alterações em RNG/hashes para os
  mesmos comandos sob diferentes políticas de apresentação.
- Documentar autoria de cues/perfis, fallback, lifecycle, performance e resultados.
- Remover caminhos obsoletos, listeners duplicados, durações antigas e componentes
  sem consumidores. Não remover recursos ainda usados por labs ou inspeção.

Aceitação: relatório diferencia teste automatizado, observação visual, benchmark
e limitação; não há dois executores principais nem escrita de gameplay pela animação.

Commit: `test(animation): certify presentation and remove obsolete paths`.

## 6. Marcos de entrega

| Marco | Etapas | Resultado verificável |
| --- | --- | --- |
| 1 — Confiabilidade | 1–6 | ordem correta, input protegido, pause, cancelamento e vitória seguros |
| 2 — Leitura e game feel | 7–9 | efeitos/resources/cartas distinguíveis e perfis uniformes por setting |
| 3 — Reutilização e certificação | 10–12 | inspeção/replay compartilhados, limites medidos e fluxo único |

O primeiro marco deve ser testado e aprovado antes de investir em variações
visuais mais elaboradas. A correção estrutural não depende de artes definitivas.

## 7. Matriz mínima de testes

- Resposta REST imediata, atrasada, rejeitada e falha de leitura após aceitação.
- Último beat com tween ativo; confirmação repetida; fila vazia e evento sem cue.
- Pause/retomada em voo, impacto, float e transição; skip em cada milestone.
- Destruição da view, saída ao menu, troca de setting, branch e endpoint durante await.
- Carta consumida, retida, criada, destruída, modificada e removida do alvo visual.
- Dano/proteção/cura em resources arbitrários, limites alterados e delta zero.
- Status/reliquias/modifiers, DoT, multihit, área, cascata, perda de alvo,
  condensação ofensiva/defensiva e aplicações intercaladas.
- Vitória/derrota/empate com vários beats restantes.
- Normal/rápido/instantâneo/manual/movimento reduzido, teclado/mouse/gamepad.
- Replay permitido/restrito, compact/full, páginas, cache e cancelamento.
- Lotes grandes dentro dos limites de conteúdo, assets ausentes e manifesto inválido.
- Determinismo de gameplay independente do ritmo, pausa e efeitos cosméticos.

## 8. Referências e limites da comparação

São referências de comportamento e princípios, não provas da arquitetura interna
de jogos comerciais nem tempos universais obrigatórios:

- [Slay the Spire — notas oficiais: ajuste de ritmo por efeito e fast mode](https://store.steampowered.com/news/posts/?appids=646570&enddate=1606707255&feed=steam_community_announcements).
- [GDC — Artistic Principles of VFX, com casos de Hearthstone](https://gdcvault.com/play/1024439/Visual-Effects-Bootcamp-Artistic-Principles).
- [Balatro — movimento reduzido nas notas oficiais](https://steamcommunity.com/games/2379780/announcements/detail/4208127528883891676).
- [Godot — Tween, propriedade única por tween e lifecycle](https://docs.godotengine.org/en/stable/classes/class_tween.html).
- [Godot — pause, process modes e sinais](https://docs.godotengine.org/en/stable/tutorials/scripting/pausing_games.html).
- [Xbox Accessibility Guideline 117 — distrações visuais e movimento](https://learn.microsoft.com/en-us/xbox/accessibility/xbox-accessibility-guidelines/117).

Planos relacionados: [arquitetura gráfica da run](GODOT_RUN_GRAPHICS_ARCHITECTURE_PLAN.md),
[mão e coleções](GODOT_COMBAT_REFERENCE_LAYOUT_HAND_AND_PILES_PLAN.md) e
[carregamento progressivo do replay](REPLAY_LOADING_ARCHITECTURE_AND_PERFORMANCE_PLAN.md).
