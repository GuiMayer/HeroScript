# Acompanhamento — implementação do core

Plano: [CORE_GAMEPLAY_GAPS_IMPLEMENTATION_PLAN.md](CORE_GAMEPLAY_GAPS_IMPLEMENTATION_PLAN.md).

Início: 2026-10-06. Base Git: `4f362d5`.

## Preservação de alterações existentes

O checkout iniciou com alterações de multi-setting/ascendant, API, documentação e apresentação Godot. Elas não pertencem a esta implementação e não serão incluídas nos commits das etapas sem uma separação explícita dos hunks. `data/configs/ascendant` e seu teste também já existiam como arquivos não rastreados.

Nenhum save, conteúdo publicado ou runtime da demo será apagado para ajustar schemas. Alterações de execução/estado exigem revisão da versão da engine e rejeição explícita de versões incompatíveis. A referência `LEGACY_GAME_DESIGN_CONCEPTS.md` permanece intocada.

## Contratos de entrega

- Um proc é uma ativação identificável, não um efeito específico de recurso.
- Condensação consome todos os stacks da seleção elegível e ativa uma vez; duração não multiplica o agregado implicitamente.
- Política de perda de alvo distingue entrada inválida de derrota causada dentro da própria ação. Ausência de política conserva validação estrita; conteúdo declara skip/stop/retarget.
- IDs e referências de outputs são escopados por ação/proc/componente/alvo; nada depende de log textual ou horário de parede.
- Manter limites atuais de execução: profundidade 32, repetição 256, passos 4096. Novos nós participam desse mesmo orçamento.
- Serialização, hashes, preview e persistência evoluem junto com os contratos, sem store paralelo autoritativo.
- Profiles/receitas e os nomes dos novos campos serão registrados aqui ao implementar cada etapa. Não anunciar schemas planejados como executáveis.

## Baseline

- API: 181 testes aprovados, nenhuma falha.
- Godot 4.7.2: `tests/layers.gd`, `SHOWCASE_LAYER_TESTS failures=0`; teste offline, não valida animações nem engine online.
- Core: 1.135 testes aprovados, nenhuma falha; duração 1m49s.

## Etapas

| Etapa | Estado | Verificação |
| --- | --- | --- |
| 0 — Baseline e contratos | Concluída | Core 1.135 / API 181; Godot layers aprovado; GDD sincronizado. |
| 1 — Alvo derrotado | Concluída | Core 1.153 / API 181, sem falhas; 18 regressões novas. |
| 2 — Contexto e fatos | Concluída | Core 1.162 / API 181, sem falhas; 9 regressões novas. |
| 3 — Parâmetros e cálculo | Concluída | Core 1.185 / API 181, sem falhas; 23 regressões novas. |
| 4 — Payloads e consumo | Concluída | Core 1.226 / API 181, sem falhas; 28 regressões novas na 4b, além das 13 da 4a. |
| 5 — Condensação | Concluída — modo inicial | Core 1.255 / API 181, sem falhas; 29 regressões novas. |
| 6 — Transformações | Concluída — 6a + 6b | Core 1.325 / API 183; 27 regressões Core e 2 API novas na 6b. |
| 7 — Afinidades/modificadores | Pendente | |
| 8 — Multi-hit | Pendente | |
| 9 — Salto por abate | Pendente | |
| 10 — Atributos persistentes | Pendente | |
| 11 — Oportunidades/conteúdo | Pendente | |
| 12 — API/preview | Pendente | |
| 13 — Godot | Pendente | |
| 14 — Verificação integrada | Pendente | |

## Etapa 1 — contrato executável

- `EffectDefinition.targetLoss`: `policy` = `Fail`, `Skip`, `StopRepeat` ou `Retarget`; este último exige `retarget` automático e resource ID para seleção ranqueada.
- A política aplica-se somente a alvos válidos no snapshot de entrada. Alvo ausente/derrotado na entrada continua falhando, inclusive com Skip/Retarget.
- `EffectTargetResolver` substitui a seleção interna duplicada; retarget ordena por ID e usa o RNG determinístico para seleção aleatória.
- Skips/interrupções preservam hashes encadeados nos steps e não sorteiam chance quando não existe alvo ativo. Filhos do efeito pulado não executam.
- Fireball, Venom Cut, Vulnerable e ações inimigas de dano + status declaram Skip para o efeito de status. Conteúdo dos settings pendentes anteriores não foi editado.
- Versão da engine: 7. Saves anteriores são preservados no disco e rejeitados quando incompatíveis, sem conversão silenciosa.
- Baseline da etapa 0: commit `ea4513f`.

