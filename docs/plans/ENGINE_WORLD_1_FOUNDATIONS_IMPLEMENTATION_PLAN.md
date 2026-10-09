# Engine — plano de correção dos fundamentos do mundo 1

Data: 2026-10-09.

Status: planejado; implementação não iniciada.

Referência de design: [Ascendant — pesquisa e plano do mundo 1](ASCENDANT_WORLD_1_DESIGN_RESEARCH_AND_PLAN.md).

Base técnica examinada: `main`, commit `fe9c8d5`, versão semântica de execução atual `20`.

## 1. Escopo e resultado esperado

Entregar três capacidades genéricas, sem codificar regras exclusivas de Ascendant:

1. Recursos de um personagem podem persistir entre encontros conforme política JSON, com recuperação fora do combate.
2. Inputs aleatórios podem receber probabilidade calculada pelo contexto/pipelines. Isso permite configurar críticos multinível independentes por impacto.
3. Desbloqueios de opções são declarados em conteúdo, derivados de fatos autoritativos por jogador/setting e capturados na criação da run.

A entrega termina com uma jornada técnica pequena que prova essas capacidades por REST. Não inclui produzir o catálogo de cartas/inimigos, balancear todo o ato, gerar mapas proceduralmente ou redesenhar a Godot.

Os nomes dos tipos e campos novos abaixo são propostas de contrato. Devem ser consolidados na etapa 1, antes de alterar estado persistido ou OpenAPI.

## 2. Diagnóstico confirmado

| Área | Evidência atual | Lacuna |
|---|---|---|
| Personagem persistente | `PersistentPlayerTransitions.Create` materializa componentes de atributos; `Materialize` transporta esses atributos | Recursos não atravessam encontros |
| Início do combate | `CombatRunCoordinator.StartEncounter` cria participantes de definições e overrides | Falta política para usar recursos persistentes em vez de defaults da entidade |
| Resolução | `RunManager.ResolveEncounter` resolve zonas, efeitos de saída, mapa e progressão | Falta promover resultado dos recursos do personagem atomicamente |
| Efeitos fora do combate | `RunActivityEffectExecutor` usa proprietário sintético com `run.ResourceState` | Carteira da run e recursos do personagem não podem ser confundidos ao curar |
| Sorteios | `EffectRandomInputDefinition` oferece Action/ParentProc/Impact; `Chance` é float fixo entre 0 e 1 | Falta probabilidade calculada, trace e congelamento de seu contexto |
| Cálculos | `CalculationResolver` é a entrada numérica de efeitos; fórmulas de níveis críticos já existem | Falta ligação entre chance, sorteio, tier, magnitude, preview e replay |
| Perfil | `PlayerProfileProjectionReader` deriva marcos básicos de runs por jogador/setting | Não há catálogo declarativo completo controlando opções elegíveis |
| Ofertas | `CardPoolResolver` resolve conteúdo revisionado e `CardOfferResolver` sorteia em ordem canônica | Falta filtro pela elegibilidade capturada na run |
| Identidade da run | `RunManager.StartRun` aloca ID com setting, definição, jogador, modo, seed/contexto e tentativa | Novos inputs externos de elegibilidade precisam entrar na identidade/fingerprint pertinente |
| Hotreload | `RunContentCompatibilityValidator` valida atributos, cards e recursos existentes | Deve incluir recursos persistentes do personagem e os novos contratos |

### 2.1. O que permanece aproveitável

ResourceSet, ResourcePool e threshold policies; player attributes; efeitos comuns; pipelines e quantidades transportadas; distribution budgets; scopes de RNG; commits, receipts, branches e replay; modo resolvido revisionado; perfis por setting; seleção determinística de ofertas.

Não criar implementações paralelas desses sistemas. Controllers permanecem transporte/validação superficial; decisões ficam nos serviços e transições da engine.

Não ampliar RunManager/EffectTriggerExecutor com todos os detalhes dessas regras. Planejadores puros de transporte, resolução de inputs e elegibilidade devem produzir candidatas/fatos; os coordenadores existentes preservam a fronteira única de validação e commit. O serviço de perfil não pode depender do serviço de criação que já o consulta.

## 3. Invariantes de arquitetura

