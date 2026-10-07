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
| 7 — Afinidades/modificadores | Concluída | Core 1.364 / API 183; 39 regressões Core novas. |
| 8 — Multi-hit | Concluída — contratos e gate integrado | Sharing/scopes, jornada combinada, persistência e replay na etapa 14. |
| 9 — Salto por abate | Concluída — contratos e gate integrado | Continuação causal executada na jornada; reinício após salto na etapa 14. |
| 10 — Atributos persistentes | Concluída — contratos e jornada | Preparação com traces duráveis e replay na etapa 14. |
| 11 — Oportunidades/conteúdo | Concluída — setting e jornada REST | Nove stops, quatro transformações na mesma instância, dez runs idênticas. |
| 12 — API/preview | Concluída | Core 1.518 / API 184; escopo do snapshot, procs e before/after. |
| 13 — Godot | Concluída | Layers, volatile_core, resolutions, smoke, UI smoke e gameplay polish: zero falhas. |
| 14 — Verificação integrada | Implementação técnica concluída; playtest humano pendente | Core 1.524 / API 186; dez jornadas idênticas; Godot aprovado. |

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

Etapa 9: continuação causal por abate e transporte de excedente pelo caminho numérico comum. A 8b2 já implementa orçamento residual compartilhado, scopes de chance/inputs aleatórios/gatilhos e os boundaries reais de BeforeImpact/OncePerProc. A verificação persistente integrada das combinações continua no gate final da etapa 14, sem chamar dez execuções de um executor de replay semântico completo.

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

## Etapa 7 — gramática de afinidades/comportamentos

- `requirements` e `compositionRules` nos upgrades existentes; predicados Required/ExcludedTags/Capabilities e seleção de efeitos raiz por tipo, identidade e tags. Nenhum registry de elementos nem branch de fogo/veneno no processador.
- Todas as entradas ativas aplicam seus patches primeiro. Requisitos e seleção usam a base estrutural final, já incluindo upgrades numéricos posteriores. Efeitos gerados não retroalimentam a seleção; fontes contextuais permanecem na bucket pipeline.
- Regras se fecham com bundles do runtime fixado; snapshots imutáveis entram no ledger. Definição autoral não fechada é rejeitada na transição. Remover/substituir reconstrói a carta e elimina contribuições antigas; remoção que quebra requisitos de outra entrada falha.
- Ordem ativa do ledger, depois priority/ruleId, depois order/componentId. BeforeSequence/AfterImpact/AfterSequence são lowering para raízes/filhos comuns. BeforeImpact/OncePerProc são reservados e rejeitados até existir uma fronteira executável correta, sem fallback silencioso.
- Bundles podem declarar effectComponentParameters para bindings externos de base permanente. `$anchor` aponta para o componente estrutural selecionado; IDs explícitos também são aceitos. Bindings finais usam a carta efetiva e não quantidades já escaladas. Parâmetros ausentes/extras ou âncora ambígua falham.
- Capabilities, compositionTrace e fingerprints da base efetiva expõem origem/âncoras/membros. `CardTransformationPlanner.Assess` fornece diagnóstico estruturado e IDs modificados usando o mesmo planner, sem mutação/RNG. Endpoint especializado e UI permanecem nas etapas 12/13; a inspeção existente já inclui o trace em effectiveBase.
- Publicação verifica templates/referências mesmo condicionais/inativos; pré-requisitos que outra transformação pode satisfazer não obrigam toda carta base a ser elegível. Candidato final sempre passa pelos compilers/validators comuns.
- Limites 32 regras/transformação, 64 membros/bundle de regra, 256 expansões/carta, 64 símbolos/lista; orçamento agregado de 4.096 nós de efeitos, inclusive filhos. Colisões, duplicatas, contradições, overflow e scopes não executáveis falham sem truncamento.
- Pacote base registra `core.ember.affinity`: basic_attack recebe burning no alvo (Skip após derrota); heal recebe regeneration no próprio ator. Slot affinity de capacidade 1, stacks 1/duração 3, custos intactos. Intensidade inicial usa os status atuais, não scaling proporcional implícito. Oportunidades da jornada não foram alteradas nesta etapa.
- 39 regressões Core novas. Pipeline configurada real verifica base melhorada + scaling do residual; executor comum verifica chance/repeats/scopes. Dez execuções, snapshots serializados, gateway/store reais com reinício/retry/dez replays semânticos e branch isolada. Testes de publicação e setting default comprovam os mapeamentos dano/cura.
- Verificação: Core 1.364 aprovados (1m26s), API 183 aprovados (35s), nenhuma falha. Engine version 15; saves e referência legada preservados. Sem alteração/teste visual Godot e sem push. Alterações preexistentes continuam fora do commit.
- Contrato: [composition-grammar.md](../systems/cards/composition-grammar.md). Etapa 6b: commit `270d8dd`.

