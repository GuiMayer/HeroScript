# Eventos de gameplay e respostas genéricas — arquitetura e plano

Data: 2026-10-28. Estado: **plano de implementação; nenhuma etapa executada por este documento**.

Conceito aprovado: acontecimentos tipados, mudanças explícitas, motivos configuráveis, causalidade rastreável e componentes observadores independentes da origem dos efeitos. Este documento define a implementação e a migração dos mecanismos atuais; não declara essa generalização disponível na engine atual.

## 1. Objetivo e limites

Permitir que conteúdo JSON declare respostas como “quando um inimigo executar um ataque”, “quando um aliado receber determinado status” ou “quando outro participante recuperar um recurso”, sem criar executores especiais para cartas, relíquias, personagens, status, compartimentos ou settings.

A engine conhece operações e contratos estruturais. O setting define o significado de ataque, dano, cura, buff, afinidade, derrota e demais classificações. Nenhum identificador como `health`, `mana`, `fire`, `hero` ou `enemy` seleciona uma implementação especial.

Princípios obrigatórios:

- Efeitos de resposta usam o executor, a bucket pipeline e os redutores canônicos. A pipeline calcula; não procura observadores nem executa callbacks de gameplay.
- A mesma ocorrência tem os mesmos fatos independentemente da origem da operação. Pagamento, regeneração, ferramenta, efeito e limite de recurso também precisam de cobertura, com motivos distintos.
- Eventos autoritativos são dados imutáveis produzidos na transação. O barramento operacional existente continua sendo diagnóstico/notificação após o commit.
- Matching, ordem, causalidade, consumo de limites e RNG precisam ser reproduzíveis. Nenhum relógio, ordem de filesystem, inscrição em callback ou ordem de `Dictionary` determina gameplay.
- A origem informa proveniência e disponibilidade; não cria um segundo interpretador de efeitos.
- Preservar os comportamentos existentes durante a migração. Alterar uma política de encadeamento ou prioridade exige configuração explícita, revisão e testes próprios.
- Não alterar layout, animações, balanceamento ou conteúdo de cartas como efeito colateral desta infraestrutura.

## 2. Diagnóstico da base atual

| Área | Situação atual | Consequência para a implementação |
|---|---|---|
| `src/Core/Effects/EffectReactionDefinition.cs` | Reações filtram recurso, operação, canal, tags e relação; dono limitado a Source/Target. | Migrar filtros e ampliar observação para participantes externos ao acontecimento. |
| `src/Core/Effects/EffectTriggerExecutor.cs` | Descobre reações exclusivamente em `currentRun.Relics`; executa após aplicação primária, antes dos filhos encadeados. Suprime novas reações em toda a descendência da resposta. | Extrair descoberta, agendamento e contexto causal sem duplicar execução de efeitos. |
| `src/Core/Effects/ImmutableEffectProcessor.cs` | `EffectApplicationRecord`, `EffectProvenance` e outcomes já registram aplicações, valores e origens. | Usar esses fatos como matéria-prima; não reconstruir mudanças por comparação vaga de snapshots completos. |
| `src/Core/Resources/ResourceMutationReducer.cs` e `ResourceCostTransitions.cs` | Operações de recurso têm caminhos canônicos além dos efeitos. | Instrumentar todos os produtores; custo não pode desaparecer nem ser anunciado como impacto de ataque. |
| `CombatStatusLifecycle`, `CombatRelicLifecycle`, `CombatResourceLifecycle`, `PhaseGraphReducer` e `CombatBoundaryExecutor` | Fronteiras e ordens próprias para ativação, rodada, encontro, duração e fases. | Esses módulos conservam o ciclo de vida; deixam de ter schedulers particulares de respostas. |
| `src/Core/Combat/Reactions/ReactionFlowReducer.cs` e `LegalActionResolver` | Prioridade/pilha de propostas de ação já existe. | Integrar janelas anteriores à resolução; não confundir proposta com fato aplicado nem criar outra pilha. |
| `src/Core/Content/GameplayContentValidator.cs` | Rejeita boundaries sem lifecycle e `CardTriggerComponentDefinition` sem execução suportada. | Manter rejeição até cada contrato estar realmente conectado; não liberar strings arbitrárias antecipadamente. |
| `CardComponentContribution`, `CardPlayExecutor`, `CardInspectionService`, `CardPresentationQueryService` e compartimentos | Composição e projeção de cartas já são contextuais e canônicas. | Registrar respostas da carta efetiva, inclusive concessões, sem alterar permanentemente a definição. |
| `GameplayCommandGateway`, commits, snapshots, replay e branches | Autoridade transacional e persistência já estabelecidas. | Integrar eventos e contadores à autoridade existente; não criar outro journal de gameplay. |
| `src/Core/Events/IOperationalEventBus.cs` | Telemetria best-effort e notificações não autoritativas. | Não usar subscribers para decidir regras nem exigir entrega externa para confirmar uma ação. |

Verificação anterior a este plano: 20 testes focados passaram em `EffectReactionTests`, `CombatStatusLifecycleTests`, `CombatRelicLifecycleTests` e `ReactionFlowReducerTests`. Isso certifica apenas os mecanismos atuais; não certifica o sistema proposto.

## 3. Separações conceituais

### 3.1 Acontecimento, alteração, motivo e origem

Exemplo: um efeito solicitou retirar 12 de um recurso, 4 foram absorvidos por outro recurso e o alvo perdeu 8.