## Etapa 2 — contrato executável

- Contexto derivado e imutável por execução; IDs determinísticos de execução, proc e impacto. Filhos registram o proc e impacto exatos do pai. Repetições têm procs distintos; alvos de um mesmo proc têm impactos distintos.
- `outputId` é um alias único e seguro para fórmulas, validado inclusive entre componentes da carta. `results.<alias>.target.last/total` e `parent.*` expõem fatos da execução, não logs. Namespaces reservados não aceitam valores forjados pelo chamador.
- Mudanças de recurso registram valor solicitado antes do clamp, anterior, atual, deltas solicitados/aplicados/limitados com sinal, políticas de threshold resolvidas e causalidade de derrota. Nenhuma interpretação do nome do recurso.
- Aplicação/reaplicação/remoção/dispel de status e modificadores produzem fatos de stacks tipados, com instância, definição, owner e contagem anterior/atual. A visão específica de modificadores é derivada desses fatos, sem ledger concorrente.
- Resultados secundários de settlements não contaminam o alias do recurso primário. Referências desconhecidas, colisões de alias e valores não finitos falham atomicamente.
- IDs e fatos acompanham applications/steps/frames e são serializáveis; dez execuções idênticas produzem os mesmos hashes. A ação de carta/ability expõe seu `effectExecutionId`.
- Versão da engine: 8. Nenhum save anterior foi removido.
- Etapa 1: commit `bf482ef`.

## Etapa 3 — contrato executável

- `EffectDefinition.parameters`: overrides tipados de Amount, StatusStacks/Duration, ModifierStacks/Duration e CardCount. Fórmulas e influências passam pelo resolver/pipeline comum, sem matemática no reducer ou na carta.
- Conversão explícita: rounding, midpoint, sinal, limites e inteiro exato. Overflow, contagem zero/inválida, seleção de IDs incompatível e campos concorrentes falham a transação.
- Pipelines e pedidos declaram `unitId`; pipelines podem organizar buckets em `stages` com scopes Shared/Actor/Target. Não existe conversão implícita entre unidades nem ordem universal imposta à composição.
- Quantidades transportáveis preservam revisão, fingerprint e receipts de estágios já incorporados. Repetir um estágio semântico no mesmo contexto falha inclusive entre perfis diferentes; um novo alvo pode usar seu próprio contexto de defesa.
- Resultados registram checkpoints, valor antes da conversão e resto com sinal. Nenhuma distribuição automática ou targeting fica dentro da matemática.
- `captureOnly` separa cálculo de consumo. Contagens/durações não produzem settlements; capturas de magnitude também não gastam a defesa usada no trace. Providers não avaliam fórmulas/bindings de canais/estágios omitidos.
- Traces dos parâmetros são serializáveis em steps/cálculos; publicação valida referências, canais, unidades e stages explícitos.
- Matemática de delta de atributos é suportada por unidade/política numérica; a mutação persistente de atributos permanece na etapa 10, não foi anunciada como operação de efeito disponível.
- Versão da engine: 9. Sem alteração da referência legada ou remoção de saves.
- Contrato e exemplos: [effect-calculation-profiles.md](../systems/math/effect-calculation-profiles.md).
- Etapa 2: commit `8ada56d`.

## Etapa 4a — leitura e consumo das stores atuais

