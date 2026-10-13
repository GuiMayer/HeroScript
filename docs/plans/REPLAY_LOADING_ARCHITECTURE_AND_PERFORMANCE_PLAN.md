# Replay: carregamento progressivo e consultas eficientes

Data: 2026-10-13. Estado: implementado em etapas;
[progresso](REPLAY_LOADING_IMPLEMENTATION_PROGRESS.md) e
[certificação](../design/REPLAY_LOADING_CERTIFICATION.md).

## 1. Objetivo e limites

Reduzir o tempo entre abrir um replay e visualizar seu primeiro comando para
menos de um segundo no ambiente local de referência, sem alterar regras,
comandos, RNG, conteúdo, hashes, checkpoints existentes ou dados das partidas.

A mudança envolve engine/persistência, API e cliente Godot. Não basta acelerar o
servidor se a interface continuar esperando o histórico inteiro.

Princípios obrigatórios:

- Commits continuam sendo a única autoridade de gravação.
- Índices, caches e DTOs são derivados e descartáveis; não são outra persistência
  de gameplay, nem prova substituta da integridade.
- Assistir ao registro, materializar um estado e verificar deterministicamente
  a run são operações diferentes, com contratos e custos diferentes.
- A Godot não reconstrói estado a partir de deltas nem interpreta regras.
- Preservar permissões, isolamento por setting/run/branch/endpoint e imutabilidade.
- Não criar rota alternativa de timeline rápida mantendo a antiga como outro
  fluxo principal. Atualizar o contrato canônico e seus consumidores juntos.
- Não desabilitar validação, ampliar checkpoints ou adicionar compressão como
  primeira resposta ao problema.
- Não migrar saves antigos silenciosamente; schemas incompatíveis continuam
  falhando explicitamente.

## 2. Diagnóstico e baseline

Arquivos centrais:

- Godot: `scripts/ui/run_replay_screen.gd`,
  `scripts/application/game_session.gd`, `scripts/engine/engine_gateway.gd`,
  `scripts/engine/http_transport.gd`.
- API: `src/API/Controllers/RunJournalController.cs`.
- Core: `RunCommitProjectionReader`, `FileRunCommitStore`,
  `RunStateDelta`, `RunSemanticReplayService`.

Auditoria local de uma run atual, schema 4, com 124 comandos, 16 checkpoints e
15,54 MiB de arquivos. Nenhuma partida foi modificada.

| Medida | Resultado |
| --- | --- |
| Timeline materializada: primeira leitura do processo | 6,19 s |
| Timeline materializada: duas leituras seguintes | 1,67–2,19 s |
| Envelopes sem materializar deltas, cache já aquecido | 0,18–0,22 s |
| JSON da timeline atual | 8.047.708 bytes |
| Projeção estimada dos campos usados pela tela | 78.979 bytes |
| Alocações cumulativas na operação materializada | aproximadamente 710–810 MB |
| Primeiro commit: leitura e serialização aquecidas | aproximadamente 2 ms |

A primeira amostra inclui aquecimento do processo/.NET. O comparativo de
envelopes não é um endpoint novo já pronto, nem verifica todos os estados.
Alocações cumulativas não significam memória residente de pico.
As medições não incluem HTTP, parsing JSON ou renderização na Godot.

Problemas:

1. A projeção chama `LoadCommitsAsync`, materializando todos os estados da página
   para depois descartá-los.
2. A timeline envia frames completos, embora a lista/renderização inicial use
   somente cabeçalhos e contexto.
3. O cliente percorre páginas de 200 antes de selecionar o primeiro comando.
   Há consulta vazia adicional e limite silencioso de 20 páginas/4.000 comandos.
4. O cache de validação não evita deserialização, aplicação de deltas ou hashes
   de estados materializados.
5. Leituras pesadas seguram o mesmo gate usado nas gravações da run.
6. O cache de commits da tela é ilimitado; requisições obsoletas não são
   canceladas/coalescidas.
