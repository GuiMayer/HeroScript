# Fluxo circular configurável e mesa com múltiplos participantes

Data: 27/10/2026. Estado: implementado em 12 etapas. Consulte o [contrato final](../content/circular-table-flow.md) e o [relatório de implementação e certificação](CIRCULAR_TABLE_FLOW_IMPLEMENTATION.md).

## 1. Objetivo e limites

Oferecer uma alternativa ao fluxo atual de combate para jogos de mesa com sequência circular, direção, inversão, salto, mãos individuais e monte compartilhado. UNO com quatro participantes será a configuração de referência, sem inserir suas regras específicas no código da engine.

O fluxo atual por rodada continua disponível. Não transformar todos os combates em mesas circulares, não criar um segundo coordenador de partida e não deslocar regras para Godot ou web. Ambos os fluxos usam os comandos REST, transações, efeitos, zonas, IA, persistência e replay canônicos existentes.

Distribuição, número de participantes, composição do deck, regras de compra, penalidades e vitória são conteúdo JSON vinculado à revisão da run. Não incluir autenticação multiplayer, novas interfaces gráficas ou animações nesta extensão. A adaptação do app limita-se a consumir as projeções e ações válidas da engine.

Os exemplos de campos novos abaixo registram as **propostas de contrato da etapa de planejamento**. Para autoria, use os nomes finais do contrato e os JSONs executáveis de `circular-table`; por exemplo, `roundPolicy` é um enum string no contrato final.

## 2. Diagnóstico conferido no projeto

| Área | Evidência atual | Consequência |
|---|---|---|
| Zonas por ator | `CardZoneParticipantProvisioner`, abertura canônica e `AttachEncounter` garantem recipientes antes dos fluxos | Reutilizar; não refazer bootstrap ou criar outro deck |
| Propriedade | `CardZonePlaySource` e `LegalActionResolver` já permitem fontes próprias de bots | Cartas reais da IA não são uma lacuna desta etapa |
| Ordem | `TurnOrderResolver` resolve Fixed, Resource, Initiative, Atb e Conditional | Ordenar participantes e percorrer a ordem são responsabilidades distintas |
| Próxima ativação | `CombatBoundaryExecutor.StartActivation` procura o primeiro ator ainda ausente de `CompletedActorIds` | Impede A voltar a agir após B inverter, se A já agiu na rodada |
| Estado | `ActivationState` guarda ordem, índice e participantes concluídos | Falta estado explícito da navegação circular |
| Alvos | Efeitos e gambits possuem seletores diferentes, sem próximo/ anterior no percurso | Falta uma consulta compartilhada à sequência |
| Comandos passivos | `EvaluatePassive` valida fase e registra PASS/END_TURN | Não expressa a obrigação de comprar antes de passar |
| IA | `AutomaticFlowDriver` usa decisões e pode emitir END_TURN automaticamente | As mesmas condições devem valer em todos esses caminhos |
| Fórmulas | `GameplayFormulaContext.Build` recebe recursos, mas não a topologia completa como contexto de consulta | `hand_count` e `opponent_hand_count` são cópias auxiliares no conteúdo UNO |
| Vitória | `CombatOutcomeResolver` deriva VICTORY/DEFEAT dos lados ligados aos controllers Player | Não publica diretamente o vencedor individual por mão vazia |
| App UNO | `requiresDrawBeforePassing` em `examples/uno-clash-web/app.js` decide localmente quando exigir compra | A API precisa rejeitar o comando inválido independentemente do app |

Complemento ao relatório: os mecanismos existentes movem sete cartas para uma mão indicada, mas isso não significa distribuir simultaneamente para todas as mãos na abertura. `ActorScope.Any` não é um foreach. A implementação deve certificar essa abertura e oferecer iteração **explicitamente configurada**, caso a composição existente não a expresse.

Outro limite: `table` e `discard` separados já permitem preservar a carta atual ao reciclar. Ainda é necessário testar esgotamento total, fallback sem progresso e compra parcial. Não presumir que a configuração atual fecha todos esses casos.

## 3. Decisões de arquitetura

### 3.1. Uma engine e duas estratégias de percurso

Adicionar uma política `activationSequence` ao fluxo resolvido, separada de `turnOrder`:

