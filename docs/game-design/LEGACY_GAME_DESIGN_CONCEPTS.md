# Conceitos de game design extraídos do projeto legado

Data da extração: 2026-10-06.

## 1. Objetivo e limites deste documento

Reunir os conceitos de jogo encontrados na documentação do projeto legado `hero-script`, para servir de matéria-prima ao novo documento de game design.

Esta é uma extração, não um GDD aprovado, uma avaliação da engine atual ou uma lista de funcionalidades implementadas. A documentação do legado contém revisões e propostas incompatíveis; elas estão identificadas sem escolher uma vencedora.

Foram excluídos:

- Arquitetura de software, código, APIs, formatos de arquivo e estratégias de implementação.
- Roadmaps técnicos, tarefas, estimativas e estados de implementação.
- Catálogos de cartas, poderes, raças, inimigos, companions, itens e eventos específicos.
- Nomes, histórias, personagens, facções e geografia do universo narrativo.
- Direção de arte, assets, paletas e receitas de animação.
- Valores de balanceamento, preços, probabilidades e fórmulas particulares, salvo conceitos estruturais necessários para entender uma mecânica.

Os conceitos abaixo não incorporam automaticamente decisões da engine ou da demo atual. As referências `[F01]` a `[F20]` apontam para as fontes ao final.

## 2. Visão e fantasia do jogador

### 2.1. Identidade do jogo

- Roguelike de estratégia e combate tático por turnos, single-player, pensado originalmente para PC.
- O jogador controla diretamente o herói e constrói sua identidade durante a run.
- Poderes são adquiridos por ofertas apresentadas como cartas e transformados em uma build própria.
- Aliados autônomos acrescentam uma camada de orquestração tática.
- A fantasia é montar uma máquina de sinergias, e não apenas aumentar atributos isolados.

### 2.2. Experiência emocional pretendida

- Descobrir oportunidades com o que a run oferece, em vez de escolher uma build completa antes de começar.
- Reconhecer uma interação inesperada e decidir investir nela.
- Perceber a build ganhar identidade e atingir uma escala de poder excepcional.
- Encerrar uma tentativa querendo experimentar outra combinação na próxima.

O documento de ideias centrais coloca essa descoberta como foco emocional dominante. O GDD mais abrangente dá maior destaque à programação dos aliados. Essa diferença de ênfase permanece relevante para a definição do novo GDD.

### 2.3. Referências de design registradas

- **Slay the Spire:** jornada por nós, escolhas de percurso e recompensas que constroem a run.
- **Hades:** adaptação às ofertas e descoberta de sinergias durante a tentativa.
- **Warframe:** customização em camadas e críticos que ultrapassam o modelo binário.
- **Path of Exile:** combinações entre formas diferentes de amplificar poder.
- **Balatro:** crescimento expressivo, sinergias e desbloqueios por feitos ou estilo.
- **Final Fantasy XII:** aliados com comportamento definido por regras condicionais.

Estas são influências declaradas pelo legado, não uma obrigação de reproduzir integralmente esses jogos. Fontes: `[F01]`, `[F02]`.

## 3. Pilares de design

### 3.1. Descoberta acima de planejamento rígido

A run oferece oportunidades; o jogador interpreta e adapta sua direção. O conhecimento ajuda a reconhecer combinações, mas não deve transformar toda tentativa na execução de uma receita fixa.

### 3.2. Expressão criativa em camadas

A expressão do jogador combina aquisição de poderes, alterações de comportamento, escolha do ritmo de recursos, configuração de aliados e decisões táticas em combate.

### 3.3. Sinergias fortes são desejadas

Builds com crescimento muito elevado são parte da fantasia. O desafio de design é tornar essa força uma recompensa por descoberta, sem deixar uma combinação óbvia dominar todas as alternativas.

### 3.4. Escolhas permanentes com significado

As decisões de construção de build têm consequências dentro da run. Entretanto, o núcleo revisado busca comunicar investimento e foco, evitando que o jogador sinta que destruiu sua tentativa por uma escolha mal informada.

### 3.5. Sistemas conectados por afinidades

Categorias e afinidades dos poderes conectam bônus, modificações, combos e reações de aliados. Essas relações devem ser reconhecíveis pelo jogador e gerar combinações entre sistemas diferentes.

### 3.6. Profundidade apresentada gradualmente

O jogador aprende primeiro a construir e usar o herói. Camadas adicionais aparecem quando passam a fazer sentido, sem exigir domínio de todas as mecânicas na primeira tentativa.

Fontes: `[F01]`, `[F02]`.

## 4. Loop de gameplay e estrutura da run

### 4.1. Loop principal

