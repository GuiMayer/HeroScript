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