- `PerRound`: comportamento atual, cada participante elegível atua antes de iniciar outra rodada.
- `Circular`: próxima ativação encontrada a partir do ator atual, no sentido persistido, sem usar `CompletedActorIds` como filtro.

`turnOrder` continua produzindo a ordenação inicial e os dados de iniciativa; `activationSequence` decide como percorrê-la. Não adicionar um `TurnOrderStrategy.Uno` nem duplicar `CombatFlowPlanner`, `AutomaticFlowDriver` ou `CombatBoundaryExecutor`.

Extrair a escolha do sucessor para um redutor puro, por exemplo `ActivationSequenceReducer`, com implementações de estratégia pequenas. Inicialização e avanço chamam esse redutor e depois executam o mesmo lifecycle existente.

A política será obrigatória nas definições de regras de combate publicadas após a atualização. Antes de ativar essa validação, migrar automaticamente os JSONs de conteúdo existentes que não declaram a política, acrescentando `flow.activationSequence: { "strategy": "PerRound" }`. Preservar políticas já declaradas, em especial `Circular`; não sobrescrever uma escolha explícita.

Não manter um fallback silencioso no runtime para ausência da política. Após a migração, definição sem estratégia, desconhecida ou parcialmente preenchida é erro de conteúdo com caminho e ID. Os demais mecanismos novos (consultas, condições de comando e resultado individual) continuam opcionais.

Essa migração é uma transformação explícita dos arquivos-fonte, executada por ferramenta e revisada no diff. Deve ser idempotente, oferecer dry-run/relatório, preservar todos os demais valores e não publicar conteúdo automaticamente. Incluir configurações empacotadas, exemplos e fixtures de publicação; fixtures propositalmente inválidas continuam inválidas. Mods externos devem receber a mesma ferramenta e uma instrução clara de migração, sem alterar pastas externas automaticamente.

Não executar essa transformação durante GET, carregamento de save ou replay. Manifestos, bundles, snapshots e journals históricos permanecem intactos. O conteúdo migrado produz uma nova revisão; continuar runs históricas depende da política de versão suportada, nunca de reescrever sua revisão vinculada. Testar separadamente equivalência de gameplay e mudança esperada de revisão/hash do conteúdo.

Exemplo do fluxo existente após a migração:

```json
{
  "flow": {
    "activationSequence": { "strategy": "PerRound" }
  }
}
```

O fragmento acima é acrescentado ao objeto `flow` existente, preservando suas outras propriedades; não substitui o objeto inteiro.

### 3.2. Estado circular imutável

Persistir um estado opcional próprio, associado à ativação, contendo:

- ID da estratégia e fingerprint da política;
- assentos/ordem canônica de instâncias;
- ator âncora/cursor identificado por ID, com índice como projeção derivada;
- direção Forward ou Reverse;
- avanço pendente, inicialmente um assento elegível;
- contagem de ativações encerradas para a política de rodada;
- versão estrutural, quando necessária à persistência.

Não guardar direção, assento ou próximo jogador em resources artificiais. Esses campos pertencem ao fluxo da engine. Recursos de gameplay, como dívida de compra, continuam configuráveis e separados.

O redutor retorna um novo estado e fatos de transição. Não altera o combate recebido, não usa relógio real e não consome RNG para navegar. O RNG existente só é usado se a política de ordem/primeiro ator escolher explicitamente um critério aleatório.

### 3.3. Semântica do percurso

Para uma mesa A, B, C, D, no sentido Forward:

| Situação após B | Próximo ator |
|---|---|
| Avanço normal | C |
| Inverter direção | A, mesmo se A já atuou |
| Saltar um participante | D |
| Inverter e saltar um participante | D, percorrendo B → A → D |

O avanço padrão é 1. Saltar um participante acrescenta 1 ao avanço; não significa avançar três posições. Alterações dentro de uma ação são aplicadas na ordem de resolução dos efeitos, não pela ordem em que o frontend recebeu respostas.

Participantes inelegíveis são ignorados segundo a política configurada; passar sobre um assento não executa START/END de ativação daquele ator. Definir inelegibilidade usando os conceitos já existentes de entidade/participação, sem testar `health` pelo nome.

