# Plano de implementação do core — cartas transformáveis e procs acumulados

Data: 2026-10-06.

Status: implementação técnica das etapas 0–14 entregue. Resultados por etapa:
[acompanhamento](CORE_GAMEPLAY_GAPS_IMPLEMENTATION_PROGRESS.md) e
[relatório de validação](CORE_GAMEPLAY_GAPS_VALIDATION_REPORT.md). Playtest humano
de compreensão e balanceamento continua separado e não foi declarado aprovado.
O texto abaixo conserva o diagnóstico inicial e a ordem das entregas; os schemas
finais estão nos contratos e no acompanhamento, não nos nomes propostos.

Referência de design: [CORE_GAME_DESIGN.md](../game-design/CORE_GAME_DESIGN.md).

## 1. Objetivo e autoridade

Permitir que o core seja definido em conteúdo e regras JSON, executado integralmente pelo HeroScript e apresentado pela Godot. Corrigir as lacunas de transformação de cartas, afinidades, múltiplos acertos, condensação, salto por abate, progressão de atributos e apresentação dessas interações.

A decisão mais recente do autor amplia o GDD: **condensação é consumir os stacks elegíveis acumulados e utilizá-los de uma vez em um único proc. Não é uma operação específica de dano, nem exige um efeito periódico.** Pode produzir cura, alteração de outro recurso, aplicação de status/modifier, compra de cartas ou outro efeito executável.

Este contrato substitui a interpretação exclusivamente ofensiva anterior da seção 8.2 do GDD, sincronizada na etapa 0. A extração de conceitos do legado permanece intocada.

As demais escolhas provisórias do GDD — divisão da magnitude no multi-hit, excedente no salto, snapshot do residual e transformações entre encontros — serão implementadas como configurações do setting inicial, não como regras universais da engine. Não considerar essas escolhas irrevogavelmente aprovadas.

### Critério geral de conclusão

Uma run curta deve permitir transformar a mesma carta, acumular efeitos, condensá-los em um proc, executar múltiplos acertos e transportar excedente após um abate. Preview, execução, journal, persistência, replay e branches devem concordar. A Godot não pode calcular as regras.

## 2. Diagnóstico que orienta o plano

| Lacuna | Situação atual | Direção |
| --- | --- | --- |
| Alvo derrotado durante uma sequência | O próximo efeito/repeat rejeita o alvo e falha a transação. | Distinguir entrada inválida de invalidação causada pela própria ação. |
| Resultados intermediários | Há registros de aplicação, mas não um contexto tipado de abate, excedente e consumo. | Fatos imutáveis utilizáveis pela continuação da execução. |
| Condensação | Não há consumo agregado e proc configurável. | Seleção, captura, consumo e um único proc atômico. |
| Residual originado por uma carta | Status possui origem, stacks e duração, mas não parâmetros capturados da carta. | Payload tipado e política explícita de avaliação. |
| Transformação qualitativa | Upgrades alteram números, targeting e disposition; não tags/composição. | Operações estruturais no mesmo processo canônico de composição. |
| Afinidades | Tags filtram componentes/influências; efeitos fixos podem ser escritos em JSON. | Bundles/regras reutilizáveis de composição e transformação. |
| Modifiers comportamentais | Script modifiers atuais são principalmente influências numéricas. | Modificações permanentes da carta distintas de buffs contextuais. |
| Multi-hit | Repeat repete o efeito, inclusive seu scaling. | Orçamento de magnitude, distribuição e política de continuação. |
| Salto por abate | Não existe transporte de excedente condicionado ao abate. | Continuação baseada em fatos e magnitude com domínio explícito. |
| Progressão de atributos | Há componentes de atributos e modificadores persistentes; não uma progressão direta canônica. | Estado persistente da entidade e operações autorizadas por conteúdo. |
| Legibilidade e integração | Inspeção e apresentação já existem, sem as novas semânticas. | Estender as projeções e a fila visual, sem regras na interface. |

Baseline diagnóstico: 201 testes direcionados do Core e 16 da API passaram. Isso não representa a suíte completa. Um diagnóstico isolado confirmou falha em repeat com primeiro impacto letal e dano letal seguido de status. A implementação deve transformar esses casos em regressões permanentes e repetir o baseline completo.

## 3. Invariantes de arquitetura

1. A engine conhece operações, parâmetros, seletores e políticas; não conhece o significado de fogo, veneno, mana, vida ou de uma carta específica.
2. Definições publicadas, instâncias, planos e resultados são imutáveis e possuem revisão/hash canônicos.
3. Carta, habilidade, status, relíquia, modo e encontro usam os mesmos contratos de execução. Proveniência explica a origem, não escolhe outra implementação.
4. A bucket pipeline recebe um pedido numérico e retorna resultado/trace. Não escolhe alvos, não remove stacks, não repete ataques e não decide abates.
5. Todo parâmetro numérico de gameplay calculado utiliza o caminho canônico apropriado, inclusive contagens quando forem derivadas de fórmulas. Conversão para inteiro, rounding, limites e sinal são explícitos.
6. Custos, RNG, consumo de stacks, alterações de carta, recursos, atributos e zonas fazem parte da mesma transação quando pertencem à mesma ação.
7. Efeitos e frames externos só são publicados depois do commit. Falha não deixa consumo, custo ou cursor aleatório parcial.
8. Preview usa o mesmo executor sobre um snapshot, sem persistir ou avançar o RNG real.
9. O estado e as regras de uma run pertencem ao seu setting/revisão. Não recorrer ao setting default para preencher conteúdo ausente.
10. Toda mutação do jogador passa pelo gateway de comandos REST existente. Não criar endpoints paralelos que escrevam diretamente no estado.
11. Acúmulo, duração e potencial futuro são conceitos separados. Condensação não inventa uma relação entre eles.
12. Não preservar caminhos legados duplicados durante a conclusão da refatoração. Atualizar consumidores e schemas de forma coordenada; dados incompatíveis são rejeitados explicitamente ou migrados por ferramenta versionada.