Escolher origem e poder inicial → escolher um nó → resolver combate ou oportunidade → adquirir ou transformar poderes → preparar a build → seguir para o próximo nó.

- A preparação e as recompensas alimentam o combate seguinte.
- Encontros de maior risco oferecem oportunidades de maior valor.
- A run termina em vitória ou derrota; o resultado pode gerar desbloqueios e consequências para tentativas futuras.

### 4.2. Percurso e ritmo

- A jornada é dividida em segmentos com identidade própria e pressão crescente.
- Combates são alternados com oportunidades de compra, recuperação, preparação e decisão narrativa.
- Há nós de risco elevado, confrontos de encerramento de segmento e oportunidades especiais de sinergia ou transformação.
- As escolhas de rota permitem avaliar risco imediato contra potencial de crescimento.
- O legado apresenta tanto escolha sequencial entre próximos nós quanto navegação por um mapa de biomas.

Não há necessidade conceitual de um único formato de percurso para todos os modos. Fontes: `[F01]`, `[F02]`, `[F14]`.

## 5. Origem do herói e identidade da build

- A origem fornece um ponto de partida e um poder inicial gratuito.
- Sua diferenciação pode envolver ritmo de geração de recursos, sobrevivência ou oportunidades econômicas.
- A filosofia registrada para as origens evita penalidades que bloqueiem o acesso a partes do jogo.
- O núcleo revisado defende uma inclinação inicial leve: a build descoberta deve definir o herói mais do que uma classe escolhida no menu.
- Existem propostas anteriores de origens com identidades mecânicas muito mais fortes. A intensidade dessa influência não está unificada.

Fontes: `[F01]`, `[F02]`, `[F09]`.

## 6. Poderes, cartas e organização da build

### 6.1. Função das cartas

- Cartas apresentam poderes, seus custos e suas possibilidades de uso ou aquisição.
- Poderes ativos são ações escolhidas pelo jogador.
- Passivas alteram capacidades ou interações sem ocupar o mesmo papel de uma ação selecionável.
- Poderes podem exigir afinidades ou pré-requisitos para entrar na build.

A documentação usa termos como arsenal, deck e mão, mas não estabelece de maneira consistente um ciclo completo de compra, descarte e reutilização. Não se deve concluir apenas dessas referências que o jogo desejado segue exatamente a circulação de cartas de Slay the Spire.

### 6.2. Raízes e poderes derivados

Uma revisão propõe duas camadas de identidade:

- **Raízes:** afinidades que habilitam famílias de poderes e concedem benefícios gerais.
- **Derivados:** ações ou manobras que aproveitam uma raiz compatível.

Essa proposta também contempla poderes independentes de raiz e poderes cuja vinculação é escolhida na aquisição. A escolha de vínculo é duradoura, com possíveis exceções concedidas por oportunidades especiais.

### 6.3. Duas formas distintas de melhorar um poder

- **Upgrade numérico:** aumenta atributos próprios do poder.
- **Transformação qualitativa:** muda a forma como o poder funciona.

A revisão de raízes acrescenta melhorias gerais de uma família e melhorias específicas de uma ação. Essas formas de investimento não são a mesma mecânica.

Fontes: `[F02]`, `[F08]`, `[F13]`, `[F17]`.

## 7. Aquisição, recompensas e raridade

### 7.1. Ofertas de poderes

- Aquisição por recompensa é gratuita, separada da compra em loja.
- Cada oferta pode ser aprendida, convertida em potencial de transformação ou substituída por uma nova oferta.
- A seleção detalhada do legado trata os slots como decisões independentes; não é necessariamente uma escolha única entre todas as cartas exibidas.
- É possível encerrar a seleção sem aproveitar todas as ofertas, na especificação de seleção.
- Oportunidades de nova oferta são limitadas e podem ser alteradas por benefícios da build.

### 7.2. Raridade e valor da oportunidade

- Raridade comunica frequência e valor esperado, não apenas aparência.
- Encontros mais arriscados favorecem recompensas de maior raridade.
- A raridade também pode afetar valor econômico e rendimento ao converter uma oferta.
- Origem ou benefícios adquiridos podem modificar a quantidade e as condições das ofertas.

### 7.3. Descoberta assistida de combos

Uma proposta garante a apresentação de um combo quando seus pré-requisitos são satisfeitos. O jogador pode aceitar essa oportunidade, convertê-la ou substituí-la; a combinação não é imposta.

Fontes: `[F02]`, `[F03]`, `[F08]`.

## 8. Ascensão e transformação de poderes

### 8.1. Investir no que já funciona

O núcleo revisado propõe consumir uma oferta indesejada para fortalecer qualitativamente um poder já adquirido. O objetivo emocional é escolher um foco de crescimento, não vender uma carta valiosa por uma moeda genérica.