- **Acontecimento:** `resource.change.resolved`.
- **Responsável imediato:** ator que executou a operação; opcional em operações ambientais.
- **Afetado:** ator/objeto cujo campo foi alterado.
- **Alteração:** recurso, campo, operação, antes/depois, pedido e resultado efetivo.
- **Motivo:** classificação declarada, por exemplo `combat.damage.direct`.
- **Características:** tags explícitas como `attack` e `fire`.
- **Origem:** referência estável ao componente e container efetivos que produziram a operação.
- **Causa:** ocorrência anterior e ação raiz às quais este fato pertence.

Subtrair vida por custo, dano ou regra ambiental não cria três algoritmos de subtração. Classificações diferentes permitem filtros diferentes sobre a mesma operação estrutural. Aumentar um recurso não implica cura; aplicar status não implica buff.

### 3.2 Fatos internos e notificações externas

O dispatcher interno precisa conhecer fatos intermediários para resolver respostas. Esses fatos pertencem ao candidato transacional e ainda podem ser descartados. Somente o commit permite publicá-los para API, logs externos e apresentação.

`resolved` significa resolvido no candidato local, não publicado duravelmente. Não usar `committed` para descrever um fato emitido antes do commit da ação.

### 3.3 Intenção e resultado

`action.proposed` e `action.resolved` são contratos distintos. Uma resposta posterior não reescreve um resultado anterior.

Interceptações só existem em janelas anteriores explicitamente suportadas: executar resposta, revalidar a proposta e resolver pelo fluxo canônico. A pilha de prioridade existente conserva sua autoridade. Não permitir callbacks no meio da matemática da bucket pipeline nem correção retroativa de um snapshot aplicado.

## 4. Convenções de hierarquia e seleção

### 4.1 Identificadores

- Caminhos separados por ponto: `resource.change.resolved`.
- Segmentos de identificadores de evento/motivo: ASCII minúsculo, iniciando por letra, seguido de letras, números, `_` ou `-`. Separador exclusivamente `.`.
- Matching ordinal e sensível a maiúsculas; publicação rejeita identificadores fora da gramática, sem lowercase silencioso em runtime.
- Essa gramática não restringe os IDs de conteúdo existentes, nomes localizados ou IDs de instância.
- Não permitir segmentos vazios, ponto inicial/final, espaços, caminhos de filesystem, regex ou escapes implícitos.
- Limites de tamanho, profundidade e quantidade de padrões são estruturais, publicados e verificados antes de compilar o índice.
- Os níveis são endereçamento/classificação, não ordem de execução.

### 4.2 Wildcards

| Seletor | Semântica |
|---|---|
| `resource.change.resolved` | Correspondência exata. |
| `resource.*` | Exatamente um segmento depois de `resource`. Não corresponde a `resource.change.resolved`. |
| `resource.*.resolved` | Um segmento variável na posição intermediária. |
| `resource.**` | Todos os descendentes, com um ou mais segmentos. Não inclui o próprio identificador `resource`. |
| `**` | Qualquer identificador completo válido, dentro do escopo declarado da assinatura. |
| `combat.damage.*` | Somente filhos imediatos desse motivo. |
| `combat.damage.**` | Todos os motivos abaixo dessa família, em qualquer profundidade. |

`*` ocupa um segmento inteiro e não casa com segmento vazio. `**` só pode aparecer uma vez e no final do padrão. Não implementar `dam*`, regex, negação dentro da string, `..`, prioridade por especificidade ou glob dependente do sistema operacional.

Para incluir um nó e todos os descendentes: `include: ["combat.damage", "combat.damage.**"]`. Exclusões ficam em uma lista `exclude`; exclusão vence inclusão. Inclusões são OR; dimensões diferentes do filtro são AND. Uma assinatura que casa com vários padrões executa uma vez por ocorrência, antes dos limites de frequência.

**Wildcard não é inferência de gameplay:** prefixos não tornam `health` um recurso letal nem transformam qualquer subtração em dano. O motor apenas compara classificações explicitamente declaradas e validadas.

### 4.3 Namespaces, versões e aliases

- Reservar raízes canônicas como `action`, `effect`, `resource`, `attribute`, `status`, `card`, `actor`, `combat`, `activation`, `round`, `phase` e `run` para contratos emitidos pelo motor.
- Eventos de domínio adicionais usam namespace de pacote, por exemplo `game.ascendant.combo.completed` ou `mod.author_package.something.resolved`.
- Motivos têm catálogo próprio; famílias como `combat.damage.direct` são conteúdo do setting, não novas instruções executáveis da engine.
- `schemaVersion` fica em campo separado; não disfarçar alterações incompatíveis de payload sob a mesma chave. Revisão do conteúdo identifica o catálogo efetivamente usado.
- Aliases só existem em migração/publicação de conteúdo, com referência explícita e sem ciclos. Normalizar antes de matching; nunca publicar duas ocorrências por causa de um alias.
- Não introduzir herança automática de comportamento a partir dos pontos. Eventuais definições derivadas usam composição de conteúdo já existente, resolvida na publicação, e continuam gerando uma definição efetiva explícita.

### 4.4 Catálogo validável

Cada tipo registra payload suportado, fronteira de emissão, campos obrigatórios/opcionais, escopo e possibilidade de abrir janela de resposta. Catálogos de motivos/tags/referências são capturados na revisão da run.