## 4. Responsabilidades e fluxo canônico

| Responsável | Deve fazer | Não deve fazer |
| --- | --- | --- |
| Compilador de conteúdo/cartas | Expandir composição, validar referências, slots e operações, produzir definição efetiva. | Ler estado vivo para editar uma definição publicada. |
| Planejador/executor de efeitos | Interpretar sequência, condições, repeats, condensação e continuação usando snapshots/fatos. | Reimplementar matemática de dano/cura. |
| Resolução de alvos | Validar seleções e aplicar políticas de perda/retarget determinísticas. | Escolher por posição visual ou ordem de Dictionary não declarada. |
| Resolução de parâmetros | Construir pedidos de cálculo e converter resultados nos campos tipados do efeito. | Aplicar mutações ou decidir o fluxo da ação. |
| Bucket pipeline | Calcular quantidades e produzir trace de etapas numéricas configuradas. | Consumir status, ler arquivos ou publicar eventos. |
| Reducers e transições de stacks | Aplicar comandos resolvidos e gerar fatos imutáveis. | Descobrir fórmulas ou selecionar a próxima carta/alvo. |
| Gateway/commit | Validar versão, persistir uma transação, journal, recibo e frames. | Executar uma segunda interpretação das regras. |
| Godot | Input, navegação, apresentação, áudio e reprodução da fila recebida. | Calcular magnitude, elegibilidade, consumo, sucesso de abate ou legalidade. |

Fluxo proposto:

```text
conteúdo revisionado + identidade da carta + transformações persistentes
  -> composição efetiva e validação
  -> plano sequencial com contexto imutável da ação
  -> seleção/captura de stacks e resolução de parâmetros
  -> pedidos numéricos à pipeline
  -> aplicação por reducers canônicos
  -> fatos de aplicação/consumo/abate/continuação
  -> commit único
  -> recibo, journal, timeline e fila visual
```

Evoluir o `EffectTriggerExecutor` para delegar responsabilidades menores, sem criar um executor exclusivo de cartas. Se for necessário um grafo compilado, ele será a única representação executável de sequência/repeat/continuação; os campos atuais devem compilar para essa representação, não continuar como um segundo interpretador.

## 5. Contrato genérico de condensação

### 5.1. O que é um proc

Um proc é uma unidade identificável de ativação. Possui um `procId`, origem, alvos, inputs agregados e resultado. Não equivale a uma unidade de dano.

**Condensar oito stacks significa uma ativação usando oito stacks, não oito execuções do efeito.** Um proc pode ter vários componentes ou alvos definidos pela receita, mas esses são filhos da mesma ativação. Para o recorte inicial, cada receita demonstra um efeito principal e um alvo, deixando a semântica observável.

Uma condensação executada como componente de uma carta multi-hit tem escopo explícito. A configuração inicial será `OncePerAction`: a mesma seleção não é consumida novamente por impacto. Outras fontes podem solicitar novas condensações em ações posteriores; isso não autoriza reutilizar stacks já consumidos.

### 5.2. Fontes elegíveis

Criar uma referência tipada para uma instância acumulável: tipo de armazenamento, dono, ID da instância, definição, revisão, contagem e payload.

Implementar adaptadores para status e modifiers que já possuem stacks. Estes adaptadores leem e alteram seus snapshots existentes; **não criar um terceiro banco de stacks autoritativo**. Outros acumuladores poderão integrar o mesmo contrato quando possuírem estado e operações reais.

Definições declaram capacidade de consumo e receitas/perfis autorizados. Seletores configuram IDs/tags, dono, origem e momento de captura. Ausência de declaração não torna todo buff automaticamente consumível. Dano periódico não concede elegibilidade implícita.

Consumir todos significa **todos os stacks da seleção elegível**, não todos os status do personagem. Se o setting quiser incluir todos os acumuladores, deve declarar isso.

### 5.3. Receita de conversão

A receita deve declarar:

- Fontes, filtros, ordenação e política para seleção vazia.
- Snapshot de referência: início da ação ou estado atual naquele ponto da sequência.
- Quais quantidades capturar: contagem, parâmetros armazenados e/ou contribuição calculada.
- Agregação e unidades compatíveis; conversões explícitas para fontes heterogêneas.
- Efeito/composição que será ativado uma vez e destino dos inputs agregados.
- Momento de avaliação das influências: antes ou depois do consumo, conforme contrato do perfil.
- Política de consumo, chance, alvo indisponível, instâncias esgotadas e hooks de remoção.
- Limites de seleção, expansão e valores.

Não somar arbitrariamente “3 de cura” e “4 de veneno”. Fontes distintas podem participar de um proc quando uma receita define a conversão. Falta de regra produz diagnóstico, não fallback.

Modo inicial: agregar contagens e parâmetros por receita; consumir integralmente os stacks selecionados; remover/desativar instâncias esgotadas; executar um proc. Consumo não é automaticamente dispel, expiração ou abate, pois esses motivos podem possuir consequências diferentes.

