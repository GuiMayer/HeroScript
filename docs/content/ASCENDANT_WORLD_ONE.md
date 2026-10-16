# Ascendant — conteúdo do mundo 1

## Estado

Catálogo e grafo do primeiro ato implementados como alpha de playtest. O fundamento da engine foi certificado separadamente. A meta de 25–40 minutos e a qualidade do balanceamento dependem de sessões humanas, ainda não realizadas.

## Contratos de conteúdo

- Namespace `w1_`; o showcase antigo permanece como fixture de regressão, não como balanceamento do ato.
- Vida inicial 100 e persistente; energia 3 por ativação; mão 5. Energia é o orçamento de ação. Blood Fuel possui um custo adicional explícito de 4 Vida, deixando ao menos 1. Ouro paga serviços, não ações de combate.
- Aumento de ataque, magia e elemento entra no mesmo bucket aditivo. Multiplicadores `more`, vulnerabilidade, defesa e crítico têm etapas distintas.
- Crítico por impacto: níveis garantidos mais fração restante, bônus linear. A etapa crítica Shared só usa fatos do sorteio daquele impacto; não reaplica atributos de origem.
- Política global `w1_global_effect_rules`: chance inicial 15%, bônus crítico 100% por tier e Armor inicial 0. Armor usa K=100 antes de Block, sem consumir o atributo. [Configuração e API](hero-attribute-scaling.md). `w1_pierce` ignora Block, mas não Armor, e declara que não pode critar.
- Multi-hit pode dividir um orçamento de origem (Twin Blades) ou aplicar o valor completo por impacto (Needle Storm). Ambos sorteiam crítico por impacto. Área paga por cobertura, calculada separadamente por alvo; não usa a distribuição de sequência de alvo único.
- Residuais capturam potência na aplicação e só executam as etapas do alvo no tick. Não sorteiam crítico novamente.
- Condensação inicial libera **150% da soma das potências atuais**, consome todos os lotes selecionados e não reaplica scaling de origem. Não corresponde à soma de todos os ticks futuros: ponderação por duração restante ainda não é um agregado da engine. Esta escolha fica explícita no catálogo, em vez de simular suporte inexistente.
- Reserva defensiva usa a mesma infraestrutura de payload/condensação, mas produz Block. Cura e investimentos permanentes apenas no combate esgotam a carta; não podem ser reciclados infinitamente.
- Identidades de regras, chaves e fallback em inglês; nomes localizados em português. Arte usa `placeholder.w1_*` nos componentes compartilhados.

## Catálogo inicial de cartas

| ID | Energia | Raridade | Função |
| --- | ---: | --- | --- |
| w1_strike | 1 | Common | Reliable single-target damage. |
| w1_guard | 1 | Common | Baseline mitigation. |
| w1_last_guard | 1 | Common | 14 Block; exile after play. One replaces a guard in the initial ten-card deck. |
| w1_second_wind | 2 | Uncommon | 10 Health + 8 Block; exile after play. Reward pool. |
| w1_solar_burst | 2 | Common | 12 damage + Scorch potency 4; exile after play. Reward pool. |
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

### Expansão por tags: 24 novas bases

O catálogo ativo agora contém **57 cartas**: 33 em `cards/world_one.json`
e 24 nos cinco arquivos `cards/tag_synergy_*.json`. O deck inicial de dez
cartas permanece inalterado. O pool normal mantém sua curadoria explícita e
inclui as 24 novas bases por raridade. O laboratório usa um pool por tags
e cinco cenários isolados; não concede progresso permanente.

