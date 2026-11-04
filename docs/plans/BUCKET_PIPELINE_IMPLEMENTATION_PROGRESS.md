# Execução — pipeline de builds

Plano: [organização e MVP](BUCKET_PIPELINE_BUILD_ORGANIZATION_AND_MVP_PLAN.md).

## Etapa 0 — baseline parcial

O plano foi versionado no commit `53b101c8`. O checkout principal já continha alterações não commitadas em engine, conteúdo, Godot e documentação; elas não foram incluídas no commit.

Execução local com `dotnet test --no-restore` no Core.Tests, filtrando `Core.Tests.Calculations`, `MultiTierCriticalTests`, `CondensationTests` e `InscriptionCalculationTests`: **139 testes passaram**, sem falhas. A execução usou o estado de trabalho local, incluindo alterações preexistentes, e não certifica um checkout limpo.

Foram localizados arquivos de pipeline nos settings `default`, `ascendant`, `volatile-core` e `prototype-test`. A lista de arquivos por si só não certifica precedência, revisão, resultado numérico ou cobertura de todos os demais settings.

Ainda faltam inventário completo de bindings/fórmulas/preferência, corpus reproduzível com hashes e medições de desempenho. Portanto a etapa 0 não está concluída; as etapas 1–12 ainda não foram implementadas nem certificadas.

## Continuação da etapa 0

`CalculationResolver.ResolvePipeline` usa primeiro o ID explícito do efeito; sem ele usa o padrão definido pelo modo para o canal; sem ambos, exige exatamente uma pipeline habilitada compatível. IDs explícitos devem estar habilitados e usar o mesmo canal.

O teste `BucketGroupingBaselineTests` comprova 145 para aumentos de 20% e 25% no mesmo bucket e 150 para buckets consecutivos, repetindo o cálculo e comparando fingerprints e entrada. Os dois casos passaram no checkout local. A etapa 0 continua aberta: ainda são necessários o inventário completo, corpus de ponta a ponta e medições. Nenhuma das etapas 1–12 está certificada.

## Inventário da etapa 0

Adicionado `docs/validation/bucket-pipeline-baseline-inventory.json`: 29 definições brutas em 9 arquivos de `default`, `ascendant`, `volatile-core` e `prototype-test`, com SHA-256 do arquivo, buckets, fórmulas declaradas e bindings. Os arquivos de pipeline não apresentam alterações locais no momento da captura. O inventário não resolve herança entre settings nem constitui manifesto de conteúdo publicado.

A referência histórica de desempenho em `docs/validation/TACTICAL_CAMPAIGN_MVP.md` registra PLAY_CARD p95 83,54 ms (65 amostras) e END_TURN p95 470,11 ms (50 amostras), em execução anterior. Esses valores não são uma nova medição desta baseline. Continuam pendentes o inventário de fórmulas/referências de todos os efeitos e modos, corpus end-to-end com hashes e medições atuais; a etapa 0 permanece aberta.

## Medição local do redutor

Criado `tools/CalculationBenchmark`, com entradas JSON em `tests/Fixtures/calculation-baseline/grouping.json`. A execução Release em .NET 10.0.9 passou, com 20 warmups e 200 amostras por caso, verificação de fingerprints e preservação dos inputs. Valores 145 e 150 confirmados. P95 local: 0,25 ms no grupo único e 0,6057 ms nos grupos separados. Resultado registrado em `docs/validation/bucket-pipeline-reducer-baseline.json`.

A medição inclui trace/hash do redutor e exclui resolver, REST, persistência e interface. Não representa latência de POST e não fecha sozinha o gate da etapa 0. Os demais itens da baseline continuam abertos até evidência própria.

## Integração de cálculo do prototype-test

Cinco casos API em Release passaram: `PublishedCriticalTiersUseLinearBonusAndDoNotReuseAStaleSameStepActorQuote` e os quatro casos `PublishedHeatCaptureAndWetReleaseUseOnePipelineWithoutRescalingStoredPotency` (Calor 0, 60, 85, 100). A suíte verifica crítico linear, fingerprints repetidos, preservação do estado autoritativo e condensação sem reaplicar a potência capturada. Duração reportada: 1 min 19 s. Esses testes usam TestWebApplicationFactory e não medem latência por sockets de produção.

