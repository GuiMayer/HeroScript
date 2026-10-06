# Transformações permanentes de cartas

Estado: etapa 6a do [plano do core](../../plans/CORE_GAMEPLAY_GAPS_IMPLEMENTATION_PLAN.md). Engine version: `13`.

Esta entrega habilita patches estruturais na aplicação atual de upgrades e estabelece o ledger para alterações futuras. Não conclui a etapa 6: bundles com namespaces, slots e comandos REST para remover/substituir uma transformação ainda serão integrados na etapa 6b.

## Identidade e autoridade

`CardInstanceState.Upgrades` é o único ledger persistido. Não há uma segunda coleção de transformações nem uma cópia persistida da composição efetiva.

Cada entrada contém:

- `transformationId`: ordinal positivo crescente, escopado pela instância da carta no estado da run/branch. Duas cartas podem ter o ordinal 1; a identidade completa inclui a carta. IDs não são reutilizados depois de remoções.
- `operation`: `Apply`, `Remove` ou `Replace`.
- `targetTransformationId`: identidade ativa selecionada em Remove/Replace; ausente em Apply.
- `upgradeId`: definição aplicada/substituta; vazio na entrada de remoção.
- `category`: `Base`, `Affinity` ou `Behavior`. Classificação para regras/apresentação, não escolha de algoritmo de efeitos.
- `contentRevision`: revisão de origem da operação.
- `patches`: snapshot imutável das operações tipadas, sem consultar novamente um catálogo mutável para executar o histórico.

Limite técnico: 1.024 entradas por carta. Excesso falha; não existe truncamento de histórico. Metadados inválidos, referências a transformações inativas e ordem/identidades duplicadas são rejeitados.

`CardTransformationLedger.Project` deriva a sequência ativa. Remoção acrescenta uma entrada, sem apagar o passado. Replacement ocupa a posição de aplicação da transformação substituída, embora tenha uma identidade nova. Assim, uma melhoria posterior continua posterior ao comportamento substituído.

## Reconstrução

O resolver começa na definição compilada e aplica os patches da sequência ativa. Ele nunca tenta inverter operações antigas: remover uma multiplicação por zero reconstrói a base, em vez de dividir por zero.

A ordem dos patches e das aplicações é significativa. Um `component/Replace` substitui explicitamente o componente completo, inclusive seus valores base; não faz merge implícito com campos anteriores. Para uma afinidade conservar uma melhoria existente, o conteúdo pode alterar tags e acrescentar contribuições em componentes próprios, sem substituir a base melhorada. Melhorias posteriores ao componente substituído continuam sendo reaplicadas na reconstrução.

Ao final, a mesma compilação usada para o conteúdo autoral valida e ordena a composição: `order`, depois `componentId` ordinal. IDs duplicados, aliases de output em conflito, singleton targeting/disposition duplicados, custos inválidos, efeitos não executáveis e bindings que perderam o componente referenciado falham. A validação inclui os efeitos dos triggers; a publicação continua rejeitando card triggers sem lifecycle executável.

Aplicação de upgrades, início de run, compilação de cenário e ativação de conteúdo validam a composição antes de aceitar o candidato. Referências a recursos, status, modifiers, receitas, perfis, fórmulas e flows são verificadas contra o runtime revisionado, incluindo opções de custos alternativos. Um hot reload não pode remover silenciosamente uma referência de um componente guardado no ledger.

## Operações estruturais disponíveis

O catálogo continua sendo `card-upgrades`. Patches numéricos anteriores permanecem no mesmo contrato, junto com:

| Discriminator | Campos | Contrato |
| --- | --- | --- |
| `tags` | `add`, `remove` | Tags únicas, não vazias, conjuntos disjuntos. Remover exige existência; adicionar exige ausência. Não usa `componentId`. |
| `component` | `componentId`, `operation`, `component` | Add exige ID livre. Remove exige componente existente e não aceita payload. Replace exige existência e preserva o ID endereçado. |
| `effect_parameter_numeric` | `componentId`, `parameter`, `operation`, `value` | Muda a parcela flat de um parâmetro tipado existente. Não altera unidade, perfil, etapas, conversão ou o resultado da pipeline. |

