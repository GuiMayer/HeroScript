# Implementação dos fundamentos do mundo 1

Guia: [plano de implementação](ENGINE_WORLD_1_FOUNDATIONS_IMPLEMENTATION_PLAN.md).

## Ordem e estado

1. Contratos e baseline: concluída.
2. Recursos persistentes por ator: concluída.
3. Transporte entre encontros e recuperação transacional: concluída.
4. Probabilidades calculadas e fatos de sorteio: concluída.
5. Críticos por níveis e previews: concluída.
6. Catálogo de desbloqueios e contribuições: concluída.
7. Elegibilidade congelada e concessões: concluída.
8. Contratos REST e apresentação: concluída.
9. Hotreload, restart, replay e branches: pendente.
10. Certificação integrada e desempenho: pendente.

## Contratos que não podem regredir

- A carteira da run e os recursos do personagem têm proprietários distintos.
- Dentro do combate, somente o ator é autoridade de seus recursos; fora dele, o personagem persistente.
- Transporte modifica apenas os valores autorizados pela política; buffs de limites não se tornam permanentes.
- Recuperação e seu custo são candidatos de uma única transação; falha não publica efeitos parciais.
- Nenhum identificador de recurso possui semântica hardcoded de dano, vida ou derrota.
- Probabilidades usam cálculos sem settlements; previews não avançam o RNG canônico.
- Elegibilidade é capturada na criação e acompanha replay/branches; consultas não concedem progresso.
- Testes usam armazenamento isolado, nunca saves do jogador.

## Registro