Zero participantes elegíveis deve terminar pela política de resultado ou retornar uma falha explícita, nunca entrar em loop. Com um participante, a política declara se pode retornar ao próprio ator. Alterações futuras de participantes no meio da partida não estão incluídas; snapshots inválidos/removidos devem ter tratamento explícito e testado.

O contrato inicial de Circular aceitará a ordem resolvida no início do encontro. Combinações com Atb, ContinuousTick ou reordenação durante a mesa serão rejeitadas na validação até existir uma política de rebase por ID implementada e testada. Não anunciar compatibilidade com todas as estratégias atuais só porque os tipos permitem combiná-las.

### 3.4. Rodadas não são voltas do índice

Inverter direção ou cruzar o índice zero não pode disparar uma rodada nova por acidente. Circular exige uma política explícita:

- `Disabled`: não emite fronteiras de rodada; ativações continuam numeradas monotonicamente.
- `ActivationCount`: após N ativações reais encerradas, inicia outra rodada; N vem da configuração ou da quantidade inicial de participantes elegíveis.

Saltos não contam como ativações realizadas. O perfil UNO inicial usará `Disabled`; status, custos e regeneração necessários serão ligados a ativações. Validar/rejeitar conteúdo que dependa de fronteiras de rodada indisponíveis nesse perfil, em vez de ignorá-lo silenciosamente.

PerRound mantém sua definição atual de rodada. Todos os gatilhos START/MIDDLE/END e as durações existentes devem ter regressão nesse caminho.

### 3.5. Mudanças na sequência são efeitos genéricos

Adicionar ao processador comum um tipo de efeito de sequência, com operações limitadas inicialmente a:

- inverter direção;
- definir direção;
- acrescentar avanço/salto ao encerramento da ativação corrente.

O efeito deve ser consumido igualmente quando vem de carta, status, relíquia ou ação. Ele desconhece UNO, cor e símbolo. A engine valida se a estratégia selecionada suporta a operação; uma inversão não pode ser silenciosamente ignorada em PerRound.

Alterar direção não encerra a ativação por si só. A transição só ocorre quando o fluxo canônico solicita o avanço. O avanço pendente é consumido uma vez nessa transição e volta ao valor padrão. Preview não o consome; retry não o repete.

Para um Duelo em que Inverter concede outra jogada, o JSON compõe a operação correspondente. Não codificar `if participantCount == 2` na implementação do efeito.

### 3.6. Uma consulta de participantes para todos os consumidores

Criar um serviço/redutor de seleção de atores, com um descritor comum e adapters nos contratos atuais. Seletores iniciais:

- explícito, origem, alvo e ator ativo, reutilizando os bindings existentes;
- todos os participantes elegíveis, opcionalmente filtrados por relação;
- próximo/anterior no sentido atual, com deslocamento inteiro;
- próximo agendado, considerando avanço pendente, como consulta distinta.

`Next` é o vizinho elegível no sentido atual. `NextScheduled` inclui os saltos já programados. Não confundir esses seletores: uma penalidade aplicada ao próximo participante não deve mudar de alvo apenas porque outra parte do efeito já programou um salto.

Efeitos, targeting de cartas/ações, gambits, condições, preview e descoberta de ações usam a mesma resolução. A engine pode resolver o alvo relativo sem o cliente enviar seu ID. Se o cliente enviar um ID explícito, validá-lo contra o seletor, sem aceitá-lo como autoridade.

Respeitar os contratos de lock/retarget de reações já existentes. O conteúdo declara o ponto de captura do alvo; a sequência é lida do snapshot desse ponto. Não alterar retroativamente um alvo já fixado depois de uma inversão.

### 3.7. Consultas à topologia, sem contadores duplicados

Criar consultas puras ao snapshot, com descritores JSON e bindings de proprietário. Resultados iniciais: contagem de cartas em uma zona, soma de zonas selecionadas e quantidade de participantes elegíveis.

Exemplos de variáveis declaradas pelo conteúdo:

- `myHand`: Count(hand, Actor/source);
- `nextHand`: Count(hand, Actor/Next);
- `availableCards`: Count(draw, RunOwner) + Count(discard, RunOwner);
- `participantCount`: Count(EligibleParticipants).

Os nomes `hand`, `draw` e `discard` aparecem somente nos descritores do setting. A engine não possui uma fórmula interna chamada `uno_available_cards`.

