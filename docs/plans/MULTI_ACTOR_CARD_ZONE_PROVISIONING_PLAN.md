# Zonas de cartas para múltiplos participantes e pool compartilhado

Data: 26/10/2026. Estado: plano, sem alteração na engine nesta etapa.

## 1. Objetivo

Disponibilizar as zonas configuradas com `ownerScope: Actor` para todos os participantes efetivos de um encontro, antes que qualquer fluxo de abertura, ativação, efeito ou decisão automática dependa dessas zonas.

Isso permite usar um monte compartilhado e mãos individuais por configuração, sem criar outro sistema de deck, movimentação ou IA. A engine continua desconhecendo o significado dos nomes `hand`, `draw`, `dealer` etc.

O provisionamento cria apenas recipientes vazios ausentes. Cartas, distribuição, embaralhamento, recolhimento e limpeza continuam sob autoridade dos fluxos JSON existentes.

## 2. Diagnóstico confirmado no código

| Arquivo / método | Situação atual | Implicação |
|---|---|---|
| `RunManager.CreateRun`, chamada de `CardZoneRunInitializer.Initialize` | Passa `[]` como participantes adicionais | A criação inicial conhece apenas o jogador |
| `CardZoneRunInitializer.Initialize` | Acrescenta o jogador aos IDs recebidos e executa o bootstrap | Funciona para participantes conhecidos, mas não descobre encontros futuros |
| `CardZoneBootstrapper.Create` | Expande zonas Actor para os IDs fornecidos, ordenados, e chama `CreateEmpty` | É um bootstrap completo; não deve ser reaplicado sobre uma topologia viva para corrigir zonas ausentes |
| `CardZoneTransitions.CreateEmpty` | Constrói uma topologia nova e rejeita endereços duplicados | Não é uma operação de merge/provisionamento incremental |
| `CombatRunCoordinator.StartEncounter`, duas sobrecargas | Resolve/materializa participantes e chama `InitializeCanonicalFlow` | Participantes reais já são conhecidos antes da inicialização transacional |
| `CombatFlowPlanner.InitializeTransaction` | Resolve o contexto da revisão vinculada e chama o executor de fronteiras | Não há necessidade de consultar conteúdo corrente para descobrir as zonas |
| `CombatBoundaryExecutor.InitializeTransaction` | Executa `encounter.started` antes da ativação inicial e dos demais efeitos de abertura | É necessário garantir as zonas antes dessa primeira fronteira |
| `RunManager.AttachEncounter` | Tem um caminho alternativo, sem `initializedRun`, que também executa `encounter.started` | Esse caminho precisa cumprir a mesma pré-condição sem repetir os fluxos do caminho canônico |
| `CardZonePlaySource.CardsForActor` | Filtra fontes jogáveis pela definição e pelo proprietário da zona | Já há base para isolar mãos privadas por ator |

O relatório fornecido corresponde à lacuna observada. Esta análise é uma inspeção do código; os testes descritos abaixo ainda precisam ser implementados/executados para esta alteração.

## 3. Decisões de arquitetura

### 3.1. Provisionar no encontro, sem antecipar o mapa inteiro

Não preencher a lista vazia de `CreateRun` com todos os adversários de todos os nós. Isso criaria zonas para atores que talvez nunca participem, acoplaria bootstrap à progressão e não resolveria participantes gerados/configurados posteriormente.

Criar zonas a partir dos IDs de instância efetivamente materializados em `CombatState.GetAllActors()`. Usar os IDs após bindings, como o binding do personagem persistente, não o ID da definição do inimigo ou um alias anterior à materialização.

Incluir todos os participantes: jogador, adversários, aliados e outros lados. O provisionador não filtra por facção, controller, intenção ou capacidade de usar cartas.

### 3.2. Uma operação incremental, pura e idempotente

Adicionar um colaborador de domínio em `Core/CardZones`, por exemplo `CardZoneParticipantProvisioner`, com contrato conceitual:

```text
EnsureParticipantZones(
    compiledSystem,
    existingTopology,
    participantInstanceIds,
    runOwnerId,
    scopeId
) -> Result { topology, createdAddresses, fingerprint }
```

O nome/assinatura final pode seguir as convenções locais. Propriedades obrigatórias:

1. Receber um sistema já resolvido da revisão da run; não carregar arquivos ou consultar o catálogo ativo.
2. Selecionar apenas definições `OwnerScope.Actor`, qualquer que seja o nome da zona.
3. Construir os endereços ausentes com o mesmo escopo de topologia já utilizado pelo bootstrap. Para o cenário do relatório, isso produz `$run::dealer::hand`.
4. Preservar todas as zonas existentes, suas cartas, ordem, atributos e instâncias registradas.
5. Criar somente `CardZoneState` vazios ausentes, com coleções imutáveis e compartilhamento estrutural quando possível.
6. Retornar as mesmas identidades/estado equivalente quando chamado novamente; não gerar IDs de cartas nem consumir RNG.
7. Não avançar a sequência ou o passo da run. O avanço pertence ao comando transacional do encontro.
8. Normalizar o conjunto de participantes e ordenar IDs/definições por `StringComparer.Ordinal`, preservando a ordem de gameplay dos participantes no combate.
9. Validar toda a entrada e a topologia resultante antes de retornar sucesso. Falhas não alteram a entrada.

Não reparar silenciosamente uma topologia inválida, apagar zonas desconhecidas, substituir zonas existentes ou criar montes globais ausentes como efeito colateral. Problemas de bootstrap/configuração devem continuar sendo reportados.

Preferir extrair/reutilizar a geração de endereços do bootstrap, sem transformar `CreateEmpty` em uma API ambígua que às vezes reinicializa e às vezes preserva dados.

### 3.3. Um ponto de pré-condição por caminho de abertura

No caminho canônico, chamar o mesmo provisionador no início de `CombatBoundaryExecutor.InitializeTransaction`, antes de `encounter.started`. Atualizar somente o candidato imutável local de `RunState.Deck`.

Assim, as duas sobrecargas de `CombatRunCoordinator.StartEncounter` convergem na mesma regra, e chamadas diretas ao planner/executor usadas em testes também recebem essa garantia.

No caminho alternativo de `RunManager.AttachEncounter`, quando `initializedRun == null`, aplicar a mesma operação antes de executar sua fronteira existente. Quando `initializedRun` já foi fornecido, validar a garantia estrutural, sem reexecutar distribuição ou lifecycle.

Não criar dois algoritmos de provisionamento. Cada caminho chama o mesmo colaborador. Não chamar `CardZoneRunInitializer` novamente nem repetir `run.started`.

### 3.4. Transação completa

Fluxo esperado:

```text
validar comando/revisão e deduplicação
→ materializar participantes reais
→ construir candidato com zonas Actor ausentes
→ executar fronteiras JSON existentes
→ ativação/efeitos/intents conforme fluxo atual
→ validar candidato completo
→ persistir um único START_ENCOUNTER
→ publicar o resultado confirmado
```

Se uma distribuição, capacidade, condição, efeito, ativação ou gravação falhar, não publicar zonas novas, alterações do pool, combate ativo ou determinismo parcialmente avançado.

A criação de zonas pode ser idempotente; a distribuição não é presumida idempotente. O retry do comando deve ser atendido pela deduplicação canônica existente, sem repetir o fluxo. Auditar publicações de eventos e caches para que observadores externos não recebam um candidato abandonado.

### 3.5. Identidade, reutilização e limpeza

Um endereço existente continua representando a mesma zona. Reutilizar o mesmo `InstanceId` de adversário no mesmo escopo preserva suas zonas e cartas na etapa de provisionamento.

Isso não estabelece a regra de gameplay de manter a mão: um fluxo JSON de encerramento/abertura pode recolher, destruir ou mover essas cartas antes de uma nova distribuição. Se não houver fluxo de limpeza, as cartas permanecem. Não adicionar um reset padrão escondido.

Para um adversário que deve ser uma entidade distinta em cada encontro, o autor do cenário deve usar identidades de instância distintas. Não trocar automaticamente o escopo das zonas para `CombatId`, porque isso mudaria os endereços e fluxos existentes.

Participantes posteriores recebem as zonas quando seu encontro começa. Atores adicionados durante um combate por uma futura mecânica de invocação exigirão a mesma pré-condição no comando de entrada; não simular suporte a essa mecânica neste ajuste de abertura.

### 3.6. Pool compartilhado e distribuição continuam configuráveis

Uma configuração de referência pode ter um `pool` com `ownerScope: RunOwner` ou `Global`, e uma zona privada com `ownerScope: Actor`. A escolha entre Global e RunOwner deve respeitar a semântica atual do setting; não introduzir outro escopo ou duplicar o monte por participante.

Após provisionar, movimentar cartas do pool para a zona do ator usando as operações e bindings já disponíveis. Não alterar automaticamente `CardInstanceState.OwnerId`: autoria/proveniência da instância e posse/endereço atual da zona não devem ser confundidos. Manter o contrato atual das transições.

**Criar zonas não distribui cartas para todos os atores.** Hoje, `CardZoneRunFlowDispatcher.Execute` usa o jogador quando não recebe `activeActorId`. `ActorScope.Any` filtra elegibilidade da invocação; não implica um loop por todos os participantes.

