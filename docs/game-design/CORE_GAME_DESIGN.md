# Core do jogo — cartas, afinidades e transformação de efeitos

Versão: 0.1 — proposta de design para discussão e validação.

Data: 2026-10-06.

## 1. Propósito e autoridade

Definir uma experiência central coerente a partir das decisões atuais do autor, aproveitando do legado apenas o que reforça essa experiência. Este documento não tenta acomodar todas as ideias anteriores.

O [documento de referência do legado](C:/Users/usuario/.gemini/antigravity/scratch/HeroScript/docs/game-design/LEGACY_GAME_DESIGN_CONCEPTS.md) permanece intocado. Ele é uma biblioteca de conceitos, não a lista de requisitos deste jogo.

Neste documento:

- **Definido pelo autor:** requisito explicitamente apresentado na conversa.
- **Proposta para o core:** recomendação de design, ainda sujeita à aprovação e ao teste.
- **Em aberto:** escolha que não pode ser deduzida da descrição atual.

Não é um plano de implementação, um catálogo de conteúdo ou uma declaração de que a engine já oferece todas essas regras. Também não há garantia de diversão ou de originalidade no mercado: o objetivo é formular uma identidade clara e verificável em jogo.

## 2. Conceitos definidos pelo autor, separados

| Conceito | Definição | O que ele não significa automaticamente |
|---|---|---|
| Cartas voláteis | Cartas de identidade flexível, transformadas ao longo da run, conforme esclarecimento do autor. | Não implica aleatoriedade de transformação, consumo definitivo ou mudança obrigatória durante o combate. |
| Efeito principal | Uma carta realiza uma ação, como causar dano ou curar. | A afinidade não substitui necessariamente a ação principal. |
| Tag de afinidade | Cada carta possui uma tag, como fogo ou veneno, que define parte do efeito em relação à ação realizada. | Não é apenas um filtro de bônus numérico nem uma categoria de custo. |
| Atributo base da carta | A carta tem uma magnitude própria que serve de ponto de partida. | Não equivale ao resultado final nem ao atributo do herói. |
| Scaling pelo personagem | Atributos do jogador modificam essa magnitude através da bucket pipeline. | Não é a mesma coisa que aumentar permanentemente a base da carta. |
| Modificadores da carta | A carta pode ter alguns modificadores que alteram o comportamento do efeito. | Não são apenas bônus de dano ou uma lista de efeitos extras sem relação entre si. |
| Identidade de flavor | Cada efeito terá nome e arte únicos. | Não exige uma regra exclusiva e isolada para cada nome ou ilustração. |

Exemplos fornecidos pelo autor:

- Dano associado à afinidade pode aplicar queimadura.
- Cura associada à afinidade pode acrescentar cura residual.
- Um modificador pode transformar o ataque em múltiplos ataques/acertos.
- Um modificador pode fazer o ataque provocar dano condensado.
- Um modificador pode fazer o dano saltar para outro alvo se matar o primeiro.

Esses exemplos definem famílias de comportamento. Quantidades, duração, condições, ordem e limites ainda precisam de regras de design.

## 3. Identidade proposta para um único jogo

### 3.1. Pitch

Um roguelike de combate com cartas em que o jogador transforma ações simples em sequências próprias: a afinidade deixa uma consequência, o personagem determina sua força e os modificadores mudam como aproveitar essa consequência.

### 3.2. Fantasia do jogador

"Eu não encontro apenas um ataque mais forte. Eu descubro como fazer meu ataque preparar, consumir ou transportar um efeito para construir a sequência que quero."

### 3.3. Verbo central

**Preparar uma consequência e escolher como aproveitá-la.**

O centro da experiência não é editar cartas por editar, colecionar tags ou aumentar números indefinidamente. É usar a build para decidir quando atacar, em quem investir efeitos, quando antecipar um resultado e como converter uma finalização em vantagem contra outro alvo.

### 3.4. Por que essa combinação merece teste

- A tag pode ter expressão diferente em dano e cura: o elemento representa um comportamento reconhecível, não apenas uma cor.
- A mesma base ganha usos distintos com modificadores diferentes.
- Efeitos residuais introduzem uma decisão de tempo: usufruir depois ou consumir agora.
- Encadeamento por abate introduz uma decisão de alvo: uma ameaça enfraquecida pode se tornar ponto de partida para uma sequência.
- Atributos dão direção à build, mas a ordem das ações continua importante.