Injetar os resultados declarados como variáveis na infraestrutura de fórmulas existente. Não introduzir outra linguagem de expressões nem fórmulas que leiam arquivos, estado global ou consultem REST. O contexto recebe explicitamente topologia, combate, ator, alvo, revisão e candidato local atual.

O mesmo provedor deve atender condições de componentes, efeitos, contagens de fluxos, gambits, legalidade de comandos e resultado. Atualizar os adapters necessários de `GameplayFormulaContext`, `CardZoneFlowContext` e contextos de decisão, sem copiar a contagem para cada um.

Usar contagem inteira exata e validar conversão para a representação numérica atual das fórmulas. Zero cartas em zona existente retorna zero; zona/endereço desconhecido é erro de conteúdo/contexto, não zero silencioso. Agregações sobre proprietários devem evitar contar o mesmo endereço duas vezes.

As consultas internas podem ler zonas ocultas para validar regras. A projeção pública não expõe seus IDs/ordem por causa disso. Caches são derivados e vinculados ao snapshot completo relevante; não indexar só por step nem manter valores entre dois candidatos mutáveis.

### 3.8. Fatos da ativação para regras temporais

“Comprou neste turno” deve referir-se a uma ativação, não a um ciclo de rodada. Acrescentar fatos imutáveis de ativação, derivados de comandos e movimentações confirmados:

- quantidade de execuções confirmadas de ação/fluxo com um identificador ou tag configurado;
- quantidade efetivamente movida por operação configurada;
- evidência de tentativa confirmada que não moveu cartas, quando a regra permitir contar essa tentativa.

O conteúdo escolhe qual fato usar. Uma ação do próprio jogador marcada como compra normal não deve ser confundida com receber uma penalidade. Uma compra parcial registra a quantidade real. Falha/rejeição não registra tentativa; preview e retry não incrementam contadores.

Preferir um acumulador compacto no snapshot da ativação, reiniciado ao iniciar a próxima, com identidade rastreável nos frames. Não varrer o journal inteiro a cada consulta e não criar resource numérico para espelhar esses fatos.

### 3.9. Legalidade declarativa para comandos e saída da ativação

Adicionar regras opcionais de legalidade no fluxo, com ID, comandos/ações abrangidos, actor scope, consultas/fórmula e código/motivo de recusa. Reutilizar o mecanismo de condições existente sempre que possível.

A regra é consumida pela autoridade canônica e serve para:

1. descobrir ações permitidas e explicar bloqueios;
2. validar comandos REST enviados diretamente;
3. filtrar decisões dos bots;
4. validar END_TURN automático;
5. impedir caminhos de saída de fase/ativação de contornarem a mesma regra.

Não basta desabilitar END_TURN no app ou validar apenas `EvaluatePassive`. Auditar `GameplayCommandGateway`, chamadas diretas ao planner, resolução pendente e invocação de fluxos durante combate. Qualquer comando que precise ser do ator ativo deve ser verificado no servidor.

Exemplo conceitual da regra relatada:

```text
needsDraw = penalty == 0
            && canContinuePlaying
            && !hasLegalCard
            && normalDrawAttemptsThisActivation == 0
            && availableCards > 0

END_TURN e PASS são permitidos somente quando !needsDraw
e não existe escolha/penalidade obrigatória pendente.
```

`canContinuePlaying`, o tratamento da penalidade, o significado de “tentativa” e as ações que PASS representa vêm do JSON. Esse exemplo não define uma regra universal de UNO.

**Evitar recursão:** `hasLegalCard` consulta a legalidade básica de PLAY_CARD (posse, custos, fase, condição, alvos etc.), sem chamar a descoberta completa de comandos e voltar à condição de END_TURN. Implementar em camadas, detectar ciclos de dependência ao compilar e compartilhar avaliações dentro da consulta.

Continuar usando `uno_draw` como ação configurada, quando aplicável, com efeitos/fluxos já existentes. Não é necessário criar um endpoint especial de comprar. O modo UNO deve manter `ai.autoEndAfterAction: false` e ter decisões de comprar, resolver pendências e terminar filtradas pelas regras canônicas; outros modos preservam sua configuração.

### 3.10. Distribuição inicial explícita para todos