- A transformação permanece durante a run.
- Nem todo poder aceita toda alteração.
- A quantidade de alterações deve preservar uma identidade reconhecível.
- A fonte de transformação deve permitir experimentação sem culpa excessiva, segundo a hipótese do núcleo revisado.

### 8.2. Famílias de transformação

- **Repetição:** realizar novamente uma ação e explorar suas reações associadas.
- **Múltiplos acertos:** dividir uma ação em impactos menores, valorizando efeitos por acerto.
- **Propagação:** estender parte do efeito a outros alvos, favorecendo confrontos em grupo.
- **Eficiência:** reduzir custo e abrir sequências de ações mais frequentes.
- **Mudança de identidade:** converter uma afinidade ou acrescentar uma afinidade híbrida.
- **Escolha de transformação pelo jogador:** definir uma propriedade no momento de aplicar a alteração, em vez de receber sempre um resultado pré-definido.

O GDD também menciona profundidade em execução, identidade, comportamento e regras de nível mais amplo. Nem todas essas camadas recebem uma definição de gameplay igualmente detalhada; a simples menção não constitui uma mecânica fechada.

### 8.3. Compatibilidade e consequências

- Alterações de ação não devem ser aplicadas indiscriminadamente a passivas.
- A alteração deve comunicar claramente seu efeito antes da decisão permanente.
- Repetições e múltiplos acertos podem interagir com crítico, aplicação de status, recuperação e reações de aliados.
- A compatibilidade é parte da identidade e do equilíbrio do poder, não apenas uma restrição de interface.

Fontes: `[F01]`, `[F02]`, `[F04]`, `[F17]`, `[F18]`.

## 9. Sinergias, combos e crescimento

### 9.1. Afinidades como linguagem da build

- Uma afinidade conecta poderes diferentes a benefícios compartilhados.
- Mudar ou acrescentar uma afinidade pode alterar quais bônus e reações um poder ativa.
- O jogador deve entender por que duas partes da build combinam.

### 9.2. Poderes catalisadores de crescimento

- Certos poderes ficam mais fortes conforme a build acumula poderes relacionados.
- Eles podem transformar uma direção apenas promissora em um crescimento exponencial.
- Sua função é provocar o reconhecimento de uma oportunidade, e não somente conceder dano adicional.
- O GDD propõe descoberta condicionada à composição da build; o núcleo descreve o encontro com esses poderes como catalisador de decisões futuras.
- A revisão de raízes propõe contar derivados de uma família, em vez de todos os poderes com determinada afinidade.

### 9.3. Combinação e fusão

Há duas propostas distintas:

- **Combo adicional:** cumprir pré-requisitos abre acesso a um novo poder ou uma nova árvore sem substituir as anteriores.
- **Fusão de raízes:** consumir raízes para criar uma identidade combinada, preservando a utilidade dos poderes derivados já adquiridos.

Também é mencionada a possibilidade de combinar combinações existentes. Seus limites e seu papel no jogo ainda não são detalhados de forma suficiente para fechar a regra.

Fontes: `[F01]`, `[F02]`, `[F04]`, `[F08]`, `[F14]`, `[F17]`.

## 10. Combate, turnos e recursos de ação

### 10.1. Agência e encadeamento

- O herói é controlado diretamente; o jogador escolhe ações e alvos.
- A proposta principal limita sequências por recursos disponíveis, em vez de impor uma única ação por turno.
- O jogador pode encadear poderes e depois encerrar voluntariamente sua participação no turno.
- A sequência descrita inclui ações do herói, reações imediatas dos aliados, fase autônoma dos aliados e ações dos inimigos.
- Variações de modo podem alterar quem inicia o combate.

### 10.2. Economia de energia

- Energia financia as ações do herói.
- A proposta de combate permite conservar energia entre turnos.
- Um ataque básico funciona como gerador de recurso e ação de baixo impacto.
- A geração desse ataque possui limitação para impedir financiamento ilimitado de ações.
- Passivas de comportamento podem substituir regeneração regular por geração condicionada a ações ou acontecimentos.

O conceito de comportamento cria uma segunda dimensão de build: além de escolher o que fazer, o jogador escolhe como financiar suas ações. O núcleo propõe apenas um comportamento desse tipo ativo por vez, com benefício e contrapartida claros.

### 10.3. Limites que ainda precisam de definição

O texto do combate permite repetir o ataque básico gratuitamente após esgotar sua geração. Limitar a geração de energia não limita, por si só, dano ou reações gratuitas repetidas. Portanto, a documentação não fecha satisfatoriamente como essa ação convive com a intenção de cadeias finitas.

