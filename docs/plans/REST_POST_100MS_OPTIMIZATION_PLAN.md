# Plano — POSTs de gameplay abaixo de 100 ms

Data: 2026-10-29. Base de referência: `4a4a9c95`, EngineVersion 34.
Estado: **planejado; nenhuma mudança de runtime implementada por este documento**.

## 1. Objetivo e critérios de conclusão

Reduzir os POSTs mantendo REST, regras, resultados, ordem de execução, RNG, imutabilidade, causalidade e persistência durável. A confirmação HTTP deve continuar significando que o comando completo foi executado e confirmado, inclusive as ações automáticas que fazem parte dele.

O alvo é **menos de 100 ms do cliente enviar o POST até receber e ler seu corpo completo**, em Release, por TCP real em loopback, com o armazenamento durável habilitado. Tempo de JSON/HTTP/resposta e espera pelo gate da run fazem parte da medida. Tempo de animação fica separado.

Critérios da matriz publicada:

- Cada tipo de POST de gameplay listado na matriz precisa ter suas amostras dentro de 100 ms; uma média global baixa não compensa um END_TURN lento.
- Publicar p50, p95, p99 e máximo por rota/comando, tamanho de resposta e estágio da run. Orçamento interno orientador: p95 até 70 ms e p99 até 90 ms, deixando margem até 100 ms.
- Incluir o primeiro comando depois de readiness, runs carregadas após restart, checkpoints e ações no final da campanha. Readiness não pode ser antecipado enquanto a preparação necessária ainda não terminou.
- Startup do processo/publicação inicial de pacotes têm medição própria, sem misturá-los à execução de um comando. Publicar quanto o startup aumentou se houver pré-aquecimento.
- POST + GET session, parse na Godot e clique até feedback recebem medidas próprias. Não substituir a medida do POST por um cronômetro encerrado ao receber apenas os headers.
- Uma rota que permanecer acima de 100 ms fica explicitamente pendente. Não retirar amostras frias, checkpoints ou outliers do relatório para declarar o alvo cumprido.

O resultado é verificável para a máquina, build, configurações, tamanhos e carga registrados. Conteúdo arbitrariamente grande e contenção ilimitada não permitem prometer um teto universal. Casos fora da matriz também devem aparecer no relatório, separados, sem alterar suas regras para acelerar.

## 2. Evidências atuais

Fontes: código atual e [certificação de eventos](../architecture/GAMEPLAY_EVENTS_CERTIFICATION_REPORT.md). Os valores abaixo são a baseline anterior, não medições novas deste plano.

| Caso | POST | Hash/canonicalização, inclusivo | POST + session |
|---|---:|---:|---:|
| Default, primeira ação | 666,46 ms | 299,32 ms | 1161,43 ms |
| Default, segunda ação | 620,04 ms | 334,29 ms | 803,57 ms |
| Ascendant, segunda ação | 388,75 ms | 217,17 ms | 625,61 ms |

Na segunda ação Default, encoding canônico marcou 318,45 ms, coleta de respostas 460,70 ms, descoberta de legalidade 75,85 ms e append 4,33 ms. **Os spans são inclusivos e sobrepostos**. Não somar seus valores, nem atribuir 334 ms exclusivamente ao SHA-256. Precisamos separar serialização, digest, chamadas repetidas e tempo exclusivo.

A jornada extensa isolada mediu 180 comandos: p95 162,87 ms e máximo 539,51 ms. Ela ainda não atende ao alvo de 100 ms. Seu fixture é diferente da matriz de END_TURN acima; os valores não representam uma comparação controlada entre versões.

### O que já existe e deve ser estendido

- `GameplayCommandGateway.Execute` já cria `CanonicalJson.BeginHashScope()`; não criar outro mecanismo paralelo de escopos.
- `CanonicalJson` já tem memoização por identidade para tipos auditados, encoding direto, composição dos agregados RunState/CombatState/RunEncounterState e cache operacional limitado de fragmentos. O problema restante inclui sucessores novos e folhas ainda serializadas integralmente.
- Já há cache tipado revisionado de definições, cache de previews e reaproveitamento de candidatas em caminhos específicos. Auditar misses/chaves antes de adicionar caches equivalentes.
- O handler já usa `ILegalActionResolver.Evaluate`/`EvaluateObserved` para o comando escolhido. Não presumir que todo POST enumera a mão inteira: medir onde `Resolve` e avaliações indiretas ainda acontecem.
- Respostas já possuem índices de tipo/motivo/snapshot, compilação limitada a 4096 entradas por catálogo e até duas coletas por sessão de resposta. Falta investigar o que impede o reuso entre sessões/subexecuções e quanto custa reconstruir registros/matchers.
- O append já usa commit preparado e um único flush durável. Não voltar a serializar o commit ou duplicar flush como parte da otimização.

