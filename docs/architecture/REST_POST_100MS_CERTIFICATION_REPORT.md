# Certificação — otimização dos POSTs REST

Data: 2026-11-01. Plano: `docs/plans/REST_POST_100MS_OPTIMIZATION_PLAN.md`.

## Estado do aceite

**A meta de todos os POSTs abaixo de 100 ms ainda não está certificada.** As mudanças de implementação das etapas 1–11, o aquecimento de metadados/planos e o cache escalar da etapa 12 estão registrados em commits próprios. Aquecimento completo de conteúdo/assinaturas e aceites de memória/concorrência em toda a matriz ainda estão pendentes na etapa 12. A etapa 13 tem correção verificada nos cenários executados, mas seu aceite de latência não passou. A etapa 13 permanece parcial enquanto existirem classes acima do limite ou cenários sem amostragem suficiente.

A meta mede TCP loopback, Release, corpo completo e append durável. GET de sessão e animações são medidas distintas. Nenhuma confirmação antecipada, descarte de traces, remoção de hashes ou redução de efeitos foi introduzida para reduzir o cronômetro.

## Garantias verificadas

- Fixture anterior às otimizações: `tests/API.Tests/Fixtures/post100-command-oracle.json`, capturado de `4a4a9c95`. Não foi regenerado para acomodar mudanças.
- Três processos independentes coincidiram com o fixture: 18 hashes de recibos/commits, criticalHash e finalHash, além de replay. EngineVersion preservada.
- Encoder mantém JSON canônico embutido, incluindo discriminadores, ordem de arrays, profundidade e representação numérica. Não compõe hashes de subhashes.
- Novos caches são produtos derivados. Permissões e versão são verificadas antes do reuso da sessão. Recibos locais só são reutilizados após confirmação; misses não ficam em cache.
- Append atômico, validação de identidade/linhagem/revisão, ByteHash, checkpoints e flush durável permanecem no caminho de confirmação.

## Evidências já concluídas

| Verificação | Resultado |
|---|---|
| Build completo Release da cópia limpa | 0 erros e 0 avisos |
| Core completo do caminho mantido | 2.219 aprovados |
| API selecionada, incluindo oráculo e contratos | 58 aprovados |
| Regressões adicionais de encoding/topologia | 100 aprovados; prova de volume no encoder aprovada |
| Godot: eventos, replay progressivo, lifecycle, transporte, animações e pool HTTP | 6 scripts aprovados em cópia isolada do cliente atual |

O importador nativo de fontes do Godot encerrou com `0xC0000005` em uma importação nova. Os scripts foram validados reutilizando os recursos importados do projeto atual. Isso não certifica uma importação limpa de fontes e não foi tratado como erro da engine REST.

## Ganhos localizados

A coleta de observadores passou a usar os compiladores/resolvedores compartilhados existentes: em um END_TURN, deserializações de conteúdo caíram de aproximadamente 1.940 para 69. A medição exclusiva separa SHA-256 de encoding e trabalho de regras.

O coordenador REST reconstruía um recibo que o combate já havia publicado após append. O reuso desse resultado confirmado reduziu a busca de aproximadamente 46–69 ms/4 chamadas para 1–3 ms/2 chamadas no cenário medido. Leituras negativas continuam consultando o armazenamento e enxergando novos commits de outros leitores/processos.

Experimento de leitura síncrona dos pequenos índices não apresentou ganho e foi descartado.

## Medições

As amostras curtas anteriores não constituem certificação estatística. Com diagnósticos desligados, antes da correção adicional de recibos: Default n=3, p50 219,27/max 386,00 ms; Ascendant n=3, p50 194,52/max 255,56 ms. Com a correção de recibos e diagnósticos ligados: Default n=3, p50 184,74/max 319,40 ms; Ascendant n=3, p50 153,54/max 292,16 ms. São campanhas diferentes; não devem ser usadas para calcular um percentual preciso de ganho.

Campanha isolada de 1.000 END_TURN por setting, diagnósticos desligados, contrato padrão, comando completo e corpo lido, commit `c646b3d9`:

| Setting | n | p50 | p95 | p99 | máximo | Ações >=100ms |
|---|---:|---:|---:|---:|---:|---:|
| Default | 1.000 | 54,73 | 100,91 | 131,00 | 286,82 | 54 |
| Ascendant | 1.000 | 74,08 | 166,57 | 220,28 | 388,34 | 178 |

Tempos em ms. Ambos os gates reprovaram por latência; não houve rejeição funcional dos comandos ou divergência de snapshot durante as 2.000 ações. As amostras foram distribuídas entre 334 encontros por setting e seeds distintas. Os percentis acima não certificam comandos de ferramentas nem outras fases da run.

Dez jornadas TCP completas passaram com transcript `04cef002c87c933770a9cf593b4b7deba5f3da387d01c2113013ed4fc695311d` e estado final `6e7f80cd33bba46ba5bb4d2db49706294bbe82d1bb80aec1d6aee7791a252520`. Foram 180 comandos de progressão/combate, mais 40 criações de run, e retries/reinícios/replay. Sua campanha foi funcional e coincidiu parcialmente com verificações de cliente/build; seus tempos abaixo são descritivos, não uma segunda certificação isolada:

