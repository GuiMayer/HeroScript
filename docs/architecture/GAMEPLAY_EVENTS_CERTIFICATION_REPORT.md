# Certificação de eventos e respostas de gameplay

Data: 2026-10-29. Escopo desta entrega: conclusão do corte de autoria (11), persistência causal (12), consultas REST (13), projeção/laboratório (14) e otimização/certificação (15). Não representa certificação universal de todos os módulos da engine nem ampliação das capacidades antes rejeitadas.

## Entrega por etapa

| Etapa | Entrega | Commit principal |
|---|---|---|
| 11 | Migração tipada, aplicação protegida, oito arquivos de regras, seis pacotes 1.1.0, conteúdo schema 2, adaptadores apenas históricos | `84435e72`, `058a5712`, `49b3e721` |
| 12 | Lote imutável de ocorrências/invocações no commit único; criação, comandos, encontro e resolução; recibos internos/journal, checkpoints/deltas/restart | `fcf25b00` |
| 12, regressão | Igualdade por valor da ancestralidade após restart, preservando rejeição de ancestrais diferentes | `e10900d3` |
| 13 | Serviço de consulta, catálogo/schema por setting/revisão, detalhes causais autorizados, cliente e OpenAPI | `647ab3a9` |
| 14 | EN/PT, fallback hierárquico, árvore causal na timeline e laboratório isolado com a mesma view | `7fd750a4` |
| 15 | Índices tipo/motivo/snapshot, compilação/fingerprint derivados, coletas por referências imutáveis, métricas e certificação | commit que contém este relatório |

## Invariantes

- Não há novo event store, fila autoritativa ou dispatcher concorrente. Estado/frames/fatos e lote causal são confirmados na mesma unidade persistente.
- Planejamento, consultas, replay visual e alternativas de IA não publicam efeitos no agregado original. Verificação semântica executa apenas em runtime isolado.
- Falha no append não publica a candidata nem confirma recibo. Retry retorna o recibo existente, sem executar resposta/custo novamente.
- RNG e contadores autoritativos não entram em caches. Quotas locais são sucessores imutáveis da ação; PriorityWindow conserva seus contadores no estado serializado.
- EngineVersion permanece **34**. Conteúdo atual usa manifest schema **2**, resposta schema **1**, commit schema **4** com lote opcional schema **1**. Bundles históricos schema 1 e commits sem lote permanecem legíveis; não se fabricam fatos ao consultar histórico antigo.
- A nova revisão de conteúdo possui hash próprio. Não se promete resultado igual entre conteúdos/revisões diferentes. Provas douradas, equivalência numérica/RNG e repetições com as mesmas entradas verificam a compatibilidade histórica.
- O Core consome recursos, proprietários, operações e classificações explícitas; não deduz dano/cura pelo nome do recurso, pela cor, pelo sinal do delta ou pela origem do efeito.

## Evidências

| Verificação | Resultado |
|---|---|
| Build completo Release | Zero warnings e erros |
| Core completo | **2208 aprovados, zero falhas/exclusões** |
| Eventos focados após índices/cache | **251 aprovados** |
| Nova matriz de compilação/matching | **4 aprovados**; capacidade, revisão, capabilities, motivos, snapshots e ordem |
| API selecionada | 44 aprovações na seleção ampliada; única falha era o fixture de latência usando ator incompatível com seu payload, corrigido; os dois casos de latência passaram isoladamente. 45 casos distintos cobertos, mais a jornada extensa isolada aprovada: **46 casos distintos**, não a suíte API inteira |
| Determinismo em processos independentes | Três processos, transcript de 18 hashes incluindo commits/recibos, dois settings e replay; aprovado |
| Jornadas de sandbox | Dez runtimes idênticos, restart com pilha aberta, branch, simulação e replay; aprovadas na seleção API |
| Compartimentos e pool multiator | Jornadas repetidas, privacidade da mão pública/privada, retry, persistência e replay semântico; aprovadas |
| CLI de migração | **7 aprovados**, inclusive preimagem, backup, rejeição sem escrita e idempotência |
| Godot 4.7.2 headless | Import sem erros e **6 scripts aprovados**: gameplay_event_presentation, replay_progressive, replay_lifecycle, replay_transport, animation_contracts, http_transport_pool |

