# Implementação da correção da latência REST

Plano: [REST_INTERACTIVE_LATENCY_CORRECTION_PLAN.md](REST_INTERACTIVE_LATENCY_CORRECTION_PLAN.md).

## Base preservada

`bdb1df1`: correções anteriores de ciclo de cartas, seleção e tooltip; plano aprovado.

## Etapa 1 — instrumentação

Diagnóstico opt-in (`Performance:DiagnosticsEnabled`), tempos operacionais por
request, header Server-Timing até envio dos headers e duração completa no logger.
Spans inclusivos de hash, legalidade, inspeção e append; não somar spans aninhados.
Nenhum tempo é persistido em fatos ou hashes. Probe Godot somente leitura em
`tests/rest_latency_probe.gd`, com FPS, host e run opcionais via argumentos.

Baseline da auditoria: GET de 496.341 bytes em 23–33 ms fora da Godot, 1.739–1.766
ms no cliente a 60 FPS. TestServer Release: descoberta inédita 915,8 ms; reuso
36,4 ms; inspeção 385,2/34,6 ms. Amostras pequenas, não p95.

Validação: teste de isolamento de spans e equivalência do hash; teste HTTP de
corpo idêntico com diagnóstico ligado/desligado. Ambos passaram.

## Etapa 2 — transporte

Workers HTTPRequest em threads, pool limitado (4), fila com prioridade para
comandos e histórico em baixa prioridade. GETs idênticos em voo compartilham I/O,
mas recebem cópias privadas e cancelamento independente. POSTs não são repetidos.
Cancelamento nativo é agendado fora da pilha de sinais para não bloquear/juntar
uma thread reentrantemente. Epochs continuam descartando respostas de outro host.
Reutilização de nodes não é anunciada como prova de keep-alive TCP; isso permanece
dependente do comportamento nativo do HTTPRequest e da medição do probe.

Testes de loopback: cancelamento antes/durante I/O, consumidores compartilhados,
troca de host, isolamento das respostas e retorno do worker ao pool.

## Etapa 3 — projeção compacta

Contexto de leitura captura uma referência imutável e fingerprint completo,
incluindo identidade/versão. O contrato de UI não serializa encounters passados,
manifesto ou regras resolvidas. No combate, envia apenas o nó atual; no mapa,
envia as rotas necessárias. Atividades de seleção/loja/preparação são filtradas
pelo nó atual. Histórico de diálogo foi preservado para o menu de conversas.
Fluxos de ferramentas são opcionais e filtrados. Teste comprova ausência de
crescimento com 100 encounters passados e preservação do hash original.

## Etapa 4 — sessão consistente

`GET /api/v1/runs/{id}/session?expectedSequence=N` captura uma run e usa overloads
de projeção por snapshot, sem recarregar por serviço. ETag inclui estado completo,
schema e capacidades; 304 só para a mesma representação. Revisão incompatível
retorna 409. O POST não foi ampliado e continua confirmando somente após o commit.
Teste REST isolado cobre mapa, combate, ETag/304, conflitos e igualdade do estado
canônico antes/depois. Nenhuma sequência histórica é apresentada como atual.

## Etapa 5 — face e impacto separados

Sessão usa o projetor canônico de valores de origem para as cartas jogáveis,
sem chamar a inspeção completa contra um alvo arbitrário. Fatos de oferta continuam
na base composta. Sanitização por nível de detalhe é compartilhada com a inspeção
existente. O bundle não carrega árvores de execução nem containers compilados da
mão inteira; inspeções detalhadas continuam disponíveis pela mesma REST API.
Teste de sessão verifica valores Source da revisão correta, sem impacts ou
inspeções completas na sincronização obrigatória. Testes de cache/inspeção e
equivalência imutável passaram com conteúdo real do mundo 1.

## Etapa 6 — descoberta conservadora e exata

A descoberta reutiliza resoluções cuja composição e avaliação completa são iguais
no mesmo snapshot (por exemplo, várias seleções brutas para SELF). O memo existe
somente durante a descoberta e é usado apenas pelo executor canônico conhecido;
executores customizados seguem pelo caminho exato anterior. Condições continuam
avaliadas para cada entrada bruta antes de qualquer reuso. Candidatos, IDs,
custos, fingerprints e sucessores permanecem iguais. A sessão não serializa
traces completos de cada candidato; preserva comandos, custos e aplicações
necessários ao preview. O endpoint detalhado continua disponível.

Decisão de segurança: não substituir a legalidade por um teste apenas estrutural.
Uma resolução distinta ainda usa o pipeline completo, pois efeitos podem falhar
durante a execução. Essa é a alternativa conservadora prevista no plano, sem
falsos positivos ou um segundo processador de regras.

Validação: 25 testes Core de legalidade/cartas e comparação diferencial em duas
seeds reais do mundo 1, incluindo todos os sucessores e imutabilidade da run.

## Etapa 7 — caches derivados e hashes

Compilação (256 entradas) e composição (128) usam LRU existente e single-flight
por stripes. Runtime é uma identidade imutável, não apenas o rótulo da revisão;
composição inclui definição e instância completas. Ambos registrados no
coordenador genérico, com estatísticas e invalidação. Falhas não são cacheadas.
Não há pré-execução de ações em background nem retenção de sucessores adicionais.

Hash padrão de agregados explicitamente imutáveis usa chaves fracas por instância
e tipo declarado, sem manter runs vivas. Serializadores customizados continuam
com memoização por escopo; objetos mutáveis não recebem essa otimização. Bytes e
algoritmo canônicos não mudaram. O contexto agregado compartilha os hashes.

Validação: 1.734 testes Core passaram, incluindo concorrência, invalidação,
runtime diferente com mesmo rótulo e composição com upgrades; três testes REST
de sessão e equivalência de descoberta passaram. Versão determinística permanece 31.

## Etapa 8 — migração do cliente

GameSession faz uma leitura de sessão por refresh, valida identidade, fingerprint,
setting, revisão e steps e publica atomicamente. Fan-out antigo foi removido do
gateway de produção. Conflito após recibo faz uma leitura explícita do novo head,
sem reenviar a ação nem apresentar o head como resultado antigo. POST de run usa
`Prefer: return=minimal`; o mesmo executor/commit confirma um recibo sem serializar
o agregado inteiro. Coleções carregam fatos completos sob demanda e por revisão.

Fixtures antigos foram adaptados somente em `tests/session_fixture_adapter.gd`;
não há fallback para o refresh antigo em produção. Testes da sessão compacta,
animação, supersessão, seleção, estabilidade de faces e transporte passaram.
Inspeção contextual continua no coordenador existente, com cache/epoch limitado.