A mesma lacuna afeta a proposta de encerrar automaticamente o turno quando não houver poderes pagáveis. Fontes: `[F01]`, `[F02]`, `[F05]`, `[F08]`.

## 11. Dano, defesa e status

### 11.1. Crescimento ofensivo

- Bônus que se somam e amplificações que se multiplicam representam investimentos diferentes.
- Combinar camadas distintas de crescimento é uma fonte intencional de sinergia.
- O jogador deve conseguir distinguir melhoria base, bônus circunstancial e amplificação da build.
- O objetivo é uma matemática compreensível o bastante para experimentar, sem exigir a complexidade integral das referências.

### 11.2. Críticos em múltiplos níveis

- Chance de crítico pode ultrapassar o limite de um crítico binário.
- O investimento adicional permite atingir níveis superiores de impacto.
- Múltiplos acertos possuem avaliações independentes de crítico na proposta detalhada.
- A apresentação deve tornar perceptível a escala atingida pela build.

### 11.3. Caminhos de sobrevivência

- Volume de vida, mitigação, escudo, esquiva e recuperação por dano são caminhos diferentes de sobrevivência.
- Escudo é uma reserva de proteção distinta da vida.
- Esquiva trabalha com risco de evitar completamente um impacto.
- Recuperação por dano depende do dano efetivo e possui contenção própria.
- Retornos decrescentes e limites defensivos buscam evitar invulnerabilidade trivial.
- A relação entre essas defesas deve produzir decisões de build, não tornar uma delas sempre superior.

### 11.4. Status como estratégia

- Status podem causar dano ao longo do tempo, modificar vulnerabilidades, reduzir defesa, restringir ações ou aumentar capacidades.
- Aplicações sucessivas podem acumular intensidade, renovar duração, estender duração ou substituir o efeito existente.
- O momento em que um status atua muda seu valor tático; o legado toma o início da ativação de seu portador como referência principal.
- Resistência a controle reduz o impacto de restrições de ação.
- Acúmulos elevados podem sofrer retornos decrescentes.
- Status não precisam compartilhar automaticamente as mesmas interações de crítico ou recuperação dos ataques diretos.

### 11.5. Condensação de dano periódico

Uma proposta permite consumir efeitos de dano periódico para transformar seu potencial restante em dano imediato. Isso cria uma decisão entre manter pressão ao longo do tempo e antecipar o resultado.

O efeito convertido preserva características relevantes do dano original; nem todo status precisa ser consumível dessa maneira.

Fontes: `[F01]`, `[F05]`, `[F07]`, `[F10]`, `[F20]`.

## 12. Companions, gambits e sincronia

### 12.1. Papel dos aliados

- Aliados expandem as possibilidades da build sem exigir que o jogador selecione manualmente cada ação de todos os combatentes.
- São adquiridos durante a jornada e podem cobrir necessidades que o herói não atende sozinho.
- O núcleo os coloca como camada secundária, opcional inicialmente e potencialmente importante nos desafios finais.
- A necessidade de aliados no fim do jogo aparece como hipótese, não como requisito definitivamente validado.

### 12.2. Gambits: comportamento por condições

- O jogador define relações de condição, ação e alvo.
- Condições podem observar vida, status, momento do combate ou ações do herói.
- Regras ordenadas permitem estabelecer prioridades.
- A especificação de combate usa a primeira regra elegível e deixa o aliado inativo quando nenhuma corresponde.
- Uma regra geral de último recurso pode ser escolhida pelo jogador; não é um comportamento obrigatório imposto.

### 12.3. Sincronia: provocar uma resposta

- Um aliado pode reagir imediatamente depois de uma ação compatível do herói.
- O jogador usa a ação própria também para provocar a resposta que deseja.
- A decisão de quando disparar uma reação depende da disponibilidade do aliado.
- Reações imediatas são distintas de decisões tomadas na fase regular dos aliados.
- A fantasia é orquestrar uma sequência, não assistir a efeitos automáticos sem relação legível com a ação escolhida.

### 12.4. Disponibilidade e derrota

- Há propostas concorrentes de disponibilidade limitada por energia própria ou por tempo de recarga.
- A proposta de recarga combina uma habilidade de identidade fixa com outra escolhida pelo jogador.
- Aliados derrotados deixam de agir e de reagir.
- O legado contempla sua restauração e não exige morte permanente como regra básica.

Fontes: `[F01]`, `[F02]`, `[F05]`, `[F06]`, `[F11]`.

## 13. Inimigos, informação e pressão tática

- Inimigos devem exigir respostas diferentes, explorando pressão ofensiva, resistência, controle, recuperação ou reação às ações do jogador.
- Quantidade de ações, padrões, condições e mudanças de fase podem diferenciar encontros.
- A progressão deve desafiar a construção da build e suas decisões de sobrevivência, não apenas aumentar números.
- A informação apresentada ao jogador é parte essencial da dificuldade.