Este plano sucede o orçamento de 1 s dos planos REST anteriores. Preserva suas correções e evita repetir etapas já entregues.

## 3. Decisões de arquitetura

### 3.1 Preservar os hashes atuais na primeira implementação

O caminho inicial será uma otimização **compatível em bytes**: mesmos JSONs canônicos, SHA-256, hashes, IDs, resultados e contratos persistidos para as mesmas entradas.

Não substituir o hash do estado por hash de versão/contador, nem substituir `SHA256(JSON completo)` por `SHA256(hashes das partes)`: essas construções geram valores diferentes. É permitido compor/reutilizar os mesmos bytes canônicos das partes e alimentar o SHA-256 com o mesmo documento final.

`ExecutionId`, `ProcId` e `ImpactId` também participam das chaves dos sorteios e das respostas. Classificá-los como identidade semântica até provar seus consumidores. Não simplificar suas entradas, remover hashes do estado inicial, mudar escopos aleatórios ou acrescentar novos IDs de comando à fórmula existente.

Uma nova identidade mais barata, Merkle root ou formato reduzido de trace exigiria uma decisão de versão e migração específica. **Não é um atalho autorizado por este plano para alcançar 100 ms.** Se a otimização compatível não bastar, documentar o custo mínimo restante e uma proposta separada; não alterar silenciosamente as regras/IDs.

### 3.2 Diferenciar cálculo, identidade e materialização de diagnóstico

O estado continua imutável e a execução continua síncrona do ponto de vista da transação. Dentro da candidata, builders privados podem guardar referências a snapshots imutáveis e memoizar produtos derivados.

Hashes necessários para resolver regras/escopos aleatórios são calculados antes de seus consumidores. Hashes usados somente para trace podem ser materializados uma única vez por snapshot, no momento necessário, mas precisam estar completos antes da validação/append que os exige. Essa separação não permite eliminar campos, fatos ou respostas.

### 3.3 Contexto operacional por comando

Consolidar no contexto existente o reuso de bytes canônicos, hashes e composição de observadores. O contexto não é serializado nem faz parte das regras. Deve durar somente a execução raiz e seus descendentes, com isolamento entre previews, branches, comandos concorrentes e clientes.

Caches compartilhados mais longos ficam restritos a conteúdo ou nós comprovadamente imutáveis, com limites explícitos. Nenhum cache compartilha RNG, contadores de frequência, decisões de pagamento ou invocações aceitas.

### 3.4 Executar e confirmar uma vez

Manter o fluxo: capturar estado → validar versão/input → calcular candidata completa → validar → preparar e gravar commit → publicar → devolver recibo.

Não antecipar 200/202, não gravar depois da resposta, não dividir um fim de turno em vários comandos artificiais e não mover regras para a Godot. Preview é especulativo; a candidata confirmada usa o mesmo processador.

## 4. Orçamento orientador de 100 ms

Orçamento inicial de engenharia, a revisar com tempos **exclusivos**, sem mudar o alvo externo:

| Parte do caminho crítico | Orçamento |
|---|---:|
| HTTP, decode, idempotência, acesso e gate sem contenção | 8 ms |
| Conteúdo, composição de observadores e legalidade necessária | 15 ms |
| Cálculos, efeitos, IA e continuações automáticas da ação | 25 ms |
| Canonicalização/hash/trace necessários, fora do custo já atribuído | 15 ms |
| Preparação do commit e append durável | 15 ms |
| Projeção, serialização e transferência da resposta | 12 ms |
| Margem operacional | 10 ms |
| Total | 100 ms |

Não truncar a quantidade de inimigos, respostas ou efeitos para caber nesses valores. Contenção tem gráfico próprio, mas permanece no total observado; duas ações da mesma run continuam serializadas.

## 5. Etapas sequenciais

### Etapa 1 — Baseline reproduzível e inventário de POSTs

**Implementação:**