### 3.1. Recursos: uma autoridade por momento

- Fora de encontro ativo, o componente de recursos do personagem em `RunState.PlayerEntity` é a reserva canônica de seus recursos persistentes.
- Dentro do encontro ativo, `CombatActorState` do participante correspondente é a autoridade operacional dos recursos em combate.
- O estado persistente anterior permanece como baseline/entrada do encontro, não como uma segunda reserva ativa a ser debitada em paralelo.
- Na resolução, uma transição pura promove somente os campos autorizados pela política, no mesmo commit que encerra o encontro.
- A projeção pública escolhe a autoridade conforme o estado da run; a Godot não decide isso nem sincroniza cópias.
- `RunState.ResourceState` continua sendo a carteira do proprietário da run. Gold de run e vida do personagem são reservas diferentes, mesmo quando os IDs de recursos coincidem.
- A política não conhece o significado de `health`, `mana`, `energy` ou `block`. Esses nomes aparecem apenas em definições do setting.

### 3.2. Cálculo, aleatoriedade e aplicação

- Cálculo resolve quantidades e devolve resultados/traces.
- RNG resolve um fato aleatório com probabilidade explícita e contexto capturado.
- O executor usa os fatos em fórmulas/pipelines e aplica o resultado ao alvo.
- Não criar um `CriticalDamageProcessor` que recalcula dano fora da pipeline.
- Preview não consome RNG canônico e não oferece resultado exato quando depende de sorteio.
- Uma falha não altera recursos, RNG, ledger ou sequência persistidos.

### 3.3. Progressão entre runs

- Progresso é separado por jogador e setting; modos e provenance definem quais tentativas contribuem.
- Fatos registrados em commits são autoridade. Perfil/cache são projeções reconstruíveis.
- A run captura as opções elegíveis no começo. Nenhum comando de gameplay consulta o perfil vivo para alterar suas ofertas.
- Desbloqueios ampliam opções neste jogo; não concedem +vida/+dano inicial permanente.
- O cliente não pode afirmar que possui um unlock nem enviar uma lista arbitrária aceita pelo modo normal.

## 4. Contratos propostos: recursos entre encontros

### 4.1. Política revisionada

Adicionar referência de política ao modo e resolução imutável no `ResolvedGameMode`, por exemplo `actorResourceLifecyclePolicyId`.

Cada regra seleciona ator/proprietário e `resourceId`, e declara:

- Inicialização: entidade publicada ou recurso persistente existente.
- Entrada de encontro: preservar current ou reiniciar pelo máximo/valor configurado.
- Saída: preservar current, resetar ou não transportar aquele recurso.
- Máximo e mínimo: autoridade persistente ou valor configurado da definição; cópia de máximo temporário não é permitida implicitamente.
- Outcome aplicável: vitória, derrota, draw, abandono e retry são tratados explicitamente.
- Recurso ausente: erro ou ignore explícito, sem criação silenciosa de uma reserva arbitrária.

Primeiro conjunto suportado: PreserveCurrent, ResetToMaximum, ResetToConfiguredValue e EncounterOnly. Não implementar uma DSL ilimitada de ciclo de vida nesta etapa.

O conjunto é selecionável no JSON. No Ascendant, a política preserva vida; energia e block são temporários/resetados conforme regra declarada. Regeneração dentro da ativação continua no sistema existente, não é duplicada nesta política.

### 4.2. Estado persistente

Estender o personagem existente com `ResourceEntityComponentState` quando a política exige persistência. Não criar `RunState.PlayerHealth` nem outra lista de pools concorrente.

Materializar apenas o que o contrato precisar preservar; recursos EncounterOnly continuam locais. Validar componentes de entidade sem supor que todos sejam atributos. Atualizar Rebind e validações que hoje aceitam somente StatEntityComponentState.

Definição de personagem, setting, revisão e identidade precisam coincidir com o participante do encontro. Override de cenário que contradiz o personagem persistente falha em modo normal; sandbox/dev precisa de autorização explícita e diagnóstico da origem do override.

### 4.3. Buffs de máximo: primeira política segura