O legado possui dois modelos de comunicação incompatíveis:

- **Intenção declarada:** mostrar a próxima ação antes da decisão do jogador, permitindo avaliar ataque, mitigação e recuperação.
- **Gatilhos declarados:** mostrar regras de reação e condições do inimigo, sem revelar a próxima ação específica.

Também coexistem seleção probabilística de ações e padrões sequenciais. Isso deve ser definido no novo GDD, sem presumir que uma interface substitui automaticamente a outra. Fontes: `[F05]`, `[F13]`, `[F15]`, `[F20]`.

## 14. Economia, loja e preparação

### 14.1. Papéis econômicos separados

- Ouro financia oportunidades comerciais, melhorias e serviços.
- O recurso de transformação financia mudanças qualitativas nos poderes existentes.
- Aprender uma recompensa não equivale a gastar esse recurso para comprar um poder.
- A economia da run cria decisões entre ampliar opções e aprofundar uma direção já escolhida.
- O núcleo revisado questiona concentrar toda a escassez no recurso que permite transformar a build.

### 14.2. Loja

- A loja permite buscar oportunidades fora das ofertas gratuitas de combate.
- Compras podem ampliar a build, melhorar poderes, adquirir aliados ou restaurá-los.
- Substituir o estoque inteiro é uma decisão distinta de substituir uma oferta individual de recompensa.
- Custos crescentes de novas tentativas limitam a busca pela oportunidade perfeita.
- Benefícios de origem ou da build podem alterar condições comerciais.
- A inclusão de relíquias permanentes na run aparece como expansão, não como parte igualmente consolidada em todos os documentos.

### 14.3. Preparação

- Entre combates, o jogador revisa poderes, aplica transformações e configura aliados.
- A mesma oportunidade de preparação pode estar disponível em diferentes pontos da jornada.
- Durante o combate, a proposta de preparação permite consulta, mas não reconfiguração livre da build.
- Compatibilidade, custo e permanência devem estar claros antes de confirmar uma alteração.

Fontes: `[F01]`, `[F02]`, `[F04]`, `[F12]`, `[F16]`.

## 15. Eventos e decisões fora do combate

- Eventos são pausas de decisão que variam o ritmo da run.
- Escolhas oferecem contrapartidas entre recursos, risco, vantagens imediatas e consequências futuras.
- Deve existir uma alternativa segura ou de menor risco, sem tornar uma opção universalmente superior.
- A proposta comunica o tipo de consequência; certos resultados podem conservar incerteza sobre sua magnitude.
- Origem, situação da build e acontecimentos anteriores podem abrir opções diferentes.
- Eventos posteriores podem responder às escolhas feitas anteriormente.
- Textos devem apoiar a decisão sem transformá-la em uma longa exposição obrigatória.
- A especificação de eventos reserva a morte direta para o combate, evitando que uma perda de vida fora dele encerre inesperadamente a tentativa.

Fontes: `[F14]`, `[F19]`.

## 16. Vitória, derrota e meta-progressão

### 16.1. Resultado da tentativa

- Vitória de combate concede recompensas e abre continuidade na jornada.
- Derrota do herói encerra a run na regra básica descrita pelo combate.
- Uma proposta adicional contempla retornos limitados após a derrota, com identidade própria e efeitos associados.
- O resultado deve explicar desempenho, progresso alcançado, contribuição da build e causa da derrota.
- Uma tentativa malsucedida ainda deve ensinar algo e estimular outra combinação.

### 16.2. Desbloqueios por feitos

- Realizações mecânicas e estilos de jogo podem desbloquear novas possibilidades para runs futuras.
- O crescimento entre tentativas amplia o espaço de descoberta.
- Os textos extraídos não fecham uma política geral de aumentos permanentes de poder; desbloqueios de possibilidades não devem ser confundidos com essa decisão.

### 16.3. Memória do mundo

- Cada run pode representar um herói diferente em um período histórico.
- Tentativas comuns são registradas como experiência; feitos marcantes podem entrar para a história particular do jogador.
- Nem toda tentativa precisa se tornar um acontecimento canônico.
- O mundo pode reconhecer heróis anteriores por nome ou legado.
- Decisões históricas podem alterar oportunidades e situações em runs futuras.
- Desbloqueios mecânicos e consequências históricas são formas distintas de progressão.

### 16.4. Eras

- Períodos históricos definem contexto e oportunidades disponíveis.
- Novos períodos são desbloqueados por progressão.
- Quando houver mais de um disponível, o jogador pode escolher em qual iniciar.
- A intenção é construir um legado através de várias tentativas, não apenas repetir uma partida isolada.