1. Inventariar rotas POST e tipos de comando: criação, encontro, carta, habilidade, passe, fim de turno, resolução/avanço de nó, recompensas, loja, preparação, diálogo, loadout, compartimentos, inscrições, abandono e ferramentas.
2. Identificar também POSTs de simulação, verificação, branch/restauração e publicação/ativação de conteúdo. Medir separadamente sua carga completa; não usá-los para inflar ou esconder percentis do gameplay.
3. Criar benchmark com Kestrel/TCP real e stores isolados. Gerar comandos pelos contratos atuais e bindings configurados, evitando ator/alvo fixo incompatível com o setting.
4. Registrar versão do código, EngineVersion, revisão do conteúdo, hardware, runtime, estado inicial, seed, command IDs, tamanho do estado, payload, resposta e configuração de persistência.
5. Separar benchmark de testes de correção e de builds. Medir sem debugger, editor importando ou suítes pesadas concorrentes; registrar repetições sob contenção em campanha própria.

**Arquivos:** `tests/API.Tests/Integration/GameplayEventLatencyTests.cs`, testes REST existentes, helpers de cliente e novo relatório da campanha de 100 ms.

**Aceite:** lista completa das rotas e baseline por comando/estágio da run. Nenhum número agregado é anunciado como todos os POSTs abaixo de 100 ms.

**Commit:** baseline e harness, sem otimização de runtime.

### Etapa 2 — Profiling exclusivo, contagem e volume

**Implementação:**

1. Estender `OperationalTiming` para distinguir tempo inclusivo, tempo exclusivo, contagem e aninhamento. Manter o formato existente de Server-Timing compatível ou versionar apenas o diagnóstico derivado.
2. Em hash/encoding, medir chamadas, hits/misses, tipo declarado, família de call site, bytes serializados, bytes entregues ao digest, alocações e tempo do digest separado do encoding.
3. Instrumentar construção de ExecutionId, hashes por passo, fingerprints, coleta/compilação/matching de observadores, avaliações selecionadas versus enumeração, preparação/append e serialização final MVC.
4. Medir se a mesma combinação snapshot/tipo/options está sendo recalculada e se o LRU expulsa partes úteis durante uma ação.
5. Manter diagnóstico desligável, limitado e fora do estado, dos hashes e do journal autoritativo. Não guardar snapshots completos no coletor operacional.

**Aceite:** flame breakdown sem soma duplicada, identificação das dez maiores fontes de custo e comparação com/sem instrumentação para quantificar overhead.

**Commit:** instrumentação e relatório inicial. Esta etapa pode alterar a ordem fina de otimizações seguintes, sem dispensar seus testes.

### Etapa 3 — Oráculo de compatibilidade e regras de hash

**Implementação:**

1. Capturar fixtures antes de otimizar: conteúdo exato, estado inicial, RNG, sequência de comandos e resultados completos.
2. Classificar consumidores de PayloadHash, StateHash, ByteHash, ExecutionId, ProcId, ImpactId, CandidateId, ResolutionFingerprint, hashes de frames e âncoras de eventos.
3. Criar comparação independente com o serializer canônico de referência, sem chamar o mesmo encoder otimizado para calcular esperado e obtido.
4. Comparar por passo e comando: valores, alvos, custos/refundos, RNG state/draw count, IDs, filas, quotas, stack, fatos, invocações, recibos, frames, hashes e estado final.
5. Incluir propriedades reordenadas, arrays ordenados, tokens `3`/`3.0`, null versus ausente, enums, polimorfismo, conversores, tipos declarados, Unicode, limites de profundidade e entradas inválidas. Preservar a distinção numérica atual do PayloadHash.

**Aceite:** testes diferenciais falham ao modificar propositalmente um campo, a ordem ou uma identidade. Fixtures ficam congelados durante as etapas seguintes; falhas não são resolvidas atualizando esperados para a nova saída.

**Commit:** provas de compatibilidade antes das otimizações.

### Etapa 4 — Reuso canônico dentro da execução raiz

**Implementação:**

1. Reutilizar `BeginHashScope` do gateway e garantir que handlers, efeitos aninhados e continuações recebam o mesmo escopo operacional quando apropriado.
2. Auditar caminhos de criação, sandbox, simulação, replay semântico e chamadas internas que contornem o gateway; instalar escopos equivalentes sem tornar seu compartilhamento obrigatório para correção.
3. Ampliar a chave do produto derivado somente quando necessário: identidade imutável, tipo declarado, opções/conversor e versão do formato. Não usar apenas sequence/revision ou igualdade parcial.
4. Reutilizar o hash de um snapshot já consultado como after de um passo quando ele for o before do próximo. Hashes idênticos em passos sem alteração também devem vir do mesmo produto derivado.
5. Evitar ciclos `objeto → JSON string → parse → objeto → hash` onde o mesmo resultado exato já existe. Não reutilizar bytes do wire ou do commit como se fossem automaticamente os bytes canônicos do estado.