### 5.4. Sequência transacional

1. Validar o comando e resolver o perfil revisionado.
2. Capturar os IDs e contribuições elegíveis no snapshot declarado.
3. Avaliar condições/chance no escopo do proc, sem sorteio por stack.
4. Planejar o consumo completo dessas instâncias; detectar ausência, revisão incompatível e conflitos.
5. Produzir o snapshot candidato com os stacks consumidos.
6. Resolver uma ativação usando os inputs capturados e as influências do momento declarado.
7. Aplicar os efeitos emitidos pelos reducers comuns.
8. Registrar seleção, contagem antes/depois, payload agregado, cálculo e resultado no mesmo proc.
9. Commitar a ação inteira ou descartar tudo.

Se a captura usa início da ação, alterações posteriores não ampliam a seleção. Caso os stacks capturados tenham sido alterados/consumidos por uma etapa anterior, não consumir silenciosamente outra quantidade: usar a política de conflito declarada. No setting inicial, validar e consumir a condensação antes de etapas que alterem essas mesmas instâncias.

Se uma chance falhar, seleção vazia ocorrer ou o alvo ficar indisponível, aplicar política explícita. Para o setting inicial: chance falha não consome; seleção vazia vira skip da condensação sem cancelar o efeito principal da carta; erro estrutural falha a transação. Um proc resolvido que aplica zero por limite do recurso pode consumir os stacks; políticas alternativas devem ser declaradas, não inferidas.

Stacks consumidos não executam ticks posteriores nem influências passivas. Novos stacks criados pelo próprio proc têm novos registros e não são incluídos retroativamente na seleção.

### 5.5. Exemplos de aceitação

| Acúmulo | Receita | Resultado esperado |
| --- | --- | --- |
| 3 cargas de restauração, base 4 por carga | Cura agregada | Uma ativação de cura com input 12, passando pela pipeline de cura. |
| 5 cargas de reserva, base 2 por carga | Adicionar ao recurso configurado | Uma ativação com input 10; o recurso pode ser mana, energia ou outro. |
| 4 cargas de compra | Fluxo de zona com quantidade agregada | Uma ativação do fluxo para comprar 4 cartas, não 4 procs. |
| Instâncias compatíveis com 2 e 3 stacks | Aplicar outro status/modifier | Uma aplicação com contagem agregada 5, sujeita à política de stacking do destino. |
| 2 stacks de residual, intensidade 3 | Conversão apenas por stacks | Input 6; duração não é multiplicada implicitamente. |
| Mesmo residual com duração finita 4 | Receita explícita de potencial restante | Input 24 somente se a regra declarar essa conversão e sua unidade. |

Esses exemplos descrevem inputs; buffs, limites, custos, rounding e resistências podem alterar o resultado final. A versão de potencial restante é opcional: não é a definição universal de condensação. Para residual dinâmico, futuros ticks dependem de estados desconhecidos; rejeitar previsão exata ou apresentar estimativa sob hipótese explícita. Status permanente não possui “potencial infinito” computável.

## 6. Contexto, magnitudes e efeitos acumulados

### 6.1. Fatos de execução

Introduzir registros tipados para ação, proc, impacto e continuação. Guardar resultado solicitado, mudanças efetivas, quantidades não aplicadas por limites, consumos, transição de derrota e causalidade pai/filho.

Um resultado anterior pode ser referenciado por ID estável do componente/proc. Não usar uma variável global ambígua `lastDamage`: sequências com vários alvos, cura e impactos precisam de referências exatas. As fórmulas recebem somente projeções numéricas desses fatos; seleção e controle continuam fora da matemática.

Derrota vem das políticas do recurso e dos fatos de mudança de estado. Um alvo já derrotado não conta como um novo abate da carta atual.

### 6.2. Domínio da magnitude e avaliação única

O resultado numérico precisa informar o que já foi avaliado: base, etapas de origem, etapas do alvo e settlements. Usar etapas/IDs declarados pelo perfil, não uma ordem universal hardcoded para todo jogo.

Uma quantidade capturada ou transportada carrega valor, unidade, revisão, referência ao cálculo e etapas já incorporadas. O próximo pedido seleciona explicitamente o perfil de continuação; não executa novamente todos os bônus sobre um número já amplificado.

Etapas do alvo são identificadas também pelo contexto do alvo: o fato de a defesa de A ter sido calculada não impede calcular a defesa de B. Proveniência de carta/status não escolhe o algoritmo.

### 6.3. Payload de status e reapplication

Guardar parâmetros tipados capturados na aplicação, origem da carta/efeito, hash da composição efetiva e revisão. Não armazenar todo o grafo da run nem esconder campos executáveis em `CustomData`.

Oferecer políticas por parâmetro:

- `Dynamic`: avaliar no momento da ativação usando referências e contexto declarados.
- `Snapshot`: registrar na aplicação a quantidade e as etapas selecionadas já avaliadas.

O setting inicial poderá congelar a contribuição da origem e recalcular condições do alvo na ativação. Congelar tudo ou recalcular tudo deve ser uma escolha explícita. Uma snapshot não pode receber novamente os mesmos fatores.

Reaplicar stacks de intensidades/revisões diferentes exige política: preservar contribuições em lotes, substituir ou combinar por receita explícita. Não sobrescrever a intensidade de todos os stacks com a última aplicação. Lotes existem dentro da instância autoritativa e participam do hash, replay, consumo e expiração.