Primeiro certificar se a composição de efeitos e bindings já permite a abertura completa com quatro participantes. Caso não permita, implementar uma operação de iteração explícita no executor de fluxos, por exemplo `ForEachParticipant`, que chame um subfluxo existente com um binding `IterationActor`.

Regras desse mecanismo:

- lista capturada antes da iteração e ordenada conforme a política explícita (assentos, ordinal etc.);
- filtro de participantes no JSON;
- não altera o ator ativo real durante a iteração;
- ordem de distribuição declarada: BatchPerActor ou RoundRobin;
- limites de profundidade/iterações e grafo de chamadas validados;
- subfluxo usa as mesmas transições de movimentação e o mesmo contexto determinístico;
- uma única transação de abertura para shuffle, mãos, carta inicial da mesa e demais efeitos;
- falha no quarto participante desfaz também as distribuições dos três anteriores;
- nenhuma iteração automática adicionada a fluxos já existentes.

Para a configuração UNO de referência: 108 instâncias no pool, sete por participante e uma carta na mesa deixam 79 no monte, se nenhuma regra adicional remover cartas. A seleção da carta inicial, suas restrições e os efeitos que ela aciona são JSON; não forçar uma carta vermelha por nome no código.

### 3.11. Resultado individual como política alternativa

Preservar o resultado atual por lados/controladores como estratégia existente. Acrescentar opcionalmente uma estratégia de condições por participante, com:

- condição de vitória declarada por consultas/fórmulas;
- pontos de avaliação configurados;
- seleção explícita do(s) vencedor(es);
- prioridade e política de empate;
- resultado persistido com IDs de atores e lados, condição e evidências.

O resolvedor passa a receber o contexto de run/deck necessário, sem carregar conteúdo corrente. Para UNO: mão vazia após resolução completa da jogada e de escolhas obrigatórias. Não avaliar a mão ainda vazia durante a montagem da partida. Não encerrar antes de uma escolha de cor ou pendência que o setting exija resolver.

O vencedor é um ator, independentemente de ser Player ou AI. VICTORY/DEFEAT podem continuar como projeções relativas ao jogador da run, mas não são a autoridade que identifica o vencedor. Journal, conclusão do encontro, histórico, replay e progressão devem consumir o resultado explícito sem aplicar premiações de vitória do jogador a um bot vencedor.

Não simular vitória individual matando os demais atores na configuração de referência. Embora a composição antiga seja possível, manter dois mecanismos autoritários simultâneos nesse mesmo perfil dificultaria verificar resultados.

## 4. Exemplos de configuração proposta

### 4.1. Seleção do percurso sem alterar a ordem inicial

Fragmento dentro de `combat-rules`:

```json
{
  "turnOrder": {
    "strategy": "Fixed",
    "recalculateAt": "CombatStart",
    "tieBreak": { "strategy": "StableActorId", "epoch": "Unspecified" }
  },
  "flow": {
    "activationSequence": {
      "strategy": "Circular",
      "circular": {
        "initialDirection": "Forward",
        "firstActor": "RunOwner",
        "eligibility": "CanParticipate",
        "allowSelfSuccessor": true,
        "roundPolicy": { "strategy": "Disabled" }
      }
    }
  }
}
```

O mapa de assentos deve corresponder à ordem resolvida; testar A/B/C/D sem depender da ordem incidental de um Dictionary. Política incompatível com o recalculo selecionado é rejeitada ao publicar o conteúdo.

### 4.2. Consultas declaradas em uma condição

```json
{
  "ruleId": "table.require-draw-before-end",
  "commands": ["END_TURN", "PASS"],
  "actorScope": "All",
  "inputs": {
    "availableCards": {
      "query": "SumZoneCounts",
      "zones": [
        { "zoneId": "draw", "owner": "RunOwner" },
        { "zoneId": "discard", "owner": "RunOwner" }
      ]
    },
    "hasLegalCard": { "query": "HasBaseLegalAction", "actionType": "PLAY_CARD", "actor": "ActiveActor" },
    "drawAttempts": { "query": "ActivationCommandCount", "actionId": "uno_draw", "actor": "ActiveActor" }
  },
  "allowConditionId": "uno_can_end_activation",
  "failureCode": "DRAW_REQUIRED",
  "messageKey": "rules.draw_required"
}
```

A fórmula revisada referenciada deve declarar também os inputs de penalidade e orçamento usados pelo perfil. Não introduzir constantes “mágicas” no interpretador para completar o exemplo.

