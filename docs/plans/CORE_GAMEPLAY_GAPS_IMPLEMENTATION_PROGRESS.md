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
| 2 — Contexto e fatos | Pendente | |
| 3 — Parâmetros e cálculo | Pendente | |
| 4 — Payloads e consumo | Pendente | |
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