7. A métrica HTTP termina antes do parsing; faltam tempo até conteúdo visível,
   payload, alocações e decomposição do custo.

## 3. Arquitetura-alvo

### 3.1 Responsabilidades

1. **Commit store**: ler bytes/envelopes e materializar estados com validação.
2. **Serviço de consulta de replay**: produzir páginas compactas e apresentação
   de um comando a partir da autoridade existente.
3. **Controller**: autorização, validação de parâmetros e resposta HTTP.
4. **Gateway/sessão**: transporte e identificação de contexto, sem regras.
5. **Replay controller Godot**: paginação, cursor local, cache e lifecycle.
6. **Replay view/presenters**: renderização e formatação, sem REST.

O `Playback` do combate ao vivo não será transformado no loader de replay. O
replay da run mantém seu cursor de leitura local; não haverá um segundo
executor de comandos ou uma fila paralela autoritativa.

### 3.2 Contratos

**GET /api/v1/runs/{runId}/timeline**

Atualizar a rota existente para publicar uma página compacta:

- Escopo: runId, settingId, branch/lineage identificável e revisão quando aplicável.
- Paginação: afterSequence, throughSequence, nextCursor, hasMore e quantidade.
- `throughSequence`: sequência máxima capturada na primeira página; os pedidos
  seguintes passam o mesmo valor. Novos comandos não alteram o replay aberto.
- Itens: sequence, step, commandId, commandType, logicalTimestamp, previousStateHash,
  stateHash, frameCount e cabeçalhos dos frames.
- Cabeçalhos: frameIndex, scope, kind, actorId, phaseId, combatId, round/activation
  e step quando publicados.
- Excluir StateAfter, StateDelta, cálculos completos, aplicações completas,
  Resolution e snapshots internos.
- Começar com 25 itens por página na demo; limite HTTP permanece validado.
- hasMore deve ser explícito, sem precisar pedir uma página vazia.
- Validação estrutural/da cadeia não deve ser apresentada como replay
  deterministicamente verificado.

**GET /api/v1/runs/{runId}/commits/{sequence}/presentation**

Adicionar uma projeção de leitura específica, não uma cópia de commits:

- Identidade e hashes do comando.
- Resumo publicado do estado: lifecycle, currentNodeId, recursos, quantidade de
  cartas/reliquias e demais campos realmente usados pelo replay.
- Transcrição de diálogo registrada, com identificadores/dados para a Godot
  formatar/localizar; sem execução de escolhas ou inferência de consequências.
- Contexto/revisão suficientes para cache e apresentação.
- Usar o mesmo materializador canônico; não criar uma reconstrução alternativa.
- Aplicar a mesma autorização de inspeção histórica do fluxo atual.

**GET /api/v1/runs/{runId}/commits/{sequence}**

Manter para inspeção técnica completa. Não é legado: representa outro nível de
detalhe da mesma autoridade. A tela normal deixa de baixar esse documento
inteiro automaticamente; o modo de inspeção o solicita explicitamente.

**POST /api/v1/runs/{runId}/verify**

Continua reexecutando os comandos em runtime isolado. Não será disparado ao
abrir, paginar ou navegar no replay; não será substituído por cache de timeline.

### 3.3 Persistência e integridade

Estender a fronteira de leitura de envelopes para paginação real, incluindo o
limite throughSequence. A API não deve acessar arquivos diretamente.

Requisitos:

- Validar identidade run/sequence, schema/engine e envelopes dos bytes capturados.
- Validar continuidade interna e vínculo com a fronteira anterior da página.
- Não usar timestamp/tamanho do arquivo como prova de bytes inalterados.
- Evitar ler/materializar todo o histórico como fallback silencioso em uma
  implementação que declare suporte à paginação.
- Preservar distinção entre cadeia estrutural válida e estados/hashes verificados
  por materialização ou reexecução.