É uma hipótese de identidade interessante, não uma afirmação de que nenhuma outra obra utiliza essas mecânicas. A distinção precisa aparecer nas decisões do jogador, não apenas na organização das regras.

## 4. Pilares do core

1. **Composição legível:** o jogador entende a ação, a contribuição da tag e a transformação causada por cada modificador.
2. **Construção descoberta durante a run:** oportunidades sugerem caminhos; o jogador não começa com uma receita completa.
3. **Sequência importa:** alvo, ordem e momento de consumo dos efeitos mudam o resultado.
4. **Atributos e cartas trabalham juntos:** melhorar o personagem altera o valor de várias cartas; melhorar uma carta altera sua base ou comportamento específico.
5. **Força com decisões:** sequências poderosas são desejadas, mas não devem reduzir todos os turnos a repetir uma combinação sempre dominante.
6. **Poucas peças, profundidade combinatória:** provar o núcleo com poucas afinidades e modificadores antes de ampliar o catálogo.

## 5. Anatomia conceitual de uma carta

**Ação principal + base + afinidade + modificadores + custo/alvo + identidade de apresentação.**

### 5.1. Ação: o que a carta faz

Define o resultado principal e os alvos válidos. Para a primeira validação, a proposta é trabalhar com dano e cura, sem abrir imediatamente todas as categorias possíveis.

### 5.1.1. Volatilidade: a carta não tem um destino único

Definido pelo autor: a carta possui identidade flexível e pode ser transformada ao longo da run. Sua trajetória depende das oportunidades e dos investimentos do jogador, em vez de ficar limitada a uma versão final obrigatória.

O objetivo de design é manter reconhecível o que a carta faz enquanto afinidade, modificadores ou melhorias alteram sua utilidade. Quais dessas propriedades podem mudar e em quais momentos ainda precisam de decisão. Flexibilidade não exige permitir trocar qualquer propriedade a qualquer instante.

Como hipótese para o core, o jogador deve conseguir reconhecer que transformou uma carta familiar em uma ferramenta própria, sem precisar reaprender todas as suas regras a cada oportunidade.

### 5.2. Base: de onde vem sua magnitude

A carta possui uma magnitude base. Uma melhoria própria da carta pode alterar essa base; atributos, buffs e outras condições do personagem entram no cálculo apropriado, sem serem confundidos com uma reescrita permanente do valor base.

Uma carta com efeito imediato e residual precisa comunicar as duas partes. Ter uma base não significa aplicar o mesmo valor integral em todas elas: a relação entre magnitude principal, intensidade residual e duração precisa ser definida.

### 5.3. Afinidade: o que a ação deixa como consequência

A tag possui significado mecânico. Seu comportamento depende da ação: a contribuição em um ataque pode ser diferente da contribuição em uma cura, preservando uma identidade comum.

Proposta inicial: uma afinidade principal por carta. Múltiplas afinidades e reações elementais ficam fora da primeira validação.

### 5.4. Modificadores: como a ação se comporta

Alteram execução, consumo de efeitos ou continuidade entre alvos. A carta pode combinar modificadores compatíveis; quantidade de espaços e regras de compatibilidade são escolhas de design, não herança automática do limite de um modificador do legado.

### 5.5. Custo e alvo: em que condições vale usar

São restrições que criam escolhas. Não decorrem automaticamente da afinidade: fogo não precisa usar um recurso exclusivo e veneno não precisa custar mais apenas por ser veneno.

Proposta: iniciar com um recurso de ação, evitando testar a economia de vários recursos junto com todas as interações novas.

### 5.6. Nome e arte: como o efeito é reconhecido

Cada efeito recebe identidade própria, conforme solicitado. Texto e apresentação devem permitir reconhecer suas regras comuns, mesmo quando nomes e artes forem diferentes.

A abrangência de "cada efeito" está em aberto: efeito individual, carta completa ou cada composição resultante. A proposta é começar com identidade própria para cartas e efeitos definidos, sem exigir uma ilustração inédita para toda combinação possível de modificadores.

## 6. Afinidades que alteram comportamento

### 6.1. Primeira gramática proposta