| ID | Energia | Raridade | Regra |
| --- | ---: | --- | --- |
| w1_power_surge | 0 | Uncommon | Gain 2 Energy, up to its maximum. Exile. |
| w1_blood_fuel | 0 | Common | Pay 4 Health, leaving at least 1. Gain 2 Energy. Exile. |
| w1_charge_cell | 1 | Common | Store 2 Energy potency for a later release. Exile. |
| w1_release_charge | 0 | Uncommon | Consume all your Energy reserves; gain their potency once, up to maximum Energy. |
| w1_guarded_strike | 1 | Common | Deal 4 + 50% of current Block as damage. Keep your Block. |
| w1_guard_wave | 2 | Rare | Deal 50% of current Block to each enemy, then consume your Block once. |
| w1_desperate_guard | 1 | Common | Gain 4 Block + 20% of missing Health. |
| w1_shelter | 1 | Uncommon | Recover 2 Health + 10% of current Block. Exile. |
| w1_cinder_brand | 1 | Common | Apply 3 captured fire potency for 4 enemy end activations. |
| w1_volatile_rot | 1 | Common | Apply 6 captured poison potency for 1 enemy end activation. |
| w1_caustic_guard | 1 | Common | Gain 3 Block. Apply 2 captured poison potency for 2 enemy end activations. |
| w1_lingering_aegis | 1 | Uncommon | Gain captured 3 Block at the start of your next 2 activations. |
| w1_vital_surge | 1 | Uncommon | Recover 10% of maximum Health. Exile. |
| w1_mending_echo | 1 | Common | Recover captured 2 Health at the start of your next 3 activations. Exile. |
| w1_release_mending | 0 | Uncommon | Consume all compatible healing payloads for one recovery equal to their potency, not their remaining ticks. |
| w1_delayed_recovery | 1 | Common | Recover captured 8 Health at the start of your next activation, once. Exile. |
| w1_seek_attack | 1 | Common | Move up to 1 attack card(s) from draw to hand, in zone order. |
| w1_gather_defense | 1 | Common | Move up to 2 defense card(s) from draw to hand, in zone order. |
| w1_recover_trace | 1 | Common | Move up to 1 residual card(s) from discard to hand, in zone order. |
| w1_prepare_guard | 0 | Common | Move up to 1 defense card(s) from discard to draw, in zone order. |
| w1_needle_storm | 1 | Common | Deal 2 base damage per hit, 3 hits. Each hit rolls critical tiers separately. |
| w1_heavy_impact | 3 | Rare | Deal 28 base damage in one impact. |
| w1_break_guard | 1 | Common | Remove 8 Block (fixed, no critical or Armor), then deal 4 base Health damage. |
| w1_relay_strike | 2 | Uncommon | Deal 14 base damage. On a kill, excess continues to one more enemy without reapplying source bonuses. |

Reservas e condensação distinguem dano, Bloqueio, cura e Energia por tags de
payload e autorização de receita. Energia não recebe scaling ofensivo.
Cura e geração direta de Energia têm produtores de uso único; uma liberação
vazia não gera recursos. Durações contam ativações do dono, não turnos globais.
Detalhes e evidências: [registro da expansão](TAG_SYNERGY_EXPANSION.md).

1. Cartas, pipelines, payloads e operador: 33 cartas compiladas (30 originais + 3 opções de uso único); custos exclusivamente em energia; grafo de conteúdo validado e fixtures antigas preservadas. [Queima de exílio e conteúdo adicional](../design/CARD_EXILE_ANIMATION.md).
2. Inimigos: oito comuns, três elites e um chefe, com ciclos por round e intenções fixadas até a ativação. Serviços e grafo publicados. Desempate de iniciativa favorece o jogador, evitando ataque de entrada não anunciado.
3. Apresentação: componentes compartilhados, nomes/descrições em inglês e português, riscos/custos nos metadados e launch atualizado. A navegação continua vindo das escolhas da API, não das arestas desenhadas.
4. Verificação técnica e pendências: [relatório do ato](../plans/ASCENDANT_WORLD_1_CONTENT_VALIDATION_REPORT.md). Vitória de uma política automática não substitui playtest nem garante viabilidade de todas as builds.

### Quantização explícita

A pipeline padrão do ato usa a unidade `scalar`, inclusive nos valores simples
de defesa/cura e nas habilidades dos inimigos. Ataques críticos tipados usam
`points`. Antes de distribuir um orçamento de origem em impactos inteiros, o
bucket `more` arredonda com `AwayFromZero`. Assim, redução de 25% ou amplificação
de 20% não produz orçamento fracionário incompatível com `quantum: 1` nem
invalida ações da IA. A pipeline de payload residual conserva sua potência
fracionária; a liquidação no alvo continua sendo outra etapa.

