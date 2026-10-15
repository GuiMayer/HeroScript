# Plano de correção da latência interativa mantendo REST

Data: 2026-10-14. Estado: proposto; não implementado.

## 1. Objetivo e limites

Reduzir o intervalo entre input, confirmação autoritativa, apresentação e próximo
input sem mudar regras, conteúdo, escolhas disponíveis, cálculos, RNG, ordem dos
efeitos, atomicidade, persistência durável ou imutabilidade.

REST continua sendo a única fronteira de ações do cliente. Não introduzir DLL
embutida, simulador de regras na Godot, estado mutável compartilhado ou um segundo
executor. Otimizar o trabalho necessário e sua distribuição, não eliminar garantias.

## 2. Evidências da auditoria

Medições locais pequenas, não percentis nem garantias universais:

| Caminho | Amostras observadas |
| --- | --- |
| HTTP local, endpoint simples | 2–7 ms |
| HTTP local, GET de uma run de 496.341 bytes | 23–33 ms |
| Mesmo GET pelo transporte atual da Godot, teste isolado a 60 FPS | 1.739–1.766 ms |
| Mesmo GET com `use_threads=true`, somente na cópia de diagnóstico | 246–258 ms |
| Parse desse JSON na Godot no experimento | 26–33 ms |
| Ações legais, primeiro cálculo, teste Release em TestServer | 915,8 ms |
| Mesmas ações legais com cache aquecido | 36,4 ms |
| Inspeção, primeira consulta/reuso no mesmo teste | 385,2 / 34,6 ms |

O teste TestServer não usa conexão TCP real, mas inclui aplicação e serialização;
não é um benchmark puro de chamada direta à engine.

Na run consultada, `encounters` respondeu por aproximadamente 393 KB do JSON;
mapa, modo resolvido e manifesto também estavam presentes. Ela estava na sequência
76 e sem combate ativo. Isso demonstra desperdício de dados, mas não mede o POST
de uma carta dessa run. O experimento não alterou saves nem enviou comandos reais.

O fluxo atual pode fazer 10–11 requisições por comando de combate. Há paralelismo
no lote de projeções, mas a leitura da run, o lote e a inspeção formam dependências
sequenciais. Não somar como se todas as chamadas fossem sequenciais.

Arquivos principais:

- `examples/godot-engine-showcase/scripts/engine/http_transport.gd`;
- `examples/godot-engine-showcase/scripts/engine/engine_gateway.gd`;
- `examples/godot-engine-showcase/scripts/application/game_session.gd`;
- `src/Core/Combat/LegalActions/LegalActionResolver.cs`;
- `src/Core/Run/Content/CardInspectionService.cs`;
- `src/Core/Run/Content/CardPresentationQueryService.cs`;
- `src/Core/Infrastructure/Persistence/FileRunCommitStore.cs`;
- `tests/API.Tests/Integration/CombatPreviewLatencyTests.cs`.

## 3. Invariantes obrigatórias

1. Mesmas entradas, seed e conteúdo fixado produzem o mesmo estado canônico,
   cursores de RNG, efeitos, aplicações, fases, movimentações e resultado.
2. Ler, descobrir opções e produzir previews não altera a run nem consome seu RNG.
3. O POST só confirma sucesso após o commit durável exigido pelo contrato atual.
4. Não reduzir a fidelidade do journal, checkpoints, replay ou branches.
5. Cliente envia intenção; engine valida e calcula. DTO leve não é regra alternativa.
6. Uma publicação da sessão contém dados da mesma revisão. Nunca misturar resultados
   de requests feitos antes/depois de um comando, hot reload ou troca de branch.
7. Cache é derivado, limitado e descartável. Recomputar sem cache preserva o resultado.
8. Não remover cópias indiscriminadamente: referências compartilhadas só podem
   apontar para dados realmente imutáveis, não Dictionaries mutáveis públicos.
9. Não ocultar atraso com impacto otimista, números inventados ou animação extra.
10. Preservar as informações úteis da UI. Carregamento sob demanda muda a entrega,
    não a semântica dos cálculos nem a autoridade sobre eles.

## 4. Arquitetura de destino

```text
Input → comando REST → executor e commit canônicos → recibo durável
                                                     ↓
                     GET de sessão compacta na revisão confirmada
                                                     ↓
                          publicação atômica no GameSession
                                                     ↓
                                apresentação e próximo input

Seleção/inspeção → GET detalhado contextual, somente quando necessário
Histórico/mapa completo/conteúdo → consultas próprias, fora do refresh por carta
```