## 7. Sequência detalhada de implementação

Cada etapa tem entregas, regressões e critério de saída. Fazer um commit isolado por etapa durante a futura implementação. Não incluir alterações locais não relacionadas. Se uma etapa precisar de mais de um commit, manter a ordem e registrar os commits no acompanhamento.

### Etapa 0 — Baseline e contratos verificáveis

**Entregas**

- Registrar Git, arquivos pendentes e resultados da suíte completa Core/API e testes headless Godot disponíveis.
- Sincronizar somente a definição de condensação do GDD com a decisão explícita do autor; preservar a referência legada e marcar as demais propostas como propostas.
- Consolidar schemas propostos, políticas do setting inicial e matriz de compatibilidade entre novas capacidades e modos.
- Definir limites de execução, versionamento de snapshots e erro para schemas antigos incompatíveis.
- Preservar saves existentes. Não apagar ou sobrescrever dados antigos para acomodar o novo schema; documentar como abri-los com a versão compatível ou migrá-los por ferramenta explícita.
- Registrar regressões de alvo letal que serão corrigidas na etapa 1.

**Saída:** contratos claros, baseline reproduzível e nenhum sucesso histórico apresentado como validação atual.

Commit sugerido: `docs: define generic stack condensation and core delivery contracts`.

### Etapa 1 — Corrigir invalidação de alvo durante a ação

**Áreas:** `EffectTriggerExecutor`, targeting/legalidade, `CardPlayExecutor` e testes de transação.

**Entregas**

- Separar seleção inicialmente inválida de alvo inicialmente válido que foi derrotado por uma etapa anterior.
- Adicionar políticas explícitas de continuação: falhar, pular efeito, encerrar repetição e retarget por seletor determinístico.
- Manter a validação estrita de IDs inexistentes e alvos não autorizados na entrada.
- Registrar skips/interrupções no trace; não converter exceções em sucesso silencioso.
- Configurar dano seguido de status para pular o status no alvo derrotado; multi-hit inicial interrompe quando não há continuação permitida.
- Tratar conjuntos automáticos vazios após abates como término esperado quando a política permitir.
- Não finalizar prematuramente o planejamento da ação antes de continuations autorizadas e limpeza transacional.

**Testes:** primeiro impacto letal, último impacto letal, dano seguido de status, último inimigo morto, alvo inválido na entrada, condições falsas, falha real posterior e rollback de custo/RNG.

**Saída:** cartas já existentes não deixam de matar um inimigo por tentar aplicar um status depois do dano.

Commit: `fix: handle targets defeated during atomic effect sequences`.

### Etapa 2 — Contexto imutável de ação, proc e resultados

**Áreas:** contratos de efeitos, `EffectApplicationRecord`, `EffectExecutionStep`, reducers, frames e serialização.

**Entregas**

- IDs determinísticos para ação/proc/impacto; relações pai/filho e referências por componente.
- Fatos de mudança efetiva de recurso, quantidade limitada, derrota causada e mudança de stacks.
- Referências tipadas a outputs anteriores e contexto escopado; publicar variáveis numéricas com namespaces documentados.
- Integrar sequência/repeat/filhos atuais em um plano canônico limitado, com orçamento de profundidade, alvos e passos.
- Extrair responsabilidades do executor conforme necessário, sem runtime paralelo de cartas.
- Persistir os novos registros desde esta etapa; manter preview e frames consumindo os mesmos dados.

**Testes:** encadeamento com dois alvos, quantidade solicitada diferente da aplicada, causalidade, serialização round-trip, IDs idênticos em dez execuções e falha sem publicação de fatos.

**Saída:** uma regra consegue distinguir aplicação, abate e resultado limitado sem consultar logs textuais ou reconstruir o journal.

Commit: `feat: add immutable proc context and effect outcome facts`.

### Etapa 3 — Parâmetros numéricos e estágios de cálculo reutilizáveis

**Áreas:** `CalculationResolver`, contratos da pipeline, influence providers e settlement planner.

**Entregas**

- Pedidos de cálculo para parâmetros tipados além da magnitude de recurso: stacks, quantidade de cartas e deltas de atributos.
- Definir domínio/unidade de quantidades e perfis de estágio necessários para capturar origem e avaliar novo alvo.
- Produzir checkpoints/outputs numéricos declarados e traces auditáveis; sem cálculo próprio no orchestrator.
- Tornar explícitos rounding, tratamento de resto, sinal, limites e conversão para inteiro.
- Separar capturas numéricas de settlements: snapshot de força não deve gastar defesa ou outro recurso na aplicação de um status.
- Rejeitar etapas incompatíveis, dados não finitos e tentativa de reaplicar fatores já incorporados sem política expressa.

**Testes:** mesmo pedido vindo de carta/status/habilidade; magnitude capturada sem settlement; bônus flat aplicado uma vez; defesa do novo alvo aplicada uma vez; erro de unidade e conversão inteira; preview sem consumo.

**Saída:** há uma única matemática para efeitos, distribuição e continuação, sem suposições baseadas em nomes de recursos.

Commit: `feat: resolve typed effect parameters through calculation profiles`.

### Etapa 4 — Payloads acumuláveis, snapshots e consumo genérico

**Áreas:** `StatusEffectDefinition/Instance`, modifiers, políticas de stacks, lifecycle e transições imutáveis.

**Entregas**