- Referências tipadas e seleção configurável de status/modifiers; nenhum novo armazenamento autoritativo.
- Capacidade opt-in `consumption.allowedRecipeIds`; periodicidade e stacks não autorizam consumo automaticamente.
- Seleção determinística por store/owner/origem/definição/tags, limitada sem truncamento. Captura preserva fingerprint completo da instância, não apenas contagem.
- Consumo integral exige snapshot idêntico e ainda elegível. Alterações de duração/revisão/definição, referências duplicadas e segunda tentativa de consumo falham antes de publicar uma mutação.
- Fatos de consumo usam a razão Consume; não ativam dispel/expiração e não avançam RNG.
- Regressão comprova que o lifecycle ignora uma instância consumida no mesmo boundary, mesmo quando ela pertence ao snapshot inicial.
- Planos e fatos serializam; dez capturas/consumos equivalentes produzem a mesma ordem/hash.
- Versão da engine: 10. Saves existentes foram preservados.
- Contrato: [accumulated-stacks.md](../systems/effects/accumulated-stacks.md).
- Etapa 3: commit `e69320e`.
- Esta subetapa não conclui a etapa 4: payloads Snapshot/Dynamic, bindings e lotes de intensidades permanecem pendentes. Também não introduz um comando REST de consumo nem anuncia condensação executável.

## Próxima etapa

Etapa 7: gramática data-driven de afinidades e modificadores comportamentais. Relacionar tags/ações a componentes, requisitos/exclusões e capacidades, ordem/escopo nos boundaries executáveis e explicações estruturadas de incompatibilidade. A etapa 6 está concluída; conteúdo demonstrativo, preview especializado e UI Godot continuam nas etapas posteriores.

## Etapa 4b — payloads e lotes

- Status/modifier declaram payloadParameters com evaluation Snapshot/Dynamic por parâmetro, perfil/unidade/stages e política de origem ausente. Aplicação exige runtime revisionado e profile compatível habilitado no modo, inclusive para Dynamic.
- payloadBindings copiam flat/formula ou base do componente da carta efetiva. Intensidade e upgrades não são reconstruídos a partir de uma carta alterada posteriormente.
- Snapshot guarda quantity capturada sem settlements. Dynamic preserva a base e influências constitutivas da carta, mas reavalia o mundo atual. Não congela atores/run nem aceita resultados históricos como fórmula dinâmica.
- payloadReapply PreserveLots mantém intensidades distintas; limites acrescentam apenas os stacks aceitos. Replace/ReplaceAll substituem lotes. Duração continua compartilhada pela instância, sob a política existente.
- Lotes vivem nas stores canônicas. Remoção parcial de modifiers reduz primeiro os lotes mais antigos; expiração e consumo integral removem a instância inteira. Referências de consumo incluem os lotes e os validam por fingerprint completo.
- inputQuantityId payload.<parameterId> fornece a soma ponderada por stacks aos parâmetros de efeitos comuns. Soma usa a camada numérica; unidade/revisão/stages incompatíveis falham. Nenhuma multiplicação implícita pela duração ou ativação por stack.
- Capturas, avaliações e agregações aparecem em payloadCalculations e cálculos do batch. Entram nos limites/hashes/identidade. Lifecycle fornece lotes vivos ao executor comum.
- Limites: 16 parâmetros por definição, 256 lotes por instância, orçamento global de 4.096 passos. Excesso não trunca contribuições.
- 28 regressões novas: Snapshot/Dynamic, origem ausente/derrotada, upgrades/base efetiva, lotes heterogêneos/caps/replacement, partial removal, lifecycle/expiração, rejeição de scaling repetido/unidades/revisões, publicação/bindings, rollback, dez execuções e round-trip serializado.
- Versão da engine: 11. Sem exclusão de saves nem alteração de LEGACY_GAME_DESIGN_CONCEPTS.md. Grants de preparação sem contexto de captura não inventam payloads; são rejeitados se a definição exige um lote.
- Contrato atualizado: [accumulated-stacks.md](../systems/effects/accumulated-stacks.md).
- Etapa 4a: commit 3a20dfa. Condensação por receita permanece na etapa 5.

## Etapa 5 — condensação em um proc