## Ameaças

Cada entidade usa habilidades/gambits próprios. O chefe troca de ciclo com vida ≤50%; após o round 12 anuncia Cataclysm. Não existe imunidade a crítico, DoT ou bloqueio. Os números abaixo são nominais: preview e pipeline calculam vulnerabilidade, redução e mitigação.

| ID | Classe | Vida | Ciclo anunciado |
| --- | --- | ---: | --- |
| w1_scout | Common | 28 | Probe: 8 |
| w1_mite | Common | 22 | Double bite: 4 × 2 |
| w1_sentinel | Common | 38 | Brace: 5 + 8 Block → Strike: 10 |
| w1_spitter | Common | 30 | Corrosive spit: 4 + residual 2 → Bite: 7 |
| w1_charger | Common | 46 | Wind-up: 5 → Charge: 14 |
| w1_binder | Common | 34 | Bind: 6 + Weakened → Pulse: 8 |
| w1_swarm | Common | 28 | Swarm: 4 × 3 |
| w1_artillery | Common | 44 | Stabilize: 7 + 4 Block → Crystal shell: 15 |
| w1_bulwark_elite | Elite | 96 | Bastion: 8 + 12 Block → Crushing blow: 18 |
| w1_corrosion_elite | Elite | 84 | Archive venom: 7 + residual 3 → Toxic strike: 14 |
| w1_prism_elite | Elite | 100 | Prism volley: 6 × 3 → Shatter: 24 → Refract: 9 + 6 Block |
| w1_prime | Boss | 180 | Survey: 12 → World fracture: 20 → Rebuild: 10 + 10 Block |

## Rotas e economia

- 14 paradas principais por caminho; nove combates (chefe incluso), oito ofertas intermediárias de três cartas. Telas de recompensa não contam como parada principal.
- Doze templates comuns, três elites opcionais na nona parada; rota sem elite sempre disponível. As ramificações convergem e nenhuma é um beco sem saída.
- Ouro inicial 30; encontros rendem 12–20, elites 32. Cartas da loja custam 25/38/55. Rerolls são pagos, sem opções gratuitas ilimitadas.
- Forja inicial e correção intermediária: uma operação por visita. A forja final custa 10 ouro e substitui descanso de 25 vida. Remover/substituir **transformações** não significa apagar uma carta do deck: remoção permanente de carta não está sendo anunciada como serviço suportado.
- A primeira recompensa de relíquia oferece crítico/defesa/residual. Elites rendem uma relíquia específica; recompensa final oferece recuperação limitada/reserva defensiva. Escolher uma rota exclui as outras, sem precisar de regras na Godot.
- Eventos: médico pago, cache com escolha vida/ouro, sucata com dano/ganho transacional, abrigo com escolha e arquivo com recuperação paga. Todo efeito altera o proprietário explícito (carteira ou jogador).
- Duas cartas alternativas desbloqueáveis. Contribuições só do modo publicado `ascendant_world_one`; nada concede bônus permanente de vida/dano. Elegibilidade congela no início da tentativa.
- Launch do setting `ascendant` agora usa `ascendant_world_one_run` / `ascendant_world_one`. Saves do showcase continuam presos à própria revisão; não há migração implícita.

### Transformações

- `w1_ranked_edge`: Ranked Edge.
- `w1_ranked_ward`: Ranked Ward.
- `w1_efficiency`: Efficient Support.
- `w1_split`: Split Impacts.
- `w1_calibration`: Critical Calibration.
- `w1_amplify`: Amplifying Support.
- `w1_fire_affinity`: Ember Affinity.
- `w1_poison_affinity`: Corrosive Affinity.
- `w1_cascade`: Causal Cascade.
- `w1_lasting`: Lingering Residual.
- `w1_recall_link`: Recall Link.
- `w1_ward_link`: Ward Link.

Slots de afinidade (1) e comportamento (2) são por carta; upgrades de base têm limite declarado. Multi-hit divide orçamento, afinidades aplicam uma dose por ação (não um payload gratuito por acerto), e cascata transporta apenas a sobra causal sem repetir crítico/origem.


