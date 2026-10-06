# Transformações permanentes de cartas

Estado: etapa 6 concluída (6a + 6b) do [plano do core](../../plans/CORE_GAMEPLAY_GAPS_IMPLEMENTATION_PLAN.md). Engine version: `14`.

Transformações numéricas/estruturais, bundles fechados, slots e remoção/substituição são executáveis pelo gateway canônico. A gramática que relacionará afinidades a ações e os requisitos/exclusões avançados pertencem à etapa 7; não são comportamentos implícitos do executor.

## Identidade e autoridade

`CardInstanceState.Upgrades` é o único ledger persistido. Não há uma segunda coleção de transformações nem uma cópia persistida da composição efetiva.

Cada entrada contém:

- `transformationId`: ordinal positivo crescente, escopado pela instância da carta no estado da run/branch. Duas cartas podem ter o ordinal 1; a identidade completa inclui a carta. IDs não são reutilizados depois de remoções.
- `operation`: `Apply`, `Remove` ou `Replace`.
- `targetTransformationId`: identidade ativa selecionada em Remove/Replace; ausente em Apply.
- `upgradeId`: definição aplicada/substituta; vazio na entrada de remoção.
- `category`: `Base`, `Affinity` ou `Behavior`. Classificação para regras/apresentação, não escolha de algoritmo de efeitos.
- `slotId`: slot configurado na definição da carta, quando a transformação ocupa um slot; nulo para operações não alocadas. Remoção conserva o slot da entrada selecionada para auditoria.
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
| `bundle` | `namespace`, `operation`, `bundleId` | Referência autoral revisionada, fechada antes de entrar no ledger. Remove não aceita `bundleId`; Add/Replace exigem uma definição existente. |

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

Os três comandos usam `POST /api/v1/runs/{runId}/commands`, com `commandId`, `expectedSequence`, `expectedStep` e os seguintes payloads:

| Type | Payload |
| --- | --- |
| `UPGRADE_CARD` | `cardInstanceId`, `upgradeId` |
| `REMOVE_CARD_TRANSFORMATION` | `cardInstanceId`, `transformationId` |
| `REPLACE_CARD_TRANSFORMATION` | `cardInstanceId`, `transformationId`, `upgradeId` |

`transformationId` seleciona uma entrada **ativa** da carta; não é índice de array nem ID de catálogo. Substituição pode selecionar outra categoria/slot, se a composição resultante for válida. A nova entrada recebe outra identidade e conserva a posição da transformação substituída.

O `CardTransformationPlanner` é a autoridade pura compartilhada por execução e discovery: valida a atividade, carrega a definição exclusivamente da revisão/configuração da run, fecha bundles, produz o ledger candidato, recompila a carta e verifica todas as referências no runtime fixado. Falhas não publicam estado, RNG, sequência ou recibo. Não há controller interpretando patches ou catálogo por configuração como fallback.

Em uma atividade `CardUpgrade`, `parameters.upgradeIds` é a whitelist. `parameters.allowRemoval` e `parameters.allowReplacement` são booleanos opt-in, inicialmente falsos. Uma transformação aceita completa essa atividade; após isso só a resolução/progressão é permitida. Modos que já habilitam `progressionPolicy.allowOutOfActivityCommands` podem operar fora da atividade, mas uma run inativa ou com encontro ativo rejeita transformações permanentes. Alterações em pleno combate exigiriam um contrato de sincronização/stack próprio, não uma mutação parcial do deck da run.

`GET /api/v1/runs/{runId}/cards/{cardInstanceId}/upgrade-options` devolve operações executáveis com `operation`, `targetTransformationId`, `upgradeId`, `category`, `slotId` e identidades da carta. `targetTransformationId` deve ser enviado como `payload.transformationId` nos comandos Remove/Replace. `GET /available-commands` já separa as opções por tipo de comando e usa `transformationId` para o payload selecionado. Metadados de apresentação não devem ser copiados para o payload: o decoder rejeita campos desconhecidos.

Discovery simula a composição completa, não apenas aplicabilidade/limites: não oferece um componente ausente, uma colisão de namespace, um slot cheio ou remoção que deixa uma melhoria/binding/output sem origem. Consulta não escreve nem avança o contexto. Revisão inexistente ou pertencente a outro setting falha explicitamente. As ofertas não reservam a carta; o gateway revalida o estado versionado.

Nas projeções de zonas/deck/sandbox, `upgrades` contém apenas a sequência ativa; `transformationLedger` preserva o histórico completo. Na carta efetiva/inspeção, `appliedUpgrades`, `transformationLedger` e `upgradeTrace` separam composição ativa, auditoria e contribuição por patch.