### 4.3. Identidade de modos/conteúdo

Manter o modo de Duelo existente e acrescentar um modo de mesa de quatro participantes, com IDs de regras próprios. O mesmo setting pode expor os dois modos se seu contrato de catálogo permitir; caso contrário, expor outro setting que reutilize os pacotes comuns.

Não substituir silenciosamente `uno_classic` nem mudar a seleção padrão do menu durante a extensão da engine. A abertura REST declara quatro instâncias, cada uma em seu lado; relações entre lados são Enemy conforme o JSON.

## 5. Etapas de implementação sequencial

### Etapa 1 — Baseline e testes de reprodução

Arquivos principais: testes de `Combat/Flow`, `LegalActions`, `CardZones` e integração REST.

1. Registrar as regras efetivas do Duelo e dos modos de combate existentes.
2. Reproduzir B inverter depois de A ter atuado: Circular deve escolher A, sem alterar PerRound.
3. Reproduzir END_TURN aceito sem a compra obrigatória ao chamar a API diretamente.
4. Reproduzir quatro mãos, esgotamento total e ausência de vencedor individual explícito.
5. Capturar seeds, revisão, comandos e hashes de referência do caminho atual.

Aceite: testes vermelhos específicos, separados das regressões verdes; nenhum ajuste de gameplay para fazer o teste passar.

Commit sugerido: `test(flow): reproduce circular table and server legality gaps`.

### Etapa 2 — Contratos, migração explícita e validação de conteúdo

Adicionar o contrato obrigatório de sequência e os contratos opcionais de consultas, condições de comando, fatos da ativação e resultado. Definir os schemas, limites, enumerações e combinações aceitas antes da execução.

Criar e executar a migração idempotente de arquivos-fonte: detectar definições de combate sem `flow.activationSequence`, acrescentar `PerRound` sem mudar outros campos e produzir relatório. Não confundir um arquivo com uma definição: percorrer todas as definições contidas em cada arquivo. Preservar qualquer estratégia explícita e reportar estruturas inválidas em vez de repará-las por suposição. Migrar as configurações, os exemplos e as fixtures válidas no mesmo estágio; só então tornar a política obrigatória na validação de publicação. Não criar compatibilidade implícita no executor.

Arquivos: `CombatFlowPolicyDefinitions`, `ActivationState`, `CombatState`, `GameModeResolver`, validação do grafo e schemas existentes. Nomes novos seguem as convenções locais.

Aceite: conteúdo atual migrado continua com gameplay PerRound; executar a ferramenta novamente não produz diff; estratégias explícitas permanecem intactas; dry-run não escreve arquivos; definições sem política são rejeitadas após a atualização. Testar arquivos com múltiplas definições, estruturas inválidas, fixtures e mods. As novas revisões são esperadas e dados históricos não são escritos. Contratos opcionais ausentes não introduzem estado circular fictício.

Commit: `feat(content): migrate existing combat rules to explicit activation sequences`.

### Etapa 3 — Extrair o percurso atual e implementar Circular

Extrair a escolha do sucessor do executor de fronteiras para o redutor de sequência. Implementar direção, cursor por ID, avanço, elegibilidade e política de rodada. Manter lifecycle e execução de fases em seus colaboradores atuais.

Arquivos: `CombatBoundaryExecutor`, `CombatFlowPlanner`, `AutomaticFlowDriver`, `ActivationState`, novo redutor de sequência e adapters de `TurnOrderResolver`.

Aceite: A→B→C→D→A; B inverte e volta a A; saltos; ausência de repetição acidental de START/END; PerRound mantém seus resultados. Nenhum sistema paralelo de turnos.

Commit: `feat(flow): add deterministic circular activation traversal`.

### Etapa 4 — Efeitos de sequência e seleção relativa compartilhada

Implementar operações genéricas de sequência no executor comum de efeitos. Criar o resolvedor de participantes e integrá-lo a efeitos, targeting de cartas/ações e gambits. Respeitar timing de locks e registrar mudanças nos frames/fatos canônicos.

Aceite: carta, status e ação com a mesma operação produzem a mesma transição; selector Next usa a mesma resposta no preview e na execução; alvo forjado é rejeitado; Duelo usa composição JSON própria.