Campos de seletores são tipados: não aceitar expressões livres que percorrem propriedades CLR ou JSON arbitrário. Padrão sem correspondência no catálogo publicado deve produzir diagnóstico; rejeitar assinatura impossível salvo wildcard de extensão explicitamente permitido pela política do pacote.

## 5. Contratos propostos

Os nomes abaixo são propostas de implementação, não APIs já existentes.

### 5.1 Envelope imutável de ocorrência

`GameplayOccurrence` deverá conter:

- `schemaVersion`, `eventId`, `kind`, `reasonId` e tags congeladas.
- Escopo: setting, revisão, run, branch, combate quando houver, ação e comando raiz.
- `parentEventId`, `rootEventId`, ordinal canônico e profundidade causal.
- Referências tipadas e opcionais: responsável imediato, responsável atribuído, afetado e origem do componente/container.
- Payload discriminado pelo tipo: alteração de recurso, status, atributo, movimentação de carta, proposta/resolução de ação, fronteira ou resultado de regra.
- Referências aos fatos de cálculo, impacto, proc, transferência ou grupo quando aplicáveis.

Não criar um dicionário universal mutável. Metadados adicionais precisam de schema, orçamento e isolamento da lógica canônica. Referências ausentes continuam ausentes; não inventar um ator fictício como responsável por regras ambientais.

`eventId` deriva de identidade de transação/comando estável, caminho do produtor e ordinal canônico. Não consumir RNG para criar IDs. Reexecução do mesmo comando retorna as mesmas identidades; branches têm identidade de escopo explícita.

### 5.2 Natureza da mudança

Um payload de recurso registra, no mínimo:

- Identidade do recurso, campo (`Current`, `Maximum` etc.) e operação (`Add`, `Subtract`, `Set` etc.).
- Operando recebido pelo redutor, delta solicitado nessa operação quando calculável, antes, depois e delta aplicado com sinal.
- Resultado (`changed`, `unchanged`, `limited` ou equivalente tipado), restrições/absorções identificadas e referência ao cálculo.
- Grupo de transferência, quando uma operação de origem produz outra no destino.

Não sobrecarregar `requestedAmount` para representar operando e delta ao mesmo tempo: `Set(20)` a partir de 15 tem operando 20 e delta +5. O pedido anterior ao cálculo também é distinto do operando recebido pelo redutor: 12 antes de absorção podem resultar numa operação de subtração de 8, aplicada integralmente. Referenciar o pedido/cálculo e os settlements separadamente; não atribuir ao clamp do recurso uma redução que aconteceu na pipeline. Preservar a política numérica canônica, rejeitar NaN/infinito e não arredondar valores de gameplay para montar uma mensagem.

Status possuem payload próprio: definição/instância, aplicação/remoção, stacks e duração anteriores/posteriores, motivo da retirada e atribuição. Movimentações possuem endereços e instâncias. Não transformar todos os fatos em uma falsa alteração de recurso.

### 5.3 Exemplos de ocorrência

Representação reduzida, ilustrativa; nomes finais serão estabilizados na etapa de contratos:

```json
{
  "schemaVersion": 1,
  "eventId": "<deterministic-id>",
  "kind": "resource.change.resolved",
  "reasonId": "combat.damage.direct",
  "tags": ["attack", "fire"],
  "sourceActorId": "actor-a",
  "attributedActorId": "actor-a",
  "subjectActorId": "actor-b",
  "origin": { "containerId": "card-instance-1", "componentId": "impact" },
  "parentEventId": "<effect-event-id>",
  "calculation": {
    "requestedMagnitude": 12,
    "settlementFactIds": ["<absorption-fact-id>"]
  },
  "change": {
    "type": "resource",
    "resourceId": "health",
    "field": "Current",
    "operation": "Subtract",
    "operand": 8,
    "requestedDelta": -8,
    "before": 30,
    "after": 22,
    "appliedDelta": -8,
    "outcome": "changed"
  }
}
```

O recurso usado para absorção tem seu próprio fato, ligado ao mesmo cálculo/impacto. Não anunciar essa absorção como outro ataque. Quando a alteração aplicada for zero, `resource.change.resolved` ainda descreve a tentativa resolvida; assinaturas exigem `appliedDelta != 0` se só interessam mudanças efetivas. Não emitir também um alias `resource.changed` como uma segunda ocorrência do mesmo fato.

Para validar o clamp separadamente: uma operação que pede subtrair 12 de um recurso atualmente em 8, com mínimo zero, registra operando 12, delta solicitado −12, antes 8, depois 0 e delta aplicado −8. Os filtros conseguem distinguir esse caso de absorção anterior ao redutor.

Uma ação com múltiplos alvos/impactos produz fatos individuais e um resumo de resolução com referências aos filhos. Um filtro de impacto não deve casar acidentalmente com o resumo da ação.

### 5.4 Resposta e registro efetivo

Separar:

1. `GameplayResponseDefinition`: conteúdo JSON imutável com filtros, condições, frequência e efeitos.
2. `GameplayObserverRegistration`: instância ativa, dono opcional, escopo, definição/revisão, ordem e origem.
3. `GameplayResponseInvocation`: correspondência congelada de uma assinatura com uma ocorrência.
4. `GameplayResponseResult`: efeitos/fatos/counters gerados, sem mutar entrada.