Árvore de execução principal em `.runtime/events-stage15-exact`: Core completo, seleção API e ferramenta. A seleção ampliada excedeu seu limite global de cinco minutos enquanto executava também a jornada extensa de dez campanhas; ela não é reportada como suíte integral verde. O caso extenso é executado separadamente, com limite de sessão explícito, e seu resultado deve ser lido na nota abaixo. O fixture novo de latência foi corrigido para usar o identificador configurado `player` e resolver o ator de input pelo binding real.

Árvore final de build/métricas: `.runtime/events-stage15-final`, árvore exportada `2310d8f372b4ee10ab061e39642612ca0b7f862f`. Os fontes do Core são byte a byte iguais aos usados nos 2208 testes completos; mudanças posteriores são documentação/schema descritivo e fixture de latência. Logs e stores de certificação são isolados em `.runtime`/pastas temporárias; não usam os saves do jogador.

**Jornada extensa isolada: aprovada em 3 min 54 s.** Dez campanhas com 18 comandos cada produziram o mesmo transcript `04cef002c87c933770a9cf593b4b7deba5f3da387d01c2113013ed4fc695311d` e estado final `6e7f80cd33bba46ba5bb4d2db49706294bbe82d1bb80aec1d6aee7791a252520`. Foram comparados custos/recuperação, fatos críticos multi-hit, persistência, desbloqueios, retry e replay sem alterar outros jogadores/settings. Em 180 comandos, p95 **162,87 ms**, máximo **539,51 ms**; 20 previews, p95 **52,74 ms**, máximo **99,42 ms**. São comandos REST desse fixture; não incluem o GET session medido na matriz abaixo.

## Medições locais

.NET 10.0.9, Release, Windows; REST via Kestrel em loopback, portas efêmeras, stores novos, três comandos END_TURN por setting, cada um seguido de GET session. O cenário usa as regras publicadas e mede a execução completa, incluindo inimigos e atualização da mão; não altera duração de animações nem espera por renderização.

| Setting / amostra | POST comando | REST + session |
|---|---:|---:|
| default / primeira fria | 666,46 ms | **1161,43 ms** |
| default / segunda | 620,04 ms | 803,57 ms |
| default / terceira | 546,29 ms | 776,27 ms |
| ascendant / primeira | 376,49 ms | 642,68 ms |
| ascendant / segunda | 388,75 ms | 625,61 ms |
| ascendant / terceira | 412,83 ms | 589,29 ms |

Os seis comandos ficaram abaixo de 1 segundo; as cinco atualizações quentes também. **O primeiro percurso frio default ainda ultrapassa 1 segundo em 161 ms.** Não é um SLA para todas as cartas, hardware, transporte remoto ou cadeias próximas ao limite. A primeira projeção de sessão permanece um ponto de otimização; a meta de todo percurso frio abaixo de 1 segundo não foi certificada.

| Registros de resposta | Construção do índice | Matching médio (32 leituras) |
|---|---:|---:|
| 0 | 0,089 ms | 0,011 ms |
| 256 | 1,325 ms | 0,586 ms |
| 1024 | 32,609 ms | 3,400 ms |

`Server-Timing` mediu separadamente matching/collect/execução, canonicalização, serialização, append e comando. Exemplo default primeira ação: coleta 403,21 ms; execução de efeitos 139,44 ms; serialização do commit 4,24 ms; preparação 13,93 ms; append 5,29 ms. Métricas aninhadas são **inclusivas** e não devem ser somadas. O custo restante dominante é composição/hash/coleta e projeção, não os milissegundos do append.