Operações numéricas: `Add`, `Multiply`, `Set`. Operações de componente: `Add`, `Remove`, `Replace`.

Quando um efeito já possui um override em `parameters`, patches dos antigos campos FlatValue/StatusStacks/StatusDuration não podem alterar a base concorrente. Usar `effect_parameter_numeric`. Um parâmetro baseado em `inputQuantityId` não aceita uma nova base permanente: sua quantidade é fornecida pela execução.

Exemplo de definição, para uma carta `strike` cuja base contém a tag `fire`:

```json
{
  "upgradeId": "ice_affinity",
  "category": "Affinity",
  "maxApplications": 1,
  "cardDefinitionIds": ["strike"],
  "patches": [
    { "type": "tags", "remove": ["fire"], "add": ["ice"] },
    {
      "type": "component",
      "componentId": "affinity.pulse",
      "operation": "Add",
      "component": {
        "type": "effect",
        "componentId": "affinity.pulse",
        "order": 20,
        "effect": {
          "effectId": "ice_pulse",
          "type": "MODIFY_RESOURCE",
          "targetResource": "mana",
          "flatValue": -2
        }
      }
    }
  ]
}
```

O recurso `mana` precisa existir no setting. A tag `ice` não determina custo, dano ou status automaticamente: a gramática data-driven de afinidades será entregue na etapa 7.

## Comandos, inspeção e cálculo

A REST API mantém o comando existente `UPGRADE_CARD`, com `cardInstanceId` e `upgradeId`. Ele já pode aplicar as definições estruturais publicadas. Composição inválida falha antes de persistir estado, sequência, contexto e recibo; retries idempotentes não acrescentam uma nova entrada.

Remove/Replace existem como transições puras internas nesta subetapa. Elas produzem candidatos, que obrigatoriamente precisam passar pelo resolver antes de qualquer commit. Ainda não estão disponíveis como ações REST; não usar mutação direta do snapshot para contornar essa ausência.

Nas projeções de zonas/deck/sandbox, `upgrades` contém apenas a sequência ativa; `transformationLedger` preserva o histórico completo. Na carta efetiva/inspeção, `appliedUpgrades`, `transformationLedger` e `upgradeTrace` separam composição ativa, auditoria e contribuição por patch.

Traces incluem identidade/categoria, valores numéricos anteriores/atuais e fingerprints de componentes substituídos. O cálculo usa a base resultante e distingue sua transformação permanente dos buffs/contexto. Patches de parâmetros também aparecem no base trace. Um componente substituído reinicia a proveniência de sua base: upgrades sobrescritos não são apresentados como contribuições atuais.

Não foi criado um cache de cartas efetivas. O fingerprint inclui identidade, definição compilada, tags, ledger completo, sequência ativa, traces e componentes. Adotar futuramente um cache requer revisão/definição/ledger no endereço; apenas `cardId` é insuficiente.

## Cenários, persistência e continuidade

O compiler de cenários utiliza a mesma transição de upgrade e conserva a ordem autoral de `upgradeIds`; ordenar IDs alfabeticamente modificava o significado de sequências Add/Multiply/substituições.

As coleções são defensivamente copiadas. Forks e cópias da mesma definição compartilham somente dados imutáveis; transformar uma instância não altera outra. Round-trip de JSON preserva histórico, tipos dos patches e hashes.

A engine version passou de 12 para 13. Saves anteriores permanecem no disco e são rejeitados por incompatibilidade de versão; não há migração silenciosa nem exclusão de saves.

Próxima entrega, 6b: bundles revisionados/namespace, slots e conflitos declarados, seleção/remoção/substituição através de comandos canônicos, discovery e testes de persistência/replay/branches desses comandos. Discovery deverá verificar composição e consultar exclusivamente a revisão da run: o endpoint atual de opções ainda usa o catálogo por configuração e os filtros de definição/limite, sem simular cada transformação estrutural. Requisitos/exclusões e limites da gramática de afinidades/comportamentos pertencem à etapa 7.