## Etapa 8a — distribuição numérica de orçamento

- `ICalculationEngine.Distribute` integra a distribuição à autoridade numérica existente. Não conhece recursos, efeitos ou alvos reais; recebe quantity capturada, política e destinatários opacos. Não executa fórmulas externas, mutações ou settlements.
- Continuous divide na malha binária do Float32 da entrada, conservando exatamente sua soma em Double. Quantized usa quantum explícito e exige múltiplo exato, parcelas representáveis e até 16.777.216 unidades. Sem rounding/truncamento implícitos.
- Destinatários ordenam por order e ID ordinal; Earliest/Latest configura o viés do resto. Limite 4.096 destinatários, com falha para excesso/duplicatas/identidades inválidas.
- Parcelas preservam unidade/revisão/receipts e recebem fingerprints próprios derivados do trace de distribuição. Orçamento já influenciado por alvo exige opt-in; nenhum receipt é apagado para permitir scaling duplicado.
- Trace registra quantum, unidades, resto, parcelas e total conservado. Pedido/resultado serializam e as coleções são imutáveis. Dez distribuições equivalentes produzem hashes iguais.
- 59 regressões: 10 em 3 parcelas; bias; zero/negativos/subnormais/extremos; 128 padrões de bits; quantums fracionários; limites; rejeições; ordens; round-trip; proveniência; captura de bônus uma vez e stages restantes por parcela. Composição manual com pipeline/planner/processador reais comprova consumo de defesa 4/1/0 e reduções 0/3/4 para orçamento 12 e defesa 5.
- Core 1.423 aprovados (1m49s), API 183 aprovados (39s), nenhuma falha. Engine version permanece 15: não houve alteração da execução/estado/hashes existentes. Nenhum conteúdo, save, UI Godot ou alteração preexistente foi incluído.
- **Pendente 8b:** policy JSON de efeitos, planejamento/executor de impactos, zero stacks como skip, scopes de chance/críticos/triggers, perda/retarget, condensação e verificação transacional/replay. Os testes da 8a não alegam que multi-hit já esteja integrado automaticamente ao gameplay.
- Contrato: [quantity-distribution.md](../systems/calculations/quantity-distribution.md). Etapa 7: commit `cfc38c7`.

## Etapa 8b1 — orçamento por sequência no executor comum