A build inicial falhou porque a API aberta bloqueava DLLs de Release. A repetição usou `--artifacts-path .runtime/bucket-pipeline-build`, sem encerrar o servidor. Não declarar importação/build original limpos por causa dessa repetição isolada. Etapa 0 ainda pendente de corpus amplo com restart/branches e medição atual de comandos completos; etapas 1–12 não implementadas nesta continuação.

## Baseline de integração executada

Passaram os testes PublishedChapterCompletesWithBossRetryDurableDialogueAndNoCheats e TransformedCardsPersistAndBranchesDivergeWithoutEditingTheParent, em Release/artifacts isolados. A jornada completa verificou retry, persistência, restart/replay e 161 hashes sucessores iguais em armazenamento independente. Os POSTs foram medidos por sockets Kestrel. Resultados atuais e contexto em `docs/validation/bucket-pipeline-integration-baseline.json`: PLAY_CARD p95 236,01 ms, END_TURN p95 1093,15 ms. O servidor local permaneceu aberto e houve concorrência; esses números não são comparáveis diretamente aos históricos como prova de regressão. O gate de desempenho não está aprovado.

A ferramenta `tools/inventory-calculations.py` capturou 237 arquivos JSON e 362 referências de fórmulas/seleção de pipeline com caminhos, JSON pointers e SHA de cada fonte; inventário completo local em `.runtime/bucket-pipeline-results/authored-references.json`. Essa parte é inventário de autoria, não resolução de herança de todos os settings. A jornada valida conteúdo publicado e herdado do prototype-test; o teste de branches valida volatile-core. Permanecem fora da certificação runtime os demais settings. Não declarar etapa 0 integralmente concluída antes de fechar essa cobertura e a medição específica de resolver/previews.

## Catálogo resolvido em runtime

Passou CalculationCatalogBaselineTests em Release isolado: dez settings publicados, 552 vínculos de pipelines habilitadas/defaults verificados em revisões fixadas, incluindo herança resolvida. Resultado em `docs/validation/bucket-pipeline-catalog-baseline.json`. A contagem representa vínculos entre modos e pipelines, não 552 definições únicas. O teste valida existência, definição numérica e correspondência de canal de defaults; não prova que todo efeito executável usa essas definições corretamente.

O contrato editorial da etapa 1 está preparado em `docs/content/calculation-build-groups.md`, ainda não commitado como etapa concluída. A etapa 0 ainda requer medição específica de resolver/previews para fechar todo o aceite originalmente descrito. As etapas 1–12 não estão concluídas.

## Continuação (2026-11-03) — resolver, etapas 1–3