Commit: `feat(effects): share relative actor selection and sequence operations`.

### Etapa 5 — Consultas à topologia e fatos por ativação

Implementar o provedor comum e os adapters de fórmula/condição. Acrescentar fatos compactos por ativação, derivados de comandos e movimentações confirmados. Não usar resources como cache de tamanho de mão e não ler journal inteiro durante descoberta.

Aceite: contagens corretas para 2/4/6 participantes; compras parciais; contagem real após cada efeito no candidato; retry sem incrementar; erros de endereços explícitos; nenhuma exposição de ordem oculta ao app.

Commit: `feat(rules): query card topology and confirmed activation facts`.

### Etapa 6 — Legalidade canônica de comandos

Integrar regras de comandos ao gateway/resolvedor e às saídas de fase/ativação pertinentes. Separar consultas de legalidade básica de cartas da camada de condições que depende delas. Retornar motivos/códigos estruturados e expor ações válidas na projeção.

Aceite: passar antes da compra é rejeitado via API; com monte e descarte esgotados a política permite encerrar; jogador e bot seguem as mesmas regras; nenhum caminho direto ou automático contorna a condição; descoberta não entra em recursão.

Commit: `feat(commands): enforce configured command and activation exit conditions`.

### Etapa 7 — Distribuição inicial declarativa e reciclagem

Certificar a composição de abertura. Se necessário, adicionar a iteração explícita descrita em 3.10, sem alterar a semântica dos fluxos atuais. Completar políticas de compra parcial/fallback e impedir ciclos de reciclagem sem progresso.

Aceite: 108 instâncias físicas, quatro mãos de sete, mesa com uma e monte com 79; duas ordens de distribuição configuráveis e reprodutíveis; mesa preservada durante recycle; falha no último subfluxo não publica distribuições parciais.

Commit: `feat(card-zones): support authored participant dealing and bounded recycling`.

### Etapa 8 — Vitória individual configurável

Adicionar a estratégia de resultado por condição e seu contexto completo. Integrar winner IDs à conclusão, respostas, histórico e replay. Preservar a estratégia atual de lados/controladores quando a nova não for selecionada.

Aceite: qualquer um dos quatro atores pode vencer; mão vazia na montagem não encerra; escolhas pendentes respeitam a política; empate determinístico; progressão do jogador reflete o vencedor correto.

Commit: `feat(outcomes): resolve configurable participant victory conditions`.

### Etapa 9 — Perfil JSON de mesa e preservação do Duelo

Adicionar modo de mesa com quatro lados, sequência circular e uso das consultas reais. Atualizar somente esse perfil para dispensar `opponent_hand_count`; extrair definições comuns com composição de pacotes suportada, sem criar uma segunda implementação de cartas.

Configurar: salto de um participante; inversão da direção; corrente de +2 somente entre respostas elegíveis; +4 sem compor essa corrente e com perda da vez; escolhas de cor e propagação da mesa; declaração/cobrança de UNO com alvos elegíveis; compra e fim de ativação. Variantes como challenge de +4 não entram sem serem explicitamente definidas no conteúdo.

Aceite: toda regra específica aparece no JSON; nenhuma referência a cor, carta, símbolo ou jogador “dealer” em classes genéricas. Duelo mantém seu comportamento próprio por configuração.

Commit: `feat(content): add four-participant circular table mode`.

### Etapa 10 — Projeções REST e adaptação mínima do app

Expor estratégia, direção, ator atual, sucessor previsto, motivo de comando bloqueado, contagens e vencedor. Atualizar os contratos OpenAPI e os clients. Remover a autoridade de `requiresDrawBeforePassing` no app: a UI usa legal actions e seus motivos; repetir a fórmula de regra em JavaScript deixa de ser necessário.

Preservar `playerCanPlayFrom` e visibilidade por zona. Representação do tamanho do monte usa `count`; não inferir cartas restantes por `108 - duas mãos`. Uma mão adversária não deve aparecer como mão jogável local.

Aceite: frontend envia intenções e apresenta o resultado; não calcula sucessor, dívida, vencedor ou obrigação de compra. Bot, Godot e web não precisam conhecer as fórmulas do modo.

Commit: `feat(api): expose circular table state and canonical command availability`.