Máximos temporários de combate não se tornam melhorias permanentes. Primeiro suportar a conservação de current com clamping aos limites persistentes quando o máximo temporário desaparece. Exemplo: current 110 em máximo temporário 120 retorna no máximo persistente 100 como 100, não como 110/120.

Essa opção precisa ser declarada na política. Transporte proporcional de percentual e outros modelos ficam reservados/explicitamente não suportados até implementação, em vez de serem aceitos sem efeito.

Melhorias permanentes alteram o proprietário persistente em uma transição declarada; não são inferidas a partir do último máximo visto em combate. Threshold de derrota continua associado à definição do recurso.

### 4.4. Recuperação e custos fora do combate

Generalizar o binding de proprietário do executor de atividades/preparações: `RunOwner` para carteira e `PlayerEntity` para recursos do personagem.

Uma opção pode pagar gold na carteira e recuperar um recurso do personagem. Ambos acontecem em uma candidata imutável única; validação/cálculo/custo/aplicação/registro devem concluir antes do commit.

Quantidades de recuperação, inclusive porcentagem do máximo, passam pela pipeline declarada. Não inventar um endpoint genérico que permite ao cliente definir a vida desejada em gameplay normal. O comando escolhe uma opção legal de atividade; a engine calcula.

O binding de proprietário é separado de SELF/TARGET dos efeitos: ao adaptar uma atividade, escolher o proprietário certo antes de invocar o processador comum, sem mudar o significado do efeito apenas por sua origem.

Se um efeito fora do combate cruzar um threshold com consequência de derrota, a consequência deve ser encaminhada à progressão da run na mesma transação. Não presumir que só um recurso chamado health pode falhar uma tentativa, nem manter um personagem derrotado disponível para entrar no próximo encontro.

## 5. Contratos propostos: probabilidade calculada e crítico

### 5.1. Input numérico de probabilidade

Estender `EffectRandomInputDefinition` para permitir uma de duas origens exclusivas:

1. Probabilidade literal existente, entre 0 e 1.
2. Descrição numérica calculada com base/formula, channel, pipeline, unidade e política de limites.

Reutilizar o contrato numérico comum/CalculationResolver, separando a descrição de um valor calculável da lista fechada de parâmetros que alteram campos de efeito, se isso for necessário. Não duplicar avaliação de fórmulas ou coleta de influências em um resolver específico de críticos.

Unidade de entrada do Bernoulli: probabilidade adimensional em `[0, 1]`. Chance crítica bruta em percentual pode ser 150 ou 200; a pipeline de probabilidade transforma apenas a fração restante em `[0, 1]`. Nunca clamp de 150% para 100% como forma de interpretar tiers.

Cálculo de probabilidade é CaptureOnly: não consome capacidade de block, não paga custos e não produz settlements. Validar/rejeitar pipeline de probabilidade que declare settlement incompatível.

Erro para NaN, infinito, referência ausente, unidade inválida, seleção ambígua de origem ou probabilidade fora da faixa após as políticas declaradas. Não corrigir silenciosamente dados inválidos no cliente.

### 5.2. Captura por scope

- Impact: snapshot do momento do impacto, antes de efeitos derivados daquele sorteio.
- Action: captura no primeiro uso daquele input na ação, com resultado reutilizado no scope.
- ParentProc: captura no primeiro uso do grupo de proc e reutilização correspondente.
- GroupId compartilhado exige contratos numéricos/contextos compatíveis; rejeitar conflito, não reutilizar probabilidade da primeira definição por acaso.
- O mundo 1 usa Impact, sem GroupId compartilhado, para crítico independente por acerto.
- Buff entre impactos só altera chance se a regra e o scope permitirem ler o novo snapshot. Nenhum efeito posterior reescreve um sorteio já registrado.

Para inputs de scope compartilhado com dependência de alvo, rejeitar inicialmente o caso ambíguo ou exigir captura explícita de um alvo do scope. Não introduzir comportamento implícito determinado pela ordem de um dicionário de alvos.

### 5.3. Fato e trace imutáveis

Registrar, além de InputId/Scope/ScopeId/Roll/Success:

- Probabilidade resolvida.
- Referência/fingerprint do cálculo e revisão.
- Identidade do contexto/impacto/proc.
- Base numérica capturada necessária aos cálculos derivados.