Fontes: `[F02]`, `[F11]`, `[F14]`, `[F19]` e fontes complementares de meta-progressão e narrativa indicadas em §20.

## 17. Experiência do jogador, clareza e aprendizado

- Apresentar profundidade gradualmente, evitando sobrecarga inicial.
- Manter legíveis recursos, fase atual, estado dos combatentes e possibilidades de ação.
- Diferenciar ações ativas, passivas, alterações permanentes e estados temporários.
- Mostrar custo e consequência antes de comprometer uma escolha.
- Explicar por que uma ação não está disponível.
- Permitir consultar regras de aliados e inimigos sem exigir memorização.
- Exibir o valor atual de uma sinergia e o que está contribuindo para ela.
- Tornar visível a relação entre uma ação do herói e uma reação de aliado.
- Comunicar a escala de impactos fortes sem perder a leitura do combate.
- Preservar espaço para o campo de batalha; detalhes secundários podem aparecer sob demanda.
- Permitir reduzir ou desativar efeitos visuais intensos sem perder informação de gameplay.
- Ao final da run, apresentar informações que ajudem o jogador a interpretar suas decisões.

Esses são objetivos de experiência, não uma escolha obrigatória de layout, menu radial ou estilo visual. Fontes: `[F01]`, `[F02]`, `[F11]`, `[F13]`, `[F16]` e fonte complementar de acessibilidade indicada em §20.

## 18. Modos e longevidade

- **Run principal:** experiência de descoberta, risco e progressão.
- **Sandbox:** exploração livre de builds, recursos facilitados e encontros ajustáveis, sem alimentar conquistas ou desbloqueios da experiência principal na proposta do legado.
- **Infinito:** continuar após concluir a run, com pressão crescente e foco em recorde pessoal.
- **Desafios e variantes:** experiências com condições diferentes, como sequência de confrontos especiais ou alteração de quem age primeiro.
- **Desafio diário:** ponto de partida compartilhado para comparar decisões sob condições comuns.
- **Runs compartilháveis:** uma identificação permite revisitar oportunidades de uma tentativa; escolhas diferentes podem produzir resultados diferentes.
- **Modos personalizados:** outras condições de jornada podem coexistir sem redefinir a identidade da experiência principal.

A reprodutibilidade é descrita no legado como mesmas condições iniciais e mesmas escolhas produzindo a mesma run. O identificador sozinho não determina as decisões do jogador. Fontes: `[F01]`, `[F02]`, `[F14]` e fonte complementar de seeds indicada em §20.

## 19. Divergências e questões ainda abertas

### 19.1. Propostas incompatíveis ou não unificadas

| Tema | Propostas encontradas | Fontes |
|---|---|---|
| Emoção da conversão de ofertas | Sacrifício escasso com tensão de perda versus investimento relativamente abundante que evita arrependimento. | F01, F02 |
| Destino da oferta convertida | Gerar recurso genérico de transformação versus alimentar o crescimento de um poder escolhido; o segundo texto não fecha todo o fluxo econômico. | F01, F02, F03 |
| Fontes do recurso de transformação | Exclusivamente conversão de ofertas versus bônus de nós e outras recompensas. | F02, F03, F14, F19 |
| Influência da origem | Identidade mecânica forte versus ponto de partida leve, subordinado à build. | F01, F09 |
| Limite de transformações | Um modificador por poder versus múltiplas camadas e menções a modificadores combinados. | F01, F02, F04 |
| Repetição de poder | Repetição apenas no primeiro uso do combate versus repetição de cada execução elegível. | F02, F04 |
| Piso de custo | Preservar custo positivo versus permitir custo zero. | F02, F04, F05 |
| Modelo de combo | Acrescentar poder/árvore versus fundir e consumir raízes anteriores. | F02, F04, F17 |
| Base de crescimento por sinergia | Contagem de poderes por afinidade versus contagem de derivados de uma raiz. | F01, F08, F17 |
| Disponibilidade dos aliados | Energia própria versus habilidades limitadas por tempo de recarga e slots de função distinta. | F01, F05, F06 |
| Informação dos inimigos | Próxima intenção explícita versus apenas gatilhos e regras de reação. | F05, F13, F15 |
| Escolha de ações inimigas | Seleção probabilística condicionada versus sequência de ações. | F05, F15 |
| Mitigação | Regras diferentes para herói e inimigos versus armadura com a mesma natureza para todos. | F07, F10 |
| Resistência a controle | Duração mínima garantida versus possibilidade de negar completamente a aplicação. | F05, F10 |

### 19.2. Lacunas de definição e hipóteses para validação

