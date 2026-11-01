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

## Etapa 10 — frames e preparação do commit

Frames congelados reaproveitam fragmentos canônicos e StateAfter estrutural no fingerprint, sem hash de subhashes. O array publicado é imutável; o encoder só aceita ImmutableArray e tipos auditados. 32 testes focados passaram e o oráculo fixo coincidiu em três processos independentes (69s), incluindo 18 hashes de commits/recibos e replay. Build sem avisos/erros. Mantidos PreparedRunCommit, validação, ByteHash, append atômico e flush durável; não foi alterada a política de checkpoints.

## Etapa 11 — projeções REST

Sessão reutiliza uma projeção por identidade fraca do snapshot confirmado, somente após validar versão e chave de permissões/perfil/schema. Não retém branches abandonadas nem resultados de erro; troca de snapshot gera outra projeção. Sete testes API passaram, incluindo reuso por identidade, avanço de versão e ETag. Contratos e frames dos POSTs permanecem iguais; não foi criado perfil mínimo que obrigue GET complementar. Benchmark aceita HEROSCRIPT_POST_DIAGNOSTICS=false e HEROSCRIPT_POST_PROFILE=default para medir separadamente o contrato padrão e o Prefer existente (combate mantém o mesmo corpo nos dois). Gateway, retry e animações Godot não precisaram mudar.

## Etapa 12 — primeiro uso e alocações

Startup prepara os planos dos agregados auditados, além dos metadados já existentes, sem criar runs/comandos ou consumir RNG. Reutilizado o cache escalar operacional limitado a 128 entradas no mesmo escopo, incluindo valores primitivos e strings curtas; chaves preservam bits de float/double e sinal/escala decimal. Nenhum buffer publicado é devolvido a pool. 46 testes focados passaram, incluindo zero negativo, escala, nullable, escaping, evicção, warmup e hot reload/concorrência disponíveis no filtro. Não foi introduzido paralelismo de efeitos nem alterado o cache histórico das revisões. Certificação e latência final continuam pendentes na etapa 13.

## Etapa 13 — certificação em andamento e correção adicional de recibos

Spans combat_plan/resolution_project/receipt_lookup localizaram uma releitura: RunSessionCoordinator ignorava o recibo já publicado pelo combate e reidratava o histórico. Agora compartilha evidência local confirmada por interface interna; após miss consulta o backend autoritativo, sem cache negativo. Segunda consulta só é eliminada quando os leitores provam compartilhar a mesma instância de backend. 31 regressões de coordenação/persistência e dois testes REST passaram. Receipt lookup caiu de ~46–69ms/4 chamadas para ~1–3ms/2 chamadas. Com diagnósticos: Default p50 184,74/max319,40ms; Ascendant p50 153,54/max292,16ms (n=3). Ainda não atende 100ms. Experimento de leitura síncrona dos índices não trouxe ganho e foi descartado. Adicionada medição TCP opcional à jornada completa e amostragem END_TURN distribuída entre encontros válidos para campanhas longas. Certificação final e amostra estatística do plano permanecem pendentes.

### Complemento estrutural após a certificação inicial

A movimentação de cartas agora decompõe DeckState/topologia/zonas auditadas, preservando o encoder real de chaves Guid e reusando o registro imutável. 100 regressões de determinismo/zonas passaram. Prova adicional acompanha 100 movimentos, compara bytes independentes e exige menos da metade dos bytes de folhas que a recodificação integral do deck. Nenhuma finalidade de zona foi inferida. Comparação de latência e oráculo final serão repetidos antes do aceite.

### Arrays imutáveis compartilhados

O escopo existente agora reconhece ImmutableArray pelo tipo real e pela identidade de armazenamento, em vez da identidade transitória da caixa criada por getters. O orçamento continua 512 entradas/8MiB; arrays mutáveis e entradas públicas sem marcador mantêm o caminho anterior. Novas provas verificam bytes/ordem, cópia com armazenamento distinto, inputs mutáveis e volume recodificado. Core completo: 2.221 aprovados; rodada final focada: 35; oráculo fixo em três processos aprovado novamente. Novo lote de 1.000 POSTs por setting será comparado à campanha c646b3d9 antes de declarar ganho de latência.

### Resultado do experimento com arrays — descartado

Campanha c646b3d9: 1.000 END_TURN por setting, TCP/Release/corpo completo/flush, sem diagnósticos ou suítes concorrentes. Default: p50 54,73/p95 100,91/p99 131,00/max 286,82ms, 54 violações. Ascendant: p50 74,08/p95 166,57/p99 220,28/max 388,34ms, 178 violações. Nenhuma falha funcional antes do gate.

Campanha 775c87b4 com as mesmas seeds/comandos: Default p50 57,87/p95 112,56/p99 152,67/max 302,50ms, 83 violações; Ascendant p50 74,79/p95 182,41/p99 242,39/max 448,57ms, 168 violações. O experimento reduziu recodificação sintética, mas não demonstrou ganho de latência real; foi removido junto com seus testes específicos. Os dois arquivos do encoder voltaram exatamente aos blobs certificados em c646b3d9. Ficam os 2.219 testes Core do caminho mantido, 58 API selecionados, seis verificações finais de oráculo/inventário, dez jornadas determinísticas e seis scripts Godot. O resultado de 2.221 testes refere-se apenas à variante experimental.

Estado: mudanças das etapas 1–11 implementadas e verificadas; etapa 12 tem aquecimento de metadados/planos e cache escalar, mas ainda requer completar aquecimento puro de conteúdo/assinaturas e aceites de memória/concorrência da matriz. Etapa 13 parcial: correção aprovada nos cenários executados, gate de 100ms reprovado; amostragem ampla dos outros comandos/ferramentas ainda pendente. Não declarar o plano ou a meta concluídos.