**Aceite:** mesma saída com cache frio, quente, desligado ou expulso; isolamento concorrente; queda comprovada de chamadas/bytes repetidos. Não basta adicionar um novo cache que nunca tenha hit no caminho crítico.

**Commit:** contexto operacional e reuso compatível.

### Etapa 5 — Encoding estrutural das partes realmente imutáveis

**Implementação:**

1. Usar o profiling para escolher folhas dominantes ainda fora da composição: componentes de atores, conjuntos de recursos, instâncias/definições capturadas, políticas, mapa, topologia de cartas, status e pilha.
2. Auditar imutabilidade profunda de cada tipo antes de ampliar opt-in. `IReadOnlyDictionary` ou um record com coleção mutável não são prova suficiente.
3. Estender o encoder já existente; preservar metadados reais do serializer e o fallback conservador de formatos/conversores não auditados.
4. Reutilizar bytes canônicos das subárvores inalteradas. Para cada sucessor, reconstruir somente os fragmentos afetados e a composição necessária do documento.
5. Tratar coleções persistentes e suas folhas explicitamente; uma nova raiz de dicionário não deve invalidar todos os valores compartilhados. Preservar ordenação ordinal de chaves e ordem de arrays.
6. Limitar caches por entradas e bytes; decidir capacidades a partir de hits/evicções. Nunca aumentar retenção até manter todas as runs históricas em memória.

**Aceite:** bytes e SHA-256 iguais ao oráculo em centenas/milhares de sucessores; alterar uma folha não serializa novamente todas as definições/mapa/encontros anteriores. Reportar ganho, memória e custo residual do digest do documento completo.

**Commit:** expansão auditada do encoding estrutural.

### Etapa 6 — Identidades de efeitos sem serialização redundante

**Implementação:**

1. Montar `EffectResultContext.ExecutionId` com os mesmos campos e valores atuais, reaproveitando hashes/fragmentos já calculados de run, combate, trigger, componentes e carta.
2. Pré-calcular o fingerprint das definições imutáveis na compilação; manter origem, owner, source, alvos, variáveis, lotes e revisões contextuais na parte da chave que realmente muda.
3. Preservar o tratamento de `observedParent`, parent proc/impact e caminhos/repetições. Não unir procs de acertos diferentes ou separar sorteios antes compartilhados.
4. Otimizar criação de chaves/objetos temporários quando comprovadamente quente. Substituições internas de encoding devem gerar os mesmos bytes do objeto anônimo anterior.
5. Aplicar o mesmo princípio a IDs de registros/respostas e fingerprints de receitas/origens sem alterar sua igualdade, ordenação ou deduplicação.

**Aceite:** ExecutionId/ProcId/ImpactId/RegistrationId e fatos aleatórios idênticos; críticos por tier, multi-hit, stacks, condensação e respostas aninhadas conservam agrupamento e contagem de sorteios.

**Commit:** identidades compatíveis mais baratas; EngineVersion permanece 34 enquanto a equivalência for exata.

### Etapa 7 — Materialização única de trace e fingerprints

**Implementação:**

1. Levantar quais hashes de before/after e fingerprints são lidos durante a própria execução e quais só compõem diagnóstico/persistência.
2. Introduzir, se o perfil justificar, um construtor privado de trace que referencia snapshots imutáveis e produtos de hash memoizados; resultados públicos continuam congelados e completos.
3. Materializar cada hash/fingerprint necessário uma vez e no primeiro ponto em que for consumido. Preservar cálculo antecipado quando ele participa de identidade, regra, cache ou sorteio.
4. Evitar serializar estados completos por meio de wrappers anônimos que bypassam o encoder estrutural. Fingerprints finais continuam representando os mesmos campos, não um subconjunto.
5. Candidatas descartadas podem deixar de materializar diagnóstico que nenhum consumidor usa, mas previews contratados, recibos e commits precisam conservar seus campos. Qualquer erro de materialização impede o append completo.