- **Resolver/preview (fecha a medição pendente da etapa 0):** `ResolverAndCapturePreviewPreserveRunAndRepeatFingerprints` (prototype-test, Release, TestWebApplicationFactory, 20 warmups + 100 amostras) passou. Resolver completo: p50 0,1387 ms, p95 0,6691 ms, p99 2,3029 ms. Captura de preview: p50 0,1604 ms, p95 0,8135 ms, p99 1,9068 ms. Fingerprints repetidos iguais e hash da run inalterado. Medição in-process, sem REST/persistência, local e com servidor aberto; não é gate de latência de POST.
- **Etapa 1 (commit `d0cd553d`):** contrato editorial em `docs/content/calculation-build-groups.md`. Fonte, operação e grupo de apresentação são dimensões independentes; nenhum comportamento numérico depende do rótulo.
- **Etapa 2 (commit `3c6ec23e`):** `CalculationAuthoringDiagnostics` emite avisos não bloqueantes `CALC_PERCENT_GROUPS` (AddPercent consecutivos compõem) e `CALC_INTERMEDIATE_ROUNDING`; integrado como warnings no `ContentGraphValidator`. Não altera política, validação de definição nem fingerprint (teste compara hash antes/depois). Nenhuma operação ou campo de schema novo. Não foi executada a suíte API completa nesta etapa; a validação de referências existentes já é feita por `CalculationEngine.ValidateDefinition` e pelo catálogo (552 vínculos).
- **Etapa 3 (auditoria):** `ActorAttributeProjectionService` usa o mesmo `InscriptionCalculationInfluenceProvider` através de `CalculationResolver`; não há segunda rota numérica, portanto não existe contagem dupla entre projeção e influência. Novo teste `TwoEquippedItemsContributeOnceEachWithStableOrderAndProjectionDoesNotMutateTheRun`: dois itens equipados geram duas influências com SourceId distintos (Inscription), permutar slots mantém conjunto e valor efetivo (50), e a projeção não altera o hash da run. Suíte Core filtrada (Calculations + Inscription*): 111 passaram. Companion contribui apenas por componentes/status/atributos genéricos; não foi encontrado provider específico, mas não houve auditoria completa dessa parte.
- **Pendentes:** etapas 4–12. A etapa 0 ainda carece de restart/branches fora de prototype-test e volatile-core. O gate de 100 ms do turno continua aberto.

## Etapa 4 — auditoria de escalamento e crítico (sem mudança de conteúdo)

- **Resultado:** nenhuma alteração em JSON ou engine. A matriz de aceite já está coberta por testes existentes; 29 testes de `MultiTierCriticalTests` e `GlobalEffectResolutionPolicyTests` passaram nesta execução (Release, artifacts isolados).
- **Cobertura confirmada por nome/dados de teste:** chance 0/50/100/150/200 com probabilidade e tiers garantidos; bônus linear com tiers 0–4 (×1…×5) e bônus configurável (1, 0,5, 1,5); prioridade da proibição por carta (opt-out) sem alterar definições; pagamentos, residuais e canais não correspondentes não adquirem regras críticas; políticas ambíguas e overrides inválidos falham fechados; multihit sorteia por impacto após distribuir um orçamento; filho não configurado não herda o multiplicador. A matriz 0/15/99/100/150/200/250/300% está em `MultiTierCriticalProjectionTests` (API) e a inspeção publica alternativas locais sem alterar o estado; esse teste de API não foi reexecutado agora.
- **Decisões deixadas em aberto de propósito:** Calor continua um bucket `heat` separado do `increased` (fator composto, 100 × 1,20 × 1,25 = 150). Convertê-lo para o mesmo grupo (145) ou para limiares mudaria gameplay/hashes e exige decisão de design e nova revisão (etapa 10). Não foram removidas repetições de pipeline por default do modo: não há prova de equivalência por efeito.
- **Não verificado:** RNG ordenado entre fontes diferentes em jornada completa e ausência de consumo de RNG no preview além do teste de inspeção citado.
## Etapa 5 — Transformações e execução estrutural

O ledger recompõe componentes por identidade e ordem. O orçamento de um efeito dividido permanece no plano de execução. Condensação seleciona os stacks, resolve um único proc e aplica consumo e componentes na mesma transação. Pagamentos e movimentação de cartas seguem os fluxos canônicos.

Os testes CardTransformationTests, CardTransformationCompositionTests e CondensationTests passaram na execução de 230 casos. Cobrem retry idempotente, snapshots, referências inválidas, falha no último componente e repetição da receita sem consumir stacks novos.


## Etapa 6 — Quantidades e captura

O valor base entra no resolver sem receipt. Captura e distribuição transportam valor, unidade, revisão e estágios já incorporados. A agregação combina quantidades compatíveis. O alvo novo resolve os estágios próprios sobre cada parcela; o estágio de origem já incorporado permanece aplicado uma única vez.

CalculationDistributionTests e CondensationTests passaram na mesma execução. Cobrem conservação exata, isolamento por alvo, capacidade defensiva viva, revisão incompatível e avaliação configurável no início da ação ou na execução.


## Etapa 7 — Condições e defesa