O recibo continua iniciando apenas a apresentação já autorizada pelo contrato
existente. A fila de impactos continua dependente dos fatos sincronizados.
Pausa, fila manual, skip e modo instantâneo permanecem iguais.

### Contexto único de leitura

Criar um contexto imutável de projeção capturado uma vez por consulta. Ele contém
a referência da run, combate atual, revisão de conteúdo, identidade da branch,
ator autorizado, permissões e token de versão. Serviços projetam esse contexto
sem recarregar a run ou chamar outros endpoints HTTP internamente.

O token deve distinguir setting, run, branch, sequência, steps, revisão de conteúdo,
versão da engine e identidade/fingerprint do snapshot relevante. Não reduzir chaves
atuais a `sequence` sem provar que todas as alterações possíveis avançam essa
identidade. Preferir fingerprint já validado ou memoização por instância imutável.

## 5. Etapas sequenciais

Cada etapa deve ser uma entrega verificável. Se houver implementação com commits,
manter um commit lógico por etapa aprovada e registrar suas medições.

### Etapa 1 — Baseline e observabilidade ponta a ponta

**Mudanças**

- Separar tempos de fila, conexão/transferência, parse, cópias/publicação, construção
  das projeções, legalidade, previews, execução, hashes, serialização e persistência.
- Correlacionar clique, commandId, sequência, resposta, primeiro movimento,
  projeção sincronizada, primeiro impacto e liberação do próximo input.
- Publicar diagnóstico operacional/`Server-Timing` quando habilitado; não inserir
  relógio de parede ou métricas dentro de hashes, fatos de gameplay ou commits.
- Acrescentar um benchmark headless Godot com FPS controlado e conteúdo real.
- Usar estado conhecido em vários comprimentos de run e medir versões inéditas,
  não somente consultas repetidas que acertam o cache.

**Testes e aceite**

- Fixtures/saves isolados; nenhuma automação modifica a partida do usuário.
- Medir serviço diretamente, TestServer, HTTP local e Godot separadamente.
- Relatório com p50/p95/máximo, bytes, quantidade de requests e uso de memória.
- Observabilidade ligada/desligada não modifica hashes ou RNG.

### Etapa 2 — Transporte HTTP independente do ritmo de frames

**Mudanças**

- Adotar `HTTPRequest.use_threads` após validar a integração, usando um pool
  limitado de workers/requisições. Um worker não atende duas chamadas simultâneas.
- Verificar experimentalmente a reutilização real de conexões. Reutilizar nodes
  não deve ser apresentado como prova de conexão TCP persistente.
- Se o HTTPRequest não atender à reutilização necessária, encapsular workers de
  HTTPClient persistentes atrás da mesma interface; não manter dois transportes
  de produção com comportamentos divergentes.
- Cancelar requisição antes de devolver o worker; callbacks atrasados não podem
  completar um pedido posterior. Manter epochs de host e conclusão exatamente uma vez.
- Preservar timeouts, erros, cancelamento e isolamento de endpoints.
- Priorizar comandos e sincronização interativa sobre leitura de histórico.
- Coalescer somente GETs idênticos em voo, com escopo/permissões iguais. Cancelar
  um consumidor não cancela os demais. POST não recebe retry genérico automático.
- Confirmar que a thread faz I/O, sem manipular SceneTree ou dados visuais nela.

**Testes e aceite**

- Comparar 30/60/144 FPS, payload pequeno e grande, host lento e conexão perdida.
- Verificar cancelamento, mudança de host e ausência de vazamento de workers/nodes.
- Medir fila explicitamente: paralelismo limitado não pode criar starvation.
- Confirmar melhora do transporte sem depender de aumentar a velocidade das animações.

### Etapa 3 — Projeção compacta do estado de interação

**Mudanças**

- Definir DTO próprio para o estado necessário à cena atual, sem serializar o
  agregado completo da run como contrato de UI.
- Incluir identidade/versão, lifecycle, ator, recursos, combate atual, zonas
  autorizadas, intenções e escolhas necessárias à interação.
- Separar mapa de navegação, histórico de encounters, manifesto completo e regras
  resolvidas. Essas informações continuam disponíveis por consultas apropriadas.
- Versionar dados estáveis por revisão; nova carta, upgrade, modifier ou hot reload
  invalida exatamente a composição correspondente.
- Não trazer a coleção inteira para atualizar apenas a mão. Catálogos e pilhas
  mantêm consultas próprias, sem truncar silenciosamente dados de gameplay.