- Ausência, corrupção, sequência faltante e snapshot de paginação inválido
  produzem erro explícito, não sucesso parcial.
- Cache hit não pode ocultar alteração de bytes na origem.
- Índice de sequências/checkpoints, se necessário, é derivado e reconstruível.

### 3.4 Lifecycle do cliente

Estados explícitos: carregando primeira página, pronto, carregando mais,
carregando comando, erro recuperável e descartado.

O primeiro comando será mostrado após a primeira página + sua apresentação,
sem esperar as páginas restantes. A lista cresce por lotes, preservando
seleção/scroll. O restante é carregado quando necessário; não iniciar um loop
que inevitavelmente baixe tudo em background.

Next/autoplay no final da página pede a próxima e continua. O fim verdadeiro
vem de hasMore=false. Reiniciar volta ao primeiro comando do mesmo snapshot.

Cache inicial: até 32 apresentações e 8 MiB aproximados por contexto, política
LRU e ajuste pelos benchmarks. Não guardar todos os commits completos.
Chaves incluem endpoint epoch, setting, run/branch, sequência e contexto de
conteúdo/protocolo. Runs de settings distintos nunca compartilham entradas.

Pedidos idênticos em andamento são compartilhados. Seleções têm prioridade
sobre prefetch. Prefetch é opcional, limitado a vizinhos, e não bloqueia a
abertura. Epoch continua protegendo a UI, mas cancelamento também deve cessar
trabalho dispensável no transporte. Cancelar não marca a API como offline.

## 4. Etapas sequenciais

### Etapa 1 — Baseline reproduzível e métricas

- Criar benchmark dedicado somente de leitura, com datasets isolados e schemas
  atuais. Não usar FindCommand como benchmark read-only: pode reparar índices.
- Medir primeira leitura do processo, leituras aquecidas e cache desativado.
- Fixtures de 25, 125, 500, 1.000 e mais de 4.000 comandos; variar tamanho de
  estado/frames, quantidade de cálculos e distâncias dos checkpoints.
- Capturar servidor: gate wait, leitura, validação, materialização, projeção e
  serialização; cliente: transferência, bytes, parsing e primeiro conteúdo visível.
- Separar custo de verify do custo de abrir/assistir.

Aceite: reproduzir o problema e registrar metodologia, hardware/build e dataset.
Commit sugerido: `test(replay): establish loading and payload benchmarks`.

### Etapa 2 — Leitura paginada de envelopes

- Estender a abstração existente e implementar leitura paginada no file store.
- Capturar head/throughSequence e enumerar apenas o intervalo necessário.
- Ler uma fronteira/lookahead compacta para continuidade/hasMore, sem estados.
- Verificar bytes/envelopes/cadeia e manter políticas de corrupção existentes.
- Adaptar doubles de testes; implementar contratos explícitos nas fronteiras.

Aceite: nenhuma materialização na consulta de envelopes; páginas sem lacunas,
com snapshot estável apesar de append; falhas de corrupção testadas.
Commit: `feat(persistence): page validated commit envelopes`.

### Etapa 3 — Timeline compacta canônica

- Criar DTOs/read model de timeline, separados de RunCommit persistido.
- Conectar serviço de consulta à leitura paginada, sem alterar consumidores que
  realmente precisam de estados completos.
- Atualizar GET timeline, DI, OpenAPI/schemas, exemplos e testes de contrato.
- Não conceder permissões usando índice/cache/DTO de exibição. Preservar policy
  atual de autorização; medir o custo de GetRun frio separadamente.

Aceite: timeline não chama LoadCommitsAsync materializado; ausência de campos
pesados verificada por contrato; payload cresce com cabeçalhos, não com tamanho
do estado/cálculos; hasMore/throughSequence corretos.
Commit: `feat(api): publish compact paginated replay timeline`.

### Etapa 4 — Apresentação individual de comando

- Implementar a projeção /commits/{sequence}/presentation.
- Usar o materializador e os dados registrados para resumo e diálogo.
- Não converter estado em texto localizado na engine.
- Preservar endpoint completo e verificação sem aliases redundantes.

