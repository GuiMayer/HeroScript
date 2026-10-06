# Leitura e consumo de stacks acumulados

Primitivas da etapa 4a do [plano do core](../../plans/CORE_GAMEPLAY_GAPS_IMPLEMENTATION_PLAN.md), versão 10 da engine. Não representam a operação completa de condensação, que depende de payloads e receitas nas próximas entregas.

## Autoridade e autorização

`AccumulatedStackTransitions` adapta os dois armazenamentos atuais: `CombatState.statusEffects` e `RunState.modifiers`. Não mantém um terceiro estado de stacks. Referências e planos são snapshots imutáveis derivados; nunca são usados como armazenamento vivo.

Status e modifier declaram `consumption.allowedRecipeIds`. A lista vazia não autoriza consumo. Ter stacks, efeitos periódicos ou uma tag de gameplay não concede essa capacidade implicitamente. Exemplo preparatório de capacidade na definição:

```json
{ "consumption": { "allowedRecipeIds": ["restoration_proc"] } }
```

Neste estágio, IDs identificam autorizações do contrato de consumo; o catálogo revisionado de receitas e sua validação cruzada serão integrados na etapa 5. Não existe um novo comando REST de consumo direto nem uma carta de condensação executável apenas com esse campo.

## Captura e seleção

`Capture` recebe combat/run, recipe ID e um `AccumulatedStackSelection` com filtros por store, owner exato, source, definition IDs e tags requeridas/excluídas. `maximumInstances` limita a seleção sem truncar: excesso é erro, não consumo silencioso de um subconjunto.

Uma referência registra store, owner, instância, definição, revisão, stacks, duração, origem, tags e fingerprint de **todo** o snapshot da instância. Alterações futuras de payload também serão cobertas por esse fingerprint. A ordem é store, owner kind, owner ID ordinal e instance ID ordinal.

Definições não autorizadas e instâncias inativas não participam. O snapshot de uma instância consumível precisa ter identidade, owner/status alvo consistente, contagem positiva e revisão. Duplicações de identidade na mesma store são rejeitadas. A seleção vazia é representável; quem planeja a ativação define a política de ausência, não este reducer.

## Consumo completo e conflitos

`Consume` valida todos os snapshots antes de construir a mutação candidata. A instância precisa continuar elegível e idêntica à captura. Mudar contagem, duração, revisão, definição ou outra informação da instância invalida o plano, mesmo quando a contagem final parece igual.

Planos duplicados, uso repetido da mesma captura e instâncias esgotadas falham. Consumo sempre remove todos os stacks das instâncias selecionadas; não existe quantidade parcial implícita neste contrato.

O resultado contém combat/run candidatos e `EffectStackChange` com razão `Consume`, contagem anterior e contagem atual zero. Não dispara dispel, expiração, derrota ou ticks implicitamente. Não muda RNG, não publica eventos e não persiste nada; a transação canônica é responsável pela publicação futura desses fatos junto com a ativação.

## Lifecycle e limite da entrega

O lifecycle de status já verifica a presença ativa da instância antes de cada trigger. Se uma instância do snapshot inicial foi consumida anteriormente no mesmo boundary, ela não executa seus triggers nem gera uma expiração fictícia. Modifiers consumidos deixam a lista autoritativa, portanto não participam de ticks posteriores.

Testes cobrem ambas as stores, filtros, limites, autorização, conflitos de snapshots, rollback, repetição indevida, ordem determinística, round-trip de planos/fatos e consumo dentro de um boundary.

Payloads Snapshot/Dynamic, binding à composição efetiva da carta, preservação de intensidades em lotes e agregação em um proc ainda estão pendentes. A seleção/consumo não multiplica stacks por duração, não soma unidades heterogêneas e não ativa um efeito por stack.