- Payload tipado para força capturada e parâmetros de ativação, com revisão e origem preservadas.
- Binding entre parâmetros da carta efetiva e parâmetros do status emitido.
- Implementar `Dynamic` e `Snapshot` por parâmetro, incluindo comportamento de origem ausente/derrotada.
- Lotes de contribuições quando a reapplication preservar intensidades diferentes; regras explícitas de merge/replace e duração.
- Referência comum de fonte acumulável e adaptadores de leitura/consumo para status e modifiers.
- Consumo completo da seleção com motivo próprio, sem dispel/expiração implícitos.
- Lifecycle deve ignorar instâncias consumidas, inclusive quando já estavam no snapshot do boundary.

**Testes:** stack de intensidades diferentes, buff alterado depois da aplicação, upgrades da carta preservados no payload, origem derrotada, consumo no mesmo boundary, expiração e replay de lotes.

**Saída:** stacks carregam dados suficientes para serem ativados ou condensados sem reconstruir a carta original a partir do estado atual.

Commit: `feat: persist stack payloads and immutable consumption plans`.

### Etapa 5 — Condensação genérica em um único proc

**Áreas:** definição/validação de efeitos, planejador, recipes revisionadas, reducers e contratos de proc.

**Entregas**

- Introduzir operação de condensação com referência a receita configurada; o nome final do schema será definido na etapa 0.
- Implementar o contrato da seção 5: seleção, captura, agregação, consumo completo e uma ativação.
- Receitas podem emitir os efeitos executáveis comuns e ligar o agregado aos seus parâmetros.
- Seleção por início da ação ou estado atual, política de ausência e proteção contra consumo duplicado.
- Configurar consumo/chance/skip/zero aplicado e tratamento de instâncias esgotadas.
- Agrupar fontes compatíveis antes da ativação; não emitir um proc por stack nem por instância selecionada.
- Implementar primeiro agregação de contagens/payloads; potencial periódico restante somente por receita declarada e limitada.
- Impedir recursão ilimitada de condensar/aplicar novos stacks/condensar novamente no mesmo escopo.

**Testes obrigatórios:** cura, recurso arbitrário, compra de cartas e aplicação de status/modifier; soma de várias instâncias; nenhum stack; chance falsa; falha no último efeito; revisões mistas; duração permanente; multi-hit não repete consumo; apenas um `procId` e uma ativação em cada caso.

**Saída:** condensação funciona sem qualquer referência obrigatória a DAMAGE, health ou DoT.

Commit: `feat: consume accumulated stacks into one configurable proc`.

### Etapa 6 — Transformações estruturais de cartas

**Áreas:** `CardInstanceState`, contratos atuais de upgrade, compiler, `EffectiveCardResolver`, comandos e inspeção.

**Entregas**

- Evoluir o histórico atual de upgrades para representar alterações numéricas e estruturais num único ledger autoritativo.
- Operações tipadas: alterar tags, adicionar/remover/substituir componente ou bundle, alterar parâmetros compatíveis e remover/substituir uma transformação selecionada.
- Identidade estável da carta e de cada transformação; categoria distingue melhoria base, afinidade e comportamento para regras/apresentação.
- Resolver removals/replacements reconstruindo a composição a partir da definição e do ledger, não tentando inverter multiplicações ou patches antigos.
- Component IDs e namespaces de bundle estáveis; rejeitar colisões e componentes referenciados que deixem de existir.
- Ordem determinística e conflitos explícitos; revalidar a composição completa depois de aplicar as mudanças.
- Caches por revisão, definição e hash do ledger; nenhum cache somente por `cardId`.
- Migrar todos os consumidores dos modelos alterados e remover o caminho substituído.

**Testes:** mesma carta troca fogo por outra afinidade; ganha/perde componente; conserva melhoria numérica; substitui comportamento; duas cartas da mesma definição permanecem independentes; slots inválidos; conflito de IDs; round-trip e forks sem compartilhamento mutável.

**Saída:** a carta realmente muda de identidade durante a run sem destruir/criar outra instância ou editar globalmente o catálogo.

Commit: `feat: support typed structural card transformations`.

### Etapa 7 — Gramática de afinidades e modificadores comportamentais

**Áreas:** conteúdo de composição/bundles, validação de compatibilidade, modelos de transformação e setting inicial.

**Entregas**

- Regras JSON relacionando tags/ação a componentes reutilizáveis. Pode-se evoluir bundles e filtros existentes; não criar um registry hardcoded de elementos.
- Fogo+dano e fogo+cura são mapeamentos do setting, não branches no executor.
- Modificadores permanentes alteram a composição da carta; buffs/status/modifiers contextuais continuam influências ou gatilhos no snapshot do personagem.
- Slots e categorias de compatibilidade configuráveis, requisitos/exclusões por tags/capacidades e limites explícitos.
- Ordem e escopo das interações declarados: antes dos impactos, por impacto, após sequência ou por proc. Somente boundaries executáveis podem ser publicados.
- Mensagens estruturadas explicam por que uma transformação é incompatível e quais componentes ela modifica.

**Testes:** mesma afinidade em ataque/cura produz consequências distintas; custo não depende implicitamente da tag; troca remove contribuição antiga; par compatível aceito; conflito rejeitado; conteúdo arbitrário sem nomes conhecidos executa da mesma forma.

**Saída:** mudar afinidade/modificador altera comportamento via conteúdo e a composição resultante é inspecionável.

Commit: `feat: compose affinity and behavior rules from content`.

### Etapa 8 — Multi-hit com orçamento e distribuição explícitos