Aceite: resumo/transcrição equivalentes aos dados do commit; regras, hashes e
snapshot autoritativos inalterados; permissões/404/cancelamento cobertos.
Commit: `feat(replay): project command presentation on demand`.

### Etapa 5 — Transporte contextual e cancelável

- Atualizar gateway/sessão para contratos novos.
- Adicionar handles de leitura cancelável, com conclusão única e cleanup do
  HTTPRequest; não deixar awaits esperando sinais de um node liberado.
- Coalescer pedidos idênticos de replay, com escopo e concorrência limitada.
- Preservar endpoint epoch e diferenças entre erro, timeout e cancelamento.
- Métricas devem incluir parsing separado e tamanho da resposta.

Aceite: trocar endpoint/sair da tela não atualiza o contexto novo, não gera
erro de conexão falso e não deixa pedidos/nodes pendurados.
Commit: `feat(godot): add scoped cancellable replay reads`.

### Etapa 6 — Separar coordenação e renderização

- Extrair ReplayController para paginação/cache/cursor/lifecycle.
- ReplayView recebe modelos e emite intents; presenters formatam fatos.
- RunReplayScreen vira adapter entre sessão/controller/view.
- Manter funções existentes: voltar ao resultado/histórico, restart, previous,
  next, autoplay, velocidade, reduced motion, diálogo e verify explícito.

Aceite: view sem REST/autoridade de regras; controller testável sem cenas reais;
nenhuma duplicação de Playback ou de fluxo de comandos.
Commit: `refactor(godot): separate replay loading from its view`.

### Etapa 7 — Primeiro conteúdo imediato e paginação incremental

- Exibir primeira página imediatamente e selecionar seu primeiro comando.
- Mostrar resumo assim que sua leitura terminar; erros posteriores não apagam
  comandos já carregados.
- Carregar mais perto do fim da lista ou por ação explícita, sem teto de 4.000.
- Next/autoplay atravessam páginas sem saltos nem comando repetido.
- Distinguir total conhecido/snapshot de quantidade carregada na UI.
- Não reconstruir ItemList inteira nem reiniciar scroll a cada página.

Aceite: atraso artificial na última página não bloqueia a primeira; datasets
com 4.001+ comandos são navegáveis integralmente.
Commit: `feat(godot): stream replay navigation page by page`.

### Etapa 8 — Cache LRU, prioridades e pedidos obsoletos

- Implementar limites por quantidade e tamanho; liberar no dispose/context switch.
- Compartilhar seleção repetida em voo e dar prioridade ao comando atual.
- Cancelar pedidos exclusivos obsoletos; não cancelar uma leitura ainda usada
  por outro consumidor.
- Prefetch, se aprovado pelas métricas, no máximo um vizinho por vez.
- Disponibilizar inspeção completa apenas sob demanda/autorização.

Aceite: scrubbing rápido não cria fila ilimitada, não mistura estados e mantém
memória/pedidos limitados independentemente da duração da run.
Commit: `perf(godot): bound replay caches and outstanding reads`.

### Etapa 9 — Otimizar leituras de estados sem enfraquecer integridade

- Evitar ler/deserializar o mesmo delta na busca reversa e na aplicação seguinte:
  capturar segmento uma vez e aplicar na ordem correta.
- Avaliar índice derivado de checkpoints antes de adicionar cache de estados.
- Se necessário, cache limitado de resultados materializados com identidade de
  todos os bytes/dependências do segmento, nunca somente run/sequence ou mtime.
- Não devolver estruturas compartilhadas mutáveis; preservar contratos
  imutáveis/defensivos e revalidação após alteração da origem.
- Reduzir duração do gate: capturar bytes/coerência sob proteção e processar fora
  dela somente quando append/delete/read tiverem semântica testada.
- Não remover gates sem provar isolamento, nem duplicar hash/materialização.

