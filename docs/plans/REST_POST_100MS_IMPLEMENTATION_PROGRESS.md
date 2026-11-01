# Implementação do plano POST 100 ms

Guia: [plano aprovado](REST_POST_100MS_OPTIMIZATION_PLAN.md). Base `4a4a9c95`, EngineVersion 34.

O checkout contém 425 entradas locais preexistentes. Staging/exportação usam apenas os deltas desta tarefa. Saves, API e Godot do jogador não são usados nos testes.

## Etapa 1 — baseline e harness

O benchmark de Kestrel usa atores configurados, lê o corpo completo e mantém o append durável. Agora publica bytes e p50/p95/p99/máximo, aceita `HEROSCRIPT_POST_SAMPLES` e tem gate separado `HEROSCRIPT_POST_BUDGET_MS`. Valores de 1000 ms do gate antigo não são usados como aceite deste plano. A amostra padrão continua curta para regressão; percentis com três amostras são apenas diagnósticos, sem confiança estatística. Jornadas devem gerar sequências válidas, não repetir END_TURN após morte.

Baseline anterior: Default POST 546–666 ms e Ascendant 376–413 ms. Nenhum alvo de 100 ms é anunciado como atingido. Etapas 2–13 pendentes.

Baseline nova em `.runtime/post100-01`: três testes API aprovados (inventário e dois settings), Release/Kestrel/store novo.

- POST_SUMMARY setting=default; n=3; p50=764.54; p95=1166.63; p99=1166.63; max=1166.63ms
- POST_SUMMARY setting=ascendant; n=3; p50=375.66; p95=580.59; p99=580.59; max=580.59ms

## Etapa 2 — medição exclusiva

Implementados spans inclusivos/exclusivos/contagem, contador limitado a 128 nomes e métricas de bytes/hits/misses/evicções. Header de diagnóstico X-HeroScript-Work existe somente com Performance:DiagnosticsEnabled; não participa de estado/recibo/hashes. 21 testes Core focados e dois testes REST passaram. Perfil isolado em stage02-profile.log: cerca de 2143 serializações DOM, 1940 leituras de conteúdo e 56 coletas em um END_TURN; digest ~11ms, encoding exclusivo ~334ms. A coleta cria CardContentCompiler e resolve o baralho repetidamente, sendo o alvo principal após as provas de compatibilidade. Sem garantia de 100ms nesta etapa. Os tempos incluem overhead explícito de diagnóstico, não são um benchmark com observabilidade desligada.

## Etapa 3 — oráculo congelado

O transcript JSON exato foi capturado do binário anterior às otimizações (4a4a9c95) e salvo em tests/API.Tests/Fixtures/post100-command-oracle.json. O teste entre processos agora compara também esse valor fixo, além de comparar três processos entre si: inclui 18 hashes de recibos/commits, criticalHash e finalHash, dois settings e replay. Build sem warnings/erros; teste com três processos aprovado em 71s. O serializer de referência e as provas de bytes/formatos inválidos permanecem independentes no Core; não atualizar o fixture para acomodar divergências de otimização.

## Etapa 4 — reuso dentro do comando

O gateway já compartilha escopo e os hashes de snapshots idênticos; não foi criado outro contexto paralelo. O perfil mostrou 205 misses/77 evicções com somente ~446KB de folhas codificadas. Ajustado o limite de entradas de 128 para 512 mantendo o teto de 8MiB e descarte ao terminar a operação, evitando expulsão prematura por quantidade. 20 testes de canonicalização/diagnóstico aprovados, incluindo bytes e evicção. O ganho de latência será medido junto à próxima composição; não é alegação de 100ms nem remoção de campos/hashes.

## Etapa 5 — compartilhamento estrutural

Encoder existente ampliado para atores, ResourceSet/ResourcePool e dicionários de atores/pools/componentes dentro de agregados já imutáveis. Componentes polimórficos mantêm o serializer de referência e discriminadores; não foi adicionado marcador global a tipos potencialmente extensíveis. Reuso é por identidade/tipo declarado e limitado ao orçamento operacional. 22 testes focados passaram; regressão nova confere bytes e SHA-256 independentes em 100 alterações de um único ator, preservando componentes compartilhados. Formato canônico não mudou.

## Etapa 6 — identidades compatíveis

ExecutionId compõe os mesmos 15 campos/bytes, reaproveitando os fragmentos do encoder existente; IDs de proc/impacto e agrupamento dos sorteios não foram simplificados. Nova prova compara a construção antiga do objeto embutido com a composição em 20 variantes de revisão/tags/alvos. 47 testes focados de identidade, origens, dispatcher e distribuição aprovaram; EngineVersion preservada.

## Etapa 7 — materialização de trace

O perfil não justificou adicionar um segundo protocolo lazy: hashes também alimentam fingerprints/identidades e o contrato exige o trace completo antes do append. Mantidos todos os campos e consumidores; o executor congela steps/records/calculations uma vez no retorno e reutiliza o mesmo array no fingerprint, eliminando cópia duplicada. 22 testes de transação/raízes/respostas passaram. Não houve descarte de diagnósticos, confirmação antecipada ou mudança do formato durável.

## Etapa 8 — composição de observadores

Coletas reutilizam os compiladores e resolvedores limitados já registrados, com isolamento por catálogo para catálogos personalizados. Catálogos internos possuem cache limitado e invalidável; decisões e quotas continuam avaliadas no estado atual. 253 testes focados, uma regressão de hits/invalidação e dois testes REST aprovados. Diagnóstico ligado: Default p50 225,43/max 448,91ms; Ascendant p50 231,94/max 294,09ms (n=3). Em um END_TURN, leituras de conteúdo caíram de 1940 para 69 e coleta para ~7ms. Meta de 100ms ainda pendente.

## Etapa 9 — execução selecionada

Auditoria confirma Evaluate/EvaluateObserved para o comando escolhido, sem resolver toda a mão no POST. Adicionada regressão VerifyNoOtherCalls para jogador e controlador automático: 14 testes aprovados. Preservados isolamento causal de EvaluateObserved, opções e decisões de IA; não foi adicionado executor ou cache de decisões duplicado.
