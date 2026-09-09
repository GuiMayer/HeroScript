# Verificação do runtime determinístico unificado

**Status:** gate arquitetural ativo
**Atualizado em:** 2026-09-09

Este documento registra como a HeroScript prova que run, combate, conteúdo,
timeline, replay e branches usam uma única arquitetura. Ele é um gate de
regressão, não uma declaração baseada apenas em convenção.

## Invariante de aceite

Para o mesmo `settingId`, `contentRevision`, seed e sequência de comandos, a
engine deve produzir os mesmos bytes de commit e os mesmos estados, cursores de
RNG, IDs, intents, frames, facts, hashes e fingerprints. A execução não pode
depender de estado retido pelo processo, arquivos mutáveis de autoria,
configuração de regra em `appsettings` ou uma segunda implementação do fluxo.

O runtime autoritativo é criado exclusivamente por `GameplayRuntimeFactory`. As
variantes live, replay e simulação compartilham codec, handlers, reducers e
resolução de conteúdo; variam apenas o store e os sinks operacionais.

## Matriz de provas

| Risco | Prova automatizada | Evidência comparada |
| --- | --- | --- |
| Estado ambiental entre execuções | `IdenticalCompleteSandboxFlow_IsBitwiseStableAcrossTenFreshRuntimes` | dez hosts novos; IDs, contexto determinístico, intents, snapshot/recibo JSON, commits REST e bytes exatos dos arquivos de commit |
| Replay apenas estrutural | o mesmo cenário chama `/runs/{id}/verify` em toda iteração | reexecução semântica, sequência, step e hash final |
| Reinício com estado transitório | `Restart_PreservesActiveCombatOpenStackBranchHistoryAndReplay` | run e encounter ativos, janela de prioridade, stack aberta, branch, índice de linhagem e continuação após novo host |
| Retry após reinício | `RetryAfterCoordinatorRestart_ReturnsDurableReceipt` e `CommandGateway_DeduplicatesFromDurableCommitAfterRestart` | receipt e commit duráveis, sem segunda transição |
| Perda de cache | `InvalidateAll_UsesDependencyOrder_AndPreservesRevisionedCaches` | invalidação ordenada sem apagar conteúdo fixado |
| Perda de projeção | `Rebuild_IndexesAncestryOnceAndServesTreeQueriesWithoutRunScans` e o teste de reinício da API | árvore reconstruída somente dos commits e linhagem |
| Recompilação de conteúdo | `BaseGame_IsARebuildableValidPackageSetting` | mesma revisão após novo compiler/provider |
| Commit corrompido | `Load_CorruptStateBytesFailFastAfterRestart` | leitura recusa divergência entre `stateAfter` e `stateHash` |
| Conteúdo corrompido | `GetPublished_CorruptArtifactBytesFailFastAfterRestart` | leitura recusa payload cujo hash difere do manifest |
| API ou nome aposentado | `PublicGameplaySurface_ContainsNoRetiredContractNames` | fonte e OpenAPI sem contratos antigos alcançáveis |
| Regra em configuração do host | `ApiAppSettings_ContainOnlyOperationalConfiguration` | somente logging, rede, administração e caminhos de persistência |
| Fallback para arquivo mutável durante a run | `RunDefinitionResolution_HasNoMutableCatalogFallback` | relíquias e upgrades resolvidos apenas pelo `ContentRuntimeResolver` fixado |

Os testes de reinício descartam completamente o host, container de DI e caches
em memória antes de abrir o mesmo store em um host novo. Portanto, uma consulta
ou continuação bem-sucedida após esse ponto também prova que snapshots de API e
índices são projeções reconstruíveis.

## Budgets executáveis

Os limites abaixo impedem trabalho ilimitado sem reordenar ou resumir a
semântica. Todos pertencem ao conteúdo fixado na run, salvo o limite de
transporte indicado.

| Superfície | Budget inicial | Autoridade | Comportamento no limite |
| --- | ---: | --- | --- |
| Stack de reações | 32 ações pendentes | `priority_stack_combat.reactions.maxStackDepth` | rejeita a proposta antes de mutar o estado |
| Timeline | 200 itens por página | `sandbox_timeline.maxItemsPerPage` | exige paginação por `afterSequence` |
| Branches | 50 por raiz | `theorycraft_tools.maxBranchesPerRoot` | recusa a criação da branch excedente |
| Simulação | 250 comandos por request | `theorycraft_tools.maxSimulationCommands` | valida a lista antes de criar/executar trabalho |
| Replay autoritativo | todos os commits retidos | `sandbox_replay.retention = all_commands` | reexecuta em ordem; não amostra nem paraleliza transições |
| Journal/eventos REST | 1–1000 itens por página | contrato de transporte | exige cursor crescente |
| Efeitos | 4096 steps, depth 32, repeat 256 | `EffectExecutionLimits` | falha a transação completa |

O replay não possui corte silencioso: o custo cresce linearmente com os commits
retidos. Quando runs longas exigirem otimização, ela deverá usar âncoras de
verificação criptográfica ou jobs assíncronos sem alterar a ordem canônica. Isso
é uma evolução operacional futura, não permissão para confiar em snapshots.

## Execução do gate

Na raiz do repositório:

```powershell
dotnet build HeroScript.slnx --no-restore
dotnet test tests/Core.Tests/Core.Tests.csproj --no-build
dotnet test tests/API.Tests/API.Tests.csproj --no-build
```

Baseline local de 2026-09-09: build completo aprovado, 1.064 testes de Core e
155 testes de API aprovados, sem skips. Na mesma máquina, as suítes levaram
aproximadamente 3 s e 19 s respectivamente; esses tempos são referência de
desenvolvimento, não um SLA.

Uma mudança que altera regra deve atualizar a revisão/versão aplicável e manter
estas provas verdes. Acelerações podem eliminar cópias, cachear resultados ou
paginar leituras; não podem alterar ordenação de comandos, frames, facts,
influências, alvos ou consumo do contexto determinístico.
