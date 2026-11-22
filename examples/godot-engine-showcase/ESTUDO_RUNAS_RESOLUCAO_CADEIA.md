# Runas de sobrescrita e resolução em cadeia

Data: 22 de novembro de 2026. Estudo de alternativas para o core de builds de HeroScript, complementar ao [diagnóstico anterior](PESQUISA_PLANO_CORE_BUILDS.md). Propostas para prototipagem, não regras aprovadas.

## 1. Tese

Uma runa não deveria ser simplesmente um buff permanente inscrito na carta. Ela pode ser uma **instrução que substitui uma característica daquela ação**: o que ela produz, como se executa, como escolhe alvos, como paga ou o que faz com o resultado.

Se as runas compartilharem uma linguagem de entradas, saídas e fatos de gameplay, o jogador pode descobrir combinações entre cartas, equipamentos, identidade, estados e modificações sem que cada combinação tenha sido programada individualmente. A carta continua sendo a unidade de decisão; a runa a transforma; o resultado de uma ação pode alimentar a próxima.

O diferencial pretendido é que duas instâncias da mesma carta possam terminar a run desempenhando funções completamente diferentes e que mudar um único encaixe reorganize várias interações do baralho.

## 2. Referências de design

| Referência | Modelo relevante | Aplicação proposta |
| --- | --- | --- |
| [Spellmasons, descrição dos desenvolvedores](https://store.steampowered.com/app/1618380/Spellmasons/) | Feitiços alteram feitiços subsequentes, inclusive o contexto e alvo; runas especializam a magia. | Uma ação pode produzir um contexto útil para a próxima, não apenas mais dano. |
| [Mages of Mystralia, Borealys](https://store.steampowered.com/app/529660/Mages_of_Mystralia/) | Construção de feitiços próprios como parte do jogo. | Poucos elementos de composição reutilizáveis valem mais do que dezenas de variantes pré-fabricadas. |
| [Noita, Nolla Games](https://store.steampowered.com/app/881100/Noita/) | O jogador cria seus próprios feitiços dentro de um mundo que possui regras sistêmicas. | Separar instruções de execução das interações de estado e deixar o sistema produzir combinações surpreendentes. |
| [Transistor, Supergiant](https://store.steampowered.com/app/237930/Transistor/) | Configuração de habilidades através de combinações de Functions. | Tornar a configuração da própria habilidade parte da identidade da build. |
| [Magic: The Gathering — The Stack and Its Tricks](https://magic.wizards.com/en/news/feature/stack-and-its-tricks-2017-11-30) | Distingue ações, custos, gatilhos e respostas em uma pilha ordenada. | Estabelecer quando um efeito é executado e quais resultados podem disparar outros efeitos. Não exige importar o sistema de prioridade de Magic inteiro. |
| [Wildfrost — atualizações dos desenvolvedores](https://chucklefish.org/blog/wildfrost-mod-support-storms-in/) | Interrupções, múltiplos ataques, efeitos por abate e charms exigem semântica cuidadosa. | Testar interações por escopo e ordem. Uma cadeia é também um problema de legibilidade e terminação. |

Spellmasons é a referência mais próxima para a ideia de resolução composta. Não proponho copiar seu sistema de feitiços, mas aprender com a distinção entre produzir uma consequência e mudar o contexto usado por uma ação seguinte.

## 3. Alternativas comparadas

| Modelo | Decisão do jogador | Potencial combinatório | Custo de compreensão / implementação | Escolha |
| --- | --- | --- | --- | --- |
| A — Sobrescrita tipada | Substituir uma propriedade de carta (afinidade, forma, consequência, custo) | Alto | Baixo a médio; a engine já compõe cartas | Base |
| B — Cadeia reativa | Conectar consequências de ações a gatilhos de outras peças | Muito alto | Médio; precisa de ordem, escopos e apresentação | Diferencial |
| C — Encadeamento explícito | Ordenar duas ou três cartas e resolver sua combinação | Alto; dá controle sintático sobre a ordem | Alto; pagamentos, alvo, compra e interrupções tornam-se decisões adicionais | Experimento posterior |
| D — Circuito de runas | Conectar portas por adjacência ou posição em uma grade com caminhos alternativos | Muito alto | Muito alto; pode virar um jogo de programação separado | Não começar aqui |

A e B são complementares. A sobrescrita cria novas propriedades da ação; B faz essas propriedades encontrarem outras peças da build. C pode ser um modo de combate futuro, caso a resolução ordenada de cartas individuais se revele insuficiente. D é valioso como referência conceitual, mas construir uma nova grade já seria mudar o foco do projeto.

## 4. Proposta: gramática de runas

### 4.1 Estrutura da carta

Separar características que já existem na definição efetiva:

- **Núcleo:** ação principal, sua base numérica e identidade da instância.
- **Afinidade:** qual consequência temática é produzida pela ação.
- **Forma:** como o núcleo é entregue (um impacto, múltiplos impactos, área, transferência).
- **Vínculo:** como um resultado ou fato de gameplay modifica a consequência ou habilita uma continuação.
- **Custo e destino:** podem ser transformados por determinados comportamentos, sem obrigar a criar novos slots.

Usar inicialmente os slots existentes em Alisyum: um de afinidade e dois espaços de comportamento. Forma e vínculo podem ser papéis de apresentação dentro da categoria Behavior, sem introduzir uma nova categoria da engine. Se duas runas pretendem sobrescrever a mesma propriedade de forma incompatível, a composição deve explicar o conflito. Se forem complementares, os dois espaços permitem a interação.

Sobrescrita é diferente de adição. Trocar Fogo por Gelo deve retirar a consequência de fogo que está sendo substituída, não criar gratuitamente dois elementos. Trocar um golpe por três impactos deve redistribuir a mesma base por padrão, sem multiplicar automaticamente o orçamento. Uma runa explícita pode alterar o orçamento.

### 4.2 Exemplo de transformação de uma mesma carta

Golpe de Aço: 6 de dano físico por 1 Energia, valores ilustrativos antes de cálculo e defesa. Considere uma variante cujo componente principal possui base distribuível.

| Configuração | Resultado pretendido |
| --- | --- |
| Original | 1 × 6 |
| Afinidade Brasa | 1 × 6 com consequência residual de fogo, em vez de consequência física anterior, se houver |
| Brasa + Fragmentação | 3 × 2; o residual total também é dividido conforme a regra da afinidade |
| Brasa + Fragmentação + Ressonância | 3 × 2; cada evento elegível pode alimentar outra peça que observe aplicação de Burn, dentro de escopo declarado |
| Gelo + Transbordo | Golpe com consequência de Geada, transportando excedente ao derrotar o alvo |
| Gelo + Lastro | Núcleo passa a aproveitar a defesa como base adicional declarada, em vez de executar a forma anterior do modificador |

Não é obrigatório multiplicar a quantidade de aplicações apenas porque há mais acertos; a definição precisa distinguir número de impactos, total de potência e eventos de aplicação. Isso cria uma razão legítima para escolher Fragmentação mesmo sem ganhar dano base: três oportunidades de crítico, ou três fatos por acerto.

### 4.3 Catálogo experimental de instruções

| Runa | Característica transformada | Contrapartida e conexões |
| --- | --- | --- |
| Fragmentação | Um impacto passa a vários impactos que dividem a base | Mais oportunidades por acerto, menos dano concentrado por impacto |
| Concentração | Forma múltipla passa a um único impacto, caso exista distribuição compartilhada | Menos oportunidades por acerto; finalização e overflow |
| Transbordo | Comportamento de excedente: desperdiçar → transportar após abate | Recompensa preparar o alvo-ponte; existe risco de não causar abate |
| Combustão | Afinidade residual de fogo substitui a anterior | Burn e consumo |
| Criogenia | Afinidade residual de gelo substitui a anterior | Geada, defesa e condensação |
| Ruptura | Consequência residual imediata é modificada para consumo de stacks elegíveis | Requer preparação; troca aplicação ou outra consequência pelo payoff |
| Ressonância | Substitui o destino de uma consequência ou configura vínculo a um fato | Liga aplicação, consumo, compra, dano ou defesa a outro efeito |
| Retorno | Disposição normal passa a retorno condicional ao topo/mão | Consistência versus espaço usado; condição explícita |
| Catalisador | Custo/efeito de economia passa a modelo condicional | Sacrifício ou consumo de estado, teto por ação |
| Guarda Viva | Forma defensiva passa a defesa com resposta ao próximo fato elegível | Timing da proteção e escolha de alvo quando apropriado |

Primeira lista não implica que todas as regras acima sejam diretamente autoráveis hoje. Fragmentação, afinidade, continuação e disposição já possuem contratos relevantes. Retorno condicional, troca integral de afinidade sem perder contribuições de base e escopos duráveis da Guarda Viva precisam de protótipos específicos. Evitar nomes ambíguos e testar o que cada runa retira.

## 5. Cadeia de resolução

### 5.1 Três mecanismos diferentes

1. **Transformação:** recompõe a carta antes da ação; por exemplo, troca a afinidade ou altera Repeat da ação.
2. **Continuação do próprio efeito:** o golpe produz resultado e uma regra determina o próximo impacto. Ex.: excedente de um abate salta para outro inimigo.
3. **Resposta de gameplay:** uma peça ativa observa um fato confirmado, como aplicação de status ou ganho de Block, e executa um efeito. Uma resposta pode produzir outro fato e continuar a cadeia se autorizada.

Não confundir custo com efeito produzido. Não transformar uma prévia em um evento. Uma reação decorrente de uma reação deve conservar sua origem e as identidades causais; somente fatos autorizados alimentam respostas.

### 5.2 Exemplo de encadeamento dentro de uma ação

Hipótese de build de fogo. Os valores são apenas ilustrativos:

```text
Golpe de Aço modificado (1 Energia)
 └ Fragmentação: 3 impactos com orçamento total 6
    ├ Impacto 1 → 2 dano → Brasa aplica 1 Burn
    ├ Impacto 2 → 2 dano → Brasa aplica 1 Burn
    └ Impacto 3 → 2 dano → Brasa aplica 1 Burn
       └ Identidade: aplicação elegível de Burn acrescenta Calor
          └ Nó de Ressonância: ao alcançar limiar, consome o contador
             └ Detonação: consome os stacks elegíveis, gera dano
                └ Ash Phoenix: compra 1 carta ao consumo
                   └ Encerrar a cadeia; o jogador volta a escolher sua ação
```

A ordem exibida é uma hipótese de design a verificar na engine. Pode ser melhor avaliar o limiar depois da sequência, em vez de durante o terceiro impacto. Isso altera o timing e a quantidade de Burn disponível, portanto precisa ser uma escolha semântica explícita.

### 5.3 Cadeia entre cartas sem jogar um segundo card game

Uma runa de vínculo pode deixar outra carta **ressonante enquanto estiver na mão**. Ela não é jogada automaticamente; pode reagir com um efeito delimitado. Exemplo:

- Carta A modificada aplica Geada.
- Carta B na mão tem uma runa de ressonância que, ao ocorrer aplicação válida de Geada, concede 2 Block (uma vez por ação raiz).
- O equipamento do Renegado reage a ganho de Block e produz um novo efeito elegível, quando propagação estiver autorizada.
- O jogador decide usar sua carta de conversão Block → dano ainda neste turno.

Isso faz a composição da mão importar. Não é necessário gerar uma fase de montagem da fila, nem permitir que cartas ocultas no deck inteiro sejam observadores emergentes. Outras zonas podem ser oferecidas com regras específicas; um efeito no descarte é uma build distinta de uma peça que precisa estar na mão.

### 5.4 Conexões não lineares

Uma regra aberta pode reagir a eventos de aplicação de residual, consumo de stacks, dano efetivamente aplicado, ganho de Block ou abate, quando os tópicos e payloads existirem. Assim, a mesma runa encontra múltiplos produtores e múltiplos payoffs.

| Produtor | Fato | Conversor | Payoff(s) |
| --- | --- | --- | --- |
| Ataque fragmentado | Impacto válido | Afinidade por impacto | Crítico, residual, dreno |
| Aplicador residual | Status aplicado | Condensação | Dano antecipado, compra, cura |
| Carta de defesa | Block efetivo obtido | Vínculo reativo ou conversor de defesa | Ataque escalado, preparação |
| Finalizador | Abate causal | Transbordo | Novo alvo, efeito ao abater |
| Carta ou identidade de economia | Recurso efetivamente ganho ou carta comprada | Vínculo com condição | Mais opções, retorno de peça |

O mesmo feito de gameplay não deve ser contado duas vezes por um efeito que observe dois tópicos equivalentes. Conversores consomem a entrada apenas quando previsto; reações não consomem estado implicitamente.

## 6. Ordem, limites e liberdade

Não recomendo pilha LIFO interativa ao estilo de Magic para toda carta. O combate PvE perderia ritmo a cada gatilho. Recomendo resolver internamente uma cadeia causal determinística, com etapas relevantes e efeitos visíveis.

- Custos, legalidade e alvos são decididos pela engine antes de confirmar uma ação.
- Efeitos de preparação podem ter timing anterior à sequência ou impacto.
- Efeitos por impacto, por proc e depois da sequência utilizam os escopos do executor.
- Uma resposta observa a ocorrência e seu snapshot declarado. O dispatcher existente controla a execução e profundidade; não reordenar fatos para a animação.
- Se duas respostas competirem, usar a ordem declarada e desempate canônico, com resultado explicado no preview/trace.
- A política de perda de alvo é explícita: pular, redirecionar ou terminar conforme a regra. O excedente usa o cálculo canônico, sem aplicar o scaling do atacante novamente.
- Gatilhos por impacto são permitidos quando fazem sentido. Economia de energia e compra com potencial recursivo começam em RootAction, ou outra quota testada. Quotas por turno/combat exigem confirmar suporte durável; não presumir.
- Distinguir um retorno finito muito grande, que é desejável, de acionamento ilimitado sem progresso. Respeitar budgets e atomicidade existentes.

O jogador pode descobrir combinações extremamente fortes sem receber permissão de enviar comandos arbitrários durante uma resolução em andamento. Limites de produto devem ser expressos pelas próprias peças; limites técnicos protegem a execução e não substituem balanceamento.

## 7. Viabilidade real no HeroScript

A engine é especialmente adequada à alternativa A+B. Já existem:

| Contrato | Aplicação |
| --- | --- |
| `CardUpgradePatchDefinition` — Set, Add, Multiply, Replace/Remove/Add de componente, tags, disposition, continuation | Sobrescrever características com identidade estável |
| `CardTransformationLedger` | Histórico por instância, remoção/substituição e reconstrução da carta |
| `CardTransformationComposition` e gramática de composição | Requisitos por capability/tag, seleção de efeito raiz, bundles parametrizados |
| Scopes BeforeSequence, BeforeImpact, AfterImpact, OncePerProc, AfterSequence | Encaixe de efeitos em pontos semânticos previsíveis |
| `EffectSequenceBudget` | Repartir magnitude de origem em vários impactos sem multiplicação acidental |
| `EffectContinuation` e receitas de condensação | Transferência causal e consumo agregado |
| `GameplayResponseDefinition` | Gatilhos com disponibilidade (mão/zonas, carta selecionada ou resolvendo), ordem, quota, RecordOnly ou AllowResponses |
| Traces de composição, execução e eventos | Preview, explicação da conexão, replay e validação |

Referências internas: [transformações permanentes](../../docs/systems/cards/permanent-transformations.md), [gramática](../../docs/systems/cards/composition-grammar.md), [continuação causal](../../docs/systems/effects/causal-continuation.md) e [respostas de gameplay](../../docs/content/gameplay-responses.md).

Há duas restrições importantes:

1. Efeitos gerados por regras de composição não alimentam recursivamente a seleção de outras regras de composição. Isso é intencional. A cadeia dinâmica deve ocorrer pela execução de efeitos/respostas, e não por expansão infinita da definição durante compilação.
2. Patches de componentes completos podem sobrescrever valores base anteriores e o histórico tem ordem definida. Precisamos provar que cada sobrescrita conserva o que pretende conservar, e que trocar a ordem não cria diferenças acidentais. Não é necessário criar uma segunda autoridade de cartas na Godot.

## 8. Protótipo proposto

Começar sem novos recursos globais, sem nova grid de runas, sem multiplayer e sem nova classe. Usar uma fatia de Alisyum com Piromante e uma fixture de Renegado para testar a transversalidade.

1. Três cartas-base: golpe, defesa e aplicador de residual.
2. Duas afinidades intercambiáveis (Brasa e Geada) e três comportamentos (Fragmentação, Ruptura, retorno/continuação conforme suporte).
3. Uma inscrição de identidade que reage a impactos ou status, uma runa-vínculo que reage enquanto uma carta está na mão, uma peça que recompensa o consumo.
4. Primeira runa no início, segunda no primeiro prêmio, terceira antes de um encontro de dois alvos. Mostrar antes/depois no inventário e na carta.
5. Matriz carta × runa × combinação de duas runas: verificar legalidade, base preservada, custos, alvo, evento, frequência, disponibilidade e replay. Incluir ordem invertida, alvo morto, mão cheia, mesmo evento repetido e duas instâncias da mesma carta.
6. Comparar no sandbox pelo HeroScript MCP base, runa A, runa B e A+B. Verificar resultado efetivo, procs, energia e quantidade de ações; usar o Godot MCP para observar a apresentação do trace.
7. Playtest humano curto: qual mod alterou de verdade a decisão? Foi possível prever a cadeia? O jogador quer tentar outra configuração? Se a resposta não for positiva, rever a gramática antes de expandir.

O critério inicial de sucesso é uma única carta básica que assume pelo menos três comportamentos distintos, uma cadeia de três peças com payoff claramente perceptível e pelo menos duas alternativas de build sem receitas rígidas. Isso merece mais prioridade do que adicionar dezenas de cartas.