Namespaces numéricos internos permanecem reservados e não podem ser forjados pelo payload REST. Ordem de inputs, impacts e contribuições é canônica.

Manter as convenções existentes de RNG em 0%/100% se não houver razão para mudá-las; caso a semântica mude, registrar e versionar explicitamente. ID/fingerprint de um comando duplicado retorna o receipt original, sem novo sorteio.

### 5.4. Crítico é conteúdo sobre esses contratos

No fixture/setting, pipelines/fórmulas declaram:

- Tier garantido: `floor(C / 100)`.
- Chance extra: fração de `C / 100`.
- Tier efetivo: tier garantido + fato de sucesso.
- Multiplicador recomendado: `1 + tier × (M - 1)`.

`C` e `M` usam atributos e influências do snapshot correto. Fórmulas de tier e multiplicador leem a mesma base capturada que produziu a probabilidade; não buscar novamente um stat depois de uma mutação intermediária.

Não hardcodar os nomes `critical_chance` e `critical_multiplier` no sorteador ou no calculador genérico. Tags/seletores de conteúdo definem onde o multiplicador se aplica.

Source scaling e orçamento compartilhado são calculados uma vez. Distribuição vem antes do crítico por impacto; alterações específicas do alvo vêm no estágio adequado. O multiplicador crítico não reaplica bônus flat já capturado.

### 5.5. Preview e intenções

Usar o mesmo serviço de resolução numérica de execução para descrever probabilidades e alternativas possíveis, sem DrawDouble nem mutação canônica.

- Não tratar média como dano garantido.
- Para um impacto simples, expor tiers/magnitudes possíveis e probabilidade.
- Para sequências, expor fatos locais por impacto e declarar dependências; não prometer uma faixa global exata ignorando condicionais, defesa, morte antecipada ou continuação.
- Não enumerar `2^N` combinações sem limite. Se o preview completo exceder orçamento, retornar resumo parcial identificado como tal.
- Intenção com sorteio indica resultado variável; pipeline determinística e aplicação certa não recebem esse aviso indevidamente.

## 6. Contratos propostos: desbloqueios e elegibilidade

### 6.1. Catálogo declarativo

Modo referencia política de progressão de perfil/desbloqueios publicada no setting. Separar a política entre tentativas da política existente que decide vitória/derrota/continuação da run.

Catálogo mínimo:

- UnlockId, metadados e opções liberadas por IDs de conteúdo.
- Condições tipadas combináveis com all/any.
- Modos/provenances contribuintes.
- Escopo por jogador/setting e regra de deduplicação.

Primeiras condições: contagem de tentativas válidas, contagem de atos/runs concluídas e nó/encontro concluído identificado. Contadores de mecânicas específicas só entram quando existe fato canônico correspondente e teste; não interpretar texto de log ou telemetria operacional.

Primeiros alvos: inclusão em ofertas de cartas, transformações/suportes e relíquias. A engine não aplica stats ou recursos permanentes por esta política do mundo 1. Outras classes de grant ficam fora do escopo e não são aceitas como se estivessem implementadas.

Referências desconhecidas, ciclos de dependência, regras sem tipo e condições não suportadas impedem publicação. Não usar C# arbitrário dentro de JSON.

### 6.2. Autoridade dos fatos e grants

Produzir contribuições de progresso compactas a partir dos resultados de comandos canônicos, sem usar relógio real, UI, analytics externos ou um segundo journal de gameplay.

Quando uma condição é satisfeita, o grant deve possuir prova autoritativa imutável com política/revisão e referências às contribuições. A persistência dessa prova usa o protocolo canônico de commits existente ou sua extensão declarada, não um save de perfil independente escrito de forma best-effort.

Para condições entre várias runs, capturar a base de progresso e as contribuições usadas na decisão. Concorrência exige revisão/CAS, deduplicação e retry de redução antes do commit; não assumir que gravar uma run e atualizar outro arquivo de perfil são uma transação.

Evitar ciclo de dependência em que o commit precisa conhecer o próprio hash final para construir o grant. Usar identidades/seq anteriores e IDs de comando que já estão definidos.

