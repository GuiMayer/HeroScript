# HeroScript — pesquisa e plano de evolução do core de builds

Data: 21 de novembro de 2026. Escopo: fluxos jogáveis de Alisyum e do MVP `prototype-test`. Referência técnica: HEAD `88314309`, com alterações locais anteriores também presentes nos arquivos auditados. Este é um plano de produto, não uma aprovação de balanceamento nem uma implementação.

## 1. Diagnóstico executivo

**A fantasia central precisa ser fabricar uma máquina de combate pessoal e descobrir como quebrar as regras com ela.** O jogo não precisa de ilustrações para isso. Precisa de decisões significativas, sinergias emergentes, poder perceptível e um ciclo rápido de descoberta, aplicação e recompensa.

Hoje a hipótese mais forte é que HeroScript mostra primeiro um deckbuilder convencional e só mais tarde permite experimentar aquilo que o diferencia. Há infraestrutura de composição, cálculo, transformações, respostas de eventos, status, progressão, replay, previews e simulação. Entretanto, a quantidade de funcionalidades técnicas não mede a qualidade das experiências que elas criam. É possível passar todos os testes e continuar jogando turnos pouco interessantes.

O objetivo não é equilibrar cada combinação até que todas causem dano idêntico. É oferecer ferramentas que façam atacar, defender, comprar cartas, pagar custos e explorar estados de formas radicalmente diferentes. Uma build excelente deve conseguir trivializar uma parte da run quando suas peças se encontram. A construção e o uso dessa build precisam envolver escolhas.

Este diagnóstico separa fatos verificáveis do conteúdo atual, hipóteses de causa do tédio e propostas experimentais. Não foi realizado um novo playtest humano para este relatório. O feedback do autor sobre o jogo é a evidência de experiência que orienta a pesquisa.

## 2. Auditoria do conteúdo e do fluxo

Contagem das definições explícitas em `data/configs/alisyum/Resources` e `data/configs/prototype-test/Resources`. Não representa necessariamente todos os objetos herdados, nem tudo que está acessível em uma única partida.

| Sistema | Alisyum | MVP prototype-test |
| --- | ---: | ---: |
| Cartas | 122 (30 por classe, 2 básicas) | 15 |
| Modificadores de carta / upgrades | 6 / 6 | 3 / 3 |
| Inscrições de personagem | 28 | 8 |
| Relíquias | 6 | 0 no package |
| Runs declaradas | 4 | 2 |

### 2.1 O gargalo é a variedade combinatória acessível

As seis runas de Obor são Eco Duplo, Escudo Gravado, Entropia Perfurante, Lâmina Afiada, Brasa Gravada e Sifão Escarlate. Há três upgrades de afinidade e três de comportamento. Todos os 122 modelos de carta têm duas categorias de slots declaradas (afinidade e comportamento); isso não significa que todos possam receber qualquer runa. A elegibilidade hoje está expressa principalmente em listas explícitas de IDs.

| Elegibilidade declarada em card-upgrades.json | Cartas |
| --- | ---: |
| Sem upgrade elegível | 15 |
| Apenas um upgrade | 53 |
| Dois ou mais | 54 |
| Total | 122 |

Portanto 68 de 122 (55,7%) têm zero ou apenas um modificador explícito elegível, antes de considerar slots ocupados, recursos e disponibilidade na rota. Cada classe começa com dez cartas, quatro delas cópias de Golpe de Aço e Guarda Básica, que não aparecem nas listas de elegibilidade dos seis upgrades. No Piromante, Ash Carapace também não aparece e Detonate Chest e Kindle Flame têm apenas um upgrade declarado cada. A carta mais interessante para transformar nem sempre é a carta que o sistema oferece transformar.

Os upgrades atuais são úteis, mas vários acrescentam valores ou efeitos fixos: 4 Block, 3 de cura, bônus crítico ou Burn. Eco Duplo muda o comportamento do golpe e abre espaço para interações por acerto. Já a lista de mods não explora na mesma escala os verbos de reciclagem, distribuição, conversão, consumo, preparação, resposta e finalização que a própria engine e algumas cartas possuem.

Importante: vinte e oito inscrições não equivalem a vinte e oito escolhas iniciais. A política de capacidade 8, sete slots tipados, inventário, classe, estações e recursos restringe o conjunto legal. As identidades avançadas do Piromante, Servo, Apostador e Renegado já têm gatilhos interessantes. Isso é uma fundação aproveitável, não motivo para reescrever a engine.

### 2.2 O diferencial chega tarde demais

As quatro runs de Alisyum têm vinte nós declarados cada, com caminhos alternativos. São quatro encontros numa rota típica completa (cinco nós de encontro no grafo, pois o segundo encontro é uma bifurcação).

Fluxo do Piromante, simplificado:

