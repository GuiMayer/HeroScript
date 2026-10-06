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
| 5 — Condensação | Pendente | |
| 6 — Transformações | Pendente | |
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

Etapa 5: integrar seleção, avaliação, consumo e ativação por receita em um único proc/transação. As bases da etapa 4 estão implementadas, mas ainda não existe um efeito de condensação executável.

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