- O jogo usa um arsenal de ações disponíveis ou uma circulação de cartas com compra, descarte e reutilização? Quais limites de uso existem por turno e por combate?
- Como ações gratuitas e reações são limitadas sem eliminar sequências fortes e interessantes?
- Quantas oportunidades de aquisição e transformação produzem sensação suficiente de construção de build?
- A transformação estimula investimento natural ou cria acumulação por receio de escolher o alvo errado?
- Qual frequência de ofertas altamente desejáveis preserva descoberta sem recriar arrependimento constante?
- Como a propagação determina alvos, e quais efeitos além de dano ela transporta?
- Como escudo, mitigação e recuperação interagem nas diferentes estratégias defensivas?
- Companions permanecem opcionais em todo o jogo ou passam a ser necessários em certos desafios?
- Sincronia é percebida como controle estratégico ou como ruído difícil de acompanhar?
- O crescimento extremo gera variedade satisfatória ou concentra todas as runs em uma direção dominante?
- Retornos após derrota são exceções conquistadas ou parte comum da experiência?
- Como escolhas históricas incompatíveis de runs diferentes alteram a memória do mundo?
- Quais limites separam exploração livre, modos alternativos e progressão principal?
- Qual nomenclatura comunica melhor a fantasia de transformar um poder?

Não são decisões novas nem um plano de implementação. São pendências explícitas nas fontes ou lacunas identificadas ao confrontar seus conceitos.

### 19.3. Critérios de sucesso presentes no legado

- Toda origem deve ter um caminho viável, sem exigir força idêntica entre elas.
- Cada poder deve ter um contexto de utilidade, evitando escolhas sempre obrigatórias ou sempre inúteis.
- Derrotas devem ser compreensíveis, sem depender de informação essencial escondida.
- Um jogador experiente deve melhorar pela leitura das oportunidades e pelo domínio das decisões.
- A dificuldade deve vir de pressão, risco e escolhas; falta de informação e encontros excessivamente longos não são substitutos adequados.
- O teto de expressão deve continuar alto sem impedir o aprendizado inicial.

Fonte: `[F20]`.

## 20. Fontes e rastreabilidade

A extração concentra-se nos documentos de design ativos do legado. Documentos em pastas técnicas foram consultados apenas quando continham regras ou objetivos de gameplay. Catálogos foram usados somente para identificar princípios gerais, sem reproduzir seu conteúdo. Material arquivado, código e planejamento de execução não foram usados como direção de produto.