**Aceite:** mesmos frames, fatos, hashes e recibos após restart/replay; input imutável; falha tardia de trace não confirma estado parcial. Registrar a redução real de trabalho, inclusive quando a preservação do contrato exigir continuar calculando um hash.

**Commit:** redução de materializações repetidas, sem novo modo de persistência ou descarte de diagnóstico.

### Etapa 8 — Composição e índices de observadores por dependências

**Implementação:**

1. Estender os caches/índices já existentes para reutilizar compilação, registros estáveis e matcher de uma composição efetiva, em vez de reconstruí-los a cada lote/subexecução.
2. Separar definição/assinatura estática de bindings e disponibilidade dinâmica. Registrar dependências reais: setting/revisão, origem e contribuidores, owner/source, roster, zonas, selecionada/em resolução, status/relíquias, lotes, vida/derrota e snapshot Before/After.
3. Usar identidade/referências imutáveis desses fragmentos e índices compilados, sem calcular um hash de toda a run para descobrir se o cache é válido.
4. Reaproveitar composição que não mudou quando houve somente uma alteração irrelevante; quando a política consultar recursos/atributos/zonas afetados, invalidar ou reavaliar a parte correspondente.
5. Preservar freeze da lista elegível, ordenação e revalidação no sucessor. Não fazer um observador recém-criado responder retroativamente ao fato anterior.
6. Para a visão adaptada da carteira, usar dependências explícitas do adapter; não confundir a carteira com recursos do personagem nem perder sua separação ao reutilizar registros.
7. Limitar retenção e integrar diagnóstico/invalidação ao coordenador de caches existente quando houver cache compartilhado. Caches locais permanecem locais.

**Aceite:** composição quente evita recompilar/rehash/reindexar registros inalterados; mudanças pertinentes invalidam corretamente. Testar remoção/recriação de observador, derrota, troca de zona, branch e ativação de revisão, quotas e carteira.

**Commit:** reuso de composição efetiva e matching, preservando decisões dinâmicas.

### Etapa 9 — Legalidade, previews e execução selecionada

**Implementação:**

1. Medir os caminhos atuais `Evaluate`, `EvaluateObserved` e `Resolve`, incluindo chamadas feitas por fases, prioridade, IA e montagem da sessão.
2. Manter o POST avaliando a escolha enviada pelo mesmo avaliador; remover somente enumerações/avaliações comprovadamente redundantes. O handler já usa avaliação selecionada: esta etapa não cria outro interpretador de regras.
3. Reutilizar resultados de candidata quando coincidirem exatamente snapshot, comando normalizado, origem/permissões e contexto relevante. Revalidar versão/autoridade antes de confirmar e nunca promover um preview obsoleto.
4. Compartilhar produtos puros de custo/alvo/fórmula quando suas dependências forem idênticas. Não reutilizar uma continuação mutável nem consumir RNG do preview no estado real.
5. Na IA, preservar o conjunto de opções consultadas, score, ordem de desempate e draws. Não acelerar escolhendo menos alternativas ou mudando a política.
6. Se preview e execução tiverem identidades causais distintas, compartilhar somente as partes puras comprovadamente equivalentes; reconstruir o envelope causal correto.

**Aceite:** comando selecionado calculado uma vez onde possível, candidatas preservadas e nenhuma legalidade aproximada. Cartas com condições, múltiplos custos/alvos e stack mantêm resultados e rejeições.

**Commit:** eliminação do trabalho duplicado de avaliação.

### Etapa 10 — Preparação do commit e caminho de persistência

**Implementação:**

1. Carregar hashes/bytes já calculados da candidata para o ponto de validação e append por meio de produtos internos confiáveis. Não aceitar hash arbitrário fornecido pelo cliente como validação do estado.
2. Revisar recomputações de PreviousStateHash/StateHash, delta, frames e serialização entre coordenador, `PreparedRunCommit` e store. Reutilizar os mesmos produtos sem omitir validações de mudança de identidade/revisão/linhagem.
3. Preservar ByteHash como hash dos bytes de armazenamento, distinto do hash canônico do estado. Uma representação delta não pode ser confundida com checkpoint em retry de baixo nível.
4. Manter um append atômico, um flush durável e publicação posterior. Não depender de cache de cursor que outro processo possa tornar inválido.
5. Otimizar índices derivados de comando/perfil somente depois de comprovar que são reconstruíveis e que o protocolo de crash/restart permanece válido.
6. Medir checkpoint versus delta, geração de progresso permanente, restart e tamanho crescente. Não alterar frequência/política de checkpoint para encobrir o custo sem analisar seu contrato de recuperação.