- Efeito CONDENSE_STACKS com condensationRecipeId; catálogo revisionado condensation-recipes registrado no mecanismo comum de conteúdo, sem endpoint paralelo nem nova store.
- Uma seleção autorizada de status/modifiers gera inputs StackCount/Payload e consumo integral. Efeitos comuns de recurso, cartas, status e modifiers recebem os inputs através de inputQuantityId condensation.<parameterId> e da bucket pipeline normal.
- Scope OncePerAction por recipeId reserva a primeira tentativa. Repetições de uma ação não reconsomem stacks, inclusive novos stacks emitidos pela própria receita. Ativação solicitante tem um alvo/repeat 1; componentes e alvos emitidos pertencem ao mesmo procId, com impactos distintos.
- Seleção ActionStart/Current e bindings de owner TargetEntity/SourceEntity/Run/Explicit/Any. Captura inicial não se expande; conflito Fail/Skip não troca silenciosamente os snapshots.
- EvaluationTiming BeforeConsumption/AfterConsumption governa payloads dinâmicos e leituras numéricas dos efeitos emitidos. Mutações continuam no candidato vivo e settlements validam a capacidade real.
- EmptySelection Skip/Fail e ZeroApplication Consume/Fail. Chance falsa não consome; perda de alvo segue a política comum. Consumo/exaustão/hooks têm contratos explícitos iniciais AllSelectedStacks/RemoveInstance/None, sem interpretar dispel/expiração/derrota.
- Seleção, inputs e fatos de consumo aparecem em applications/steps/frames existentes. Último componente que falha descarta todos os candidatos, inclusive zonas, recursos, novos stacks e RNG.
- Publicação valida referências de autorização/receitas, definições selecionadas, schemas/perfis/unidades e bindings. Recursão de condensação em receitas é rejeitada mesmo em filhos nunca executados. Limites permanecem 32/256/4.096; agregação de lotes é limitada antes da expansão.
- StackCount usa unidade stacks e contagem inteira exata limitada a 16.777.216. Payloads preservam unidade, revisão e receipts; misturas incompatíveis falham. O modo inicial não implementa conversão de unidades heterogêneas nem potencial periódico restante automático.
- 29 regressões novas: cura/dano/recurso arbitrário, cartas/status/modifiers, stores/instâncias múltiplas, proc único/impactos distintos, chance/empty/conflict/zero, perda de alvo, Snapshot atual/inicial, influências antes/depois, limitação de expansão, rollback e dez execuções/round-trip.
- Versão da engine: 12. Saves preservados, versões incompatíveis rejeitadas. Demo/conteúdo/preview especializados não foram anunciados como concluídos.
- Contrato e JSON executável: [condensation-recipes.md](../systems/effects/condensation-recipes.md).
- Etapa 4b: commit 2fd4f7c.

## Etapa 6a — ledger e operações sobre a composição

- Upgrades evoluíram para um ledger único com identidade crescente por carta, operação Apply/Remove/Replace, referência à transformação selecionada, categoria e revisão. Nenhuma store autoritativa paralela.
- Removals/replacements internos derivam a sequência ativa e reconstruem a composição. Replacement conserva a posição de aplicação; IDs removidos não são reutilizados. Limite técnico de 1.024 entradas, com falha sem truncamento.
- Patches tipados de tags, componentes Add/Remove/Replace e base flat de parâmetros numéricos. Campos numéricos antigos não podem concorrer com overrides tipados. Quantidade de input não é reinterpretada como uma nova base.
- Compiler comum valida a composição final, incluindo bindings em triggers, aliases, singletons, custos e políticas. Componente Replace substitui explicitamente todo o payload; não há merge automático nem promessa de conservar um valor que o próprio patch sobrescreveu.
- Aplicação atual UPGRADE_CARD já aceita patches estruturais publicados e valida o candidato antes de persistir. Início de run, cenários e hot reload também validam composição/referências revisionadas; o compiler de cenários usa a transição comum e preserva a ordem autoral.
- Contadores e ofertas derivam somente transformações ativas. Zonas/deck/sandbox/inspeção distinguem upgrades ativos do transformationLedger histórico. Base traces da pipeline incluem upgrades dos parâmetros e não atribuem uma base substituída a contribuições sobrescritas.
- Fingerprint efetivo inclui tags e histórico completo, além da definição, identidade, sequência ativa e componentes. Nenhum cache somente por cardId foi adicionado.
- Engine version 13; saves preservados e versões incompatíveis rejeitadas. Referência legada intocada.
- Esta entrega não conclui a etapa 6: bundles com namespace, slots e comandos REST Remove/Replace, discovery e suas regressões de persistência/replay/branches continuam pendentes na 6b. Transições internas de remoção/substituição são apenas candidatos; não habilitam mutações externas diretas.
- Contrato e JSON: [permanent-transformations.md](../systems/cards/permanent-transformations.md).
- Etapa 5: commit 6ce4078.
- Verificação final da 6a: Core 1.298 testes aprovados (1m29s), API 181 aprovados (35s), nenhuma falha. 43 regressões novas: 40 de transformações/transações/cálculo/projeções/versão e 3 de publicação de referências estruturais. Custos alternativos e payloads nulos foram incluídos na validação comum. Nenhuma validação visual Godot foi anunciada nesta entrega.