**Áreas:** execução de repeats, perfis de parâmetros, componentes de comportamento e targeting.

**Entregas**

- Distinguir repetir uma quantidade por impacto de repartir um orçamento por ação; ambos são políticas de conteúdo.
- Setting inicial: calcular contribuição da origem uma vez, repartir a magnitude e avaliar fatores/settlements de cada alvo por impacto.
- Distribuir restos de rounding em ordem declarada, preservando o total quando a política exigir conservação.
- Distribuir intensidade/stacks residuais separadamente; um resto inteiro não vira stack adicional em todo impacto.
- Impactos aos quais a distribuição atribuir zero stacks não tentam aplicar um status inválido; registrar a ausência dessa contribuição sem abortar a carta.
- Chance, críticos e gatilhos declaram escopo por ação/impacto. Não multiplicar bônus flat inadvertidamente.
- Aplicar políticas da etapa 1 aos acertos restantes; permitir parada ou retarget configurado.
- Garantir condensação `OncePerAction` ao combinar os dois modificadores.

**Testes:** magnitude 10 em 3 impactos; bônus flat adicionado uma vez; defesa consumida por impacto; stacks discretos; alvo morto no primeiro impacto; nenhum novo alvo; repeat tradicional mantém semântica explicitamente configurada.

**Saída:** multi-hit não é universalmente um multiplicador gratuito de dano ou de stacks.

Commit: `feat: distribute multi-hit budgets with explicit proc scopes`.

### Etapa 9 — Continuação por abate e transporte de excedente

**Áreas:** fatos de aplicação, selectors, perfis numéricos de continuação e composição de efeitos.

**Entregas**

- Condição de continuação ligada ao fato de derrota causado pelo impacto atual, não ao nome do recurso.
- Setting inicial transporta o excedente após aplicação ao alvo anterior, com unidade/domínio registrados. Repetir impacto e transportar impactos restantes não são aliases silenciosos.
- Cálculo do excedente utiliza pedido canônico com resultado solicitado, mudança efetiva e limites; não fórmulas independentes no UI/orchestrator.
- Próximo alvo recebe novo pedido que não reaplica scaling da origem já incorporado, mas avalia suas condições relevantes.
- Definir seletor/ordem visível, exclusão de alvos visitados, máximo de saltos, ausência de alvo e afinidade que acompanha ou não a continuação.
- Definir quais impactos/procs podem originar salto e limites de recursão; sem execução implícita de todos os modificadores em todos os filhos.
- Registrar caminho completo na resolução, inclusive salto que termina por falta de excedente/alvo.

**Testes:** subtração 15 sobre recurso com 10 restantes produz excedente 5 antes das regras do novo alvo; excesso zero; defesa distinta; origem com bônus; derrota por recurso não chamado health; nenhum inimigo restante; limite de saltos; multihit/condensação com continuação autorizada.

**Saída:** salto é uma continuação causal, não dano em área nem uma cópia gratuita do impacto completo.

Commit: `feat: continue effects on defeat with audited overflow transfer`.

### Etapa 10 — Atributos persistentes e progressão canônica

**Áreas:** estado de entidades da run, materialização de encontros, componentes de atributos, command handlers e effect reducers.

**Entregas**

- Representar a entidade persistente do jogador e seus atributos no agregado da run, com identidade/revisão explícitas; hoje os componentes de combate não bastam para armazenar essa progressão entre encontros.
- Definir autoridade por campo: atributos persistentes da run materializam o ator; estado de combate contém o resultado atual das operações daquele encontro. A sincronização ocorre apenas em transições declaradas.
- Operação genérica tipada para alterar atributo identificado por componente/value ID, com Add/Multiply/Set, limites e autorização de conteúdo.
- Alterações permanentes atualizam a base persistente; buffs temporários permanecem modifiers/status, não uma reescrita permanente disfarçada.
- Preparações/recompensas podem produzir alterações por efeitos comuns. O cliente escolhe uma opção, não envia novos atributos arbitrários.
- Atualizar providers, inspeção, hashes, save/load e materialização sem fallback para a definição original.

**Testes:** atributo melhorado afeta várias cartas elegíveis e próximo encontro; carta não elegível não muda; buff expira sem apagar melhoria; alteração falha não persiste; replay/fork preserva a progressão.

**Saída:** o investimento no personagem é diferente do upgrade da carta, observável e persistente.

Commit: `feat: persist authored actor attribute progression across encounters`.

### Etapa 11 — Oportunidades de transformação e conteúdo demonstrativo

**Áreas:** `RunProgression`, offers, preparação/upgrade, gateway e arquivos de setting.

**Entregas**

- Evoluir oportunidades existentes para oferecer aquisição, melhoria base, afinidade, comportamento e atributo, sem novo manager de run paralelo.
- Comando resolve opção revisionada, carta/ator elegível, custo, compatibilidade e política do modo; payload contém IDs, não patches arbitrários em gameplay normal.
- Perfis dev/sandbox podem ter ferramentas autorizadas, mantendo o mesmo comando/planejador e journal.
- Setting inicial isolado, com poucas cartas e duas afinidades; não substituir as configurações dos outros settings.
- Três ou mais encontros curtos, múltiplos inimigos, ameaça anunciada e confronto final; oportunidades permitem builds diferentes.
- Conteúdo de condensação ofensiva, cura e pelo menos um caso não associado a dano/cura, além de pares de modificadores compatíveis.
- Custos/orçamento por JSON; não acrescentar ataque gratuito ilimitado para contornar escassez de cartas.