Um cache/projeção pode manter contadores incrementalmente, mas deve ser reconstruível das provas/commits. Grant concedido não é revogado só porque uma definição posterior mudou; mudanças de catálogo exigem semântica explícita de disponibilidade, preservando a prova histórica. Não criar uma chave de achievement hardcoded para cada carta.

### 6.3. Antiduplicação e provenance

- Comando terminal duplicado não concede novamente.
- Replay verifica/reconstitui o fato; não publica recompensa de perfil nova.
- Simulações internas não contribuem.
- Sandbox/dev e branches não contam por padrão; policy pode autorizar provenances específicas.
- Deduplicar tentativas/linhagens conforme regra declarada, para que bifurcar uma run não multiplique progresso automaticamente.
- Abandono/derrota só contribuem para as condições que explicitamente os aceitam.
- Um setting nunca lê as contribuições de outro para completar condições locais.

### 6.4. Captura na criação da run

Serviço de criação da engine resolve perfil/elegibilidade antes de inicializar a run; controller não coleta unlocks e não fornece fatos confiáveis por conta própria.

Capturar um `RunEligibilitySnapshot` conceitual contendo:

- Jogador/setting e catálogo/política revisionados.
- Revisão/fingerprint do progresso consultado.
- Unlocks e IDs/opções efetivamente elegíveis, em ordem canônica.
- Provenance da captura.

Incluir o fingerprint efetivo desse input na identidade/hash de criação pertinente. Mesma seed e conteúdo com desbloqueios diferentes não são os mesmos inputs. Atualizações irrelevantes do histórico não devem mudar a identidade efetiva quando a elegibilidade é igual; manter a revisão de perfil consultada como provenance separada.

Distinguir tentativa/commandId de identidade do conjunto de regras. O retried POST de criação reutiliza o snapshot capturado do receipt; não relê o perfil e cria uma run diferente depois de timeout.

Daily challenges e simulações podem usar elegibilidade fixed/explicit conforme policy, nunca a lista livre de um cliente normal. A mesma lista fixed oferece comparabilidade entre jogadores quando esse é o objetivo do modo.

### 6.5. Consumo no gameplay

Pool revisionado e elegibilidade da run são inputs distintos:

1. Resolver candidatos da definição de pool publicada.
2. Filtrar pelas regras de disponibilidade e snapshot capturado.
3. Canonicalizar candidatos.
4. Sortear usando o RNG da run.
5. Persistir oferta e seu fingerprint.

Aplicar a mesma elegibilidade em recompensa, loja, reroll, opções de transformação, concessões de preparação e grants explícitos. Não corrigir apenas o pool de recompensa deixando caminhos alternativos liberar conteúdo bloqueado.

Deck inicial/configuração normal exige validar elegibilidade dos IDs gated. Overrides só existem com capability e provenance adequada.

Pools vazios devem produzir resultado declarado: recusa/nenhuma oferta se a atividade permite, ou erro de conteúdo antes da transação. Não adicionar uma carta default fora das regras como fallback invisível.

## 7. Persistência, versões, hotreload e desempenho

### 7.1. Mudança semântica

Versionar os novos contratos e a execução quando alterarem hashes/RNG/resultados. Não continuar anunciando versão de execução `20` se a interpretação mudou.

Campos opcionais ausentes e políticas desabilitadas não materializam componentes fictícios só para ocupar schema. Contudo, não prometer replay semântico de snapshots antigos em runtime incompatível. Dados históricos são preservados; versão indisponível é informada claramente, sem conversão silenciosa ou limpeza de saves.

Não criar rotas legadas de compatibilidade. Atualizar OpenAPI, exemplos e consumidores canônicos juntos.

### 7.2. Hotreload

- Policies e pipelines seguem a revisão ativa/pinned da run.
- Rebind valida recursos do personagem além da carteira/atores existentes.
- Remover recurso persistente ou alterar limites incompatíveis exige regra explícita; sem ela, ativação falha atomicamente.
- Definições de chance e novos input namespaces entram na validação de containers/efeitos.
- Mudar o catálogo global não altera opções elegíveis de uma run já criada.
- Ativação de conteúdo novo valida as opções do snapshot contra o conteúdo candidato. Não recaptura automaticamente o perfil vivo.
- Uma opção desbloqueada em tentativa futura não aparece na run antiga por abrir o menu novamente.