Calor entra por binding de recurso e tag, Molhado entra na liberação configurada e armor precede o consumo da capacidade defensiva. A autoridade de aplicação é o settlement; o redutor trabalha com números resolvidos.

CalculationSettlementTests e os testes de distribuição passaram na mesma execução. O teste API de Calor/Molhado da etapa 0 cobriu quatro entradas, mas a matriz completa de bordas, Sleep e cooldown ainda exige certificação. O checkout tem uma alteração preexistente que move Calor para `increased`; ela fica fora deste commit.


## Etapas 8 e 9 — apresentação e revisão de conteúdo

A Godot apresenta mudanças de valor a partir de entradas e saídas publicadas pelo trace, em ordem. Buckets sem alteração são omitidos. O teste headless de apresentação passou, incluindo os cenários existentes em EN/PT. A cobertura adicionada usa trace de origem; a exibição de etapas por impacto e alvo continua pendente.

No prototype-test, cada participante definido no setting tem 3 de energia. O fluxo repõe o máximo na ativação. As 12 definições de carta gastam 1 de energia e conservam seus pagamentos de zona, condições e efeitos térmicos. Antes, essas definições tinham custo de recurso vazio. O teste REST verificou energia inicial de ambos os lados, gasto e reposição, além de retenção e ativação do companion. Os gambits de inimigo ainda usam suas regras próprias; o custo de energia das cartas não converte gambits em cartas. Escudo passivo por atributo e limiares de Calor não foram implantados nesta mudança.


## Etapa 10 — compatibilidade de revisão

A alteração da etapa 9 está limitada aos JSONs de autoria do prototype-test e produz uma nova revisão na publicação. A ativação em runs existentes segue o fluxo de revisão da engine. A suíte de reload, runtime, manifestos, atributos, crítico e inscrições passou com 38 casos; o teste REST PackageSetting_ValidatesPublishesAndDoesNotRebindActiveRun passou. A jornada de branch do volatile-core passou na certificação anterior à alteração de energia. O teste de migração de uma run real do prototype-test entre as duas revisões específicas permanece pendente.


## Etapa 11 — certificação do capítulo com energia

O teste de capítulo passou com persistência isolada por sockets Kestrel, em Release, .NET 10.0.9, Windows 10.0.26200, 12 processadores lógicos. A jornada concluiu o capítulo após perder e repetir o chefe, verificou restart e replay e comparou 170 hashes sucessores em stores independentes. Hash inicial 746ccf98649033b84370cf0e30ef66a0ea604047ab613ffe9fe1a2748bb09e6b. Hash final 5effd0b5ad2c9d5050f3a780d6b1af4d91157f17f57f1c50f989524cf6641c71.

| POST | Amostras | p50 | p95 | p99 | p95 sem a primeira |
| --- | ---: | ---: | ---: | ---: | ---: |
| PLAY_CARD | 71 | 57,35 ms | 102,45 ms | 184,81 ms | 102,45 ms |
| END_TURN | 53 | 207,68 ms | 663,16 ms | 1049,27 ms | 663,16 ms |

As amostras percorrem o capítulo, sem fase de aquecimento dedicada. O teste também registra a distribuição sem a primeira amostra de cada comando. O gate de 100 ms não passou. A diferença em relação à baseline não mede uma otimização isolada, pois a revisão, a jornada e a concorrência mudaram. O checkout contém alterações preexistentes e os hashes certificam a revisão usada na execução local.

## Etapa 12 — documentação e pendências

Os guias de autoria, dano e crítico agora distinguem soma no mesmo bucket, composição entre buckets, unidades e defesa. O exemplo JSON do benchmark pode ser reproduzido no checkout.

A passagem pelas etapas 0–12 não certifica todos os critérios de aceite. Permanecem abertos escudo passivo por atributo, payoff por limiares de Calor, matriz completa de Sleep/cooldown, trace por impacto e alvo na Godot, migração específica entre revisões do prototype-test e gate de latência. O teste de capítulo verifica viabilidade técnica, sem equivaler a playtest de equilíbrio.
