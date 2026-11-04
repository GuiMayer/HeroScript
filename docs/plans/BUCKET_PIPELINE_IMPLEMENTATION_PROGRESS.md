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
