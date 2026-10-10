# Ascendant — conteúdo do mundo 1

## Estado

Implementação inicial em andamento. O fundamento da engine foi certificado separadamente; este documento acompanha o conteúdo do ato. A meta de 25–40 minutos e a qualidade do balanceamento dependem de sessões humanas, ainda não realizadas.

## Contratos de conteúdo

- Namespace `w1_`; o showcase antigo permanece como fixture de regressão, não como balanceamento do ato.
- Vida inicial 100 e persistente; energia 3 por ativação; mão 5. Só energia paga cartas. Ouro paga serviços, não ações de combate.
- Aumento de ataque, magia e elemento entra no mesmo bucket aditivo. Multiplicadores `more`, vulnerabilidade, defesa e crítico têm etapas distintas.
- Crítico por impacto: níveis garantidos mais fração restante, bônus linear. A etapa crítica Shared só usa fatos do sorteio daquele impacto; não reaplica atributos de origem.
- Multi-hit divide um orçamento de origem antes dos sorteios individuais. Área paga por cobertura, calculada separadamente por alvo; não usa a distribuição de sequência de alvo único.
- Residuais capturam potência na aplicação e só executam as etapas do alvo no tick. Não sorteiam crítico novamente.
- Condensação inicial libera **150% da soma das potências atuais**, consome todos os lotes selecionados e não reaplica scaling de origem. Não corresponde à soma de todos os ticks futuros: ponderação por duração restante ainda não é um agregado da engine. Esta escolha fica explícita no catálogo, em vez de simular suporte inexistente.
- Reserva defensiva usa a mesma infraestrutura de payload/condensação, mas produz Block. Cura e investimentos permanentes apenas no combate esgotam a carta; não podem ser reciclados infinitamente.
- Identidades de regras, chaves e fallback em inglês; nomes localizados em português. Arte usa `placeholder.w1_*` nos componentes compartilhados.

## Catálogo inicial de cartas

| ID | Energia | Raridade | Função |
| --- | ---: | --- | --- |
| w1_strike | 1 | Common | Reliable single-target damage. |
| w1_guard | 1 | Common | Baseline mitigation. |
| w1_scorch | 1 | Common | Immediate damage plus a short residual. |
| w1_poison | 1 | Common | Delayed single-target pressure. |
| w1_release | 1 | Uncommon | Consume all Scorch/Corrosion potency in one proc at 150%; not remaining-duration damage. |
| w1_precision | 1 | Common | Card-local +50 critical chance. |
| w1_focus | 1 | Uncommon | One-time combat investment: +80 critical chance. |
| w1_lance | 2 | Rare | Card-local +75 critical chance; supports guaranteed tiers. |
| w1_flurry | 1 | Common | One shared budget split into two independent crit rolls. |
| w1_barrage | 2 | Uncommon | Shared source budget; four per-impact rolls. |
| w1_sweep | 2 | Uncommon | Pays for coverage rather than single-target burst. |
| w1_shield_bash | 1 | Common | Bridge between defense and offense. |
| w1_fortify | 1 | Uncommon | 35% increased Block for this encounter. |
| w1_bulwark | 2 | Uncommon | Efficient defense, at the cost of offensive tempo. |
| w1_reserve | 1 | Common | Defend now and store a future Block proc. |
| w1_release_guard | 0 | Uncommon | Consumes defense reserve, not damage stacks. |
| w1_fracture | 1 | Common | Makes following impacts more effective. |
| w1_dampen | 1 | Common | Reduce one actor's outgoing damage instead of racing it. |
| w1_recall | 1 | Common | Convert energy into hand options. |
| w1_insight | 0 | Uncommon | Exhausts; cannot cycle forever. |
| w1_mend | 1 | Uncommon | Limited recovery; exhausts each encounter. |
| w1_momentum | 1 | Uncommon | One-time 30% increased damage investment. |
| w1_catalyst | 1 | Uncommon | Captured residual potency scales at application, not again on release. |
| w1_venom_flurry | 2 | Uncommon | Multiple hits plus one status application, not one free payload per hit. |
| w1_inferno | 2 | Uncommon | Delayed coverage for multi-actor encounters. |
| w1_detonate | 2 | Rare | Cash-out before attacking; avoids a duplicate tick. |
| w1_riposte | 1 | Uncommon | Consumes own Block after converting 75% into an attack. |
| w1_pierce | 1 | Uncommon | Trade base damage for ignoring Block. |
| w1_rehearse | 1 | Rare | Critical/draw sidegrade unlocked after reaching world-one completion. |
| w1_renewal | 1 | Rare | A second limited recovery option; unlock, not permanent stats. |

## Verificação por etapa

1. Cartas, pipelines, payloads e operador: 30 cartas compiladas; custos exclusivamente em energia; grafo de conteúdo validado e fixtures antigas preservadas.
2. Inimigos, serviços, rotas e apresentação: próximas etapas.
3. Jornadas e balanceamento: registrar evidências sem substituir playtest humano por sucesso de bot.