```text
Diálogo → Preparação / identidade → Combate 1 → Escolha de carta
→ Combate 2 (patrulha ou elite) → Escolha de carta
→ Relicário / eventos → Bazar → Fogueira OU Forja OU Arsenal
→ Combate 3 → Escolha de carta → Chefe → Encerramento
```

A primeira oportunidade explícita de comprar runas de carta nas lojas ou adquiri-las na forja aparece depois de dois combates. A forja consome três fragmentos, concorre na rota com repouso e arsenal, e não permite remoção ou substituição naquele nó. O jogador já teve várias oportunidades de ver ataques básicos antes de acessar o principal diferencial. As recompensas iniciais oferecem três cartas da classe e uma escolha, com reroll pago em fragmentos e opção de decompilar; esses fragmentos também são necessários para a forja.

Hipótese: a economia e a rota valorizam o acesso tardio ao sistema que deveria ser a fonte de entusiasmo desde o primeiro encontro. A build seria percebida como um apêndice do deckbuilder. A oportunidade inicial de equipar identidade ajuda, mas não substitui transformar uma carta e imediatamente usá-la.

### 2.3 Combate e complexidade

- Alisyum começa com mão de cinco e energia máxima inicial de três no Piromante. O sistema move todas as cartas possuídas para o baralho no início do encontro. Comprar mais cartas pode diluir peças essenciais, se a aquisição não vier acompanhada de ferramentas de consistência ou escolha.
- Há design promissor nas cartas existentes: `pyro_ash_phoenix` compra ao consumir Burn; `pyro_chain_eruption` prepara, detona e espalha; `pyro_infernal_echo` retorna ao topo; o Renegado converte Block em dano; o Apostador acumula críticos em tiers; o Servo troca vida por dano e usa dreno. A apresentação precisa mostrar os elos dessas interações, e a progressão precisa permitir montá-las cedo.
- Alguns gambits descrevem golpes de 7, golpes pesados em múltiplos de três rodadas, Block em limiar de vida e escalada tardia. A existência de padrões não prova que os encontros sejam interessantes. É necessário medir se eles fazem o jogador mudar ordem, alvo, timing ou investimento. Nunca confundir a cobertura de gambits legais com qualidade de desenho de inimigos.
- No conteúdo atual ainda existem cartas que declaram gastar Calor (`pyro_runic_venting`, custo alternativo de `pyro_thermal_crush`), embora o autor tenha pedido que Calor seja tratado como contador. Validar se esses consumos devem continuar como manipulação do contador, não como moeda primária. Conferir regras efetivas em preview e descrição; não alterar silenciosamente.
- O roteiro `docs/validation/TACTICAL_CAMPAIGN_MVP_PLAYTEST.md` permanece identificado como não realizado. O relatório de balanceamento anterior do MVP mede cartas isoladas, não a experiência completa da run. Precisamos medir a composição e o ciclo, não repetir apenas ranking de dano por carta.

## 3. Pesquisa de game design

### 3.1 Mecânicas, dinâmica e experiência