### 7.3. Performance e caches

- Coleta de chance reutiliza runtimes e definições revisionadas; não relê JSON a cada impacto.
- Contexto de scope compartilhado evita cálculos repetidos quando sua semântica exige captura única.
- Cache de probabilidades/previews inclui revisão, snapshot e inputs pertinentes; não usar só cardId.
- Cache de pools elegíveis inclui fingerprint da elegibilidade, revisão e ID do pool. Definições públicas globais não são contaminadas pelo perfil de um jogador.
- Progresso incremental evita varrer todas as runs em todo comando. Se frio, reconstrução ocorre na fronteira de perfil/criação, nunca no processamento de cada carta.
- Medir crescimento com histórico grande, multi-hit e várias consultas de preview. Meta: p95 abaixo de 1 segundo para resposta útil em host aquecido na máquina-alvo; separar I/O frio e reconstrução de perfil.

## 8. Plano sequencial de implementação

### Etapa 1 — Contratos e regressões de referência

Entregas:

- Consolidar schemas/policies propostos, ownership, snapshots e regras de RNG/progressão.
- Fixtures mínimas independentes do catálogo do ato: dois encontros, recuperação paga, efeito multi-hit e opção bloqueada.
- Caracterizar vida/atributos atuais, literal Chance, modo sem novas policies, perfil e offers.
- Registro das lacunas com testes de comportamento pretendido no momento de implementação; não deixar testes permanentemente skipados como substituto de funcionalidade.

Pontos principais: RunModeDefinition, RunDefinition, EntityState/ResourceSet, EffectScopedInputs, CalculationResolver e projeção de perfil.

Gate: schemas com validação e casos inválidos definidos; exemplos deixam claro o que é existente e o que é novo.

Commit sugerido: `docs: define generic actor resources probability and eligibility contracts`.

### Etapa 2 — Policy e personagem com recursos persistentes

Entregas:

- Compilar/resolver policy revisionada pelo modo.
- Criar/rebindar o componente de recursos persistentes do personagem.
- Validar identidade, bounds, recurso ausente, estado só de atributos e policy desabilitada.
- Atualizar publicação/graph validator e snapshots necessários.

Gate: criação e rebind são puros; wallet e actor resources possuem owners distintos; nenhuma alteração dos outros settings por default implícito.

Commit: `feat: add configurable persistent actor resource policies`.

### Etapa 3 — Entrada, saída, recuperação e commit atômico

Entregas:

- Transportar recursos na criação do encontro antes dos efeitos de início.
- Promover saída uma vez na resolução, antes de efeitos de saída que recuperam o ator, no mesmo commit.
- Definir vitória/derrota/draw/abandono/retry; retry pode restaurar baseline ou preservar desgaste só por opção explícita.
- Generalizar binding do executor de atividades e preparação para actor versus run owner.
- Expor diagnóstico de transporte com before/after/policy/source, sem novo journal paralelo.

Gate: perder vida em A → restart → pagar cura → B recebe a vida correta. Falha no custo/efeito/commit não promove metade da operação; repeated command não duplica cura nem transferência.

Commit: `feat: carry actor resources atomically across encounters and activities`.

### Etapa 4 — Probabilidade calculada e fatos por scope

Entregas:

- Implementar origem numérica calculada de Chance usando entrada comum de cálculos.
- Resolver contexto por scope, validar grupos, executar RNG e registrar trace/probabilidade.
- Atualizar validators de efeitos, conteúdo, limites e namespaces reservados.
- Integrar rollback e receipts ao fluxo existente.

Gate: literal continua disponível; calculated probability funciona para qualquer efeito elegível, não apenas dano; 0/1, erro de unidade, não finitos e scoped groups são testados.

Commit: `feat: resolve scoped random probabilities through calculation pipelines`.

### Etapa 5 — Crítico multinível, distribuição e preview

Entregas:

- Fixture JSON com chance bruta, tier e multiplicador linear.
- Pipeline de impacto consumindo fato crítico, sem caminho alternativo de dano.
- Preview/inspection/intent explicam alternativas e incerteza, com limites de trabalho.
- Verificar upgrades/base, buffs, stat influence, more/increased, mitigation, DoT e condensação sem reaplicação indevida.