## Etapa 6b — bundles, slots e comandos de transformação

- `componentBundles` passa a usar referências `bundleId`/`namespace`; o campo sem namespace foi removido, sem fallback legado. Bundles fechados expandem IDs de componentes, bindings locais, aliases e referências inline a resultados; recursos e definições de efeito não são reinterpretados.
- Patches autorais `bundle` Add/Remove/Replace são fechados a partir do runtime fixado antes de entrar no ledger. `bundle_snapshot` persiste componentes imutáveis e é rejeitado na publicação autoral. Namespace agrupa IDs pelo prefixo, sem registry/membership autoritativo paralelo. Colisões e dependências perdidas falham.
- Slots configuráveis na definição da carta: identidade, capacidade, categorias. `slotId` é capturado na transformação. Ocupação deriva do ledger ativo; remoção libera o slot e replacement valida o novo candidato sem duplicar capacidade.
- Comandos `REMOVE_CARD_TRANSFORMATION`/`REPLACE_CARD_TRANSFORMATION` registrados no codec/gateway/handlers/replay normais, sem rotas de mutação paralelas. Identidade selecionada deve estar ativa. Uma mudança aceita avança apenas o passo transacional; não consome RNG.
- `CardTransformationPlanner` concentra política, conteúdo fixado, fechamento, transição pura, compilação e referências. A mesma simulação determina discovery. O controller não consulta mais o catálogo mutável nem interpreta limites/patches. Definições são reutilizadas somente dentro do planner vinculado a um runtime; cartas efetivas não são cacheadas por ID.
- Atividades `CardUpgrade` respeitam a whitelist `upgradeIds`, flags booleanas opt-in `allowRemoval`/`allowReplacement` e conclusão de uma operação. Discovery e execução bloqueiam transformações permanentes com encontro ativo. O modo pode habilitar o opt-in existente de comandos fora de atividade; não há mutação parcial do combate.
- Publicação valida bundles não utilizados, identidade de catálogo, snapshots forjados, slots e referências; ausência de runtime na consulta falha explicitamente em vez de publicar pares crus. Composição final também rejeita aliases de output que perderam sua origem.
- Cenários fecham bundles na revisão correta e preservam a ordem autoral também no payload/fingerprint do cenário. Base efetiva/inspeção expõe slots, ledger e traces ordinários por membro de bundle.
- 27 regressões Core e 2 API novas. Store real em disco + gateway de produção: Apply/Replace/Remove, reinício, retry idempotente e branch isolada. Dez replays semânticos comparam hashes/frames; parent permanece intacto. Testes também cobrem namespaces/aliases/bindings, colisões, capacity/category, ofertas composicionais, whitelist, conclusão, ausência/cross-setting de runtime e rejeição de snapshots autorais.
- Engine version 14. Saves e a referência legada foram preservados. Alterações preexistentes de multi-setting/Godot não entram no commit desta etapa. Nenhum push ou teste visual foi realizado.
- Contrato atualizado: [permanent-transformations.md](../systems/cards/permanent-transformations.md); gateway/discovery em [runs-and-combat.md](../api/runs-and-combat.md).
- Etapa 6a: commit `a298adc`.
- Verificação final da 6b: Core 1.325 aprovados (1m28s), API 183 aprovados (34s), nenhuma falha. Consulta `/available-commands` calcula e enriquece ofertas a partir do mesmo snapshot capturado, sem reler a run no meio da projeção.