- Manter DTOs imutáveis e fazer projeções por serviços, não por lógica de controller.

**Testes e aceite**

- Contratos cobrindo combate, mapa, loja, recompensa, preparação e diálogo.
- Comparar a projeção compacta com as informações equivalentes do agregado.
- Acrescentar encontros passados não aumenta o payload do combate atual.
- Nenhuma informação de zona oculta, conteúdo futuro ou RNG oculto é revelada.

### Etapa 4 — Consulta de sessão agregada e consistente

**Mudanças**

- Criar, como proposta de contrato, `GET /api/v1/runs/{runId}/session`.
- Capturar um snapshot e montar as seções necessárias usando o contexto de leitura
  único: estado compacto, comandos disponíveis, capacidades, zonas, atributos,
  fatos das cartas visíveis e opções de interação.
- Não implementar um agregador que faça HTTP contra os endpoints individuais.
- Permitir informar a sequência esperada. Estado avançado retorna conflito
  explícito e orienta ressincronização; não etiquetar dados atuais como históricos.
- ETag deve incluir seleção de seções, revisão e escopo de autorização. Um 304
  só é válido se o cliente já possui exatamente aquela representação.
- Manter o POST leve: recibo durável e identidade da revisão. Inicialmente, usar
  um GET agregado após o recibo; não embutir todos os previews no POST.
- Consultas individuais úteis permanecem fachadas dos mesmos serviços, sem
  autoridades ou implementações de regra duplicadas.

**Testes e aceite**

- Concorrência entre GET e POST entrega uma versão inteira ou conflito, nunca mistura.
- Troca de setting/branch, hot reload, cheats e modificadores invalidam versões.
- O contexto não mantém locks de escrita durante cálculos ou serialização extensos.
- Testar ETag, autorização, seções opcionais e falha sem publicação parcial.

### Etapa 5 — Separar face, cálculo de origem e impacto contextual

**Mudanças**

- Reaproveitar os serviços existentes de apresentação e cálculo; não criar outro
  modelo de cartas ou interpretar fórmulas na Godot.
- Face usa base composta e valores de origem autoritativos. Efeitos do alvo e
  circunstâncias ficam na projeção contextual correspondente.
- Produzir em lote os fatos necessários das cartas visíveis, sem inspecionar a
  mão inteira contra um alvo arbitrário após cada comando.
- Reservar traces completos, árvores de procs, recipientes compilados e outros
  detalhes grandes para inspeção/debug quando solicitados.
- Previews contextuais passam a ser obtidos por carta/alvo/opção de custo ativa.
- Nunca substituir cálculo contextual desconhecido por zero ou por valor estimado.

**Testes e aceite**

- Mesmas bases, upgrades, buffs, atributos e circunstâncias produzem os mesmos valores.
- A face não oscila entre descrição genérica e calculada durante o carregamento.
- O tooltip não mostra o cálculo da carta anterior enquanto um preview novo carrega.
- Informação nova é publicada por revisão completa; falha não inventa fallback factual.

### Etapa 6 — Descoberta de ações sem simular impactos desnecessários

**Mudanças**

- Extrair do caminho canônico uma descoberta de ações que compartilhe condições,
  alvos, custos, fases, prioridade e orçamento com o executor existente.
- Filtrar combinações estruturalmente impossíveis antes de gerar candidatos.
  Não remover combinações válidas de múltiplos alvos ou custos alternativos.
- Não executar toda a resolução de dano/procs apenas para descobrir que uma carta
  pode ser usada, quando as regras permitem decidir sua legalidade sem isso.
- Para condições cuja legalidade depende de resolução, manter avaliação exata
  como fallback. Otimização não pode autorizar/rejeitar por aproximação.
- O POST continua validando e executando pelo pipeline canônico, com controle de
  versão e idempotência. Não confiar na descoberta feita anteriormente.
- O `CandidateId` atual depende de fingerprints de resolução: não prometer
  calculá-lo barato sem executar essa resolução. Se a projeção leve precisar de
  uma referência, usar `commandRef` próprio, claramente distinto, derivado da
  versão e intenção normalizada, sem alterar IDs determinísticos de gameplay.
- Endpoint detalhado de preview continua podendo calcular o resultado completo.

**Testes e aceite**

- Comparação diferencial entre descoberta nova e resolução atual: mesmo conjunto
  de intenções enviáveis e custos, desconsiderando apenas campos de apresentação.
- Casos com prioridades, stack, status, resource costs, modifiers, seleções,
  efeitos condicionais, multi-hit, críticos por tier e condensação.