- `parameters[].distribution` opt-in para Amount e stacks aplicados. Sem o campo, repeat continua recalculando o efeito completo. Perfil explícito, stages de origem como prefixo e stages de impacto como sufixo disjunto; validação comum e publicação rejeitam políticas incompatíveis, inclusive em filhos dormentes.
- `EffectSequenceBudgetPlanner` preserva o snapshot de entrada, captura origem uma vez sem settlements e delega toda aritmética a `ICalculationEngine.Distribute`. Slots por índice de repeat; target e impacto não entram na fórmula da origem. Nenhuma alteração automática da pipeline default.
- Cada impacto continua pela pipeline com sua quantity/receipts e usa alvo/capacidade vivos. Defesa 5 sobre orçamento 12 em três parcelas consome 4/1/0 e produz reduções 0/3/4. Source bonus não é reaplicado. Stage Actor não pode reaparecer no sufixo.
- Amount aceita Continuous/Quantized; stacks exigem Quantized quantum 1. Zero stacks gera skip zero_contribution, sem reducer, payload ou filhos; zero após stage de alvo também registra cálculo e pula. Zero Amount conserva o contrato normal de efeitos.
- PerSequence acrescenta um sorteio por chamada da sequência; PerEffect/PerTarget preservam seus significados. Condições por impacto, slots pulados/perdidos sem redistribuição. Não representa chance global da ação nem implementa críticos/triggers globais.
- Um alvo por impacto, seleção sem RNG extra para planejamento. Skip/StopRepeat/Retarget existentes conservam autoridade e índice da parcela. Ausência de candidato interrompe; seleção inválida na entrada, grupos e múltiplos targets explícitos falham.
- Condensação emite impactos distribuídos no mesmo proc e continua OncePerAction. BeforeConsumption controla o snapshot de captura; distribution opt-in usa defesa viva por impacto. Parâmetros não distribuídos preservam a política anterior. Falha posterior descarta recursos, defesa, stacks/modifiers, consumo e RNG.
- sequenceBudgets expõe captura/alocação uma vez; impactShares referencia parcelas, inclusive em perda/retarget. Traces entram nos hashes/frames existentes e serializam; sem store paralela. Capturas/slots contam no limite global. Resolução direta e payload schemas não ignoram distribution não planejada.
- 35 regressões novas com runtime fixado e pipeline/providers/settlements/reducers reais: repeats tradicionais, origem imutável, conservação contínua/inteira, bias, zeros, defesa, chance, perda/retarget/ausência, condensação, rollback, publicação/branches dormentes, dez execuções e round-trip de definições/steps.
- Engine version 16; saves anteriores preservados e incompatíveis rejeitados. Referência legada e alterações preexistentes de settings/Godot continuam intocadas. Nenhum push nem teste visual desta entrega.
- **Pendente 8b2:** orçamento residual compartilhado entre pai/filhos, scopes globais de chance/críticos/triggers e boundaries reservados. Persistência/reinício/replay/branches específicos de multi-hit no gateway também precisam de regressões; dez execuções no executor não são anunciadas como replay semântico completo. Etapa 8 continua em andamento.
- Contrato e JSON: [sequence-budgets.md](../systems/effects/sequence-budgets.md). Etapa 8a: commit `769e1da`.
- Verificação final da 8b1: Core 1.458 aprovados (1m33s), API 183 aprovados (42s), nenhuma falha. Não há teste visual Godot nem replay semântico persistente específico de multi-hit nesta entrega.

## Etapa 8b2 — residual compartilhado e scopes executáveis

- distribution.scope Sequence/ParentSequence; filho com repeat 1 captura uma vez no snapshot do pai e recebe sua parcela por índice. Irmãos isolados e frames efetivos preservados na árvore; múltiplos alvos por impacto não duplicam slots. Ausência de pai e escopos misturados falham.
- PerAction/PerProc de chance usam identidade lógica da definição, não caminho expandido de repeat. chanceGroupId compartilha nós explicitamente; probabilidades conflitantes são inválidas.
- randomInputs com Action/ParentProc/Impact produzem fatos imutáveis e o namespace rolls.<inputId>.success. groupId permite sharing explícito Action/ParentProc. Namespace externo forjado falha. Executor não multiplica dano nem sabe o significado de critical; fórmulas/providers/pipeline usam o input. Captura de orçamento recebe apenas inputs Action.
- executionScope EveryInvocation/OncePerAction/OncePerParentProc e executionGroupId declaram tentativas, sem ativação implícita. Scope skip e lotes/slots continuam limitados pelo orçamento técnico.
- childTiming BeforeParentImpact/AfterParentImpact executa no boundary real. Antes do cálculo, variáveis refletem mutações dos filhos; derrota do alvo respeita policy do pai. Traces permanecem encadeados. Zero stacks não ativa filhos anteriores. Composição BeforeImpact/OncePerProc agora fecha para esses contratos comuns; teste antigo de rejeição foi atualizado para scope desconhecido.
- Engine version 17. Sem mudança automática dos settings, remoção de saves, implementação visual ou push. Mudanças preexistentes permanecem fora da entrega.
- Testes de budgets compartilhados, irmãos, zeros, scopes, grupos, RNG, source bonus crítico uma vez, snapshots preservados, dez execuções e rollback. Validação integrada de runs/save/load/replay/branches das novas combinações será completada no gate final, após existir o setting demonstrativo.
- Core: 1.478 aprovados (18s). Auditoria de contratos públicos poda diretórios gerados antes da leitura; não percorre saves `.runtime` como código-fonte. Saves existentes não foram removidos. Teste de loja seleciona oferta que o saldo pode pagar, sem depender da ordem sorteada.
- API: 183 aprovados (1m06s), nenhuma falha. O gate final continua obrigatório para as combinações novas na jornada real.