- Baseline recebido: `fe9c8d5`; dois planos e dois identificadores Godot ainda não versionados.
- Baseline: 31 testes aprovados (atributos persistentes, setting Ascendant, inputs aleatórios e perfil).
- Etapa 2: componentes de recursos persistentes, política revisionada opcional por modo, ações explícitas por resultado/retry, validação de identidade/schema/proprietário e limites persistentes. Recursos não listados continuam exclusivos do encontro.
- Engine version 21: novos snapshots de personagem incluem recursos; saves anteriores não são apagados nem reinterpretados silenciosamente.
- Gate da etapa 2: suíte Core completa aprovada (1.537 testes) e 67 testes focados aprovados após a validação adicional do grafo. Integração de saída/recuperação ainda pertence à etapa 3.
- Suíte API completa aprovada após a etapa 2: 193 testes.
- Etapa 3: promoção no commit da resolução (incluindo política de retry), owner binding em mapa/preparação/diálogo, cura via processador/pipeline comum e consequência de derrota fora de combate. Ascendant passa a preservar vida e restaurar energia por JSON.
- Testes de jornada: recuperação + custo com o mesmo resource ID em proprietários distintos; rollback por pagamento/efeito/durabilidade; restart, receipt duplicado, branch isolada e 10 replays por roteiro de recuperação/vitória/retry.
- Gate da etapa 3: 1.546 testes Core aprovados antes do ajuste de identidade; 50 testes focados aprovados após o ajuste. A suíte API revelou um encontro com ID fixo; corrigido com `identityBinding: RunPlayer` e confirmado por dois testes REST (incluindo identidade personalizada). Suíte API completa será repetida no próximo gate.
- Launcher exige a capability `persistent-actor-resources` para não reutilizar um processo anterior sem os novos contratos. Nenhum processo/save real foi alterado pelos testes.
- Etapa 4: probabilidade numérica pelo `CalculationResolver`, unidade explícita, CaptureOnly, validação de contratos compartilhados, captura Action/ParentProc/Impact e fatos com probabilidade/cálculo/revisão/snapshots. Disponível para qualquer efeito, inclusive stacks; não há calculador específico de dano/crítico.
- Engine version 22: fingerprints e traces incorporam novos fatos. Launcher passa a exigir `calculated-random-inputs`; versões anteriores/saves reais não foram reiniciados ou apagados.
- Gate Core após etapa 4: 1.569 testes aprovados, incluindo 22 casos novos de probabilidade, publicação, rollback e repetição/serialização. Sintaxe do launcher validada.
- A jornada REST antiga dependia de ordenação lexical de IDs e eliminava alvos antes de exercer overflow. Uma primeira correção preservava alvos indefinidamente e foi substituída: o bot prioriza saltos projetados e ataques não letais que preparam a instância transformada, mas nunca bloqueia todos os ataques letais. As regras da engine e a exigência de executar o salto não foram enfraquecidas.
- Etapa 5: capturas numéricas genéricas, namespaces reservados, fato crítico consumido pela pipeline, default de canal explícito no modo, fixture Ascendant multinível e previews locais limitados/condicionais. Corrigido o suporte de módulo na simulação do acumulador matemático; não foi criado calculador específico de crítico.
- Engine version 23 e capability `multi-tier-random-previews`. Godot sinaliza preview amostrado e não apresenta o valor aleatório como garantido; fallback inglês e tradução portuguesa mantidos.
- Gate Core: 1.590 testes aprovados, incluindo capturas, crítico real, child sem multiplicação/reroll implícitos, condensação genérica e módulo/arity. Gate REST: 194 testes aprovados + jornada pesada aprovada separadamente (195 no total). Teste Godot de apresentação aprovado; permanece aviso preexistente de dois objetos no encerramento.
- Jornada REST em dez stores/hosts independentes: 55 comandos por tentativa; receipts, commits, hash final, restart e replay idênticos. Condensação e salto causal efetivamente executados. Hash final `e0bec7167f18d8ebef09a9d97c129601fd9e0b7a1a032df4f4353bdb4fd1d01c`.
- Medição local in-process da jornada (820 comandos/consultas): p95 261,53 ms, p99 422,79 ms, máximo 777,05 ms. Comandos finais p95 402,16 ms; avaliações p95 183,03 ms. Não constitui certificação com histórico grande nem substitui a etapa 10.
- Etapa 6: catálogo declarativo de opções (cartas/upgrades/relíquias), condições tipadas All/Any e dependências, provenances configuráveis e dedupe por linhagem/instância. Ascendant referencia um catálogo publicado, sem tuning adicional de combate.
- Provas e contribuições entram no mesmo append de `RunCommit`; bases de perfil encadeadas por revisão/seq, lease por jogador/setting, cache LRU incremental e rebuild pelos envelopes. Nenhum segundo armazenamento autoritativo foi introduzido; o fence arquitetural continua intacto.
- Ausência de policy não concede conteúdo arbitrariamente. Removido `completed-run-content`; badges de histórico continuam separados. Perfil apresenta `progressSequence`, `progressRevision` e `unlockProofs`.
- Engine version 24 e commit schema 4; launcher exige `configurable-profile-progress`. Os saves reais não foram usados nem removidos pelos testes.
- Testes novos: redução/rebuild e dez hashes de prova idênticos; condições inválidas/ciclos/referências; isolamento; branches explícitas e herdadas; duas instâncias/8 runs concorrentes com grant único; retry, falha de durabilidade, restart e delta com prova. Cursor de append atualizado quando outro host avança a mesma run.
- Jornada REST adicional: duas tentativas, combate Ascendant real, vitória e resolução concedendo os dois unlocks dependentes no mesmo commit; replay sem nova contribuição. Criação idempotente e gating de ofertas permanecem na etapa 7; GET e histórico não antecipam esse comportamento.
- Provas de meta-progressão usam schema próprio (1): continuam legíveis quando a versão de execução do gameplay antigo está indisponível, sem migrar/reinterpretar atores, efeitos ou deltas. Teste confirma preservação dos desbloqueios e dos bytes históricos; a execução incompatível continua rejeitada.
- Gate final da etapa 6: 1.623 testes Core e 196 testes API aprovados, sem skips; jornada REST pesada aprovada separadamente (197 testes API no total). Build API sem erros/avisos, sintaxe do launcher validada e `git diff --check` aprovado.
- Jornada REST isolada da etapa 6: dez runtimes/stores, 55 comandos por tentativa, receipts/commits/restart/replay equivalentes; hash final `9fe87a1de365e2af45e21f98cba4c766ca3be354099cfbbd47a95456bc37e9d1`. Condensação e salto causal executados. A primeira execução concorrente excedeu o limite de sessão de 5 minutos; a repetição isolada passou em 4m55s, com orçamento de sessão de 10 minutos, sem reduzir as assertions.
- Medição local isolada (820 comandos/consultas): p95 287,10 ms, p99 454,99 ms, máximo 600,24 ms; comandos finais p95 436,26 ms, avaliações p95 220,33 ms. Não certifica reconstrução fria com histórico grande; essa verificação permanece na etapa 10.
- Etapa 7: serviço de criação captura elegibilidade por jogador/setting/política antes da inicialização. Fingerprint efetivo separado da revisão histórica; grants antigos autorizam apenas os alvos de suas provas. Modos Profile/Fixed/Unrestricted e pools Reject/AllowEmpty, sem fallback arbitrário.
- Recompensas, lojas, rerolls, transformações, preparações, relíquias e fluxos de zonas (incluindo efeitos/ferramentas/fronteiras) consomem a mesma captura. Barreiras antes de criar e antes de persistir, incluindo candidatos intermediários, impedem criar e consumir uma opção bloqueada no mesmo fluxo. Branches/simulações herdam elegibilidade, não a receipt raiz.
- POST de criação recebe commandId opcional: retries devolvem a criação original sem recapturar perfil ou retroceder o save. Seed sem chave usa identidade por inputs; sem seed nem chave cada POST é outra tentativa. Receipt no commit inicial e lease operacional por chave serializam hosts; nenhum segundo save autoritativo.
- Engine version 25, commit schema 4 e meta schema 1; launcher exige frozen-run-eligibility. Read model expõe eligibility/creationReceipt e rejeita inputs de elegibilidade forjados. OpenAPI, quickstart e exemplo HTTP corrigidos. Saves reais não foram usados, reiniciados ou migrados.
- Gate Core da etapa 7: 1.640 testes aprovados, sem skips. Novos casos cobrem escopo, provas históricas, catálogo fixo, cheats, ordenação/RNG, pool vazio, todas as aquisições, tentativa de substituir captura, criação concorrente/restart, branches/simulações e identidade de retries.
- Gate REST final da etapa 7: 201 testes API aprovados e jornada pesada isolada aprovada (202 no total), sem skips. Inclui tentativa de forjar unlocks, retry unseeded com chave, colisão de payload, comando raiz, provas dependentes reais e captura antiga preservada após desbloqueio. Cinco testes de documentação aprovados; JSON OpenAPI, sintaxe do launcher e `git diff --check` válidos. A UI Godot não foi alterada nesta etapa, além do contrato de compatibilidade do launcher.
- Jornada REST isolada da etapa 7: dez runtimes/stores, 55 comandos por tentativa; receipts/commits/restart/replay equivalentes e condensação/salto causal executados. Hash final `e730a07e8faade3830bdd5c99661bf3879586ebfbfe41b5619e77b6cb8df3cbb`. Passou em 4m20s, com orçamento de sessão de 10 minutos.
- Medição local in-process (820 comandos/consultas): p95 265,73 ms, p99 418,09 ms, máximo 558,76 ms. Comandos finais p95 398,77 ms; avaliações p95 180,27 ms. Não certifica reconstrução fria/histórico grande, ainda na etapa 10.
- Etapa 8: projeção pura de recursos escolhe a autoridade do personagem e identifica a carteira da run. Perfil/unlocks apresentam catálogo revisionado, condições/evidências e alvos disponíveis pelas provas; a avaliação de condições é compartilhada com o reducer, sem segundo interpretador.
- Unidade/origem/cálculo da probabilidade e revisão/snapshot agora acompanham os previews locais. Engine 26 versiona a expansão de snapshots de intenção; commit/meta schemas permanecem 4/1. Capability/launcher foundation-read-models; OpenAPI e documentação atualizados.
- Godot remove schemaVersion da criação, preserva commandId após resposta incerta, apresenta reservas separadas e resumo de opções do setting no histórico. Inglês fallback/tradução portuguesa, sem regras calculadas no cliente. Teste offline foundations e regressão de setting/Continue aprovados (aviso preexistente de dois objetos no encerramento da regressão de menu).
- Gate da etapa 8: 1.640 testes Core e 202 testes REST não pesados aprovados, sem skips. Inclui owner, catálogo e reads sem grants. Layout Godot em 1280×720/800, 1920×1080 e 2560×1080, dois idiomas e duas escalas aprovado; corrigida a fixture offline que não implementava connect_engine (a primeira execução produzia erro de script apesar das assertions aprovadas). Jornada completa será repetida no gate final.
- Próximas etapas: auditoria transversal de hotreload/restart/replay/branches (9), seguida de certificação integrada/performance (10). [Contrato REST/Godot](../api/world-one-foundations.md).
- A execução será registrada aqui com testes e limites reais, sem marcar etapas incompletas como concluídas.