**Testes:** oferta deterministicamente repetível; transformação preserva ID; opção inválida/fora da atividade rejeitada; settings coexistem sem cruzar revisões; deck/atributos entram no encontro seguinte.

**Saída:** o jogador constrói a composição ao longo de uma run sem depender do editor/debug.

Commit: `feat: add authored core build opportunities and demonstration setting`.

### Etapa 12 — API, inspeção e preview completos

**Áreas:** queries/projections, `CardInspectionService`, controllers finos, contratos e OpenAPI.

**Entregas**

- Manter mutations no gateway existente `POST /api/v1/runs/{runId}/commands`, com identidade, versão, autorização e recibo idempotente.
- Estender evaluations de cartas existentes com transformação efetiva, slots, influências, payloads, elegibilidade, stacks consumíveis, proc previsto e caminho de saltos.
- Ofertas/capabilities informam escolhas válidas e motivos de indisponibilidade. Adicionar query específica somente se não couber nas projeções atuais.
- Reutilizar os mesmos contratos para inspecionar efeitos de habilidade/status quando essas capacidades forem expostas; não copiar regras de preview.
- Separar detalhe de jogador e debug: apresentar o resultado útil sem revelar conteúdo oculto do modo.
- Preview é condicionado ao snapshot e input; marcar incerteza quando depender de sorteio/contexto futuro e invalidar por versão.
- Reavaliar quando a seleção de alvo ou revisão muda; custo e estado pós-custo seguem a mesma ordem da execução real.

**Testes:** preview versus execução a partir do mesmo snapshot; GET sem mutação; idempotência; concorrência/version conflict; capabilities; pacote incorreto/revisão ausente; schema/cliente Godot.

**Saída:** nenhum controller conhece a regra de condensação ou precisa recalcular dano para responder.

Commit: `feat: expose core transformations and proc previews through canonical APIs`.

### Etapa 13 — Godot: legibilidade, identidade e fila visual

**Áreas:** gateway/session, presenters, `card_view`, telas de combate/atividade, art manifest e localização.

**Entregas**

- Camada de comunicação converte contratos em view models; componentes de UI não enviam pedidos REST diretamente nem decidem regras.
- Carta apresenta base/valor contextual, afinidade, modificadores permanentes, buffs temporários, custos e efeitos principais/residuais distintos.
- Tooltip de condensação mostra seleção, contagem, consumo e efeito agregado; não exibe sempre o rótulo “dano”.
- Indicar número de impactos, regra de distribuição, proc único e salto previsto com ordem de alvos visível.
- Oportunidades entre encontros apresentam antes/depois da mesma carta/atributo e incompatibilidades fornecidas pela engine.
- Expandir manifesto visual com IDs estáveis para cartas, efeitos e comportamentos; placeholders identificados e fallback visual sem interferência no gameplay.
- Nomes/textos em inglês como fallback e chaves em inglês; português disponível, sem regras codificadas no texto localizado.
- Reproduzir consumo e resultado agregado como uma ativação, mesmo quando houver vários registros filhos. A engine já concluiu a ação; animação/confirmar avanço não governa o cálculo.
- Atualizar incrementalmente a UI, preservando containers, fontes e seleção; evitar reconstruções que causem piscadas ou texto vertical.

**Testes:** mão vazia, textos longos PT/EN, telas suportadas incluindo 2560x1080, múltiplos efeitos no proc, resize, uma animação principal por condensação, troca de setting, replay e interfaces de transformação.

**Saída:** o jogador consegue explicar o que mudou na carta e o que foi consumido, sem precisar da tela de debug.

Commit: `feat: present card transformations and aggregated procs in Godot`.

### Etapa 14 — Validação integrada, desempenho e documentação

**Entregas**

- Executar todas as suítes Core/API e os testes headless disponíveis da Godot; registrar limitações de validação visual separadamente.
- Rodar a run demonstrativa dez vezes em runtimes novos com seed/revisão/comandos iguais; comparar estado, RNG, cálculos, procs, frames e fingerprints sem campos externos não determinísticos.
- Save/load e replay semântico em meio ao acúmulo, após transformar carta, após condensar e após um salto; testar forks com escolhas diferentes e prefixo histórico idêntico.
- Hot reload em modos permitidos preserva revisões do histórico. Rebase/mudança de conteúdo que altere execução é comando explícito; nunca reescrever snapshots passados.
- Auditagem de todas as origens de efeito para uso dos novos contratos; remover campos/caminhos substituídos, stubs de capability e fallbacks silenciosos.
- Benchmark de início/meio/fim da run, muitos stacks e muitos alvos. Agregar por instância/lote, não iterar um proc por stack ou consultar o journal completo.
- Medir resposta completa do comando e evaluations representativas em ambiente definido; alvo de p95 abaixo de 1 segundo, registrando latência de engine e round-trip separadamente.
- Registrar também p99, pior caso e crescimento entre início/fim da run; p95 isolado não permite afirmar que todas as respostas estão abaixo de 1 segundo.
- Caches derivados por revisão/hash, invalidação por ledger/estado, payloads de detalhe controlado e nenhum cache autoritativo que altere o resultado.
- Atualizar README, GDD, documentação de efeitos/status/cartas/cálculos/progressão, schema, OpenAPI, exemplos de JSON e guia Godot.
- Fazer playtest separado da validação técnica: verificar entendimento de base/afinidade/modificador e se preparar, condensar, recuperar e encadear produzem escolhas distintas. Testes automatizados aprovados não comprovam diversão ou balanceamento.