Por isso, a configuração de teste deve usar fronteiras já executadas no contexto de cada ator, como a ativação, e comprovar a distribuição antes da decisão automática correspondente. Não transformar `encounter.started` em N invocações silenciosamente: isso poderia multiplicar fluxos que movem ou embaralham o pool uma única vez.

Se o setting exigir uma distribuição simultânea a todos antes da primeira ativação e os mecanismos atuais não expressarem isso, registrar essa necessidade como uma extensão de fluxo separada e configurável. Não ampliar este ajuste com um foreach implícito, novo endpoint ou comportamento especial para `dealer`.

### 3.7. Permissão e visibilidade

Reutilizar `CardZonePlaySource` e a validação canônica do comando. Uma mão Actor só deve fornecer cartas ao ator proprietário, mesmo se o cliente enviar manualmente o ID de uma carta alheia.

Preservar as permissões já existentes de fontes Global e RunOwner: o teste de mãos privadas não deve proibir um compartilhamento que o setting configurou como jogável.

As zonas adicionais precisam respeitar `Visibility.Contents` e `Visibility.Order`. Um cliente do jogador pode receber a existência/contagem de uma mão adversária sem receber suas identidades ou ordem privada. Isso não equivale a implementar autenticação multiplayer.

Auditar consumidores que agregam todas as zonas com `allowsCardPlay`: essa propriedade indica capacidade da zona, não propriedade pelo jogador. Corrigir somente consumidores que apresentem cartas alheias como mão do ator local ou ignorem os filtros existentes.

## 4. Etapas de implementação

### Etapa 1 — Testes de reprodução e contrato

Adicionar uma fixture mínima com jogador, dealer, pool compartilhado e zona Actor, com nomes alternativos em uma variação. Reproduzir a falta da zona e registrar os invariantes de preservação, revisão e ausência de distribuição automática.

**Arquivos:** testes de `CardZones`, `Combat/Flow` e `CombatRunCoordinatorTests`.

**Aceite:** teste vermelho no caminho real de abertura; nenhuma dependência de Godot, web ou nomes especiais de cartas/atores.

**Commit sugerido:** `test(card-zones): reproduce missing participant zones at encounter start`.

### Etapa 2 — Provisionamento incremental imutável

Implementar o colaborador e, se útil, uma primitiva de merge de endereços em `CardZoneTransitions`. Reutilizar o planejamento de endereços sem recriar topologias vivas. Retornar endereços criados em ordem canônica e fingerprint verificável.

**Aceite:** idempotência; inputs intocados; IDs duplicados de entrada tratados como conjunto; validação de identidades/endereço; nenhuma carta criada/movida e nenhum consumo de RNG. Zonas existentes preservam integralmente conteúdo e ordem.

**Commit:** `feat(card-zones): ensure missing actor zones without resetting topology`.

### Etapa 3 — Integrar a pré-condição à inicialização canônica

Aplicar o provisionamento antes de `encounter.started` em `CombatBoundaryExecutor.InitializeTransaction`. Utilizar os participantes materializados e o sistema da revisão vinculada. Garantir que ambos os overloads do coordenador passam pelo mesmo contrato, antes de intents/IA que consultem cartas.

**Aceite:** todos os participantes, inclusive de encontros posteriores, têm suas zonas antes dos fluxos dependentes; falha retorna sem publicar candidato.

**Commit:** `feat(combat): provision participant card zones before encounter lifecycle`.

### Etapa 4 — Fechar o caminho alternativo e a transação

Atualizar `AttachEncounter` sem `initializedRun`, usando o mesmo provisionador. No caminho já inicializado, validar a cobertura sem disparar outra fronteira. Testar retry, falha de persistência e falha depois de movimentar cartas no candidato.

**Aceite:** um único commit de encontro; nenhuma distribuição duplicada; nenhum evento/candidato parcial observável; sequência e determinismo de origem preservados na falha.

**Commit:** `fix(run): keep participant zone provisioning atomic across encounter entry paths`.

### Etapa 5 — Validar a configuração de pool e os consumidores

Construir uma configuração de integração exclusivamente por JSON com pool único, mãos Actor e fluxo de distribuição existente no contexto de cada ator. Confirmar que o bot usa instâncias de sua própria zona. Auditar legal actions, execução direta, projeções REST e consumidores de mão do ator local.

Não adicionar um deck alternativo, cartas artificiais para a IA ou uma implementação de compra no controller. Se a distribuição exigida revelar outra lacuna, documentar seu alcance antes de introduzir semântica nova.

**Aceite:** mover do pool para ambas as mãos; coleção física consistente; tentativa de jogar carta da mão alheia rejeitada; informações privadas ocultadas. Frontends não tratam a mão adversária como mão do jogador.

**Commit:** `test(card-zones): certify shared pool dealing and actor-owned play sources`.