- Todo comando aprovado produz o mesmo estado, RNG e trace pelo executor anterior.
- Ganho deve aparecer em versões inéditas, não somente com cache aquecido.

### Etapa 7 — Cache e fingerprints com dependências corretas

**Mudanças**

- Separar caches de conteúdo compilado, base composta da instância, valores de
  origem, descoberta e preview contextual, cada um com dependências explícitas.
- Compartilhar o escopo de memoização de hashes durante uma leitura agregada.
- Evitar recalcular hashes completos apenas para procurar uma entrada já conhecida,
  usando identidade imutável validada; nunca usar identidade mutável como chave.
- Aplicar single-flight aos cálculos derivados idênticos; controlar cancelamento
  de consumidores sem cancelar o trabalho necessário aos demais.
- Limitar memória por capacidade e, quando necessário, estimativa de bytes; os
  resultados de preview podem reter snapshots sucessores grandes.
- Medir hits, misses, bytes e evicções; não cachear falhas transitórias.
- Não pré-calcular todas as opções em background para mascarar um cache miss:
  isso pode competir com o próximo comando e aumentar consumo de CPU/memória.

**Testes e aceite**

- Cache desativado/aquecido/evictado produz resultado canônico equivalente.
- Alterar ator, alvo, recurso, custo, transformação, setting, branch ou revisão
  não reutiliza resultado incompatível.
- Carga longa não produz crescimento ilimitado nem retenção acidental de runs.

### Etapa 8 — Migrar o GameSession para o caminho compacto

**Mudanças**

- Trocar o refresh padrão pelo GET agregado na revisão do recibo.
- Manter POST → recibo → GET de sessão, em vez do fan-out atual por carta.
- Publicar todas as seções obrigatórias atomicamente. Detalhes opcionais ficam
  separados, etiquetados pela mesma revisão, sem bloquear o input normal.
- Inspeção selecionada usa cache exato e cancellation/epoch por intenção; resposta
  atrasada de outra carta não sobrescreve o tooltip atual.
- Não fazer request de preview em cada frame de hover; coalescer a mesma intenção.
- Busca de mapa/conteúdo só acontece quando necessária e pode reutilizar sua versão.
- Após migrar e validar, remover do caminho interativo o refresh antigo, sem
  manter dois sistemas parcialmente autoritários ou fallback silencioso.
- Recuperação de POST incerto consulta o recibo pelo mesmo commandId antes de
  reenviar. Não substituir a intenção nem gerar novo ID automaticamente.

**Testes e aceite**

- Dois requests principais por ação no fluxo saudável; inspeção sob demanda é separada.
- Rejeição, desconexão, recibo recebido com refresh falho, retry idempotente,
  navegação, pause/manual/skip e troca de host/setting/branch.
- Sem impactos otimistas, cliques duplicados, seleção herdada ou faces piscando.
- Logs e replay continuam consumindo a mesma resolução canônica.

### Etapa 9 — Reduzir cópias e trabalho visual sem expor mutabilidade

**Mudanças**

- Medir `duplicate(true)`, indexação e reconstrução de presenters sobre os novos
  payloads antes de alterar as fronteiras defensivas.
- Capturar uma cópia privada por revisão e criar índices derivados por instância,
  ator e zona. Evitar copiar a mesma seção repetidamente na mesma atualização.
- Não devolver Dictionaries internos mutáveis. Manter cópia na fronteira pública
  ou introduzir um acesso realmente somente leitura com testes de isolamento.
- Atualizar componentes por chave semântica; mudanças de diagnóstico/versão não
  podem reconstruir cartas com a mesma face ou reiniciar animação.
- Estado visual transitório permanece separado do snapshot de gameplay.

**Testes e aceite**

- Modificar objetos entregues a uma view não altera sessão, envelope ou outro consumidor.
- Medir parse, publicação e frame time separadamente de rede.
- Mão, fan, tooltip, coleções, mapa e controles mantêm posição e identidade.
- Não trocar identidade visual de carta por reaproveitamento indevido entre instâncias.

### Etapa 10 — Certificação, durabilidade e documentação

**Mudanças**

- Comparar ponta a ponta antes/depois em build Release, hardware e fixtures iguais.
- Investigar persistência somente se o profiling indicar custo relevante; preservar
  flush durável, append atômico, hashes, ordem e validação de commits.
- Não substituir persistência real por store em memória nos números divulgados
  como desempenho do jogo. Benchmark puro deve ser identificado separadamente.