| Afinidade e ação | Expressão | Situação de design |
|---|---|---|
| Fogo + dano | Impacto imediato com queimadura como consequência. | Exemplo alinhado à descrição do autor. |
| Fogo + cura | Recuperação imediata com recuperação residual. | Leitura proposta do exemplo do autor; a associação exata precisa de confirmação. |
| Veneno + dano | Aplicação de pressão residual que recompensa investimento no alvo. | Proposta de contraste para a validação. |
| Veneno + cura | Não definida. | Não inventar uma correspondência apenas para completar a tabela. |

Nem toda combinação precisa existir no primeiro recorte. Porém, uma combinação oferecida ao jogador deve possuir comportamento explícito; não pode depender de adivinhar o significado de uma tag.

### 6.2. Diferenciar sem criar um catálogo enorme

Para testar duas afinidades, elas precisam produzir decisões diferentes. Hipótese inicial:

- Fogo concentra retorno mais cedo, com consequência residual curta.
- Veneno recompensa preparação e manutenção de pressão por mais tempo.

Isso é uma proposta de perfil, não um balanceamento aprovado. Se trocar apenas a tag não mudar nenhuma decisão, as afinidades serão variações cosméticas do mesmo efeito.

### 6.3. O residual deve importar

Um efeito residual cria interesse quando continuar, consumir ou abandonar seu potencial tem consequências. Não basta acrescentar uma pequena parcela automática que o jogador nunca considera.

O combate deve apresentar perguntas como: "Esse alvo morre antes de aproveitar o efeito?", "Vale antecipar a pressão?" e "Preciso recuperar agora ou posso esperar?".

## 7. Magnitudes e bucket pipeline

Definido pelo autor: o atributo base da carta é modificado por atributos do personagem através da bucket pipeline.

Para o design, a separação é:

- **Carta:** fornece base, ação e composição do efeito.
- **Personagem:** fornece os atributos relevantes para aquele cálculo.
- **Condições:** podem acrescentar bônus, reduções e fatores aplicáveis.
- **Cálculo:** entrega a magnitude resultante, considerando as regras pertinentes.
- **Efeito:** usa essa magnitude no alvo e no momento definidos.

A pipeline calcula magnitudes; ela não precisa conhecer a identidade da carta, selecionar o próximo inimigo, consumir um status ou determinar se uma ação deve se repetir. Modificadores podem gerar vários pedidos de cálculo, sem criar uma segunda matemática própria para esses resultados.

Toda quantidade relevante de dano — direto, periódico, condensado ou encadeado — deve seguir o caminho canônico de cálculo, com clareza sobre quais fatores já foram incorporados. Aplicar ao próximo alvo não significa reaplicar automaticamente todo bônus sobre um número já amplificado.

Para cura e demais magnitudes, a proposta é manter a mesma distinção entre base, scaling e aplicação, com as regras adequadas ao efeito; não tratar cura como dano com sinal invertido nem aplicar mitigação de dano indiscriminadamente.

O jogador deve distinguir três investimentos:

1. Alterar a base de uma carta.
2. Alterar atributos que beneficiam cartas compatíveis.
3. Alterar a forma como uma carta utiliza sua magnitude.

Não ficam definidos aqui fórmulas, nomes de atributos ou ordem obrigatória de buckets para todos os estilos de jogo.

## 8. Três modificadores para demonstrar o núcleo

### 8.1. Múltiplos acertos

Definido pelo autor: um ataque pode virar um ataque múltiplo.

Proposta para validação: dividir a magnitude principal em acertos menores, em vez de simplesmente duplicar o dano total. A identidade surge das interações por acerto e da possibilidade de finalizar o alvo durante a sequência.

Precisam ser explícitos:

- Se a consequência da tag é aplicada por acerto ou por uso da carta.
- Se sua intensidade também é repartida.
- O que acontece com acertos restantes quando o alvo morre.
- Quais cálculos independentes são necessários a cada impacto.

Recomendação inicial: repartir a intensidade residual junto com a magnitude para não converter automaticamente multi-hit no melhor aplicador de todos os status. Modificadores ou cartas futuras podem alterar essa regra de forma visível.

### 8.2. Condensação

Definido pelo autor: um ataque pode provocar dano condensado.

Proposta para validação: além de seu efeito principal, a carta consome efeitos residuais ofensivos elegíveis já presentes no alvo e antecipa seu potencial restante como dano imediato.

- Consome o que converte; o mesmo potencial não continua causando dano depois.
- Não cria um bônus livre desvinculado dos efeitos que foram preparados.
- Deve mostrar o que pode ser consumido e o que não pode.
- A ausência de residual não precisa invalidar o ataque principal; essa é uma proposta, não uma regra herdada do legado.