O framework MDA de Hunicke, LeBlanc e Zubek distingue regras, comportamento produzido pela combinação das regras e experiência vivida. O jogo deve ser projetado na direção inversa: definir a descoberta e o domínio que se quer produzir, identificar as dinâmicas necessárias e então escolher mecânicas. [MDA, paper original](https://www.cs.northwestern.edu/~hunicke/pubs/MDA.pdf).

Para HeroScript, uma carta que aplica Burn é uma mecânica; empilhar e detonar Burn antes que o inimigo morra é uma dinâmica; descobrir que uma runa permite sustentar uma cadeia de detonações é a experiência desejada. Adicionar dez versões de uma carta de Burn sem mudar a dinâmica pode aumentar o catálogo sem aumentar a diversão.

### 3.2 Balatro: permitir escaladas memoráveis

O diário de desenvolvimento de LocalThunk registra experimentação de sistemas, descarte de ideias que não funcionaram e a importância de ouvir jogadores da demo. O valor da referência está na combinação de cartas familiares com peças que reinterpretam suas propriedades e recompensas com efeitos cumulativos. [The Balatro Timeline](https://localthunk.com/blog/balatro-timeline-3aarh).

Aplicação: cada run de HeroScript deve conter momentos em que uma nova peça reinterpreta as anteriores. Uma runa de ataque com mais 2 de dano é um upgrade; uma runa que permite o ataque disparar quando um efeito residual termina pode tornar várias cartas anteriormente comuns valiosas. O benefício memorável deve ser visível no resultado, não escondido em uma fórmula.

### 3.3 Magic: sinergias abertas e complexidade em camadas

Mark Rosewater distingue designs que empurram cartas para um grupo estreito e designs modulares que se conectam com conjuntos mais amplos. Também descreve design lenticular: regras simples de ler podem carregar implicações estratégicas que aparecem com experiência. [Come Together](https://magic.wizards.com/en/news/making-magic/come-together-2003-10-06-0); [Lenticular Design](https://magic.wizards.com/en/news/making-magic/lenticular-design-2014-03-31).

Aplicação: organizar mods por verbos e eventos comuns (acerto, crítico, carta comprada, descarte, consumo, defesa, abate). Uma peça deve encontrar vários produtores e consumidores, não ter texto exclusivo para uma única carta. Manter identidade de classe com motores especializados, mas permitir peças-ponte. A compreensão básica da carta não deve exigir ler a árvore inteira de cálculos.

### 3.4 Warframe: escassez gera autoria

O guia oficial de mods descreve capacidade compartilhada, drenagem e diferenças de polaridade. Mods de aumento alteram comportamento de habilidades, além de força numérica. [Warframe — guia oficial de mods](https://support.warframe.com/hc/en-us/articles/200500194-Mod-Guide-Use-Fusion-Transmutation-Sale).

Aplicação: HeroScript já tem slots e capacidade, portanto não precisa copiar a polaridade nem adicionar outro recurso. Usar os limites existentes para opor, por exemplo, eficiência de energia, repetição e recuperação. É importante poder experimentar uma montagem sem transformar cada erro de alocação em uma run perdida. O compromisso permanente pode existir depois que o jogador vê claramente seu efeito.

### 3.5 Slay the Spire: medir escolhas, não apenas vitórias

Anthony Giovannetti apresentou o processo de balanceamento orientado por dados e feedback durante Early Access. [Palestra GDC](https://www.gdcvault.com/play/1025731); [slides do autor](https://media.gdcvault.com/gdc2019/presentations/Giovannetti_Anthony_SlayTheSpire.pdf).

Aplicação: observar o que foi oferecido, escolhido, ignorado, equipado, aplicado, realmente usado e com quais outras peças. Taxa de escolha sem denominador de ofertas não tem significado. Taxa de vitória sofre viés de raridade, habilidade, etapa e composição. Não nerfar uma combinação só porque funciona: verificar acesso, custo de oportunidade, risco e se resta variedade de estratégias.

### 3.6 Into the Breach: inimigos como problemas legíveis

O postmortem da Subset Games explica como ataques comunicados antecipadamente orientaram as escolhas de design do combate. [Into the Breach Design Postmortem](https://media.gdcvault.com/gdc2019/presentations/Into%20the%20Breach%20Postmortem%20Final.pdf).

Aplicação: preservar incerteza onde faz parte do sistema de gambits, mas comunicar o que é conhecido e o que pode mudar. Um inimigo deve tornar o momento de consumir status, acumular Block ou aceitar dano uma decisão. Uma barra de vida maior não cria automaticamente um desafio novo.

### 3.7 Juice sem arte

Martin Jonasson e Petri Purho demonstram como feedback visual e sonoro pode enriquecer uma mecânica simples; Kelly Folmer apresenta o contraponto de que polimento sem contexto pode distrair. [Juice It or Lose It](https://gdcvault.com/play/1016487/Juice-It-or-Lose); [Don't Juice It or Lose It](https://gdcvault.com/play/1021398/Don-t-Juice-It-or).

Aplicação: o pico visual e sonoro deve acompanhar as decisões importantes. O jogo pode manter peças abstratas, tipografia, ícones, linhas, cores e formas geométricas. O feedback dá destaque ao comportamento, não é substituto dele.

## 4. Pilares propostos em ordem de prioridade

| Pilar | O jogador deve experimentar | Alteração prioritária |
| --- | --- | --- |
| Descoberta imediata | “E se eu colocar isso nesta carta?” | Primeira transformação antes do primeiro encontro, ou logo após uma abertura muito curta |
| Composição sistêmica | “Essas três peças fazem algo que nenhuma faz sozinha.” | Mods transversais, produtores, conversores e recompensas |
| Autoria e progressão | “Esta carta é minha; decidi como ela funciona.” | Slots claros, comparação e evolução em etapas, concorrência entre ofertas |
| Combate expressivo | “Posso executar a cadeia agora ou esperar?” | Ameaças com hierarquia, decisões de ordem e alvo, economia dinâmica |
| Clareza e causalidade | “Entendi por que cheguei a esse resultado.” | Preview de fatos, destaques de fonte e timeline compacta |
| Ritmo e recompensa | “Quero experimentar a próxima peça.” | Menos telas intermediárias, resposta rápida, clímax proporcional à cadeia |

Arte final, novas classes, aumento indiscriminado de cartas, progressão permanente extensa e redesign completo da HUD não resolvem os primeiros quatro pilares. Deixar fora da primeira fase.

## 5. Gramática de builds: mods de personagem, cartas e mods de carta

### 5.1 Papéis distintos e complementares

- **Identidade do personagem:** define incentivo recorrente e a forma de crescer durante cada combate. Não é apenas uma coleção de multiplicadores.
- **Equipamentos e runas do personagem:** conectam classes de ações, criam conversão, alteram limites ou concedem gatilhos gerais.
- **Cartas:** são verbos táticos com custo, alvo, timing e papel reconhecíveis.
- **Afinidade de carta:** acrescenta ou transforma consequência temática, usando o slot já existente quando compatível.
- **Mod de comportamento da carta:** muda como a ação ocorre: repetição, distribuição de efeitos, retorno, condição, consumo ou alvo.
- **Relíquia:** altera uma regra suficientemente ampla para motivar mudar a construção ou a rota.

Não estabelecer um quarto inventário nem uma nova árvore de habilidades. Testar primeiro a transformação dos sistemas existentes.

### 5.2 Famílias de mods candidatas

Os nomes e valores abaixo descrevem experimentos, não funcionalidades certificadas. Implementação deve começar verificando as operações suportadas pelas definições, cálculo real, patch de componentes e respostas de eventos. Um novo contrato de engine requer prova de que não há composição com contratos existentes.

| Família | Exemplo | Cria sinergia com |
| --- | --- | --- |
| Repetição | Eco: divide o ataque em dois impactos com potência distribuída, preservando gatilhos por acerto | Crítico, dano residual, dreno, buffs por acerto |
| Geração | Brasa/Geada/Sangria: ataque compatível deixa o residual definido | Detonadores, afinidades, conversão de status |
| Condensação | Ruptura: permite consumir residual do alvo na ação | Aplicadores, bônus por consumo, alvo prioritário |
| Propagação | Transbordo: excedente de finalização salta para outro alvo | Multihit, finalizadores, efeitos ao abater |
| Conversão | Lastro: parte da defesa acumulada alimenta a ação ofensiva | Escudo Gravado, builds defensivas, preservação de Block |
| Reciclagem | Retorno: sob condição, a carta reaparece no topo ou retorna do descarte | Compra, desconto, exílio, combo |
| Economia | Catalisador: devolve energia uma vez quando a ação satisfaz condição explícita | Custo de oportunidade, recuperação e ações gratuitas |
| Defesa ativa | Guarda reativa: protege e pune o próximo impacto recebido | Block, vulnerabilidade, preparação de próximo turno |
| Temporal | Suspensão: adia parte do resultado e torna a próxima ação mais valiosa | Retenção, residual, planejamento |
| Precificação | Aposta: troca custo e previsibilidade por teto crítico maior | Dano crítico, muitos impactos, mitigação de risco |

Começar com 6–8 famílias efetivamente combináveis e 12–16 mods totais como teto experimental, incluindo os seis atuais após revisão. Não criar versões I/II/III para inflar variedade. Priorizar o que proporciona interações distintas. Uma regra de compatibilidade por tags/capabilidades de efeitos, caso o contrato suporte, é preferível a manter centenas de IDs manualmente; validar compatibilidade de cada patch antes de usar. Toda carta inicial básica deve ter ao menos uma trajetória de transformação coerente.

### 5.3 Quatro builds demonstradoras

As setas mostram as interações pretendidas. Alguns componentes já existem, outros representam propostas que precisam de implementação e ensaio.

| Build | Sequência possível | Escolha interessante |
| --- | --- | --- |
| Fornalha encadeada | Magma Slash com Eco → aplica residual por regra definida → condensação → Ash Phoenix compra → próxima ação | Detonar cedo para matar antes do golpe pesado ou investir mais um turno e ampliar a cadeia? |
| Hemomancia vampírica | Blood Pact troca vida por ataque → identidade gera Dízimo e dreno → mod de retorno ou economia → recuperação | Quanto de vida comprometer e quando transformar sustentação em explosão? |
| Cascata crítica | Loaded Toss com Eco → múltiplos críticos acumulam bônus → efeito de recompensa de crítico → All In / finalizador | Construir chance garantida, aumentar o multiplicador ou obter mais tentativas? |
| Fortaleza cinética | Block com mod de guarda → identidade reage ao ataque → Crystal Bash ou Diamond Ram converte Block → resultado gera nova defesa | Conservar Block para sobreviver ou converter em dano no momento certo? |

Adicionar cruzamentos possíveis, mesmo sem misturar todas as cartas de classes: Escudo Gravado pode beneficiar gelo e sangue; Eco pode beneficiar crítico, dano por acerto e efeitos de fogo; reciclagem pode servir tanto uma carta de setup quanto um finalizador. Cada peça transversal deve ter pelo menos duas aplicações úteis e cada motor principal, mais de uma forma de iniciar e concluir.

### 5.4 Quebrar a escala, preservar a execução

Exemplo hipotético: ação de base 6, um impacto; duas melhorias numéricas de +20% no mesmo bucket resultam em 8,4 antes de arredondar. Uma versão que distribui 6 em três impactos e libera 4 de efeito secundário por impacto atinge 18 unidades totais (ignorando armadura, arredondamento, custo e outros fatores). Um produtor e um consumidor podem multiplicar o valor sem simplesmente inflar a base. A engine é quem confirma os valores efetivos e o escopo dos gatilhos, não a UI.

Permitir construções raras que vençam rapidamente e transformem desvantagens em recursos. Não colocar um teto universal de dano só para impedir grandes números. Distinguir:

1. **Combo desejável:** grande payoff com peças específicas ou janela conquistada.
2. **Dominância:** opção quase sempre superior, independentemente de build e contexto; revisar acesso, benefício marginal e custo.
3. **Loop inválido:** repetição sem avanço de estado, recursão ilimitada, proc duplicado, recurso negativo ou quebra de execução; tratar como defeito.

Usar identidades de proc e os orçamentos da engine para provar terminação. Definir escopo por carta, acerto, alvo, ativação ou combate explicitamente; jamais limitar tudo a uma ocorrência ou contar ativações no cliente por conveniência. Se um combo finito for enorme, apresentar seu resultado em grupo sem eliminar os fatos do journal.

## 6. Ritmo da campanha e combate

### 6.1 Primeira sessão experimental

Não começar reformando as quatro campanhas. Usar um recorte curto e publicado como variante de QA de Alisyum, inicialmente Piromante porque seu conjunto já contém uma cadeia produtor–consumidor.

| Etapa | Proposta |
| --- | --- |
| Abertura | Selecionar identidade e escolher entre três transformações iniciais que mudam comportamento de uma carta possuída; preview imediato e aplicação válida |
| Encontro 1 | Ameaça curta, previsível, que demonstra a transformação; sem necessidade de ler tutorial longo |
| Recompensa 1 | Escolha relevante entre mod/peça de build e carta complementar; se for carta, mostrar para quais mods é candidata |
| Encontro 2 | Dois alvos com prioridades distintas; permite descobrir combinação e tradeoff |
| Oficina rápida | Aplicar, substituir ou testar uma peça conforme regras do modo; investir em força, consistência ou sobrevivência |
| Elite / chefe | Teste de preparação e clímax; permitir vitória explosiva quando a build encaixar |

Alvo inicial de 12–18 minutos de duração da fatia, primeira transformação em até dois minutos e primeiro proc relevante até o fim do primeiro encontro. São alvos provisórios para teste humano, não especificação de duração universal. Se o usuário preferir campanhas longas depois, estender a partir do núcleo.

Testar economia que garanta ao menos três oportunidades reais de alterar o funcionamento de cartas/identidade ao longo de uma rota curta. Ofertas devem incluir uma opção coerente com peças existentes e opções de desvio/ponte; usar composição explícita de pools e sorteio reproduzível. Não dar sempre as peças ideais nem obrigar um arquétipo completo. Testar diferença entre melhoria direta de carta, aquisição nova e reroll, incluindo possibilidade de pular recompensas sem penalidade artificial. Medir consistência: quando uma carta central não aparece, existe compra, busca, retorno ou alternativa útil?

### 6.2 Desenho dos encontros

| Encontro | Pergunta tática | O que testa |
| --- | --- | --- |
| Executor anunciado | Você finaliza antes do golpe ou prepara defesa? | Timing de residual e dano direto |
| Guardião + suporte | Mata o suporte que fortalece ou concentra dano no guardião? | Priorização, overflow, área |
| Inimigo que escala | Acelera o payoff ou estabiliza a defesa? | Consistência, risco e tempo |
| Inimigo em fases | Usa payoff agora ou guarda para uma janela exposta? | Sequenciamento e capacidade de adaptação |
| Chefe | Sua máquina consegue vencer de modo surpreendente? | Clímax e autoria |

São arquétipos de desenho que podem ser implementados com comportamentos já suportados, após inspeção. Evitar imunidade total a Burn/crítico apenas para combater uma build; preferir ameaça que valoriza uma alternativa ou janela vulnerável. Bosses não precisam sobreviver a toda combinação rara. Evitar que estratégia defensiva seja sempre a escolha segura independentemente do encontro. A intenção precisa transportar tipo, alvo, faixa quando incerta, condição relevante e tempo até a ação; se mudar, a UI deve indicar a mudança.

## 7. Godot: interface a serviço do core

Usar os componentes existentes `pattern_workshop_view.gd`, `card_modifier_inventory_view.gd`, `unified_loadout_screen.gd`, presenters de fatos e de preview. A compra separada da aplicação, seleção, comparação e compatibilidade já existem; primeiro reduzir navegação e integrar esse fluxo às recompensas. Não reescrever todos os painéis.

### 7.1 Fluxo de transformação em uma única experiência

- Exibir carta escolhida, slots ocupados/livres, mod e antes/depois lado a lado. Mostrar incompatibilidade com texto do motivo.
- Preview do resultado de definição e de números segundo um contexto declarado. Separar efeito previsto de estado hipotético: não prometer que um futuro crítico ou proc ocorre quando é condicional.
- Confirmar o item e a carta de destino com custo e irreversibilidade. Usar apenas comandos legais e previews autoritativos; se adquirir e aplicar forem passos diferentes, deixar o estado intermediário recuperável e evitar cobrança duplicada.
- Carregar os ícones compactos do mod na carta durante o combate, com texto completo no detalhe. Usar áreas fixas para evitar que tamanho de cartas ou inimigos varie com status e mods.
- Ao selecionar recompensa, oferecer indicação de interações possíveis com peças possuídas, sem forçar uma recomendação nem esconder opções.

### 7.2 Feedback de causalidade

Para cada sequência confirmada, projetar uma cadeia curta: carta base → mod que altera execução → efeito da identidade → resultado. Exemplo meramente ilustrativo: “Eco: 2 impactos · Fornalha: +4 Burn · Ruptura: +22 dano”. Cada número deve vir de facts/calculation traces da engine, com fallback honesto quando o resultado não está disponível.

Fazer o jogador saber qual peça entrou em ação: destacar slot ou marcador da carta, pulso no contador, mudança de cor sem depender somente de cor, total acumulado no alvo, som elemental existente quando pertinente. Para três impactos, usar três acentos rápidos e um resumo; para cadeia longa, produzir clímax com número crescente e resumo final em vez de várias animações bloqueantes.

A UI não deve simular efeitos, reinterpretar gambits ou avançar contadores por conta própria. A engine determina a verdade; Godot determina o ritmo e a apresentação.

### 7.3 Instrumentar ritmo antes de ajustar timings

Medir separadamente clique → reconhecimento, API/preview, tempo de playback bloqueante, reconciliação e próxima ação legal. Os perfis do `motion_manifest.json` já distinguem `prototype-test` e `alisyum`; duração individual não comprova o tempo total de uma cadeia. Preferir agrupamento causal, paralelismo cosmético e aceleração/skipping sem perda de estado. Preservar hover de cartas e áudio recentemente instalado.

O Godot oferece execução headless, profiler, debugger e monitores personalizados. Usar isso para medir tempo de bloqueio por beat, chamadas de preview descartadas, tamanho da mão, contagem de nós e picos de frame. [Documentação CLI](https://docs.godotengine.org/en/stable/tutorials/editor/command_line_tutorial.html); [Performance](https://docs.godotengine.org/en/stable/classes/class_performance.html); [CPU optimization](https://docs.godotengine.org/en/stable/tutorials/performance/cpu_optimization.html).

Alvos de UX iniciais: resposta visual p95 inferior a 100 ms em hardware de referência; sequência comum de carta em até 350 ms de bloqueio de apresentação, excluindo processamento de API que ainda deve ser medido; cadeia rara com apresentação rica e opção de acelerar; nenhum input perdido e nenhum preview obsoleto confirmável. Calibrar com gravações e profiler antes de aprovar.

## 8. Desenvolvimento com IA e arquitetura de validação

A IA deve ser nossa máquina de experimentação, não o árbitro de diversão. O processo de harness engineering descrito pela OpenAI é relevante porque coloca ferramentas, feedback automatizado, testes e invariantes à disposição de agentes; relata também que agentes tendem a reproduzir padrões ruins já existentes. [Harness engineering](https://openai.com/index/harness-engineering/). No campo de testes de jogos, Holmgård e coautores demonstram personas de jogo por busca com heurísticas, úteis para explorar estilos distintos e avaliar conteúdo. [Automated Playtesting with Procedural Personas](https://arxiv.org/abs/1802.06881). Isso não mede satisfação humana.

### 8.1 Aproveitar o que já existe

O HeroScript MCP tem `validate_content`, `validate_gambits`, `inspect_card`, `inspect_entity`, `simulate_encounter`, `simulate_branch`, `analyze_cards` e `verify_replay`. A documentação em `tools/HeroScript.Mcp/README.md` registra simulações isoladas na porta 5280, limite de até cem comandos/rodadas e uma heurística fixa que não estima jogo ótimo. O Godot MCP pode abrir a cena de QA, capturar tela, inspecionar nós e reportar erros. Já existe teste comparando o estado/identidade/hash com a API real do sandbox.

Proposta de pipeline:

```text
Hipótese de design + variante JSON em revisão de QA
   → validação do conteúdo e matriz de compatibilidade
   → engine: cenários, seeds, políticas de jogo, traces e comparação A/B
   → selecionar casos típicos, extremos e quebras suspeitas
   → Godot MCP: executar/inspecionar apresentação de QA
   → jogador humano: testar descoberta, decisões e feeling
   → registrar decisão; manter, ajustar ou reverter variante
```

Usar diretórios isolados e versões fixas de conteúdo. Não editar saves existentes, não importar runs de QA na campanha, não elevar permissões de modos de gameplay. A validação de candidato não implica publicar conteúdo. Reutilizar o fluxo de autoria e revisão do projeto para alterações.

### 8.2 Personas determinísticas antes de aprendizado de máquina

Comparar políticas: iniciante que joga a primeira carta legal; agressiva que maximiza dano imediato; defensiva que mitiga ameaça; busca de sinergia que prioriza setup e payoff; exploradora que tenta combinações diferentes; e busca limitada de dois ou três passos para estabelecer uma referência tática.

As políticas devem usar comandos legais e informação acessível ao jogador relevante, sem conhecimento indevido da ordem futura. Uma política de teste white-box pode explorar estados extremos, mas seus resultados não pertencem às métricas de experiência humana. Seed, revisão, versão da política e decisão de cada passo devem ser registrados. Separar seeds de desenvolvimento de seeds de verificação, compartilhar seeds entre variantes quando comparáveis e não interpretar amostras pequenas como conclusão definitiva.

Começar com pequenos lotes (por exemplo, 20 seeds por cenário e política, respeitando limites atuais do MCP); aumentar somente após demonstrar o valor. Para comparar centenas de runs, avaliar o desenho já proposto em `docs/bot-simulations/arquitetura-bots-simulacao-ml.md`: runner C# reutilizando Core, métricas persistidas e processamento em lote. Medir o gargalo antes de construir treinamento RL, gRPC, embeddings ou infraestrutura extra. IA de linguagem é mais útil inicialmente como autora de hipóteses, análise de traces, geradora de fixtures e revisora de relatórios.

### 8.3 Guardrails de implementação

- Uma tarefa de IA por hipótese e commit isolado; escopo, arquivos, comportamento esperado e contraexemplo documentados.
- Testes de conteúdo, operação de patch, compatibilidade, composição de duas/três peças, quantidade de procs por escopo, pagamentos, legalidade, alvo, persistência, replay e determinismo.
- Matriz automatizada carta × mod: cobertura declarada, aplicação real, motivo de impossibilidade e diff efetivo. Não inferir compatibilidade somente do nome do slot.
- Testar com os módulos legítimos do MVP e Alisyum, sem misturar regras específicas de um setting na Godot.
- Fixture de Godot para transformação + combate com múltiplos status, mão cheia, sequência longa, mudança de intenção, confirmação de recompensa e reconexão/retry; screenshots e gravações como evidência. O teste precisa verificar estados, não apenas se o aplicativo abriu.
- Registrar escolhas de design aprovadas pelo autor na documentação do projeto. IA deve consultar o registro e exemplos de conteúdo aprovados antes de gerar novas cartas/mods, para não expandir padrões de baixo valor.

## 9. Métricas e método de avaliação

| Dimensão | Medição objetiva | Meta experimental inicial |
| --- | --- | --- |
| Acesso à fantasia | Tempo até a primeira carta transformada; oportunidades legais por rota | Até 2 min; ao menos 3 oportunidades na fatia |
| Cobertura | Cartas com zero, uma ou mais possibilidades reais; mods órfãos | Todas as básicas transformáveis; reduzir substancialmente zero/uma opção |
| Descoberta | Primeiro proc de sinergia; origem e número de peças distintas | Pelo menos uma interação no encontro 1; pelo menos um payoff de duas ou mais peças por rota |
| Diversidade | Ofertas, escolhas condicionadas a oferta, uso de carta/mod, distribuição de builds | Pelo menos duas estratégias plausíveis por herói testado |
| Picos de poder | Dano/defesa/compra/energia por ação e turno; p50/p90/p99; distribuição de duração | Observar cauda de poder; investigar extremos sem nerf automático |
| Decisão | Ações legalmente disponíveis, alternativas com resultados distintos; escolhas de alvo e ordem | Evitar turnos resolvidos sempre pela mesma opção |
| Ameaça | Dano recebido evitável, HP final, mortes, tempo de kill, falhas de gambit | Nenhum softlock; encontros com perguntas táticas distintas |
| Ritmo | Latência de feedback, tempo bloqueado, cliques por transformação, menus/combate | Clique→feedback p95 <100 ms; reduzir navegação |
| Robustez | Limites de proc e comando, hash/replay, pagamento, save/retry | Zero regressões e loops inválidos nos casos executados |

Para medir sinergia, comparar base, base+A, base+B e base+A+B com mesma seed e cenário. Para uma métrica em que maior é melhor, como dano em uma janela fixa, interação = resultado(A+B) - resultado(A) - resultado(B) + resultado(base). Valor positivo indica retorno combinado maior que a soma de ganhos isolados naquela métrica; não comprova que o combo é divertido. Separar resultados imediatos e residuais, mitigação, custo, compra, cura e alvo. Medir valores fora de combate e em encontros com adversários distintos. Os resultados anteriores do MVP focaram dano direto: ampliar para ação e run completa.

### Playtest humano obrigatório

O autor e, se possível, 3–5 jogadores com diferentes familiaridades com deckbuilders testam a versão de referência e a experimental com campanhas de QA separadas. Alternar a ordem entre participantes quando possível. Primeiro observar sem explicar a build; depois pedir que descrevam a carta modificada e o que aconteceu na cadeia. Registrar o instante de primeira decisão empolgante, momentos de espera, confusão, arrependimento de compra e o que fariam diferente em outra run. Perguntar sobre agência, variedade, vontade de continuar e sensação de poder; pedir exemplo concreto, não apenas nota.

Critérios qualitativos de continuidade: jogadores conseguem narrar a causa de pelo menos uma combinação; identificam uma rota de build alternativa que desejam testar; alterações são percebidas no combate. Se esses sinais não aparecem, rever design mesmo que cobertura e métricas técnicas passem. Não usar feedback da IA como substituto nem fabricar resultados de playtest.

## 10. Plano de implementação incremental

| Fase | Entregável | Gate antes de continuar |
| --- | --- | --- |
| 0 — Baseline | Inventário carregado vs disponível por rota; telemetria existente; revisão de referência, seeds, fixtures, capturas de UI; teste humano curto de referência | Métricas reproduzíveis, nenhuma alteração de regra |
| 1 — Prova da fantasia | Variante curta do Piromante com transformação precoce, dois caminhos de motor e três encontros relevantes | Transformação válida e percebida no primeiro encontro; autor decide se o core merece ampliar |
| 2 — Gramática dos mods | Revisar seis mods existentes, incluir famílias prioritárias; matriz de eligibilidade e cenários de composição | Pelo menos dois caminhos reais; composição testada, sem novos loops inválidos |
| 3 — Progressão | Recompensas e lojas com escolhas de carta/mod/identidade, economia e rota revisadas; playtest A/B | Uma sequência natural de aquisição e uso; nenhum dead end; escolha significativa entre opções |
| 4 — Combate | Redesenhar encontros e chefe do recorte; calibrar mãos, custos, ameaça e cadência dos gambits | Sequenciamento e alvo importam; combate acelera quando build encaixa; cobertura de legalidade |
| 5 — Godot expressivo | Oficina integrada, carta transformada legível, preview e causa de procs; ritmo, feedback e som | Fluxo observado ponta a ponta; screenshot/replay corretos; zero regressões de layout e estado |
| 6 — Expansão provada | Transferir gramática ao Servo, Apostador, Renegado e MVP com suas regras próprias; comparar diversos estilos | Builds com identidade distinta; resultados humanos favoráveis; documentação e regressão por setting |

Paralelamente às fases 1–4, entregar apenas os pequenos ajustes de UI necessários para testar cada hipótese. Fase 5 consolida o fluxo; não esperar até o fim para tornar uma transformação jogável.

Não prometer duração fechada de desenvolvimento até executar a baseline, porque os contratos exatos necessários para novos mods ainda precisam ser validados. Um commit por etapa ou experimento reversível, sempre separado de alterações não relacionadas.

### Primeiro pacote de trabalho que recomendo

1. Capturar três cenários de referência de Alisyum: início normal do Piromante, início do segundo encontro e chefe, usando QA isolado. Registrar ofertas, cartas modificáveis, ações, latência e causas de resultados.
2. Criar uma variante curta que oferece a primeira runa na abertura, com comparação imediata e uso no combate 1. Tornar básicas compatíveis com mods coerentes, testar dois motores: Eco/Calor e Burn/condensação.
3. Comparar original e variante com poucas seeds e duas políticas, verificar replay e inspeção. Rodar uma sessão humana curta para decidir qual motor dá vontade de explorar. Se o resultado continuar chato, mudar oportunidade de construção e regras de interação, não redecorar a interface.

O próximo objetivo do projeto não deve ser adicionar um grande volume de conteúdo. Deve ser criar uma única run curta em que três ou quatro decisões de build sejam memoráveis e que o jogador queira iniciar outra run para experimentar uma combinação diferente.