Aceite: estados equivalentes aos atuais; alteração de delta/checkpoint invalida
cache; leituras concorrentes não bloqueiam a run durante processamento CPU
desnecessário; testar append/delete/cancelamento/bytes modificados.
Commit: `perf(persistence): bound historical state materialization work`.

### Etapa 10 — Certificação ponta a ponta e documentação

- Executar matriz Release com API QA e preferências/saves Godot isolados.
- Testar três settings, histórico final e run ativa, branch, diálogos, UI EN/PT,
  resoluções e permissões normal/sandbox/dev.
- Comparar hashes finais e verificação semântica antes/depois para a mesma
  sequência; abrir/navegar não deve gravar commits nem alterar seleção da run.
- Incluir falhas no meio da paginação, saída da tela, mudança de endpoint,
  corrupção, recurso/setting desconhecido, cache eviction e reabertura.
- Atualizar documentação de API, arquitetura, reprodução e resultados.

Aceite: metas abaixo medidas; regressões funcionais/integração aprovadas.
Commit: `test(replay): certify progressive loading and integrity`.

## 5. Metas e critérios de desempenho

Ambiente de referência: Windows local, API Release, Godot atual e disco local.
Identificar hardware e versões no relatório; não generalizar tempos para outras
máquinas. Não aquecer toda a run antes de medir a abertura fria.

- Meta principal: primeiro comando **com conteúdo útil**, não apenas skeleton,
  visível em menos de 1 s; avaliar p95 em séries separadas frias/aquecidas.
- Fixtures-alvo: 125, 500 e 1.000 comandos com estados/frames representativos.
- Stress: mais de 4.000 comandos sem truncamento e sem necessidade de baixar
  todo o histórico para abrir.
- Primeira página de 25 itens: orçamento inicial de 100 KiB de JSON, ajustado
  somente com conteúdo representativo e justificativa.
- Seleção em cache: conteúdo no próximo frame útil, sem HTTP.
- Seleção sem cache: meta p95 abaixo de 400 ms no mesmo ambiente.
- Cache/pedidos/memória limitados por configuração; não por duração da run.
- UI sem parsing ou reconstrução de lista proporcional ao histórico inteiro.
- Separar uso transitório de memória/alocações do tamanho retido no cache.

No CI, priorizar limites estruturais (DTO sem campos pesados, número de leituras,
materializações, páginas, pedidos e memória retida). Tempos absolutos entram no
benchmark controlado, não em assertions frágeis de testes unitários.

## 6. Riscos e decisões de implementação

- Compactar a timeline reduz a inspeção automática. Preservar logs/detalhes por
  leituras explícitas, sem ocultar funcionalidade ou expandir permissões.
- Projeção por envelopes não prova todos os hashes de estado: não rotulá-la como
  verificação semântica. Verify permanece disponível e explícito.
- Primeira autorização ainda pode hidratar estado via GetRun e bloquear
  sincronamente. Medir; se exceder orçamento, criar consulta assíncrona de
  autorização pelo estado canônico, sem confiar em metadados de exibição.
- throughSequence fixa o histórico observado, mas bytes adulterados ainda
  precisam causar falha: append-only não dispensa revalidação.
- Compressão HTTP é complemento posterior, não substitui remover overfetch e
  materialização. Só incluir após medir suporte/configuração real do transporte.
- Evitar mudanças na política de checkpoints ou schema de gravação durante esta
  refatoração. Qualquer necessidade descoberta deve virar decisão explícita.
- Não afirmar que a meta de um segundo foi atingida antes de medir API → Godot.

## 7. Ordem de entrega

Dependências principais: 1 → 2 → 3 → 4 → 5 → 6 → 7 → 8 → 9 → 10.

Commit por etapa, testes antes do próximo marco e documento de progresso
atualizado durante a futura implementação. Não fazer push sem solicitação.
A implementação e seus resultados estão registrados no documento de progresso.