**Aceite:** retry, colisão de sequência, falha antes/depois da gravação, restart, checkpoint+tail e progresso mantêm o comportamento. O append continua incluído no orçamento de 100 ms.

**Commit:** preparação única e persistência sem redundância.

### Etapa 11 — Respostas REST e sessão sem duplicação

**Implementação:**

1. Medir bytes e custo real da projeção/serialização de `CombatCommandController`, `RunCommandController.MapReceipt` e `InteractionSessionService`.
2. Reutilizar projeções imutáveis do mesmo estado confirmado e serializar cada trecho necessário uma única vez. Não recalcular regras para montar uma resposta que já tem os resultados no recibo.
3. Conservar o contrato padrão atual. Caso ampliar `Prefer: return=minimal`/projeção compacta, documentar e testar a negociação, sem substituir silenciosamente respostas existentes.
4. O perfil compacto deve permitir aplicar o resultado confirmado sem uma sequência de GETs bloqueantes, usando versão/recibo/frames e as projeções realmente necessárias. Preservar informações que a UI consome.
5. Autorizar por perfil/modo e visibilidade antes de reutilizar uma projeção. Incluir permissões/schema na chave; não vazar mão oculta ou diagnóstico completo entre clientes.
6. Na Godot, manter gateway separado da UI, retry do mesmo comando e latest-response-wins. Evitar reconstruir toda a tela a cada recibo ou bloquear animações por leituras suplementares desnecessárias.
7. Medir tanto o perfil padrão quanto o compacto. Uma resposta 200 mínima antes do commit não satisfaz o contrato.

**Aceite:** contratos existentes e cliente atual funcionam; ambos os tamanhos e tempos são publicados. POST e GET session são cronometrados separadamente e juntos, sem transferir trabalho indispensável para um GET obrigatório oculto.

**Commit:** projeções e integração REST/Godot, sem mudança visual, animações ou regras.

### Etapa 12 — Primeiro uso, memória e concorrência

**Implementação:**

1. Pré-aquecer metadados/planos de encoding, definições e assinaturas estáticas do setting selecionado pela composição de startup existente, antes de readiness.
2. Aquecer apenas cálculos puros; não criar runs, executar comandos fictícios, sortear probabilidades, alterar perfil ou gravar saves para esquentar caches.
3. Garantir que hot reload publique a nova revisão e seus produtos coerentes, sem invalidar o runtime histórico utilizado por outra run.
4. Reduzir alocações quentes e usar pooling apenas para buffers/builders estritamente operacionais. Nunca devolver ao pool arrays ou buffers que ainda sustentem snapshots, respostas ou caches imutáveis.
5. Validar single-flight de produtos derivados, limites de memória, desalocação de branches e isolamento entre settings. Não paralelizar efeitos cujo encadeamento define a ordem das regras/RNG.
6. Medir concorrência entre runs e serialização de duas ações na mesma run; manter exatamente as regras de conflito/idempotência.

**Aceite:** primeiro POST após readiness e primeiro comando de run restaurada entram na matriz; sem vazamento de memória, duplicação de escrita ou contaminação entre revisões.

**Commit:** preparação do primeiro uso e controle de alocações/concorrência.

### Etapa 13 — Certificação e gate de 100 ms

**Implementação:**

1. Executar a matriz abaixo sem builds nem suítes pesadas simultâneas. Usar várias seeds, runs novas/restauradas, começo/meio/fim, tamanhos de mão e composições relevantes.
2. Para cenários estáveis, coletar pelo menos 1000 amostras por tipo de comando distribuídas em jornadas válidas; publicar também as primeiras amostras frias e todas as falhas. Para comandos raros, publicar o tamanho real da amostra e sua limitação.
3. Executar testes de correção em campanha separada, comparando bytes/hashes com o oráculo e processos independentes. Não incluir o custo do serializer de referência na medida da implementação otimizada.
4. Acrescentar gate específico de POST de 100 ms. Não reaproveitar o gate antigo de POST+session/1000 ms como se ele fosse a mesma meta.
5. Reportar todas as violações por tipo, fase, tamanho, causalidade e checkpoint. Não reduzir número de efeitos, amostra ou fases para eliminar um outlier.
6. Executar Core completo, API relevante, CLI caso conteúdo/publicação tenha sido tocado e testes Godot de contrato/transporte/replay; verificar fluxo real de jogar carta, terminar turno e continuar após restart.