Exemplo conceitual de “quando um inimigo recuperar um recurso por cura, conceder proteção ao dono”:

```json
{
  "responseId": "watch_enemy_recovery",
  "observe": {
    "kinds": { "include": ["resource.change.resolved"], "exclude": [] },
    "reasons": { "include": ["combat.heal.**"], "exclude": [] },
    "subject": { "relationshipToOwner": "Enemy" },
    "resource": { "field": "Current", "direction": "Increase" }
  },
  "condition": "event.change.applied_delta > 0",
  "availability": { "snapshot": "BeforeOccurrence", "requireLivingOwner": true },
  "frequency": { "scope": "Occurrence", "maximum": 1 },
  "order": { "priority": 0 },
  "propagation": { "mode": "AllowResponses", "policyId": "bounded_responses" },
  "effects": [
    { "type": "MODIFY_RESOURCE", "target": "SELF", "targetResource": "barrier", "operation": "ADD", "flatValue": 2 }
  ]
}
```

`barrier`, o motivo e a classificação de cura precisam existir no setting do exemplo. A operação acima não exige que o dono seja origem ou alvo da cura. `SELF` representa o dono da resposta; o ator responsável pelo acontecimento continua em `event.source`, sem ser sobrescrito pela nova subexecução.

## 6. Disponibilidade e independência da origem

O coletor de registros percorre os containers efetivos autorizados pelo contexto: atores, status, relíquias, cartas, regras de modo/encontro e outros providers que forneçam o mesmo contrato.

- Providers materializam registros; não fazem matching, fórmulas ou execução próprios.
- Para cartas, declarar quando a resposta está ativa: carta selecionada, em uma ou mais zonas publicadas, durante sua resolução, ou outro escopo implementado. Não presumir que toda carta do deck está permanentemente ativa.
- Usar endereços/seletores de zonas, não papéis hardcoded como mão/descarte. Compartimentos concedem o componente ao container da carta e reutilizam o mesmo registro; não recebem scheduler próprio.
- Inscrições mantêm suas responsabilidades atuais. Dar suporte ao contrato de resposta em outro container não transforma inscrição em relíquia nem mistura inventários.
- Dois componentes equivalentes com o mesmo contexto efetivo devem executar os mesmos efeitos; diferenças legítimas de disponibilidade/proveniência permanecem explícitas.
- Para escopos sem dono, filtros relativos ao dono são inválidos, salvo binding explícito. Não escolher o primeiro ator como fallback.

A assinatura declara quem observa e quais atores serão alvo dos efeitos. Essas duas decisões não se confundem com responsável e afetado do acontecimento.

## 7. Causalidade e atribuição

Separar responsável imediato, responsável atribuído e origem histórica. Uma cadeia pode ser carta → status aplicado → tick posterior → mudança de recurso → regra de derrota.

Políticas declarativas de atribuição devem cobrir preservação do aplicador, atribuição ao dono atual, transferência explícita e ausência de responsável. Não atribuir uma invocação ao jogador ou uma reflexão ao atacante por convenção implícita.

Um status que atua em outro turno precisa conservar referência causal e atribuição no estado persistente; não depender de uma ocorrência efêmera ainda em memória. Remoção da carta, derrota do aplicador ou reinício do host não apagam essa explicação.

Para fatos históricos, guardar referências mínimas e os campos indispensáveis à reprodução; logs completos ficam no journal, sem copiar ancestrais recursivamente em cada payload.

A derrota continua sendo consequência da regra configurada, inclusive thresholds de recursos. `actor.defeated` identifica regra, ocorrência causal e atribuição. Não introduzir perda automática por recurso chamado `health`, nem recuperação/resurreição implícita através de uma reação.

## 8. Semântica de execução e transação

### 8.1 Fluxo canônico

```text
comando REST autorizado
  → proposta e janelas anteriores permitidas pelo fluxo existente
  → cálculo puro / operação canônica
  → fatos tipados no candidato transacional
  → matching dos observadores elegíveis
  → agendamento determinístico das respostas
  → execução pelo executor comum
  → novos fatos e respostas autorizadas
  → validação do sucessor / commit canônico
  → projeções, logs externos e apresentação
```

Não chamar HTTP para disparar uma resposta interna. Não gravar um commit por efeito intermediário. Um comando duplicado retorna o recibo original, sem redespachar respostas.

### 8.2 Snapshots e elegibilidade

Cada ocorrência identifica sua fronteira e os fatos antes/depois. A captura de observadores deve ser explícita e versionada: `BeforeOccurrence` ou `AfterOccurrence`, quando suportados. A migração escolhe a fronteira equivalente a cada caminho atual; não impor uma troca de snapshot global.

Congelar a lista de invocações e seus bindings naquele momento. Um observador criado depois não é inscrito retroativamente. Auto-observação de um componente recém-aplicado só é possível com política explícita de captura posterior, não por efeito da ordem de coleta.

Condições referem-se a namespaces tipados (`event`, `owner`, `scope`), com leitura de snapshot identificada. Relações de facção/equipe pertencem à fotografia declarada do acontecimento. Se a execução exigir dono ainda vivo/registro ainda disponível após respostas anteriores, a revalidação é uma política separada; não refazer todo o matching silenciosamente.

Os efeitos de cada resposta usam o sucessor atual da transação, preservando os fatos congelados da ocorrência. Caches e previews não podem compartilhar estado de execução, RNG ou contadores mutáveis.