## Etapa 9 — continuação causal

- `continuation` no efeito comum; causalidade exige derrota produzida pelo registro primário daquele impacto, qualquer recurso. Hijack de derrota de filhos/settlements não é permitido.
- `EffectContinuationPlanner` usa pedido numérico canônico e perfil autoral de overflow, com requested/applied/limited facts. Unidade/revisão/receipts preservados; próximos stages exclusivamente Target e nenhuma reaplicação da origem.
- Selectors comuns, relações, ordenação, exclusão de visitados, limites e RNG determinístico. Chance/condition/inputs Impact/filhos só acompanham conforme flags explícitas. Parcela atual de multi-hit e sua continuação não confundem o tratamento dos repeats restantes.
- Condensação consome uma vez e somente sua saída autorizada continua no mesmo proc; impactos têm identidade/parentImpactId próprios. Traces completos e motivos de parada na estrutura comum. Qualquer falha posterior descarta todos os candidatos.
- Policies e perfis validados na publicação/execução, inclusive em filhos dormentes. Pipelines também aceitam os namespaces tipados de random/continuação; não aceitam payload externo forjado.
- Engine version 18; saves e mudanças preexistentes preservados. Contrato: [causal-continuation.md](../systems/effects/causal-continuation.md).
- Verificação: Core 1.500 aprovados (12s), API 183 (51s), nenhuma falha. Vinte e duas regressões: transporte 15/10/5, defesa por alvo, origem única, unidade, morte não-health, causalidade de filhos, zero/limites/ausência, políticas, multi-hit, condensação, rollback, publicação dormente, dez execuções e round-trip. Sem afirmação de replay semântico integrado ou teste visual nesta entrega.

## Etapa 10 — base persistente e operações de atributos

- `playerDefinitionId` captura `playerEntity` (identidade/revisão/stats) na run. Entrada de encontro materializa a base antes da inicialização; mudanças Encounter não sobrescrevem RunBase. Recursos/inventário/abilities não recebem autoridades novas.
- `valueRules` autoriza Add/Multiply/Set por componente/value ID, com bounds finitos. `MODIFY_ATTRIBUTE` usa Amount pelo resolver/pipeline comum, reducer puro e `attributeOutcome`. Lifetime RunBase somente fora do encontro; Encounter somente no ator vivo.
- Opções publicadas de preparação capturam `effects`; o comando canônico resolve IDs/custos/grants/efeitos e persiste atomicamente. Boundaries validam toda a árvore, não só raízes. Nenhum endpoint de patches arbitrários.
- Rebase valida schema/valores/novos bounds e conserva investimentos. Inspeção separa persistentActor de actor; provider de stats mantém filtros/IDs configuráveis. Buffs e upgrades da carta permanecem separados.
- Engine version 19. Regressões incluem operações/limites, snapshot, candidato vivo, vários tags elegíveis, buff expirado, rollback, rebase, round-trip e store real com reinício/retry/dez replays semânticos/fork isolado.
- Contrato: [persistent-attributes.md](../systems/entities/persistent-attributes.md). Alterações preexistentes continuam fora da entrega; em CombatRunCoordinator somente o hook de materialização pertence à etapa.
- Verificação final: Core 1.511 aprovados (13s), API 183 (56s), nenhuma falha. Snapshot/hash, progressão e replays usam o store/gateway existentes; não foi adicionado save paralelo.

## Etapa 11 — setting e oportunidades do core