Recomendação inicial: condensar efeitos existentes antes do uso da carta, deixando a consequência nova da afinidade fora dessa conversão. Isso preserva uma sequência de preparação e aproveitamento, em vez de fazer a carta preparar e consumir tudo sozinha.

Permanecem em aberto a fração convertida e a elegibilidade. A cura residual existe como parte da gramática, mas sua condensação não é necessária para validar o primeiro modificador ofensivo.

### 8.3. Salto por abate

Definido pelo autor: se o ataque matar o alvo, o dano pode saltar para o próximo.

Há três leituras possíveis: transferir dano excedente, repetir a magnitude do impacto ou transferir acertos ainda não executados. Elas não são equivalentes.

**Proposta para o core: transferir o excedente do impacto letal.** Isso dá valor a preparar um inimigo como ponte e controlar o momento de finalização, sem transformar todo abate em uma cópia gratuita do ataque inteiro.

- O próximo alvo recebe um novo impacto com suas próprias condições relevantes.
- Transferência não reaplica automaticamente bônus já contabilizados.
- O cálculo precisa estabelecer como o excedente é expresso, para evitar descontar a defesa do alvo anterior duas vezes ou ignorar a defesa do seguinte.
- O texto deve informar se a afinidade acompanha o salto e quantos saltos são permitidos.
- Sem outro alvo válido, a cadeia termina.

Recomendação inicial: um salto e escolha automática de alvo em ordem visível. Seleção manual durante a sequência e cadeias extensas ficam para outro teste.

### 8.4. Combinação entre modificadores

Proposta: testar primeiro cada modificador isolado; depois permitir um par compatível. Isso não reduz o objetivo final a um único modificador por carta.

O primeiro par sugerido é múltiplos acertos + condensação. Ele obriga a fechar quando a condensação ocorre; recomendação inicial: uma vez por uso da carta, sem consumir ou recriar o mesmo residual em cada acerto.

Condensação + salto e múltiplos acertos + salto entram na validação seguinte, quando estiver claro quais impactos podem acionar cada continuação. "Tudo ativa tudo" não é uma regra suficiente.

## 9. Loop jogável mínimo proposto

### 9.1. Dentro do combate

1. Ler o estado e a ameaça dos inimigos.
2. Examinar as cartas disponíveis, seus custos e consequências.
3. Escolher se prepara residual, antecipa um efeito, recupera vida ou tenta uma finalização encadeada.
4. Executar ações enquanto houver recursos e cartas disponíveis.
5. Encerrar o turno; inimigos e efeitos temporizados avançam.
6. Adaptar o plano ao novo estado.

Proposta: intenções inimigas visíveis e fase clara para cada efeito. A incerteza da compra de cartas já oferece adaptação; esconder também a ameaça pode dificultar avaliar o núcleo.

### 9.2. Circulação das cartas

Para ter um jogo de cartas, e não apenas habilidades desenhadas como cartas, é necessário definir disponibilidade e reutilização.

Proposta provisória: baralho, mão e descarte; cartas usadas vão para o descarte e retornam por reciclagem do baralho durante o combate. Cartas não usadas são descartadas ao terminar o turno. Quantidade comprada e orçamento de ação serão calibrados depois.

Essa circulação é uma proposta separada da volatilidade definida pelo autor. Identidade transformável ao longo da run não implica carta consumida definitivamente depois do uso. Retenção e transformação durante o combate continuam sendo decisões próprias.

Não incluir um ataque básico gratuito ilimitado: o jogador não deve ignorar as escolhas de cartas para vencer repetindo uma ação externa ao sistema.

### 9.3. Entre combates

Combate → oportunidade de acrescentar ou transformar uma carta, ou direcionar um atributo → próximo combate com a composição alterada.

Proposta: decisões curtas e ofertas limitadas, sem um editor completo a cada parada. A rota pode começar linear; um mapa ramificado não é necessário para provar essa experiência.

A forma de obter modificadores e atributos ainda não é definida pelo autor. Para testar, oportunidades diretas entre encontros evitam introduzir loja, ouro, outra moeda e uma economia de conversão ao mesmo tempo.

## 10. Um exemplo de decisão, não um catálogo

O jogador enfrenta uma ameaça com muita vida e outra já enfraquecida. Tem uma carta que prepara dano residual, uma que condensa e uma com salto por abate.