### 8.3 Ordenação

Definir uma ordem total: fronteira/ponto de inserção, chave de agenda compilada, prioridade normalizada, IDs estáveis de registro/instância/componente e ordinal. Serializar a política efetiva na revisão; rejeitar desempates incompletos.

O matcher não ordena por posição no array `include` nem por especificidade de wildcard. Dois padrões sobrepostos não causam duas execuções. Reações diferentes continuam podendo responder ao mesmo fato.

**Migração crítica:** hoje reações de recurso ordenam por prioridade crescente, relíquia e ID; lifecycles de status/relíquias possuem agrupamentos e prioridades decrescentes próprios. Não substituir isso por um único `OrderBy(priority)` e afirmar equivalência. O compilador migrador deve produzir chaves explícitas que reproduzam a ordem completa e o ponto de inserção anteriores. Normalizações numéricas precisam detectar overflow.

### 8.4 Propagação, frequência e limites

- Distinguir emitir fatos de permitir que esses fatos gerem outras respostas.
- Políticas propostas: `RecordOnly` e `AllowResponses`, com propagação explícita para descendentes.
- A tradução das reações atuais usa `RecordOnly` na descendência, preservando a supressão atual de loops de lifesteal/reflexo.
- Conteúdo novo pode permitir cadeias, com limites de profundidade, invocações, eventos e trabalho por transação. Reutilizar o orçamento atual de efeitos; não reiniciá-lo em cada resposta.
- Frequências propostas: ocorrência, impacto, ação raiz, ativação, rodada, combate e run. Somente anunciar um escopo quando seu contador e reset forem implementados.
- Contadores que influenciam ações futuras pertencem ao estado imutável/snapshots. Escopos apenas transacionais usam estado local privado. Ações suspensas na pilha exigem identidade/contagem persistentes enquanto atravessam comandos.
- Limites de segurança excedidos rejeitam toda a transação, com diagnóstico causal. Não truncar respostas e confirmar um resultado parcial.
- O desaparecimento de um observador e um novo exemplar com o mesmo ID de definição não podem compartilhar contadores por acidente.

### 8.5 Janelas com escolha

Resposta automática e ação de resposta por jogador são modalidades distintas do mesmo protocolo de ocorrências. Uma janela que aguarda input usa a pilha/decisões pendentes já existentes, com continuação serializável. Nunca persistir closures, Tasks, iteradores ou referências de objetos de UI.

Definir barreiras para não publicar resultado provisório como resolução definitiva antes da escolha. Revalidar custo, alvo, cancelamento e condições no ponto canônico já configurado. Outcomes/derrota continuam respeitando a política de avaliação existente.

## 9. Plano de migração

| Sistema atual | Destino | Invariante de compatibilidade |
|---|---|---|
| `EffectReactionDefinition` de relíquias | Componentes de resposta com filtros de recurso/motivo/canal, bindings Source/Target e política de propagação traduzidos. | Mesma perda real, elegibilidade, revisão de aquisição, fórmulas `parent.*`, ordem e supressão de descendentes. |
| `EffectTriggerDefinition` em status/relíquias | Respostas a fronteiras canônicas. | Mesmos alvos, prioridades, duração, captura de instâncias e execução antes/depois da expiração. |
| Eventos de abertura/fim | Ocorrências de fronteira no fluxo existente. | Idempotência de abertura/fim e `CompletedLifecycleBoundaries` preservadas. |
| Triggers de fases | Registro/ocorrência no ponto da fase apropriado. | O grafo de fases continua decidindo transições; o dispatcher apenas executa respostas. |
| Regeneração, pagamentos e restrições | Ocorrências tipadas com motivo mecânico explícito. | Não passam a ser cura/ataque por causa do recurso ou sinal; assinaturas legadas não passam a observá-los. |
| Aplicação/reaplicação/remoção/expiração de status e alterações de atributos | Fatos canônicos e filtros tipados. | Sem hooks paralelos em cada handler; stacks e duração mantêm suas regras. |
| Movimentos de cartas e zonas | Fatos produzidos pelo movimento/fluxo canônico, com grupos de shuffle/compra identificados. | Preservar IDs, ordem, visibilidade e RNG; não adicionar semântica de compartimento às zonas. |
| Cartas e componentes concedidos | Registro a partir do container efetivo, com disponibilidade declarada. | Upgrade/modificador/concessão preservam proveniência e não duplicam observadores. |
| Prioridade/pilha | Proposta, janela e resultado integrados ao protocolo. | Mesma ordem, pagamento, locks, reembolso e revalidação de alvos. |
| Event bus operacional e logs | Projeções pós-commit de ocorrências confirmadas. | Não executam regras; falha de subscriber não muda o resultado da run. |

A migração de conteúdo é uma ferramenta explícita e determinística, com modo de diagnóstico, diff e validação. Não fazer conversão ad hoc ao executar uma ação.

Durante a construção, permitir comparação shadow **somente de observação**, sem executar respostas duas vezes. Cada família migrada possui um único responsável por execução. O corte final remove os dispatchers antigos de gameplay e os formatos antigos da autoria atual; se necessário, o importador de versões anteriores fica apenas na fronteira de migração.

Snapshots/commits históricos não são reescritos silenciosamente. Definir suporte de leitura e execução por `engineVersion`/schema: replay executável antigo requer caminho explicitamente compatível ou artefato de engine preservado; projeção de fatos antigos não equivale a reexecução certificada. Não manter dois motores concorrentes para runs novas.