| Comando | n | p50 | p95 | máximo |
|---|---:|---:|---:|---:|
| ADVANCE_NODE | 30 | 25,29 | 70,13 | 72,71 |
| APPLY_PREPARATION_OPTION | 10 | 30,60 | 77,41 | 77,41 |
| CREATE_CARD_SELECTION | 10 | 54,81 | 140,90 | 140,90 |
| CREATE_PREPARATION | 10 | 32,27 | 72,16 | 72,16 |
| END_TURN | 20 | 86,98 | 167,96 | 270,39 |
| INVOKE_CARD_ZONE_GAMEPLAY_FLOW | 20 | 29,80 | 48,94 | 67,46 |
| PICK_CARD_REWARD | 10 | 23,49 | 47,60 | 47,60 |
| PLAY_CARD | 20 | 56,13 | 102,31 | 125,70 |
| RESOLVE_COMBAT | 10 | 25,68 | 52,45 | 52,45 |
| RESOLVE_NODE | 20 | 24,50 | 62,62 | 66,84 |
| START_ENCOUNTER | 20 | 43,47 | 101,50 | 628,65 |
| START_RUN | 40 | 78,04 | 216,51 | 1.142,77 |

### Experimento descartado

O commit `775c87b4` tentou reutilizar o armazenamento de arrays imutáveis compartilhados. Passou por 2.221 testes Core, 35 focados e pelo oráculo, mas a campanha com as mesmas seeds/comandos não demonstrou ganho real de latência:

| Setting | n | p50 | p95 | p99 | máximo | Ações >=100ms |
|---|---:|---:|---:|---:|---:|---:|
| Default | 1.000 | 57,87 | 112,56 | 152,67 | 302,50 | 83 |
| Ascendant | 1.000 | 74,79 | 182,41 | 242,39 | 448,57 | 168 |

A redução de recodificação no teste sintético não justificou manter a complexidade. O commit `524b52bf` removeu o experimento e seus testes específicos. Código e testes mantidos foram comparados ao commit `c646b3d9` e são idênticos. Os tamanhos dos 2.000 corpos de resposta coincidiram entre as duas campanhas; as provas de equivalência de hashes continuam sendo o oráculo fixo e as jornadas determinísticas.

Após a remoção: build completo sem erros/avisos, 33 testes focados aprovados e dois testes REST de diagnóstico aprovados. O Core completo de 2.219 testes e os testes de oráculo/inventário já haviam passado sobre esses mesmos blobs. O número 2.221 pertence somente à variante descartada.

## Custos restantes e continuação do plano

Perfil final com diagnósticos ligados, separado do gate, manteve 333–396 fallbacks DOM por END_TURN e aproximadamente 650–710 folhas recodificadas. O SHA-256 não foi removido e não é o único custo: os spans exclusivos de encoding variaram de ~11–42ms por categoria no perfil. Não somar durações inclusivas como se fossem custos independentes.

1. **Etapas 5/6:** localizar os tipos/consumidores responsáveis pelos fallbacks restantes, especialmente fronteiras polimórficas e objetos anônimos, e ampliar somente caminhos auditados com prova de bytes. Não generalizar cache para objetos mutáveis nem mudar IDs/RNG.
2. **Etapa 12:** decompor o primeiro uso do planejamento: o primeiro Default mostrou ~113ms exclusivos em combat_plan, contra ~10–27ms em amostras posteriores. Separar compilação/JIT/conteúdo antes de escolher aquecimento puro de assinaturas/definições. Não aquecer por comandos fictícios.
3. **Etapa 10:** commit_prepare ainda teve ~7–19ms exclusivos em amostras quentes; separar validação de elegibilidade, criação de delta e preparação do envelope antes de otimizar. Append e flush permanecem no orçamento.
4. **Etapas 12/13:** medir memória, concorrência entre runs e duas ações na mesma run, restore e histórico longo em campanha controlada. As regressões existentes não substituem esses aceites de performance.
5. **Etapa 13:** repetir o gate após cada correção comprovada e ampliar a amostragem de PLAY_CARD, criação/início de encontro, progressão, build e ferramentas. A jornada tem apenas 10–40 amostras por classe; nenhuma classe pode ser certificada por uma mediana global.

A meta segue reprovada. O resultado mantido é a primeira tabela de 1.000 ações por setting, não a variante experimental nem o perfil com diagnósticos ligados.

## Reprodução

Executar a partir de uma cópia limpa e isolada, com armazenamento de testes, sem builds ou suítes concorrentes durante as medições:

```powershell
$env:HEROSCRIPT_POST_DIAGNOSTICS = 'false'
$env:HEROSCRIPT_POST_PROFILE = 'default'
$env:HEROSCRIPT_POST_SAMPLES = '1000'
$env:HEROSCRIPT_POST_BUDGET_MS = '100'
dotnet test tests/API.Tests/API.Tests.csproj -c Release --no-build --no-restore --artifacts-path .runtime/certified --filter 'FullyQualifiedName~GameplayEventLatencyTests' --logger 'console;verbosity=detailed'
```

O benchmark distribui END_TURN entre encontros válidos e seeds, em vez de continuar após a morte. O contrato padrão e o Prefer existente podem ser medidos separadamente; o endpoint de combate conserva o mesmo corpo para ambos. Para profiling, repetir com `HEROSCRIPT_POST_DIAGNOSTICS=true`, separadamente da campanha de aceite.

A jornada completa aceita `HEROSCRIPT_FOUNDATION_TCP=true` e publica `POST_MATRIX` por tipo de comando. Amostras pequenas de comandos raros permanecem explicitamente limitadas; sua mediana não substitui a matriz exigida pelo plano.