O cache de compilação é derivado, limitado a 4096 entradas por catálogo com retenção fraca; inclui definição/capacidades e separa setting/revisão pela identidade do catálogo. A sessão guarda no máximo duas coletas e as invalida por referência imutável de run/combate. O matcher cruza tipo, motivo e snapshot, mantendo filtros de proprietário/recurso/condição no caminho canônico. Testes de orçamento/ciclos/cadeias aprovam rollback integral; não se descarta fato para satisfazer tempo de resposta e não se promete subsegundo para uma cadeia com milhares de respostas.

## Uso e reprodução

- Laboratório: `examples/godot-engine-showcase/scenes/gameplay_event_lab.tscn`. Fixtures offline explícitos, dois settings/recursos e seleção EN/PT; mesmos presenter/view da timeline.
- REST: `GET /api/v1/content/{settingId}/revisions/{revision}/gameplay-events/catalog`, `.../gameplay-responses/schema` e `GET /api/v1/runs/{runId}/commits/{sequence}/gameplay-events`.
- Inspeção completa exige `timeline.inspect_state` e `debug.card_inspection.full`. Carta oculta não é exposta pelo novo lote em recibo/journal normal. Não há POST para injetar fatos.

```powershell
dotnet build HeroScript.slnx -c Release --artifacts-path .runtime/certified
dotnet test tests/Core.Tests/Core.Tests.csproj -c Release --no-build --no-restore --artifacts-path .runtime/certified
dotnet test tests/API.Tests/API.Tests.csproj -c Release --no-build --no-restore --artifacts-path .runtime/certified --filter "FullyQualifiedName~GameplayEventQueryTests|FullyQualifiedName~CombatSandboxIntegrationTests|FullyQualifiedName~CrossProcessFoundationTests|FullyQualifiedName~MultiActorPoolJourneyTests|FullyQualifiedName~DeckCompartmentJourneyTests"
dotnet test tests/API.Tests/API.Tests.csproj -c Release --no-build --no-restore --artifacts-path .runtime/certified --filter FullyQualifiedName~GameplayEventLatencyTests --logger "console;verbosity=detailed"
dotnet test tests/API.Tests/API.Tests.csproj -c Release --no-build --no-restore --artifacts-path .runtime/certified --filter FullyQualifiedName~WorldOneFoundationJourneyTests -- RunConfiguration.TestSessionTimeout=900000
dotnet build tools/GameplayResponseMigration -c Release --artifacts-path .runtime/tools
python -X utf8 -m unittest discover -s tools/GameplayResponseMigration/tests -v
```

Para Godot, executar em cópia isolada com user directory de QA: `--headless --path <cópia> --editor --import --quit`, seguido de `--headless --path <cópia> --script res://tests/gameplay_event_presentation.gd` e dos demais cinco scripts. Não iniciar ou substituir a API do jogador para a certificação.

## Capacidades reservadas e limites de cobertura

- Frequências Activation/Round/Combat/Run continuam rejeitadas; a entrega persiste PriorityWindow, não anuncia os demais escopos como disponíveis.
- Novos publishers/extensões precisam de capability confiável e catálogo próprio; não são habilitados por JSON arbitrário. A descoberta REST mostra o catálogo efetivamente suportado, não as intenções futuras do plano.
- Rebind/ativação de revisão possuem fatos tipados e revisão de origem explícita, mas não concedem a uma resposta o poder de trocar revisões/cancelar regras arbitrariamente. Não há nova pilha nem prevenção retroativa.
- O laboratório usa fixtures visuais claramente identificados; a integração real é certificada em Core/API. Renderização visual manual em todas as resoluções e a suíte inteira API não foram certificadas nesta entrega.
- A falha ampla de balanceamento/jornada 65/67 já registrada antes deste plano não é corrigida alterando gameplay para obter teste verde.