### Etapa 11 — Transações, revisão, persistência e regressão completa

Testar falhas após inversão, após a terceira mão distribuída, depois de uma compra parcial e durante a gravação. Comparar o estado publicado antes/depois e confirmar ausência de eventos observáveis de candidatos abandonados.

Repetir partidas com mesmos inputs em processos novos; validar retry, checkpoint, branch e replay semântico. Recarregar conteúdo sem ativá-lo não altera a política da run; mudança de revisão autorizada passa pelo comando canônico e valida compatibilidade com a sequência em curso.

Revisar a versão determinística, atualmente 33. Se a representação/semântica nova exigir outra versão, alterá-la em etapa explícita, documentar a política de saves e não reescrever journals antigos. Preservar gameplay existente não significa prometer compatibilidade de hash entre versões diferentes.

Aceite: regressões de PerRound e cenários sem Actor passam; jornadas equivalentes têm hashes iguais na mesma versão; branches não alteram origem; REST, replay e retomada concordam sobre direção, próximo ator, cartas e resultado.

Commit: `test(determinism): certify circular table transactions branches and replay`.

### Etapa 12 — Documentação e entrega

Atualizar guias de combate, targeting, zonas, efeitos, condições, resultados, conteúdo e API. Publicar dois exemplos mínimos: combate atual por rodada e mesa circular; acrescentar matriz de compatibilidade e scripts/testes reproduzíveis.

Registrar os commits por etapa, comandos executados, contagem de testes e limitações verificadas. Não declarar suporte a jogos multijogador remotos, mudanças dinâmicas de assento ou combinações ATB não implementadas.

Commit: `docs(flow): document circular table authoring and certification`.

## 6. Matriz mínima de testes

| Grupo | Casos obrigatórios |
|---|---|
| Percurso | 2/4/6 atores; ambas direções; inversão após ator já ter agido; dois reverses; saltos cumulativos; wrap; zero/um elegível |
| Rodadas | Disabled não dispara round hooks; ActivationCount conta ativações reais; PerRound mantém lifecycle atual |
| Alvos | Next versus NextScheduled; origem diferente de ativo; preview/execução; relação; lock e retarget; alvo explícito incorreto |
| Iteração | BatchPerActor e RoundRobin; ordem estável; sem mutar ator ativo; limite de iteração; rollback na última mão |
| Pool | 108 IDs únicos; mãos privadas; 28 distribuídas; uma mesa; 79 restantes; recycle não inclui mesa |
| Compra | Uma carta; compra parcial; monte vazio com descarte; ambos vazios; fallback sem progresso; dívida maior que disponível |
| Comandos | Compra antes de passar; compra já realizada; carta jogável; penalidade; escolha pendente; request forjado; bot; saída automática |
| Queries | Contagem por ator; agregado sem duplicação; endereço ausente; snapshot candidato correto; cache sem valor obsoleto; precisão inteira |
| Conteúdo | Skip atinge um; Reverse não depende de completed; +2 acumula e segue direção; +4 não mistura correntes; UNO usa alvo elegível |
| Resultado | Jogador/bot vencedor; vitória após descarte; fim de escolhas; empate; partida não encerra durante setup; prêmio correto |
| Integridade | Estado original intacto; RNG somente configurado; falha de fluxo/persistência sem publicação parcial; retry idempotente |
| Histórico | Reinício, branch, checkpoint e replay preservam a sequência; origem imutável; revisão fixa; política de versão explícita |
| Regressão | Duelo atual, deckbuilder PerRound, Global/RunOwner, sem zonas Actor, prioridade/stack e animações existentes |
| Migração de conteúdo | Ausência recebe PerRound; política explícita preservada; múltiplas definições por arquivo; dry-run; segunda execução sem diff; publicação sem política rejeitada; saves e bundles históricos intocados |

## 7. Critério final de conclusão

Uma partida REST com quatro participantes deve ser reproduzível integralmente a partir de JSON e comandos: distribuir, jogar, inverter, saltar, comprar/reciclar, resolver penalidades e determinar vencedor sem intervenção de regras no app.

O mesmo servidor deve continuar executando o fluxo de combate atual por rodada. A escolha entre essas experiências deve resultar da política do modo, e não de outra implementação de engine, um endpoint especial de UNO ou um frontend parcialmente autoritário.