Gate: testes de 0%, 50%, 100%, 150%, 200%; multi-hit independente; flat aplicado uma vez no orçamento; previews repetidos não alteram RNG/hash.

Commit: `feat: compose multi-tier critical impacts and truthful previews`.

### Etapa 6 — Catálogo de desbloqueios e contribuições autoritativas

Entregas:

- Política declarativa de condições, provenance e alvos de unlock.
- Contribuições/grants idempotentes vinculados aos commits, com bases revisionadas para condições entre runs.
- Projeção reconstruível de perfil e cache/incremental reduction, sem segundo save autoritativo.
- Dedupe por tentativas/linhagens e isolamento por setting.

Gate: rebuild e projeção incremental concordam; concorrência não perde/concede duas vezes; simulação/replay não concede progresso; ausência de policy não gera grants arbitrários.

Commit: `feat: derive configurable setting unlocks from canonical progress facts`.

### Etapa 7 — Snapshot de elegibilidade e todos os caminhos de concessão

Entregas:

- Serviço de criação captura elegibilidade confiável uma vez; atualizar wiring do serviço, não colocar lógica em controller.
- Identidade/fingerprint consideram novos inputs efetivos; POST repetido reutiliza receipt.
- Filtrar rewards, shops, rerolls, transformations, preparations e grants explícitos.
- Branch/simulation herdam o snapshot da origem conforme policy.
- Limites de pool vazio e desafios fixed claramente declarados.

Gate: desbloquear B não muda ofertas da run A; mesma seed/revisão/elegibilidade gera mesmos candidatos/sorteios; listas forjadas de cliente são rejeitadas.

Commit: `feat: freeze run eligibility and enforce canonical content grants`.

### Etapa 8 — REST, conteúdo, projeções e integração mínima da Godot

Entregas:

- GET de run/projeções apresenta owner correto dos recursos e snapshot de elegibilidade.
- GET de perfil/unlocks por setting apresenta disponibilidade e provas/marcos apropriados, sem expor dados de outro jogador.
- Novos tipos de policy acessíveis via `GET /api/v1/content/{kind}` e validados pelo mecanismo de publicação existente.
- Endpoints de preview/inspection retornam probabilidades, unidade, origem e grau de certeza.
- Comandos de recuperação continuam atividades canônicas; comandos arbitrários de recurso só por capability existente.
- Atualizar DTOs/OpenAPI/changelog/capabilities e verificação do launcher para host compatível.
- Ajustar somente comunicação/apresentação mínima da Godot para esses contratos. O polimento do ato continua no plano de conteúdo.

Gate: controllers seguem finos; não há cálculo/redução de regras em GDScript; inglês fallback e erros compreensíveis; nenhum endpoint legado duplicado.

Commit: `feat: expose canonical actor resources random previews and eligibility`.

### Etapa 9 — Hotreload, save/load, replay, branches e versão

Entregas:

- Compatibilidade de ativação de resources/policies/chance/eligibility.
- Gates de execução por versão e diagnóstico de runtime indisponível.
- Testes de reinício após captura de eligibilidade, sorteio crítico, cura e resolução de encontro.
- Branches herdam estado sem compartilhamento de mutação; histórico mantém policy/revisão usada.
- Cache invalidation por revisão/snapshot/progressão confirmada.

Gate: hotreload incompatível não muda estado; compatível segue policy; replays não consultam perfil atual nem sorteiam novamente na projeção.

Commit: `test: verify foundation state across reload restart replay and branches`.

### Etapa 10 — Jornada técnica completa e relatório

Entregas:

- Dois settings isolados e dois perfis, ambos via host de teste com storage temporário.
- Roteiro: criar → dano recebido → multi-hit crítico → vitória → resolver → pagar recuperação → desbloquear opção por condição → nova run → nova oferta elegível.
- Verificar run anterior com elegibilidade antiga e setting secundário sem progresso indevido.
- Dez repetições do mesmo roteiro em stores isolados com mesmos inputs externos capturados.
- Medir latência de execução/preview e reconstrução de perfil com vários tamanhos de histórico.
- Documentar contratos finais, exemplos JSON, versão exigida e comportamento do launcher/saves.