## 10. REST, inspeção e apresentação

- Manter o gateway de comandos existente como entrada de ações do jogador.
- Não oferecer endpoint para o cliente anunciar “um inimigo foi curado” ou injetar fatos como verdade autoritativa.
- Expor catálogo de eventos/motivos, schemas, padrões suportados e diagnósticos de autoria por consultas read-only, alinhadas aos padrões atuais de API/OpenAPI.
- Projeções de resolução incluem causalidade, respostas disparadas, motivo do skip e referências aos cálculos, sem copiar a mesma árvore causal inteira em cada frame.
- Legalidade, simulação, preview e IA usam o mesmo dispatcher em snapshots privados. Quando houver aleatoriedade ou escolha externa, representar incerteza/continuação, não inventar resultado exato.
- Consultas históricas preservam revisão e permissões de visibilidade. Ocultar uma carta na UI não pode revelar seus componentes através do catálogo de invocações.
- A Godot transforma fatos em mensagens localizadas. Templates podem diferenciar motivo exato e família, com fallback explícito até uma descrição estrutural. Template desconhecido nunca muda gameplay.
- A árvore causal usa IDs já publicados e paginação; UI não remonta regras para descobrir por que uma resposta aconteceu.

## 11. Etapas sequenciais de implementação

Cada etapa deve ter commit próprio após seus testes. Dependências abaixo são obrigatórias; não liberar autoria pública de um contrato ainda sem execução.

### Etapa 0 — Inventário e baseline executável

Inventariar todos os produtores de alterações e consumidores de triggers, incluindo custos, regeneração, thresholds, ferramentas, fases, inicialização, fim e cartas. Registrar ordem real e snapshots usados. Separar mudanças locais alheias à tarefa antes de versionar.

Criar fixtures de referência para lifesteal, reflexo, alteração nula, absorção, derrota, multi-hit, status periódico, relic lifecycle, fase, zonas, PriorityStack e modo sem observadores. Capturar resultados, RNG, ordem de execução, condições e revisões.

**Aceite:** mapa de cobertura por produtor e matrizes de equivalência disponíveis; nenhum novo fato altera hashes ou comportamento nesta etapa.

### Etapa 1 — Especificação de hierarquias e matcher

Implementar parser/validador puro, matching exato/`*`/`**`, listas de inclusão/exclusão, deduplicação e índice por segmentos. Publicar a tabela de semântica deste documento como contrato testável.

**Testes:** níveis intermediários, descendentes, raiz isolada, wildcard total, exclusões, padrões sobrepostos, entradas inválidas, case/Unicode, limites e mesma seleção sob permutações das listas. Testar também que prioridade não muda com a especificidade do padrão.

**Aceite:** matching sem RNG, sem regex fornecida pelo usuário, bounded e independente de ordem de carregamento.

### Etapa 2 — Contratos imutáveis de ocorrências e catálogo

Introduzir envelope, referências causais, payloads discriminados, catálogos e schemas JSON. Fixar convenções de nomes, namespaces e versões. Reusar registros de aplicação/cálculo como fontes, sem duplicar autoridade de valores.

**Testes:** serialização/round-trip, cópias independentes, recursos desconhecidos válidos, source ausente, `Set` versus delta, zero, precisão e rejeição de NaN. Validar colisões de namespace, schemas incompatíveis e aliases cíclicos.

**Aceite:** contratos claros para os fatos já implementados; eventos customizados não podem fingir uma mutação canônica.

### Etapa 3 — Contexto causal e identidade de transação

Criar contexto privado de execução com contagem canônica de ocorrências, ação raiz, pai e atribuição. Estender estados duráveis necessários para status/procs atrasados e propostas pendentes; definir o corte de versão antes de gravá-los.

**Testes:** IDs repetíveis, multi-hit, múltiplos alvos, tick após reinício, fonte removida, branch, retry e ação que falha sem consumir RNG/contadores.

**Aceite:** cadeia completa sem GUID aleatório novo, relógio ou captura de referências mutáveis.

### Etapa 4 — Produção canônica dos fatos, ainda sem respostas novas

Instrumentar efeitos, recursos, custos, limites, status, atributos, cartas e fronteiras. Definir qual módulo emite cada fato para impedir emissão duplicada pelo redutor e pelo executor. Preservar ligações entre cálculo, absorção, transferência e mudança primária.

Registrar fatos no candidato e comparar shadow com os traces atuais. Não habilitar observers novos nem transformar logs operacionais em produtores de gameplay.

**Aceite:** cada alteração elegível do inventário tem exatamente seu fato; custos/fatos neutros não são classificados automaticamente como dano/cura; nenhuma mudança de resultado.

### Etapa 5 — Respostas como conteúdo e registros efetivos

Implementar definições, filtros tipados, bindings, namespaces de fórmula, disponibilidade e coletor compartilhado. Providers apenas fornecem registros. Compilar e validar registros de modo/encontro, ator, relíquia, status e cartas efetivas sem executá-los ainda.

**Testes:** observador terceiro, Source/Subject/Owner distintos, relações configuradas, disponibilidade por zona, conteúdo concedido por compartimento, remoção de registro e conflito/duplicata. Verificar que propriedade e visibilidade da carta continuam válidas.