- Package/setting isolado `volatile-core` / Volatile Crucible; depende da base, não altera default/ascendant. Sete cartas, duas afinidades, upgrade base, multi-hit/cascade combináveis, três encontros, escolhas de aquisição/transformação/preparação.
- Condensação de potência para dano/cura e contagem para APPLY_MODIFIER; residual snapshot e orçamento ParentSequence explícitos. Power persistente entra no stage de origem somente em cartas elegíveis. Custos publicados e intents/gambits existentes.
- Patch tipado `effect_continuation` preserva outras partes do efeito. Atividade CardUpgrade aceita custos, consumidos pelo comando e verificados pelo mesmo planner usado em assessment/discovery; opções expõem costs. Não há substituição global do catálogo nem novo manager.
- Seis testes do package/composição/execução/condensação/isolamento e um de affordability/discovery. Jornada REST/reinício/replay/forks/benchmark e UI serão gates posteriores, sem afirmar que o cliente já apresenta as novas mecânicas.
- Guia: [volatile-core-setting.md](../content/volatile-core-setting.md).
- Verificação: Core 1.518 aprovados (20s), API 183 (1m25s), nenhuma falha. O stage 12 amplia consultas/preview e o stage 13 apresenta os contratos na Godot.
# Etapa 12 — contratos de apresentação

Concluída: assessment de transformação antes/depois, custos e versão; evaluations
com escopo do snapshot e agrupamento de procs/consumos/continuações. Controllers
delegam ao planner; GET não executa mutations nem revela cartas de zonas ocultas.
OpenAPI e guia REST atualizados. Core: 1.518 testes; API: 184 testes aprovados.
O gate integrado de preview/execução do setting continua na etapa 14.
# Etapa 13 — apresentação Godot

Concluída: escolha de Apply/Remove/Replace mantém IDs e custos publicados;
confirmação consulta before/after; tooltip apresenta parâmetros, consumo e saltos;
um proc condensado tem uma ativação visual, preservando todos os registros.
Manifesto tem slots individuais `core_*`, nomes PT/EN e fallback inglês.
Godot layers, volatile_core, resolutions, smoke, UI smoke e gameplay polish:
zero falhas. Layout cobre mão vazia e até 3840×2160, incluindo 2560×1080.
Verificação visual humana e balanceamento são gates separados.

## Etapa 14 — gate técnico final

- Engine version 20: RNG único em atividades sintéticas e traces de preparação/
  boundaries preservados pelo plano e coordenador, nos frames/fatos do commit.
- Validação de atributos em ativação/rebase e referências de continuação em
  upgrades dormentes; nenhum save ou snapshot histórico foi reescrito.
- Jornada publicada com nove stops; quatro upgrades na mesma instância por
  oportunidades normais. Modo sandbox autoriza branches por policy JSON.
- Core: 1.524 aprovados, zero falhas/skip (26s). API: 186 aprovados; jornada de dez
  repetições (4m29s) e 185 regressões (1m16s), zero falhas/skip. Grupos separados para não confundir
  a comparação determinística com a duração das outras suítes.
- Dez hosts/stores: 47 comandos em cada run, recibos e commits integrais idênticos.
  Hash final `229f2c00a8ac6136a8b377492dc9c5c65d0da02b2036c9dd44d00ace225ea476`.
  Reinício após transformação/salto; fork com acúmulo, consumo isolado e replay.
- Godot layers, volatile_core, volatile_core_online, resolutions, smoke, UI smoke,
  gameplay polish e diálogo: validação headless. O teste de diálogo foi migrado
  do campo deck removido para a projeção genérica de zonas, sem fallback legado.
- p95 HTTP core 310,77ms (p99 528,21ms, max 1.301,98ms); TCP Godot 972ms
  (max 1.418ms). Executor medido com 2/48/98 stacks e 2/20/100 alvos. p95 abaixo
  de 1s no ambiente medido, **não** promessa de todas as respostas abaixo de 1s.
- Documentação de conteúdo, atributos, GDD, OpenAPI, README e guia Godot atualizada.
  Referência legada e mudanças preexistentes preservadas; nenhum push.
- Gates integrados das etapas 8–11 fechados tecnicamente. Entendimento, diversão,
  ritmo e balanceamento dependem de playtest humano, não de snapshots aprovados.

Evidências, medições e limites: [relatório final](CORE_GAMEPLAY_GAPS_VALIDATION_REPORT.md).
Commit: `test: verify deterministic core runs and document final contracts`.
