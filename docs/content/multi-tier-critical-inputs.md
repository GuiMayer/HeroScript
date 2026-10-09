# Capturas numéricas, críticos multinível e previews

Disponível na engine version **23**, capability `multi-tier-random-previews`.
O crítico é uma composição de conteúdo: a engine não reconhece nomes de stats,
cartas, recursos ou inputs como regras especiais de crítico.

## Modelo do Ascendant

A carta `ascendant_critical_lance` usa quatro pipelines habilitadas pelo modo:

1. `ascendant_critical_chance` captura a chance percentual do ator.
2. `ascendant_critical_bonus` captura o bônus por nível: multiplicador do ator
   menos 1, mais treinamento/upgrades da carta.
3. `ascendant_critical_probability` registra o número de níveis garantidos
   como checkpoint e calcula a probabilidade do nível adicional.
4. `ascendant_critical_amount` consome esses fatos no cálculo normal do impacto,
   incluindo increased, more, vulnerabilidade, defesa e arredondamento.

As fórmulas de game design equivalem a:

```text
guaranteed = floor(chancePercent / 100)
probability = (chancePercent % 100) / 100
tiers = guaranteed + success
impact = incomingAmount * (1 + tiers * capturedBonus)
```

A DSL existente avalia sequencialmente, da esquerda para a direita. Portanto,
a fórmula publicada da bucket crítica é:

```text
rolls.critical.checkpoints.guaranteed + rolls.critical.success * rolls.critical.values.bonus + 1 * bucket.input
```

Isso não introduz precedência matemática nova nem um calculador de dano paralelo.
Com chance 150%, há um nível garantido e 50% de chance do segundo; em 200%, há
dois níveis garantidos e nenhum sorteio fracionário. Cada impacto da carta usa
`scope: Impact`, portanto multi-hit sorteia separadamente.

## Capturas genéricas no JSON

Trecho do contrato publicado em
`data/configs/ascendant/Resources/cards/ascendant_cards.json`:

```json
{
  "inputId": "critical",
  "scope": "Impact",
  "probability": {
    "formulaValue": "captures.chance + 0",
    "channel": "critical_probability",
    "pipelineId": "ascendant_critical_probability",
    "unitId": "probability",
    "captures": {
      "chance": {
        "parameter": "Amount", "flatValue": 0,
        "channel": "critical_chance", "pipelineId": "ascendant_critical_chance",
        "unitId": "percent", "conversion": { "sign": "NonNegative" }
      },
      "bonus": {
        "parameter": "Amount", "flatValue": -1,
        "channel": "critical_bonus", "pipelineId": "ascendant_critical_bonus",
        "unitId": "factor", "conversion": { "sign": "NonNegative" }
      }
    }
  }
}
```

- Até oito capturas por input, em ordem canônica, independentes entre si.
- IDs são alfanuméricos/underscore, com até 64 caracteres.
- Cada captura exige pipeline explícita e unidade correspondente; não aceita
  distribuição nem quantidade transportada como fonte.
- Capturas usam o mesmo snapshot e scope do fato aleatório. Mudanças posteriores
  de buff/atributo não reescrevem um fato já registrado.
- Todas são `CaptureOnly`; settlements e consumo de capacidade são proibidos,
  inclusive em definições dormentes.
- `captures.<id>` existe apenas durante o cálculo de probabilidade. Os efeitos
  consomem `rolls.<input>.values.<id>` e `rolls.<input>.checkpoints.<stage>`.
- Esses namespaces são reservados; comandos não podem forjar seus valores.
- Inputs Action/ParentProc seguem a política de captura compartilhada descrita
  em [inputs aleatórios calculados](calculated-random-inputs.md).

As capturas, traces, revisão e fingerprints integram o fato e o commit existente.
Nenhum arquivo de gameplay é relido por impacto.

## Base, transformações, orçamento e efeitos derivados

Upgrade altera a base/componente permanente da instância antes da resolução.
Stats, buffs e demais fontes são contribuições contextuais, não novos upgrades.
No exemplo, `ascendant_critical_calibration` soma 0,25 ao bônus capturado; não
é aplicado outra vez à quantidade final nem soma um multiplicador independente.

Distribuição continua opt-in: uma pipeline com estágios explícitos captura o
orçamento de origem uma vez; os estágios do impacto consomem cada parcela,
sorteiam seus inputs e liquidam a defesa viva. O teste de três impactos comprova
que um flat adicional não é multiplicado pela quantidade de hits.

Children, ticks de status e receitas de condensação não ganham críticos
automaticamente. Só os inputs/pipelines expressamente declarados por seu conteúdo
introduzem sorteios ou scaling. Condensação permanece genérica para stacks,
inclusive efeitos que não causam dano.

O perfil padrão do canal pode ser escolhido por JSON:

```json
"defaultCalculationPipelines": { "effect_amount": "ascendant_effect_amount" }
```

A seleção deve estar habilitada e ter o canal correto. Um efeito com pipeline
explícita mantém sua escolha. Sem default e com várias pipelines compatíveis,
a resolução falha em vez de selecionar por ordem de ID. A alteração desta etapa
transforma a lance em exemplo multinível; não faz todas as cartas herdarem
críticos automaticamente, nem constitui balanceamento/conteúdo final do ato.

## Contrato de apresentação

Inspections e intents expõem `randomOutcomes.impacts` com probabilidade,
capturas, checkpoints e alternativas numéricas condicionais por impacto.
As consultas não avançam RNG, step ou estado canônico.

- Máximo de 64 inputs projetados, duas alternativas possíveis por input.
- Chances 0/1 mostram somente a alternativa possível.
- Não há expansão cartesiana das combinações de vários hits/inputs.
- `conditionalOnReachingImpact: true` e
  `validity: LocalNumericAlternativesWithCapturedContributions` significam:
  números locais calculados com as contribuições/contexto do caminho capturado.
  Não recalculam a obtenção de buffs/condições de um caminho contrafactual.
- Múltiplos inputs fracionários no mesmo impacto, base dependente de fórmula ou
  serviços indisponíveis podem retornar `unavailableReason`, sem inventar números.
- `isGlobalOutcomeRange` é sempre falso: morte, defesa e continuação podem
  mudar os impactos seguintes. As alternativas não são uma média nem uma faixa
  exata de resultado da ação inteira; scopes compartilhados não são independentes.
- A execução amostrada existente permanece auditável, mas sua inspection usa
  `previewScope.validity: SampledPathNotGuaranteedOutcome` e
  `dependsOnRandomInputs: true` quando há aleatoriedade.
- Legal actions/intents sinalizam incerteza. A Godot mostra “Varies — inspect”
  no valor fracionário e aviso localizado, sem executar regras ou fórmulas.

## Validação e versão

Testes cobrem 0/50/100/150/200%, capturas após alterações entre hits, orçamento
distribuído, defesa, upgrade real, buff de chance, children sem reroll implícito,
serialização dos fatos e previews repetidos. Há teste REST com dez inspections
iguais e snapshots de combate/run inalterados, além da regressão de jornada com
dez hosts isolados, restart e replay.

A versão 23 muda traces, fingerprints e resultados do conteúdo crítico.
O launcher exige a capability nova; saves históricos não são apagados,
migrados silenciosamente ou prometidos como semanticamente compatíveis.