**Saída:** nenhuma lacuna descrita neste plano permanece coberta por uma aproximação visual ou por um sistema alternativo.

Commit: `test: verify deterministic core runs and document final contracts`.

## 8. Dependências e gates de entrega

| Gate | Etapas | Demonstração exigida |
| --- | --- | --- |
| Execução confiável | 0–2 | Acerto letal não cancela sequências legítimas; resultados têm causalidade e persistência. |
| Condensação genérica | 3–5 | Consumir todos os stacks elegíveis em um único proc de cura, recurso e efeito não numérico de recurso. |
| Cartas voláteis | 6–7 | Mesma instância troca afinidade/comportamento, mantém melhorias e passa na validação. |
| Core combinatório | 8–9 | Multi-hit e salto funcionam isolados e combinados com condensação, sem duplicar magnitude ou consumo. |
| Run jogável | 10–13 | Atributos e cartas evoluem entre encontros; API e Godot tornam escolhas legíveis. |
| Entrega estável | 14 | Replay/forks/save-load, determinismo, regressão, desempenho e docs comprovados. |

Não iniciar a demo visual das novas mecânicas antes de os contratos correspondentes existirem na engine. A Godot pode evoluir após cada gate, mas não deve simular capacidades faltantes.

## 9. Matriz mínima de combinações

Além dos testes isolados, verificar:

- Afinidade ofensiva + status residual + upgrade base + atributo do personagem.
- Afinidade de cura + snapshot + mudança de buff após aplicação.
- Dois lotes com força distinta + reaplicação + condensação única.
- Multi-hit + residual dividido + rounding + alvo derrotado no primeiro acerto.
- Multi-hit + condensação: consumo uma vez e impactos separados.
- Condensação + salto: só a saída autorizada origina a continuação.
- Multi-hit + salto: transporte de excedente e tratamento dos impactos restantes explicitamente diferentes.
- Condensação que gera novo acúmulo: stacks novos não são reconsumidos no mesmo snapshot.
- Consumo de status/modifier que também oferece influência passiva: momento de avaliação respeitado.
- Falha no último componente depois de custo, consumo e alteração de zona: rollback integral.
- Transformação + troca de setting/revisão + persistência: sem resolução pelo pacote errado.
- Estado inicial igual + comandos iguais: hashes/procs/ordem iguais; preview anterior não altera o resultado.

## 10. Escopo e decisões ainda abertas

Este plano não inclui companions, metaprogressão, narrativa extensa, árvores/fusões, reações de prioridade reservadas nem um catálogo grande de artes. Eles não resolvem as lacunas do core.

Precisam continuar visíveis como escolhas de design:

1. Quais afinidades e comportamentos entram no primeiro setting; veneno+cura continua não definido.
2. Quantidade de slots e momentos permitidos para transformação, por modo.
3. Conteúdo elegível para condensação e conversões entre fontes heterogêneas.
4. Snapshot/dynamic, merge de contribuições e política de duração de cada acumulador.
5. Orçamento, crit/chance e resto de multi-hit; parâmetros de balanceamento.
6. Domínio do excedente, número de saltos e afinidade que acompanha a continuação.
7. Forma de aquisição de atributos/modificadores e duração da run.

Essas escolhas não bloqueiam a construção dos contratos genéricos. Os perfis demonstrativos devem identificá-las como configuração inicial, não como regra universal.

## 11. Arquivos de referência na implementação

- `src/Core/Effects/EffectTriggerExecutor.cs`: execução atual, repeats e validação de alvos.
- `src/Core/Effects/ImmutableEffectProcessor.cs`: comandos resolvidos e registros de aplicação.
- `src/Core/Effects/EffectDefinition.cs` e `EffectDefinitionValidator.cs`: schema e contrato executável.
- `src/Core/Calculations/CalculationModels.cs`, `CalculationResolver.cs` e `CalculationInfluenceProviders.cs`: matemática/contexto/settlements.
- `src/Core/StatusEffects/StatusEffectDefinition.cs` e `StatusEffectInstance.cs`: stacks, payloads e revisão.
- `src/Core/Combat/Flow/CombatStatusLifecycle.cs`: timing e expiração.
- `src/Core/Combat/Modifiers/ScriptModifierDefinition.cs`: influências contextuais; não confundir com alteração permanente da carta.
- `src/Core/Run/CardInstanceState.cs`, `Content/CardUpgradePatchDefinition.cs`, `CardContentCompiler.cs` e `EffectiveCardResolver.cs`: identidade e composição.
- `src/Core/Run/RunState.cs`, `RunProgression.cs`, `PreparationTransitions.cs` e command handlers: progressão e autoridade da run.
- `src/Core/Run/Content/CardInspectionService.cs` e `src/API/Controllers/CombatCardController.cs`: preview/query existentes.
- `src/API/Controllers/RunCommandController.cs`: fronteira canônica de mutations.
- `examples/godot-engine-showcase/scripts/application`, `engine`, `presentation` e `ui`: separação da demo.
- `tests/Core.Tests/Effects`, `Run`, `Combat/Flow`, `Calculations`, `tests/API.Tests/Integration` e testes Godot: regressão.

Os nomes de novos tipos, campos e recipes deste plano são propostas de contrato, não funcionalidades já disponíveis. A implementação deve registrar no acompanhamento o schema final e os consumidores migrados.