- Preparar a ameaça resistente pode maximizar dano futuro, mas não interrompe imediatamente sua pressão.
- Condensar cedo pode eliminar uma ameaça agora, abrindo mão de observar seus efeitos ao longo dos turnos.
- Finalizar a ameaça enfraquecida com salto pode transformar parte do impacto em progresso contra a resistente.
- Recuperar vida pode ser necessário, mas recuperação residual talvez chegue tarde demais para a ameaça anunciada.

A resposta deve depender de ameaça, efeitos acumulados, recursos, mão e atributos. Se uma opção for superior independentemente desses fatores, a combinação ainda não produz o core desejado.

## 11. Uso seletivo do legado

| Conceito do legado | Direção neste documento | Motivo |
|---|---|---|
| Descoberta durante a run | Preservar como pilar. | Dá significado às oportunidades de cartas e modificadores. |
| Transformação qualitativa | Preservar, adaptando aos exemplos atuais. | É mais central que simplesmente aumentar dano. |
| Afinidades conectando sistemas | Preservar e tornar comportamental. | A tag participa do efeito, além de relacionar bônus. |
| Condensação de dano periódico | Aproveitar como proposta tática. | Cria preparação e escolha de momento. |
| Matemática em camadas | Preservar sem impor fórmulas antigas. | Separa base, scaling e transformação. |
| Builds muito fortes | Preservar como recompensa possível, não obrigação de scaling exponencial. | A força deve surgir de interações interessantes. |
| Um modificador irreversível por poder | Não herdar. | O autor pede alguns modificadores; o limite será validado. |
| Consumir ofertas para obter PP/Ascensão | Adiar. | É uma economia adicional, não condição para testar as cartas. |
| Raízes, árvores e fusões | Adiar. | Sobrepõem outro sistema de identidade à afinidade da carta. |
| Companions, gambits e sincronia | Adiar. | O protagonista do primeiro teste é a sequência de cartas. |
| Críticos em múltiplos níveis | Adiar. | Acrescentam uma dimensão de cálculo não essencial a este núcleo. |
| Eras, canonização, facções e progressão narrativa | Adiar. | Não ajudam a verificar as decisões dentro do combate. |
| Loja, relíquias e vários modos | Adiar como requisitos de game design. | Podem ampliar um núcleo já validado. |

Adiar uma mecânica não exige removê-la da engine. Significa não depender dela para afirmar que este jogo funciona.

## 12. Recorte para testar diversão

### 12.1. O que precisa existir na experiência

- Um herói com atributos que alterem de maneira perceptível algumas cartas.
- Poucas cartas com bases, custos e papéis compreensíveis.
- Duas afinidades com perfis distintos, incluindo pelo menos uma expressão ofensiva e uma de cura.
- Efeito residual ofensivo e recuperação residual.
- Múltiplos acertos, condensação e salto por abate.
- Encontros com mais de um inimigo para que alvo e encadeamento tenham valor.
- Ameaças anunciadas que diferenciem urgência de preparação.
- Oportunidades de alterar a composição entre encontros.
- Uma sequência curta com começo, desenvolvimento da build e confronto final.

Nomes e artes únicos devem comunicar identidade; placeholders identificados são suficientes para testar as regras. Produzir todo o catálogo visual antes desse teste não prova a experiência.

### 12.2. Critérios para continuar expandindo

- O jogador consegue explicar o efeito principal, a contribuição da tag e a mudança do modificador.
- Mudar a tag ou o modificador muda ao menos uma decisão real, não apenas o valor exibido.
- Há situações em que preparar, condensar, encadear e recuperar são escolhas justificáveis.
- Atributos diferentes incentivam prioridades diferentes sem tornar o personagem incapaz de usar todas as outras cartas.
- O jogador reconhece uma sequência que construiu, em vez de atribuir tudo a efeitos automáticos obscuros.
- Escolhas entre encontros levam a maneiras distintas de resolver o confronto seguinte.
- O jogador quer repetir a experiência para explorar outra interação.

### 12.3. Sinais de que o core precisa mudar