**Aceite:** efeitos equivalentes não dependem de uma chamada especial por origem; registro impossível não é anunciado como executável.

### Etapa 6 — Dispatcher transacional e políticas de propagação

Implementar matching, snapshot de elegibilidade, fila/agendamento e invocação pelo executor comum. Integrar frequência, quotas, política de descendentes, limites compartilhados e rollback.

**Testes:** resposta de resposta, auto-observação explícita, criação/remoção durante dispatch, zero alteração, owner derrotado, ciclo, orçamento, falha depois de várias respostas e ausência de eventos externos antes do commit.

**Aceite:** exatamente uma invocação por registro/ocorrência antes das políticas de frequência; nenhuma gravação intermediária ou recursão sem orçamento.

### Etapa 7 — Migrar as reações de recurso das relíquias

Traduzir `EffectReactionDefinition`, Source/Target, filtros, revisão de aquisição, fórmulas `parent.*` e supressão de descendentes. Remover a coleta direta de `currentRun.Relics` do executor para a família migrada.

**Testes:** repetir integralmente `EffectReactionTests`, fixtures reais de Blood Covenant e casos de reflexo; comparar valores, alvos, RNG e ordem, incluindo custos/settlements que não disparavam antes.

**Aceite:** um único dispatcher executa essas respostas; nenhuma ampliação implícita de gameplay em conteúdo existente.

### Etapa 8 — Migrar lifecycles, fases e fronteiras

Migrar status e relíquias de abertura/fim, ativação/rodada e gatilhos de fase. Conservar timing de duração, ordenação hierárquica, captura de instâncias, ativação elegível e fronteiras já concluídas. Integrar regeneração como produtora de fatos, sem mudar quando ela ocorre.

**Testes:** `CombatStatusLifecycleTests`, `CombatRelicLifecycleTests`, fases configuradas, expiração, duração permanente, novo status durante fronteira, efeitos que derrotam ator e início/fim repetidos.

**Aceite:** módulos de lifecycle continuam donos do tempo/estado de existência; despacho de respostas pertence ao mecanismo comum.

### Etapa 9 — Habilitar respostas pós-fato em todos os containers suportados

Conectar alterações de status, atributos, cartas/zonas e atores às assinaturas genéricas. Liberar o componente canônico em cartas e bundles somente com disponibilidade, validação de composição e execução completas. Integrar proveniência de upgrades/modificadores/compartimentos.

**Conteúdo de prova:** inimigo cura outro inimigo e terceiro ator reage; buff no aliado gera resposta do observador; custo de mana observado sem ser dano; carta concedida por compartimento reage na zona configurada; efeito equivalente vindo de carta, status, relíquia e regra.

**Aceite:** mesma semântica independente da origem, com diferenças de disponibilidade explicitamente configuradas. Nenhuma restrição é removida do validador apenas para fazer o JSON compilar.

### Etapa 10 — Integrar intenções e janelas anteriores

Conectar `action.proposed`, cancelamento e resolução à PriorityStack existente. Definir quais eventos podem abrir janela; persistir continuations e frequência que atravessam comandos. Revalidar após respostas sem mover regras para o app.

**Testes:** ordem LIFO/FIFO configurada, elegibilidade, pagamento antecipado/tardio, cancelamento, alvos perdidos, reembolso, nova resposta, save/load no meio da janela e branches independentes.

**Aceite:** não há uma segunda pilha nem alteração retroativa de fatos resolvidos. Contratos anteriores à resolução não implementados continuam rejeitados.

### Etapa 11 — Migração de conteúdo e corte de autoria

Criar ferramenta read-only/dry-run e modo de aplicação explícita com diff; migrar os pacotes versionados e atualizar manifests. Tratar ordem, defaults e namespaces por migração tipada, não por heurística de nomes.

Remover os campos/dispatchers antigos da autoria/runtime atuais após equivalência; preservar apenas os mecanismos explicitamente necessários para arquivos históricos. Documentar a versão determinística e o suporte de replay.

**Aceite:** pacotes existentes compilam pelo caminho canônico; mistura ambígua de formatos é rejeitada; não existem duas autoridades executando o mesmo gatilho.

### Etapa 12 — Commit, replay, timeline e ferramentas

Integrar ocorrências, invocações e contadores aos recibos/journal/checkpoints/snapshots atuais. Projetar logs operacionais somente depois do commit. Definir o contrato de retomada dos estados anteriores ao corte.

**Testes:** idempotência, falha de append, restart, snapshot + tail, replay semântico, branch com mesmo ancestral e configuração nova que não modifica a run histórica. Uma resposta repetida por consulta de timeline é falha de teste.

**Aceite:** causação e contadores reconstituídos; nenhuma consulta executa efeitos ou muda a autoridade do estado.

### Etapa 13 — REST, descoberta e inspeção

Adicionar consultas de catálogo/schema e detalhes causais pelos serviços de aplicação, mantendo controllers como transporte. Atualizar OpenAPI, exemplos e SDK/cliente quando houver. Integrar preview/simulação/IA ao mesmo dispatcher e às permissões do modo.

**Testes:** isolamento entre settings, revisão errada, payload forjado, sem permissão, carta oculta, preview read-only, latest-response-wins e ausência de comando para injetar fatos autoritativos.

**Aceite:** autoria e diagnóstico são possíveis por JSON/REST; engine não espera animação nem confirmação visual para executar respostas automáticas.