### Etapa 6 — Reutilização de atores, revisão, replay e regressões

Cobrir o mesmo adversário em dois encontros, com e sem fluxos explícitos de recolhimento, e outro adversário com nova identidade. Repetir a jornada com seed/comandos/revisão iguais. Testar reload de conteúdo: a run continua usando sua revisão vinculada, ou a revisão ativada por um comando canônico já previsto pelo projeto.

**Aceite:** repetição sem reset implícito; hashes reproduzíveis; criação de zonas não avança RNG; zonas Global/RunOwner e configurações sem zonas Actor mantêm o comportamento anterior.

**Commit:** `test(determinism): cover participant zones across encounters retries and replay`.

### Etapa 7 — Contrato REST, documentação e certificação

Usar os endpoints canônicos existentes para abrir uma tentativa de QA, entrar no encontro, ler zonas, executar fluxos/ações permitidos e verificar replay. Registrar o estado antes/depois de falhas e a ausência de modificações no save de origem.

Documentar endereço por ator, momento da criação, revisão, distribuição por contexto e política de reutilização. Explicar que uma zona vazia é diferente de uma zona ausente. Não criar endpoint de “criar mão do dealer”.

**Aceite:** fixture de pool com mais de um ator passa via API real; documentação explica precisamente o que é automático e o que pertence ao JSON.

**Commit:** `docs(card-zones): document participant provisioning and multi-actor pool validation`.

## 5. Matriz de regressão obrigatória

| Caso | Verificações |
|---|---|
| Jogador + dealer | Todas as definições Actor para ambos, vazias antes dos fluxos |
| Vários lados e atores | Aliados/adversários recebem zonas sem hardcode por lado/controller |
| Novo adversário em encontro posterior | Apenas endereços ausentes são acrescentados |
| Idempotência | Segunda chamada cria zero zonas e mantém hashes, cartas e RNG |
| Ordem de entrada diferente | Provisionamento produz o mesmo resultado, sem reordenar turnos/participantes |
| Pool compartilhado | Um monte físico; movimentações existentes conservam identidades e membership único |
| Repetição do ator | Cartas preservadas pelo provisionamento; limpeza somente quando o fluxo a configura |
| Ownership de jogo | Carta privada alheia rejeitada na descoberta e na execução direta |
| Visibilidade | Sem IDs/ordem privados vazando nas projeções ou intenções do adversário |
| Falha de fluxo/capacidade/ativação | Nenhuma alteração persistida, publicação parcial ou avanço de origem |
| Falha de gravação | Sem novo encontro/zona confirmado; retry canônico não duplica distribuição |
| Revisão vinculada | Não cria zonas adicionadas apenas no conteúdo corrente não ativado pela run |
| Global/RunOwner | Endereços, permissões e cartas anteriores mantidos |
| Nomes arbitrários | Mesma funcionalidade sem `hand`, `draw`, `player` ou `dealer` especiais |
| Histórico | Checkpoint/serialização/replay/branch preservam a topologia registrada e os hashes aplicáveis |

## 6. Compatibilidade e limites

- Não fazer migração com escrita durante GET, leitura de replay ou carregamento de uma projeção.
- Uma run antiga recebe as zonas faltantes ao iniciar um novo encontro sob a implementação aplicável. Um combate antigo já aberto com participantes sem zonas não é corrigido implicitamente por esta etapa; uma eventual migração exigiria operação canônica e política de versão próprias.
- Criar zonas vazias muda o hash de topologias Actor que antes omitiam adversários. Isso é uma mudança real de estado, mesmo sem mudar cartas. Verificar a política de versão da engine/replay antes de atualizar golden tests; não declarar hashes históricos iguais nem reescrever journals antigos.
- Configurações sem definições Actor devem ser no-op neste provisionamento. Configurações com Actor devem preservar cartas, recursos e fluxos existentes, mas agora terão os recipientes adicionais previstos.
- A certificação compara jornadas novas equivalentes e também verifica a leitura compatível de dados históricos conforme a política de versão suportada. Não prometer replay entre versões incompatíveis.
- O trabalho não inclui invocação de atores durante combate, inventários/decks novos, foreach implícito de distribuição, multiplayer ou alterações no algoritmo do bot.

## 7. Critério de conclusão

O cenário inicial do relatório deve passar de `DEALER_HAND_CREATED=False` para `True`, e o fluxo configurado deve conseguir movimentar cartas do pool para esse endereço sem `Card zone not found`.

Antes de qualquer fluxo de distribuição, as novas zonas permanecem vazias. Reexecutar o provisionamento não altera cartas ou determinismo. Falhar a abertura não deixa zonas ou movimentos persistidos, e todos os caminhos de encontro obedecem à mesma garantia.