- Readiness não deve enumerar o histórico como parte de cada ação. Separar liveness,
  prontidão e diagnóstico de inventário; otimizar readiness após medir suas dependências.
- Atualizar OpenAPI, exemplos REST, arquitetura Godot, testes e relatório de desempenho.
- Marcar medições antigas como históricas, sem substituí-las por promessa universal.

**Testes e aceite**

- Suites Core, API, integração Godot, transporte e replay.
- Encerrar/reabrir processo, reconstruir estado, repetir commandId e verificar branches.
- Comparação diferencial de estado e fatos de gameplay após cada comando, não
  somente de vitória/derrota no final da run.
- Nenhuma migração de engineVersion apenas por otimizar transporte/DTO/cache.
  Se a equivalência determinística falhar, corrigir a otimização antes de avançar.
- Documentar limites e cenários ainda acima do orçamento.

## 6. Matriz mínima de cenários

- Run curta, intermediária e longa; mesma cena atual com quantidades diferentes
  de encontros anteriores para detectar dependência indevida do histórico.
- Primeiro cálculo de cada nova revisão, cache quente, frio, desativado e sob evicção.
- Mão pequena, típica e no limite do setting; limites são os configurados no jogo,
  não cortes introduzidos pelo benchmark.
- Um e vários inimigos; efeitos simples e combinações de procs/status/condensação.
- Atacar, defender, comprar, exilar, encerrar turno e concluir encounter.
- Recompensa, crafting/modifier, loja, diálogo e retorno ao mapa.
- Normal, experimental, sandbox, dev/modder; hot reload permitido/proibido.
- Branch, seleção histórica, replay, perda de conexão e concorrência entre clientes.
- 30/60/144 FPS e animação normal/reduzida/instantânea; I/O não pode depender
  materialmente da duração de uma animação.

Para percentis de referência, usar pelo menos 100 ações/consultas relevantes
distribuídas em múltiplas seeds e estados. Identificar as amostras de startup e
de compilação inicial separadamente, sem excluir versões inéditas normais do jogo.

## 7. Orçamentos de referência

Metas de engenharia para a demo local e matriz de conteúdo publicada, a confirmar
pela etapa 1; não limites arbitrários de mecânicas ou garantias para mods ilimitados:

| Medida | Meta inicial p95 |
| --- | --- |
| POST de carta, com commit durável | até 250 ms |
| POST de fim de turno, com ações automáticas | até 500 ms |
| Sessão compacta em revisão inédita | até 150 ms |
| Preview contextual selecionado | até 150 ms |
| Clique até sincronização interativa, sem duração artística | até 500 ms na ação típica; abaixo de 1 s na matriz de referência |

Registrar máximo junto dos percentis; nenhum outlier recorrente deve ser escondido.
Uma meta excedida abre diagnóstico, não autoriza cortar efeitos, alvos, traces ou
durabilidade. Animações e confirmações manuais são medidas separadamente.

O payload interativo deve escalar com os dados visíveis atuais, não com o número
de encounters anteriores. Publicar bytes reais por cenário em vez de impor um
limite global que eliminaria conteúdo válido.

## 8. Ordem e pontos de aprovação

1. Etapas 1–2: comprovar e reduzir a penalidade do transporte sem mudar contratos.
2. Etapas 3–4: ter um contrato compacto consistente e testado no servidor.
3. Etapas 5–7: reduzir trabalho derivado, mantendo equivalência diferencial.
4. Etapas 8–9: migrar o cliente e remover o caminho interativo redundante.
5. Etapa 10: certificar, medir e documentar antes de considerar a entrega concluída.

Se uma etapa não passar nos testes de equivalência, não construir as seguintes
sobre ela. Cada relatório deve separar tempo do servidor, transporte, parse,
sincronização e apresentação. Uma melhora de percepção não substitui a melhora
da latência real, e vice-versa.

## 9. Referências

- Auditoria local de 2026-10-14 e teste `CombatPreviewLatencyTests`.
- [Correção anterior de espera das cartas](ACCEPTED_CARD_LATENCY_CORRECTION.md).
- [Arquitetura atual do cliente](../../examples/godot-engine-showcase/ARCHITECTURE.md).
- [Godot HTTPRequest](https://docs.godotengine.org/en/stable/classes/class_httprequest.html):
  opção de threads e proibição de requests simultâneos no mesmo node.
- [Godot HTTPClient](https://docs.godotengine.org/en/stable/classes/class_httpclient.html):
  reutilização do cliente e controle da conexão.