| Referência | Documento legado | Conceitos utilizados |
|---|---|---|
| F01 | [game-design-core.md](C:/Users/usuario/.gemini/antigravity/scratch/hero-script/game-design-core.md) | Fantasia, pilares, descoberta, Ascensão, crescimento, aliados secundários e perguntas abertas. |
| F02 | [GDD_Overview.md](C:/Users/usuario/.gemini/antigravity/scratch/hero-script/docs/02_GameDesign/GDD_Overview.md) | Visão ampla, loop, economia, recompensas, combos, experiência e modos. |
| F03 | [CardSelectionSystem.md](C:/Users/usuario/.gemini/antigravity/scratch/hero-script/docs/02_GameDesign/Sistemas/CardSelectionSystem.md) | Decisões por oferta, novas ofertas, raridade e oportunidades de combo. |
| F04 | [ScriptModifierSystem.md](C:/Users/usuario/.gemini/antigravity/scratch/hero-script/docs/02_GameDesign/Sistemas/ScriptModifierSystem.md) | Alterações qualitativas, compatibilidade e interações. |
| F05 | [CombatLoop.md](C:/Users/usuario/.gemini/antigravity/scratch/hero-script/docs/02_GameDesign/Sistemas/CombatLoop.md) | Ações, recursos, fases, gambits, sincronia e comunicação de inimigos. |
| F06 | [Companions.md](C:/Users/usuario/.gemini/antigravity/scratch/hero-script/docs/02_GameDesign/Mecanicas/Companions.md) | Autonomia, recarga, habilidades equipáveis e restauração. |
| F07 | [Atributos.md](C:/Users/usuario/.gemini/antigravity/scratch/hero-script/docs/02_GameDesign/Mecanicas/Atributos.md) | Caminhos defensivos, recursos, crítico e natureza dos upgrades. |
| F08 | [Powers.md](C:/Users/usuario/.gemini/antigravity/scratch/hero-script/docs/02_GameDesign/Mecanicas/Powers.md) | Tipos de poderes, comportamentos, crescimento e combos, sem catálogo. |
| F09 | [Races.md](C:/Users/usuario/.gemini/antigravity/scratch/hero-script/docs/02_GameDesign/Mecanicas/Races.md) | Identidade de origem e acesso às possibilidades do jogo, sem catálogo. |
| F10 | [StatusSystem.md](C:/Users/usuario/.gemini/antigravity/scratch/hero-script/docs/02_GameDesign/Sistemas/StatusSystem.md) | Status, acúmulo, duração, controle e condensação. |
| F11 | [CombatResolution.md](C:/Users/usuario/.gemini/antigravity/scratch/hero-script/docs/02_GameDesign/Mecanicas/CombatResolution.md) | Resultados, retornos após derrota, aliados derrotados e aprendizado pós-run. |
| F12 | [ShopSystem.md](C:/Users/usuario/.gemini/antigravity/scratch/hero-script/docs/02_GameDesign/Sistemas/ShopSystem.md) | Oportunidades econômicas, serviços, estoque e expansão por relíquias. |
| F13 | [ArtBible.md](C:/Users/usuario/.gemini/antigravity/scratch/hero-script/docs/03_Arte/ArtBible.md) | Apenas legibilidade, informação de combate e consulta de detalhes. |
| F14 | [RunSystem.md](C:/Users/usuario/.gemini/antigravity/scratch/hero-script/docs/05_Tecnico/Sistemas/RunSystem.md) | Percurso, segmentos, tipos de oportunidade, variantes e eras. |
| F15 | [EnemyIntentSystem.md](C:/Users/usuario/.gemini/antigravity/scratch/hero-script/docs/05_Tecnico/Sistemas/EnemyIntentSystem.md) | Intenções declaradas e padrões de ação. |
| F16 | [PreparationSystem.md](C:/Users/usuario/.gemini/antigravity/scratch/hero-script/docs/05_Tecnico/Sistemas/PreparationSystem.md) | Preparação fora de combate, consulta e decisões permanentes. |
| F17 | [PowerSystemRework.md](C:/Users/usuario/.gemini/antigravity/scratch/hero-script/docs/09_ImplementationPlan/PowerSystemRework.md) | Apenas decisões de design sobre raízes, derivados, fusão e afinidades. |
| F18 | [dynamic_script_modifiers.md](C:/Users/usuario/.gemini/antigravity/scratch/hero-script/docs/05_Tecnico/Sistemas/dynamic_script_modifiers.md) | Apenas transformação com propriedade escolhida pelo jogador. |
| F19 | [EventSystem.md](C:/Users/usuario/.gemini/antigravity/scratch/hero-script/docs/05_Tecnico/Sistemas/EventSystem.md) | Escolhas fora de combate, contrapartidas e consequências futuras. |
| F20 | [BalanceamentoEPlaytesting.md](C:/Users/usuario/.gemini/antigravity/scratch/hero-script/docs/06_Producao/BalanceamentoEPlaytesting.md) | Apenas objetivos de equilíbrio, aprendizado e dificuldade. |

Fontes complementares:

- [MetaProgressionSystem.md](C:/Users/usuario/.gemini/antigravity/scratch/hero-script/docs/02_GameDesign/Sistemas/MetaProgressionSystem.md): desbloqueios, história persistente, eras e conflitos ainda abertos.
- [game-lore-canon.md — seção 10](C:/Users/usuario/.gemini/antigravity/scratch/hero-script/docs/08_Lore/game-lore-canon.md): apenas o conceito de legado entre runs, sem importar a ambientação ou os exemplos narrativos.
- [SeedSystem.md](C:/Users/usuario/.gemini/antigravity/scratch/hero-script/docs/05_Tecnico/Sistemas/SeedSystem.md): runs compartilháveis e condições comuns de desafio.
- [damage_calculation_bucket_pipeline.md](C:/Users/usuario/.gemini/antigravity/scratch/hero-script/docs/05_Tecnico/Sistemas/damage_calculation_bucket_pipeline.md): apenas distinção entre formas de crescimento e camadas de defesa, sem estrutura técnica.
- [SistemasDeInfraestrutura.md](C:/Users/usuario/.gemini/antigravity/scratch/hero-script/docs/02_GameDesign/Sistemas/SistemasDeInfraestrutura.md): apenas acesso à informação sem depender de efeitos visuais intensos.

## 21. Síntese do núcleo recorrente

O legado descreve um jogo de descobrir e transformar uma build durante uma jornada de encontros, usando afinidades para conectar poderes, formas de crescimento e aliados autônomos. O combate deve permitir explorar essas relações por decisões diretas e sequências expressivas. Cada tentativa alimenta a vontade de experimentar novamente; uma camada adicional pode transformar feitos marcantes em desbloqueios e memória do mundo.

Esse núcleo é a base conceitual extraída. As regras contraditórias e as hipóteses listadas acima continuam abertas para a definição do jogo desejado.