Traces incluem identidade/categoria, valores numéricos anteriores/atuais e fingerprints de componentes substituídos. O cálculo usa a base resultante e distingue sua transformação permanente dos buffs/contexto. Patches de parâmetros também aparecem no base trace. Um componente substituído reinicia a proveniência de sua base: upgrades sobrescritos não são apresentados como contribuições atuais.

Não foi criado um cache de cartas efetivas. O fingerprint inclui identidade, definição compilada, tags, ledger completo, sequência ativa, traces e componentes. Adotar futuramente um cache requer revisão/definição/ledger no endereço; apenas `cardId` é insuficiente.

O planner reutiliza somente definições compiladas e upgrades fechados dentro de sua própria instância, vinculada a um runtime imutável. Cada candidato ainda é reconstruído pelo ledger atual; não há cache de composição que possa contaminar outra carta ou branch.

## Bundles e namespaces

O campo autoral é `componentBundles`, com referências `{ "bundleId": "residual_fire", "namespace": "affinity" }`. `componentBundleIds` não é mais aceito: não há dois caminhos de composição. Conteúdo antigo deve ser atualizado explicitamente antes da publicação.

Um namespace é um identificador ASCII de 1–64 caracteres, começando por letra ou underscore. Cada componente local `pulse` torna-se `affinity.pulse`. Bindings locais `cardEffectComponentId` são reendereçados; aliases de output tornam-se `affinity__hit` e referências inline `results.hit.*` são remapeadas, inclusive em filhos, condições e parâmetros. IDs de recursos, status, modifiers, perfis e fórmulas publicadas não são renomeados.

Bundles são containers fechados não vazios: bindings/aliases locais precisam existir dentro do próprio bundle. Não se presume uma dependência externa com o mesmo nome. Referências a outputs ausentes, colisões de componentes e aliases são rejeitadas na compilação da carta final.

Um patch autoral:

```json
{
  "upgradeId": "residual_fire",
  "category": "Affinity",
  "slotId": "affinity",
  "cardDefinitionIds": ["strike"],
  "patches": [
    { "type": "bundle", "namespace": "affinity", "operation": "Add", "bundleId": "residual_fire" }
  ]
}
```

`bundle_snapshot` é a representação interna persistida, com componentes já expandidos; publicação rejeita esse discriminator em upgrades autorais. O ledger guarda revisão e snapshot. Ler/reexecutar uma transformação nunca resolve seu bundle novamente no catálogo atual.

O namespace define um grupo de componentes pelo prefixo `namespace.`: Add exige o grupo ausente; Remove/Replace exigem grupo presente e operam sobre todos os membros atuais desse prefixo. Não há um segundo registry persistido de membership. Patches individuais podem endereçar esses IDs; conteúdo que acrescente um componente ao mesmo prefixo o inclui explicitamente no grupo. Remover/substituir um bundle invalida dependências externas se elas perderem seus componentes ou outputs. Replace é substituição integral, não merge; melhorias **posteriores** na sequência ativa continuam sendo reaplicadas.

## Slots declarados pela carta

```json
{
  "transformationSlots": [
    { "slotId": "affinity", "capacity": 1, "allowedCategories": ["Affinity"] },
    { "slotId": "behavior", "capacity": 2, "allowedCategories": ["Behavior"] }
  ]
}
```

IDs únicos, capacidade positiva até 1.024 e categorias válidas/únicas são obrigatórios. O slot é parte da definição compilada/fingerprint e aparece na inspeção da base efetiva. Ocupação é derivada da sequência ativa, nunca de contador mutável. Remoção libera capacidade; substituição desconsidera a entrada antiga e valida a alocação nova. Slot desconhecido, categoria incompatível e excesso falham. `slotId` ausente não ocupa um slot: a categoria, por si só, não inventa restrições de game design.

## Cenários, persistência e continuidade

O compiler de cenários utiliza a mesma transição de upgrade e conserva a ordem autoral de `upgradeIds`; ordenar IDs alfabeticamente modificava o significado de sequências Add/Multiply/substituições.

As coleções são defensivamente copiadas. Forks e cópias da mesma definição compartilham somente dados imutáveis; transformar uma instância não altera outra. Round-trip de JSON preserva histórico, tipos dos patches e hashes.

A engine version passou de 12 para 13 na 6a e para 14 na 6b. Saves anteriores permanecem no disco e são rejeitados por incompatibilidade de versão; não há migração silenciosa nem exclusão de saves.

Regressões da 6b exercitam o gateway de produção com store em disco, reinício, retry idempotente, branch independente e dez reexecuções semânticas dos comandos Apply/Replace/Remove, comparando hashes e frames. Não foi alterada nem validada visualmente a UI Godot nesta etapa. Requisitos/exclusões e limites da gramática de afinidades/comportamentos pertencem à etapa 7.