- Condensar sempre é melhor que esperar, ou nunca compensa.
- Múltiplos acertos são superiores em toda situação por multiplicarem gratuitamente cada aplicação.
- Salto depende de uma coincidência rara e não pode ser preparado.
- Curar indefinidamente é mais seguro que decidir quando finalizar.
- Atributos aumentam números, mas não alteram prioridades de cartas ou alvos.
- Combos exigem tanta leitura que o jogador não entende por que funcionaram.
- Uma carta prepara, consome e encadeia sozinha, eliminando a necessidade das outras.
- O jogo fica interessante somente depois de acrescentar companions, relíquias ou outro sistema externo.

Esses sinais orientam playtest, não constituem resultados já observados.

## 13. Decisões prioritárias ainda em aberto

| Decisão | Questão | Proposta provisória, quando houver |
|---|---|---|
| Propriedades transformáveis | Quais partes da identidade flexível podem mudar: ação, afinidade, modificadores ou base? | Começar por modificadores e melhorias; testar mudança de afinidade em seguida. |
| Momento da mudança | As transformações ao longo da run ocorrem durante combate, entre combates ou na aquisição? | Entre encontros para a primeira validação; não é uma restrição já aprovada. |
| Circulação | Carta volta durante o combate ou existe um limite maior de usos? | Compra e descarte convencionais para isolar o primeiro teste. |
| Papel da tag | Qual comportamento cada afinidade acrescenta a cada ação? | Uma afinidade principal e poucas combinações explicitamente definidas. |
| Scaling | Quais atributos favorecem impacto, residual, cura ou outras magnitudes? | Poucos atributos e relações legíveis; valores ainda abertos. |
| Residual | Sua força fica fixada na aplicação ou acompanha mudanças posteriores do personagem? | Registrar a força calculada na aplicação para o primeiro teste, sem confundir isso com exigência geral da engine. |
| Modificadores | Quantos cabem, como são adquiridos e podem ser substituídos? | Isolados primeiro, um par compatível depois. |
| Condensação | Acrescenta dano ao ataque, substitui parte dele ou só existe com efeito elegível? | Ataque principal mais consumo de residual anterior. |
| Salto | Transporta excedente, repete impacto ou transporta acertos restantes? | Excedente, com um salto inicial. |
| Ordem das interações | Quando aplicar tag, condensar, reconhecer abate e saltar? | Fechar cada combinação antes de oferecê-la ao jogador. |
| Identidade visual | Qual unidade recebe nome e arte únicos? | Cartas e efeitos definidos; modificações identificáveis sem multiplicação obrigatória de artes. |

As propostas tornam possível discutir uma experiência concreta, mas não representam aprovação do autor. Volatilidade já significa identidade flexível transformada ao longo da run. O que pode ser transformado e em quais momentos ainda deve ser definido antes de detalhar conteúdo ou implementação.

## 14. Fontes e relação com o GDD legado

- Requisitos centrais: descrição atual do autor nesta conversa.
- [game-design-core.md](C:/Users/usuario/.gemini/antigravity/scratch/hero-script/game-design-core.md): descoberta, transformações qualitativas, clareza e escopo reduzido.
- [GDD_Overview.md](C:/Users/usuario/.gemini/antigravity/scratch/hero-script/docs/02_GameDesign/GDD_Overview.md): confronto com a visão anterior e seleção do que não deve ser exigido agora.
- [ScriptModifierSystem.md](C:/Users/usuario/.gemini/antigravity/scratch/hero-script/docs/02_GameDesign/Sistemas/ScriptModifierSystem.md): referência de múltiplos acertos e compatibilidade, sem herdar seus limites automaticamente.
- [StatusSystem.md](C:/Users/usuario/.gemini/antigravity/scratch/hero-script/docs/02_GameDesign/Sistemas/StatusSystem.md): referência de residual e condensação, adaptada como proposta e não como obrigação.
- [Extração de conceitos do legado](C:/Users/usuario/.gemini/antigravity/scratch/HeroScript/docs/game-design/LEGACY_GAME_DESIGN_CONCEPTS.md): consulta ampliada para extensões futuras; preservada sem alterações.

## 15. Síntese

O core proposto é construir e executar sequências de cartas cuja afinidade cria consequências, cujos números dependem do personagem e cujos modificadores mudam como essas consequências são aproveitadas. O primeiro jogo precisa ser interessante por preparar, escolher o momento, condensar e encadear — antes de depender de sistemas adicionais.

O próximo passo de design é delimitar as oportunidades de transformação e aprovar ou revisar as regras provisórias essenciais. O próximo passo de validação é verificar se essas poucas peças realmente produzem decisões diferentes e vontade de jogar novamente.