### Etapa 14 — Projeção de mensagens e laboratório

Adicionar templates localizados por motivo/tipo, fallback hierárquico explícito e navegação da cadeia causal. Criar cenário isolado com pelo menos dois settings usando recursos e classificações diferentes, e mesmos componentes do jogo.

**Testes:** inglês como fallback, PT, nomes ausentes, motivo desconhecido, fonte ambiental, atribuição de status atrasado, log de absorção/transferência e histórico sem vazar conteúdo oculto. A mensagem não é lida de volta pela engine como regra.

**Aceite:** usuário consegue explicar o responsável, afetado, natureza, valor pedido/aplicado e causa sem consultar implementação.

### Etapa 15 — Performance, certificação e documentação final

Indexar por tipo/motivo/escopo e invalidar por revisão + composição efetiva + estado relevante. Não revarrer todos os componentes para cada candidato quando o índice pode eliminar os não elegíveis. Caches são derivados, bounded e nunca guardam RNG/counters autoritativos.

Executar regressão Core/API/Godot relevante e jornadas reais: duas configurações, observador externo, status atrasado, multi-hit, transferência, fontes distintas, PriorityStack, restart, branches e replay. Comparar repetidamente a mesma sequência de comandos e fatos. Medir custo sem observers, centenas de registros e cadeias próximas ao limite.

Medir separadamente matching, execução, serialização, commit e tempo REST + sessão. Buscar o orçamento interativo inferior a 1 s na matriz publicada, sem prometer SLA universal nem encurtar animações. Regressão de segurança nunca é resolvida descartando fatos ou durabilidade.

**Aceite:** relatório com cobertura, versões, hashes/semântica, tempos, limitações e comandos de reprodução; inventário sem produtores relevantes esquecidos; status deste plano atualizado por etapa.

## 12. Matriz mínima de regressão e aceite

| Grupo | Casos obrigatórios |
|---|---|
| Hierarquia | Exato, `*`, `**`, exclusões, sobreposição, segmentos vazios, namespaces, aliases e revisão. |
| Natureza | Add/Subtract/Set, Current/Maximum, recurso arbitrário, alteração zero, limites, absorção e transferência. |
| Classificação | Mesmo delta por custo, dano, cura, regeneração e regra; nenhum nome/sinal inventa semântica. |
| Origem | Mesmo efeito e contexto vindo de carta, habilidade, status, relíquia, regra e componente concedido. |
| Observação | Dono fora da origem/alvo, relações de equipe, responsável ausente, dono derrotado e fonte removida. |
| Status | Aplicação, reapply, stack, duração, remoção/expiração e proc após restart com atribuição preservada. |
| Cartas | Disponibilidade por endereço de zona, entrada/saída, destruição, carta oculta, concessão e composição inválida. |
| Causalidade | Pai/raiz, resumo versus impactos, custo versus settlement, ausência de duplicação e múltiplos motivos relacionados. |
| Ordem | Prioridades iguais, permutações de conteúdo, instâncias repetidas e equivalência com todas as ordens legadas. |
| Segurança | Ciclos, crescimento explosivo, quotas, fórmulas inválidas, referências ausentes e rollback total. |
| Frequência | Por ocorrência/impacto/ação/ativação/rodada/combate/run, resets, remoção/recriação e branches. |
| Input pendente | Save/load em janela, pagamento/reembolso, cancelamento e revalidação pelo fluxo canônico. |
| Persistência | Retry, falha de gravação, snapshot, replay, revisão histórica, hot reload e bifurcação isolada. |
| Projeção | Localização, fallback, causa legível, ausência de vazamento e nenhuma regra na Godot/event bus operacional. |
| Performance | Sem observers, índice grande, multi-hit e cadeias; bytes e latência no servidor e no percurso REST. |

## 13. Critério de conclusão

O trabalho só termina quando:

1. Os três exemplos do autor são expressos por componentes JSON, inclusive com observador que não participa diretamente do acontecimento.
2. A semântica dos seletores hierárquicos é única na autoria, runtime, preview, API e ferramentas.
3. Toda mudança relevante do inventário produz fatos tipados por um produtor definido.
4. Todos os formatos de gatilho/resposta da autoria atual passam por um dispatcher e pelo executor comum; não há execução duplicada de compatibilidade.
5. A migração preserva resultados, ordem, RNG, restrições e durabilidade dos cenários de referência, com diferenças de formato/versionamento explicitamente certificadas.
6. Runs novas, antigas suportadas, saves, retomadas, branches e hot reload têm política documentada, sem conversão silenciosa de histórico.
7. O cliente consegue apresentar o motivo e a causalidade a partir dos fatos confirmados, sem recalcular gameplay.
8. O relatório final distingue implementação técnica de aprovação humana e de garantia universal de desempenho.

## 14. Fora deste ciclo

- Editor visual completo de regras, scripting arbitrário, código executável enviado por JSON e consultas de propriedades por reflexão.
- Transporte multijogador, sistemas novos de inventário/companions ou conversão de todo log técnico em evento de gameplay.
- Efeitos calculados por texto de mensagem, inferência de comportamento a partir de nomes de recursos ou tipos de container.
- Reescrita do balanceamento, do mapa ou da apresentação das cartas.

Adicionar um tipo inteiramente novo de operação continua exigindo executor e schema suportados. A generalização permite combinar e observar capacidades existentes; não torna semânticas inexistentes executáveis por nome.