Gate: hashes, commits, probabilidades/fatos, transferências e ofertas equivalentes; nenhum save real do usuário usado como banco de testes; regressões Core/API/Godot pertinentes aprovadas.

Commit: `test: certify deterministic world one engine foundations`.

## 9. Matriz mínima de testes

| Grupo | Casos obrigatórios |
|---|---|
| Transporte de recurso | Preserve, ResetMax, ResetValue, EncounterOnly; resource arbitrário; ator inválido/ausente; wallet distinta; max temporário; clamp; limite negativo permitido por definição |
| Resolução | Vitória, derrota, draw, abandono e retry; saída com cura/custo; saída falha; receipt duplicado; persistência falha; recuperação só fora de combate quando exigido |
| Atributos | RunBase persiste; buff temporário não vira base; definição/revisão incompatível falha; personagem só de atributos continua válido onde permitido |
| Sorteio | Literal/calculado, 0/1/fração, bounds/unidade, NaN/infinito, group conflict, Action/ParentProc/Impact, input forjado, ordem canônica e rollback |
| Crítico | 0/50/100/150/200%, buff e alteração entre impactos, N acertos independentes, tier/factor correto, arma versus spell por filtros JSON |
| Orçamento | Flat/source uma vez, pesos, arredondamento/resto, alvo morto, continuação, mitigation por impacto, DoT sem novo crit salvo configuração explícita |
| Preview | N consultas sem mudança de RNG/step/hash; não garante média; limites de trabalho; efeitos condicionais sem falsa faixa exata |
| Progresso | Condições all/any, IDs desconhecidos, ciclos, modes/provenance, duplicate grant, branch/simulation, concorrência, rebuild/incremental e regra revisionada |
| Elegibilidade | Setting/player isolados, fixed challenge, spoof de lista, mudanças posteriores de perfil, pool vazio, recompensa/loja/reroll/upgrade/preparação/deck inicial |
| Persistência | Restart em boundaries diferentes, replay com perfil atual alterado, branch isolada, hotreload compatível/incompatível, versão indisponível |
| Performance | Chance por impacto sem leitura de arquivo, preview repetido, histórico grande, cache por setting e elegibilidade, p95 de comando/resposta útil |

Testes determinísticos com RNG controlado verificam thresholds e cada ramo. Testes estatísticos, se usados, são auxiliares com seeds fixas/tolerâncias e não substituem contratos nem introduzem flakiness.

## 10. Sequência de commits e dependências

`1 contratos → 2 recursos persistentes → 3 fluxo/recuperação → 4 probabilidade → 5 crítico/preview → 6 progresso → 7 elegibilidade → 8 REST → 9 persistência/replay → 10 gate final`

Implementar com commits pequenos por etapa, cada qual compilando e com suas regressões. Testes de save/replay pertencem também a cada etapa que muda estado; a etapa 9 é o gate transversal, não a primeira oportunidade de testá-los.

Ao iniciar a implementação, revisar as mudanças pendentes: este plano, o plano de design e dois `.gd.uid` já estavam sem commit. Não incluir arquivos temporários/saves; não editar ou excluir as mudanças anteriores apenas para obter worktree limpa.

## 11. Critério final de pronto para produzir o mundo 1

- É possível perder um recurso arbitrário no primeiro combate, recuperar fora dele e entrar no segundo com valor canônico correto.
- É possível definir chance dinâmica e níveis críticos em JSON; cada impacto registra seu sorteio e cálculo pela pipeline.
- É possível desbloquear uma opção por condição declarada, por jogador/setting, e consumi-la só em runs cuja elegibilidade permite.
- Consultas/preview/replay não alteram RNG ou progresso.
- Restart, receipts, branches e ativação de conteúdo não duplicam nem perdem transições.
- Engine/API são autoridade; Godot só apresenta e envia input.
- O fundamento funciona numa fixture curta sem exigir dezenas de cartas ou inimigos do ato.

Depois deste gate, executar as etapas de conteúdo e balanceamento do plano Ascendant. Não misturar correção de contrato com tuning de 30 cartas, para que a causa de cada mudança de resultado seja identificável.