**Aceite:** equivalência funcional completa e matriz publicada atendendo menos de 100 ms por POST. Se houver classe fora da meta, a etapa permanece parcial para ela, com profiling e próxima correção concretos. Uma melhora de 600 para 150 ms não é conclusão.

**Commit:** certificação, gates, documentação e resultados reproduzíveis.

## 6. Matriz mínima de regressão

| Grupo | Casos obrigatórios |
|---|---|
| Comandos comuns | PLAY_CARD, habilidade, passe, END_TURN com todos os inimigos/efeitos automáticos, START/RESOLVE_ENCOUNTER |
| Run e build | Criação/continuação, avanço/resolução de nó, recompensas, loja, preparação, diálogo, loadout, inscrições, compartimentos e abandono |
| Fontes | Carta, entidade, status, relíquia, modo, encontro e componente concedido; mesmo processador numérico |
| Matemática/RNG | Crítico por tiers, multi-hit, DoT, condensação genérica, shield/absorção, armor, cura, recursos arbitrários, gastos/refundos e rolagem por grupo/impacto |
| Respostas | Zero, dezenas e centenas de registros; matching por tipo/motivo/snapshot; cadeia longa; revalidação, remoção e recriação; quotas/rollback |
| Pilha | Input pendente, nested windows, pagamento, fizzle, cancelamento configurado, retomada após save/restart |
| Tamanhos | Várias mãos/baralhos, muitos status, vários atores e histórico crescente; final do ato completo sem crescimento indevido da latência |
| Persistência | Checkpoint e delta, append falho, retry, índices reconstruídos, perfil separado por setting e jogador, processo interrompido |
| Conteúdo | Dois ou mais settings reais, revisão antiga, revisão nova, hot reload e branches; nenhuma referência cruzada pelo cache |
| Hash/encoding | Oráculo independente, bytes exatos, tipos/conversores, payload numérico lexical, profundidade, evicção e cache desligado |
| Segurança/concorrência | Versão inválida, commandId reutilizado com payload diferente, duas ações da mesma run e runs distintas, cartas ocultas/permissões |
| Cliente | Mesmos componentes do combate/laboratório, REST real, resposta tardia, retry, replay, cartas usáveis e animações com a duração configurada |
| Ferramentas | Simulação, verificação, fork/restauração e ativação/publicação; carga completa medida, sem tratá-las como simples aceites assíncronos |

## 7. Dependências e ordem de commits

`1 baseline → 2 profiling → 3 oráculo → 4 reuso por comando → 5 encoding estrutural → 6 identidades → 7 trace → 8 observadores → 9 legalidade → 10 commit → 11 REST/sessão → 12 primeiro uso → 13 certificação`.

Cada etapa terá commit próprio com descrição do ganho, validação, contagem/volume evitados e limite restante. Quando uma etapa não produzir ganho mensurável, não manter complexidade adicional apenas porque foi planejada. Corrigir o diagnóstico e registrar o resultado.

Etapas 5–9 podem ter sua prioridade fina ajustada pelo profiling exclusivo, mas todas as mudanças passam pelo oráculo da etapa 3. Uma otimização posterior não autoriza enfraquecer uma garantia anterior.

## 8. Entregáveis e definição de pronto

1. Runtime mantendo REST, resultados, identidades, formatos persistidos e autoridade do commit atual.
2. Profiling exclusivo e contadores de chamadas/bytes/alocações com overhead conhecido.
3. Harness e gates separados para POST, GET session, percurso combinado e transporte Godot.
4. Relatório com matriz, percentis/máximos, amostras frias, crescimento da run, conteúdo/hardware, hashes de referência e comandos de reprodução.
5. Atualização dos planos REST e da documentação de hashes/caches com o estado realmente implementado.
6. Nenhum save do jogador ou bundle histórico reescrito para validar a otimização; testes em stores/copias isolados.

O alvo só será declarado alcançado quando os POSTs da matriz atenderem a 100 ms com as regras completas e os mesmos resultados, incluindo o append durável. Qualquer proposta de mudança de identidade, formato de trace, regra de checkpoint ou garantia de confirmação será apresentada como mudança arquitetural distinta, com sua própria análise de compatibilidade.
