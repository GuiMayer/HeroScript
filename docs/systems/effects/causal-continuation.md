# Continuação por abate

Contrato executável da etapa 9. Engine version 18. Uma continuação pertence ao proc original, com um novo impacto e `parentImpactId`. Não depende do nome `health`, de animações ou de logs textuais.

## Autorização e domínio

`continuation` é opt-in no efeito que altera um recurso Current, com um alvo por impacto e parâmetro Amount explícito. Somente seu registro primário pode autorizar a continuação: `resourceOutcome.causedDefeat` precisa ser verdadeiro. Uma derrota produzida por um filho, settlement ou impacto anterior não autoriza o pai.

A primeira versão transporta uma quantity no domínio **pós-aplicação**. Preserva unidade, revisão e receipts de origem/alvos anteriores. Não converte unidades implicitamente, não recalcula base e não reaplica stages Actor/Shared. Os próximos stages são exclusivamente Target, para permitir defesa e settlements do novo alvo.

O planner fornece `continuation.requested_change`, `continuation.applied_change` e `continuation.limited_change` à pipeline autoral de excedente. Ela recebe a quantity do impacto, mas não produz settlements. A aritmética pertence à pipeline; o executor só testa zero, seleciona e transporta. Resultado negativo ou não finito falha a transação.

```json
{
  "continuation": {
    "maximumHops": 3,
    "selector": "LOWEST_RESOURCE_ENEMY",
    "selectionResourceId": "health",
    "overflowPipelineId": "applied_overflow",
    "overflowChannel": "overflow",
    "overflowStageIds": ["applied_remainder"],
    "impactStageIds": ["target_defense"],
    "carryChildren": false,
    "retestChance": false,
    "retestCondition": false,
    "rollImpactInputs": false
  }
}
```

Para subtração, a pipeline pode declarar um bucket Formula com `continuation.requested_change - continuation.applied_change * -1`. A gramática numérica atual avalia da esquerda para a direita: sobre solicitação -15 e aplicação -10, retorna 5. Isso é configuração de conteúdo, não fórmula escolhida pelo tipo DAMAGE. Operações diferentes precisam de um perfil adequado.

O perfil de overflow declara a mesma unidade do Amount e stages Target com IDs semânticos distintos dos stages do perfil de impacto. Esses receipts são associados ao alvo anterior; o stage de defesa pode ser aplicado ao próximo alvo, mas não duas vezes ao mesmo contexto. A seleção do perfil também respeita a whitelist do modo.

## Seleção, limites e interações

Seletores iniciais: RandomEnemy, LowestResourceEnemy e HighestResourceEnemy nos nomes do enum (`RANDOM_ENEMY`, etc.). Excluem derrotados e visitados, usam relações configuradas e desempate ordinal da autoridade de targeting existente. Ranking requer recurso explícito. Random avança o RNG determinístico; zero excedente, falta de candidato e limite não sorteiam.

- `maximumHops`: 1 a 32, subordinado ao limite comum de profundidade/passos. Nenhum truncamento silencioso de trabalho.
- `carryChildren`: autoriza executar os filhos configurados no novo impacto. É uma nova aplicação desses componentes, não uma divisão implícita dos residuais. Seus próprios scopes, budgets e condições continuam válidos.
- `retestChance` e `retestCondition`: opt-in para retestar no novo alvo. Com false, a autorização do impacto original é conservada.
- `rollImpactInputs`: permite novos inputs Impact; fatos aleatórios incorporados à origem permanecem herdados. Não multiplica valores por conta própria.
- Multi-hit: cada parcela pode transportar seu próprio excedente; as parcelas seguintes continuam sob a policy de perda de alvo do repeat. Não são fundidas nem transportadas como um novo orçamento único.
- Condensação: apenas saídas com `continuation` podem saltar. O consumo permanece único no proc original, sem selecionar novamente os stacks.

O trace `steps[].continuation` registra hop, origem, destino, recurso, visitados, cálculo de overflow e `stopReason`: `no_causal_defeat`, `hop_limit`, `no_overflow` ou `no_next_target`. Impacto pulado por chance/condição usa o skip comum. IDs e traces entram nos hashes existentes, sem store paralela. Falha posterior descarta toda a ação, incluindo consumo, custos, defesa, alterações de zonas e RNG.

Publicação e execução validam policies/perfis mesmo em filhos dormentes. Fatos/quantities `continuation.*` não podem ser forjados por callers. Os testes usam pipelines, fórmula, targeting, reducers e settlements reais; save/load/replay integrado da jornada pertence ao gate final.
