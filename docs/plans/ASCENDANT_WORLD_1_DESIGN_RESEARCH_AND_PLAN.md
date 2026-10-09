# Ascendant — mundo 1: pesquisa de design e plano de implementação

Data: 2026-10-09.

Status: plano de design e implementação; nenhuma alteração de gameplay realizada nesta revisão.

Base examinada: `main`, commit `fe9c8d5`, setting `ascendant` / Ascendant Matrix.

## 1. Objetivo e decisões do autor

Transformar a vitrine de scaling em um primeiro ato jogável, inspirado na construção de builds de Path of Exile e nas decisões de combate e percurso de Slay the Spire. Lore, nomes finais e arte continuam placeholders.

Decisões confirmadas nesta conversa:

- Mundo 1 é o primeiro ato de uma run futura; a demo termina após seu chefe.
- Uma tentativa completa deve durar aproximadamente 25–40 minutos, incluindo decisões.
- Mapa ramificado, com riscos e recompensas visíveis.
- Construção híbrida: adquirir cartas e transformar cartas com modificadores.
- Começar com crítico, DoT contínuo, DoT condensado, múltiplos acertos e tank; incluir peças flexíveis para combinações emergentes.
- Build forte no fim do ato, mas chefe ainda exige decisões táticas.
- Dificuldade que permite derrota, com intenções claras e erros identificáveis.
- Apenas energia como recurso de pagamento de ações no mundo 1.
- Persistência entre tentativas: desbloqueios de opções, sem bônus permanentes de poder.
- Chance crítica acima de 100% garante níveis; a fração restante sorteia o próximo nível. Sorteio independente por acerto.
- Recompensas aleatórias com escolha e recusa, lojas e crafting direcionado limitado.
- Vida perdida persiste entre encontros; recuperação exige escolhas.

Os valores, quantidades e fórmulas recomendados abaixo são parâmetros iniciais de playtest, não números já aprovados nem garantias de diversão. As decisões confirmadas acima são o direcionamento; os parâmetros devem poder mudar nos JSONs.

O [GDD core](../game-design/CORE_GAME_DESIGN.md) e o [arquivo de conceitos legados](../game-design/LEGACY_GAME_DESIGN_CONCEPTS.md) permanecem intocados. Este plano aplica-se ao Ascendant: não incorpora silenciosamente crítico multinível ao escopo original do `volatile-core`, que o adiava.

## 2. Diagnóstico do setting atual

### 2.1. É uma demonstração, não um ato

`data/configs/ascendant/Resources/runs/ascendant_showcase.json` tem cinco paradas lineares:

1. Calibration: combate contra um drone.
2. Serration: uma relíquia garantida.
3. Matrix reward: uma seleção de carta.
4. Matrix forge: um upgrade.
5. Ascension: combate contra um único adversário final.

Há seis definições próprias de cartas, dois adversários, uma relíquia, um status e quatro upgrades. O pool de recompensa enumera quatro cartas. Não há nesse percurso escolhas de rota, descanso, loja nem pressão de vários encontros sobre a mesma reserva de vida.

### 2.2. O poder é entregue antes de a build existir

O personagem começa com 100 de vida, 3 de energia, 45% de weapon damage, 35% de spell damage, 40% de elemental damage e multiplicador crítico 1,75. O modo acrescenta 15% de increased damage. O deck já contém cartas com bônus flat, increased elemental, multiplicadores more e multiplicador critical.

Estimativas estáticas da pipeline atual, antes da relíquia, contra alvo sem block e sem outros efeitos:

| Carta | Energia | Cálculo aproximado | Resultado arredondado |
|---|---:|---|---:|
| Strike | 1 | `(8 + 2) × (1 + 0,45 + 0,15)` | 16 |
| Elemental Burst | 1 | `6 × (1 + 0,35 + 0,15) × (1 + 0,40 + 0,65) × 1,20` | 22 |
| Critical Lance | 2 | `7 × (1 + 0,45 + 0,15) × 1,75 × 1,15` | 23 |
| Support Cascade | 2 | `5 × (1 + 0,35 + 0,15) × 1,25 × 1,20`, por alvo | 11 por alvo |
| Kinetic Guard | 1 | Ganho configurado de block | 12 block |

Essas contas são inferências dos JSONs, não resultados de uma simulação desta revisão. A primeira etapa técnica deve confirmar os traces reais, incluindo filtros e origem de cada influência.

Problemas sugeridos pelas contas:

- Elemental Burst já possui eficiência elevada sem investimento durante a run.
- Critical Lance, apesar de rara, perde em dano por energia para duas Strikes contra alvo sem defesa.
- Cascade precisa de encontros multi-inimigo para justificar sua função; o percurso atual tem apenas encontros de alvo único.
- Guard pode cobrir completamente ataques simples com pouco investimento.
- A relíquia oferece uma melhora numérica garantida, sem escolha nem mudança significativa de comportamento.

Raridade não deve significar obrigatoriamente maior dano por energia: contudo, uma carta mais cara precisa justificar seu custo por função, consistência, alvo, setup ou sinergia.

### 2.3. Os inimigos quase não testam a build

Drone: 68 de vida, uma habilidade `enemy_basic_attack` herdada, dano-base 6. Prime: 150 de vida, block inicial 18, uma habilidade `enemy_crushing_blow` herdada, dano-base 12. Os stats e demais influências podem alterar esses números; não tratá-los como dano final publicado.

As definições próprias não oferecem ciclos de preparação/ataque, combinações de adversários, suporte, pressão crescente nem janelas de vulnerabilidade. Aumentar apenas vida tende a prolongar a repetição sem criar decisões.

### 2.4. Crítico multinível não está conectado neste setting

A pipeline `ascendant_effect_amount` aplica o bucket `critical` quando o efeito tem as tags correspondentes. Critical Lance já possui essa tag e multiplica pelo stat `critical_multiplier`: não há ali uma chance dinâmica nem o cálculo de níveis críticos.

O conteúdo-base contém `CRIT_GUARANTEED_TIER`, `CRIT_EXTRA_CHANCE` e `CRIT_DAMAGE_MULTIPLIER`, com testes matemáticos. Isso não prova a integração entre atributos, sorteios por impacto, execução, preview e replay do setting.

`EffectRandomInputDefinition` já oferece scopes Action/ParentProc/Impact e fatos imutáveis de sorteio. Entretanto, sua chance atual é um número fixo entre 0 e 1. Chance crítica baseada nos atributos e buffs do personagem exige uma integração genérica adicional ou uma resolução declarativa equivalente, a validar — não apenas chamar a fórmula existente.

### 2.5. Vida não acompanha o percurso

`CombatRunCoordinator.StartEncounter` cria participantes usando definições e possíveis overrides iniciais. `PersistentPlayerTransitions` materializa atributos persistentes, mas não transporta os recursos do último encontro. `RunManager.ResolveEncounter` resolve mapa, progressão e efeitos de saída, sem uma política de transporte dos recursos do personagem.

Além disso, a definição Ascendant atual não declara `playerDefinitionId`. O mecanismo de atributos persistentes existe em outros percursos, mas não foi habilitado aqui.

Consequência: não basta adicionar um descanso ao JSON. Primeiro precisamos de uma autoridade canônica para vida e outros recursos que devam atravessar encontros, sem confundir recursos do personagem com a carteira da run.

### 2.6. Outras fronteiras relevantes

- O grafo de mapa já aceita ramificações por `nextNodeIds`. Seu `Create` materializa um grafo declarado; não assumir geração procedural de atos apenas por existir seed.
- Intenções têm `LockUntilActorActivation`, além de recomputação. Usar e testar a política de lock para decisões táticas confiáveis.
- Transformações, afinidades, sequências de impactos, condensação e continuação por abate têm contratos genéricos exercitados no `volatile-core`. Reutilizar seus mecanismos, não copiar suas regras/números nem depender desse setting inteiro.
- Histórico e perfis são separados por setting. Os desbloqueios atuais são marcos básicos, como `completed-run-content`; não há evidência de um catálogo declarativo completo de desbloqueios do mundo 1 governando suas ofertas.
- Esta revisão foi de código e conteúdo, não uma coleta de tempos, taxas de vitória ou relatos de playtest. Esses dados ainda precisam ser produzidos.

## 3. Pesquisa e aplicação ao projeto

As fontes abaixo são apresentações dos desenvolvedores, publicações oficiais e um estudo empírico. São fundamentos de design, não prescrições numéricas para este jogo. Fontes históricas são usadas para princípios identificados, não para afirmar o estado de todos os patches atuais.

### 3.1. Slay the Spire: papel de cada carta e balanceamento iterativo

Na apresentação de Anthony Giovannetti, o objetivo de equilíbrio é que as cartas tenham um lugar sem distorcer o jogo. O processo combina iteração, métricas e feedback; dados não substituem interpretação. A estratificação de habilidade também importa para avaliar resultados. Fonte: [Mega Crit/GDC — Metrics Driven Design and Balance, slides de 2019](https://media.gdcvault.com/gdc2019/presentations/Giovannetti_Anthony_SlayTheSpire.pdf).

Aplicação proposta:

- Toda carta, upgrade e encontro terá uma ficha de função: qual decisão cria, para quem é útil, o que custa e qual alternativa compete com ela.
- Medir eficiência não apenas por dano: defesa, compra, consistência, setup, consumo de stacks, remoção de ameaça e alcance também são valor.
- Separar jogadores novos e experientes e builds incompletas/completas. Uma média global pode esconder um arquétipo inviável ou uma carta obrigatória.
- Revisar dados junto com observação de partidas, especialmente vitórias fáceis mas entediantes.

### 3.2. Slay the Spire: percurso, risco e interação de relíquias

A descrição oficial destaca construir decks com sinergias, escolher caminhos mais arriscados ou seguros e descobrir relíquias que interagem com o deck. Fonte: [Mega Crit — página oficial do jogo na Steam](https://store.steampowered.com/app/646570/Slay_the_Spire/).

Aplicação proposta: rota é uma decisão sobre a build e a vida restante, não apenas a escolha de uma recompensa colorida. Um elite opcional oferece maior oportunidade de poder; um descanso preserva a tentativa, mas compete com melhoria. Relíquias devem mudar decisões além de elevar todas as saídas da pipeline.

### 3.3. Path of Exile: multiplicadores podem destruir o combate

O manifesto Expedition identifica dois riscos: dano suficiente para eliminar chefes antes de sua interação e suportes multiplicativos tão eficientes que substituem opções interessantes de utilidade. Também discute custos e contrapartidas para poderes e triggers. Fonte: [Grinding Gear Games — Game Balance in Path of Exile: Expedition](https://www.pathofexile.com/forum/view-thread/3147157).

Aplicação proposta:

- Não começar com quase toda a cadeia multiplicativa desbloqueada no deck.
- Reservar ganhos importantes para investimentos encontrados durante o ato.
- Suportes de repetição, transporte e condensação precisam competir com bônus numéricos por valor real, não apenas por aparência.
- Preservar custo de oportunidade: energia, carta comprada, alvo, setup, espaço de transformação e recurso de crafting.
- Manter o chefe interessante com leitura de intenções e mudanças de pressão; evitar invulnerabilidade arbitrária usada para compensar descontrole de dano.

### 3.4. Path of Exile: semântica de increased e more

Uma explicação oficial de Mark/GGG demonstra que aumentos aplicáveis de tipos diferentes se somam, em vez de criar multiplicações independentes apenas por receberem nomes diferentes. Fonte: [GGG — Game Mechanics: Questions and Answers](https://www.pathofexile.com/forum/view-thread/1693154).

Aplicação proposta: revisar o `elemental` AddPercent separado do `increased`, pois hoje cria mais uma camada multiplicativa. Para um vocabulário inspirado em PoE, os bônus chamados increased de uma mesma saída e estágio devem somar; more/less explicitamente separados multiplicam. Efeitos do atacante e modificações do dano recebido podem pertencer a etapas distintas, conforme a regra declarada. Não copiar mecânicas antigas de ailment dessa publicação histórica.

### 3.5. Path of Exile: variedade combinatória com reutilização

A apresentação de Chris Wilson descreve reutilização de conteúdo, geração procedural, eixos de aleatoriedade e sistemas profundos como ferramentas de longevidade. Fonte: [GGG/GDC — Designing Path of Exile to Be Played Forever](https://www.gdcvault.com/play/1025784/Designing-Path-of-Exile-to).

Aplicação proposta: poucas famílias de adversários combinadas em encontros diferentes e modificadores compatíveis produzem variedade com menos conteúdo descartável. Para o primeiro ato, um grafo autorado ramificado é suficiente; não é necessário construir uma economia live service ou um gerador complexo para provar a gameplay.

### 3.6. Cartas fáceis de ler, difíceis de dominar

Mark Rosewater diferencia complexidade de compreensão, de interação no tabuleiro e estratégica. Cartas comuns podem ter profundidade estratégica sem ser difíceis de interpretar, e as comuns sustentam o ambiente de jogo. Fonte: [Wizards — Quite the Rarity](https://magic.wizards.com/en/news/making-magic/quite-rarity-2018-03-12).

Aplicação proposta: texto curto no corpo da carta, comportamento detalhado na inspeção e números realmente afetados pela build em destaque. Uma defesa simples pode esconder uma escolha interessante entre prevenir dano agora e preparar um golpe depois. Raridade regula especialização e frequência; não torna a opção comum obsoleta.

### 3.7. Aleatoriedade: informação e agência precisam de teste

Um estudo exploratório com 18 participantes comparou quatro condições em um cardgame próprio. A condição de input incluía cartas inesperadas, não apenas embaralhamento normal. Os resultados e limitações não sustentam uma regra universal de que input randomness é sempre melhor ou de que crítico necessariamente reduz satisfação. Fonte: [Zhang et al. — Effect of Input-output Randomness on Gameplay Satisfaction in Collectable Card Games](https://arxiv.org/pdf/2107.08437).

Aplicação proposta: ofertas e compras variam por seed, mas oferecem decisões informadas. Preview crítico exibe possibilidades/probabilidades, não um resultado secreto falso. Nenhuma luta inicial deve exigir um crítico favorável para sobreviver à primeira rodada. Testar frustração de mãos ruins e correções disponíveis, não eliminar toda variância.

## 4. Identidade de gameplay do mundo 1

Pitch: construir um motor de combate próprio durante um percurso curto, usando cartas como ações transformáveis, e provar que esse motor suporta ameaças variadas sem abandonar decisões de alvo, sequência e defesa.

Pilares:

1. Cada turno apresenta pelo menos uma escolha relevante entre avançar a vitória e controlar a ameaça.
2. Crescimento muda o que a carta faz ou como vale utilizá-la, não apenas quantos dígitos aparecem.
3. Inimigos anunciam problemas diferentes, com mais de uma resposta viável.
4. A vida conecta encontros e dá significado à rota.
5. Uma build boa ganha força, mas não recebe imunidade universal nem solução automática para todos os encontros.
6. Híbridos são permitidos: os arquétipos abaixo são famílias de interação, não classes que bloqueiam combinações.

Fora do mundo 1: campanha completa, multiplayer, mercado, loot de equipamentos completo, árvore passiva gigante, dano permanente comprado entre runs, resistências elementais extensas, lore final e novo redesign de menus.

## 5. Modelo das cinco famílias de build

| Família | Decisão central | Peças de sustentação | Fraqueza que pode ser administrada |
|---|---|---|---|
| Crítico | Investir em consistência versus magnitude e escolher o ataque certo para a janela de dano | Chance, multiplicador, preparação curta e ataque de impacto | Burst inconsistente quando pouco desenvolvido; investimento compete com defesa e compra |
| DoT contínuo | Aplicar stacks, controlar o adversário e deixar o tempo trabalhar | Aplicadores, duração/intensidade e defesa eficiente | Ameaças urgentes exigem dano direto ou aceleração; não pode depender só de esperar |
| DoT condensado | Escolher quando trocar dano futuro por resolução imediata | Aplicadores compartilhados com DoT, consumidor e alguma reaplicação | Consumir cedo demais perde valor; guardar demais deixa a ameaça agir |
| Múltiplos acertos | Alocar impactos e explorar consequências por acerto sem duplicar orçamento | Repetição, suporte por impacto, controle de alvo e compra | Porções menores e setup; precisa de opções contra defesa e alvos de alta vida |
| Tank | Usar defesa para comprar tempo e convertê-la em progresso de vitória | Block, retenção limitada, enfraquecimento e finalizador defensivo | Defesa não pode ser um loop infinito sem pressão nem tornar toda intenção irrelevante |

Peças transversais: compra/filtragem, vulnerabilidade curta, dano em área, recuperação limitada, gerenciamento de descarte e retorno, execução/overflow por abate. Pelo menos algumas dessas cartas devem funcionar em mais de duas famílias.

### 5.1. Relação entre DoT contínuo e condensação

- Compartilham aplicadores e payloads compatíveis.
- DoT contínuo ganha valor pelo tempo; condensação troca esse tempo por um proc imediato.
- Não causar tick e consumir o mesmo payload duas vezes.
- Definir nos JSONs quais stacks são elegíveis, como a duração restante se converte em orçamento e se a receita usa intensidade ou dano futuro restante.
- Proposta inicial para comparação legível: usar o orçamento futuro restante, com fator de conversão configurável; não assumir que stackCount por si só representa toda a magnitude.
- O proc resultante usa a pipeline de destino declarada. Bônus já capturados na aplicação não reaplicam automaticamente na liberação.
- Condensação continua genérica para cura, buffs e outros payloads; o mundo 1 usa prioritariamente DoT ofensivo, sem estreitar o processador da engine.

### 5.2. Tank não significa ficar parado

As primeiras opções devem incluir uma defesa com utilidade adicional e um finalizador que aproveite parte da reserva defensiva. Retenção de block deve ter limite, condição ou custo. Recuperação repetível não pode restaurar gratuitamente toda a vida da run em um adversário inofensivo.

Alternativas para limitar stall: pressão anunciada que cresce lentamente, recuperação finita por encontro, carta que sai do ciclo após uso ou contrapartida explícita. Evitar limite arbitrário de turnos como solução principal e não penalizar combate lento legítimo de tank/DoT.

## 6. Regras de scaling e crítico recomendadas

### 6.1. Separar os estágios

Fluxo conceitual:

`base/upgrades → scaling compartilhado da origem → orçamento/distribuição → sorteio por impacto → scaling do impacto → defesa do alvo → aplicação e registros`

Esse fluxo não é uma segunda calculadora: cada quantidade numérica relevante deve ser calculada por pedidos às pipelines declaradas. RNG produz fatos de sorteio; a pipeline não consulta UI, banco de saves nem decide a ação de IA.

Recomendações:

- Mesma semântica de increased/more no vocabulário do setting.
- Base da carta e atributos do personagem continuam separados.
- Bônus flat compartilhado não é reaplicado inteiro em cada fragmento de um ataque dividido.
- O suporte que transforma uma ação em N impactos declara o orçamento total e os pesos. Não multiplicar o dano total por N sem que isso seja uma melhoria explicitamente paga.
- Defesa e outros ajustes específicos do alvo resolvem por impacto; crítico por impacto fica depois da distribuição quando a distribuição representa orçamento comum.
- DoT possui regras de elegibilidade explícitas: proposta inicial, ticks não fazem novo sorteio crítico. Um crítico do ataque inicial só afeta o DoT se a definição disser isso.
- Preview mostra impacto imediato e consequência futura separadamente; números não comparáveis não são somados como se fossem iguais.

### 6.2. Níveis críticos

Com chance em percentual `C ≥ 0`:

- `n = floor(C / 100)`.
- `p = (C / 100) - n`.
- Um sorteio Bernoulli com chance `p` produz `extra` igual a 0 ou 1.
- `tier = n + extra`.

Multiplicador recomendado para começar: `1 + tier × (M - 1)`, em que `M` é o multiplicador de nível 1. Crescimento linear por nível, não `M ^ tier`.

Exemplo: com `C = 150%` e `M = 1,5`, cada acerto dá 1,5× ou 2×, cada possibilidade com 50%. A média teórica é 1,75×, mas não é o resultado garantido de um uso da carta. Em multi-hit, impactos independentes podem produzir combinações diferentes.

Essa regra multinível é uma escolha deste jogo, não uma afirmação de que PoE usa esse modelo.

Sem teto fixo de 100% na engine. Proposta de disponibilidade para o mundo 1: atravessar 100% deve exigir especialização; níveis mais altos devem ser investimento excepcional, não atributo inicial. Magnitude, chance, custo e quantidade de fontes serão calibrados juntos.

Nos casos `p = 0`, definir e testar a política de consumo de RNG. Chance não finita, unidades inválidas e resultados fora dos limites declarados falham antes do commit. Nenhuma chance calculada deve consumir RNG durante previews.

### 6.3. Economia mínima

- 3 de energia por ativação como referência inicial.
- Mão de 5, ciclo de compra/descarte configurado no sistema de zonas existente.
- Energia reinicia por ativação; block temporário limpa no começo da ativação de seu dono, salvo suporte/regra explícita de retenção.
- Vida persiste; gold e material de crafting pertencem à economia da run.
- Mana não é exibida como recurso vazio nem exigida no conteúdo do mundo 1; permanece disponível à engine e aos demais settings.
- Aumento de energia é raro e tem contrapartida; não oferecer energia/compra infinitas como resultado de poucas peças comuns.

## 7. Percurso e conteúdo alvo

### 7.1. Ritmo de 25–40 minutos

Proposta: 12–14 paradas significativas por caminho, com aproximadamente 8–10 lutas contando elites e chefe. Uma tela transitória de recompensa não conta como encontro adicional nem precisa de um clique de navegação redundante.

Três trechos:

1. Introdução: 2 lutas curtas, uma escolha de direção e uma transformação acessível.
2. Desenvolvimento: rotas alternativas, grupos de adversários, loja/crafting, um elite opcional e decisão de recuperação.
3. Prova: encontros de pressão maior, oportunidade final de preparar a build e chefe.

Garantias do grafo:

- Chefe sempre alcançável.
- Pelo menos uma rota sem elite e uma com investimento arriscado.
- Todos os caminhos oferecem acesso a uma transformação e a recuperação antes do chefe.
- Atividades alternativas convergem quando apropriado; não duplicar todo o conteúdo em cada rota.
- Recompensa informa tipo, não necessariamente o drop exato: segurança não elimina surpresa.
- Não farmar nós resolvidos na experiência normal.
- Não consumir seed diferente porque a UI abriu um tooltip ou consultou uma oferta.

Começar com grafo autorado. Variedade por templates/seed só entra com seleção canônica persistida e regras de repetição; geração procedural completa pode ser adiada.

### 7.2. Escopo inicial de produção

| Conteúdo | Alvo inicial de catálogo | Observação |
|---|---:|---|
| Cartas | 30 definições no total | Revisar as seis atuais; combinar funções e peças compartilhadas para cobrir cinco famílias |
| Transformações/suportes | 12 | Numéricas e comportamentais, com compatibilidade, custo e contrapartida |
| Relíquias | 8 | Mistura de suporte geral e construção de build; não oito bônus incondicionais de dano |
| Adversários comuns | 8 definições | Cada um com papel e ciclo/condição identificável |
| Templates de encontro comum | 12 | Combinações de 1–3 inimigos, divididas em introdução/desenvolvimento/prova |
| Elites | 3 modelos | Encontrar 0–2 conforme a rota, sem exigir todos na mesma tentativa |
| Chefe | 1 modelo | Dois estados/ciclos legíveis; segundo chefe só após o primeiro passar nos playtests |
| Eventos de gameplay | 5 | Trocas curtas entre vida, dinheiro e oportunidades de build; lore mínima |
| Serviços | Loja, crafting e descanso | Reutilizar atividades canônicas; ampliar apenas onde houver lacuna comprovada |

Os totais são orçamento de conteúdo, não obrigação de entregar tudo em uma única etapa. Antes de completar o catálogo, provar uma fatia pequena que exercite as cinco famílias.

### 7.3. Cobertura das cartas

Para cada família, garantir no pool básico desbloqueado:

- Duas entradas acessíveis: não depender de achar uma rara para começar a jogar aquele estilo.
- Uma ferramenta de preparação e uma de conversão/finalização.
- Defesa e compra compatíveis.
- Pelo menos uma ponte para outra família.
- Uma alternativa funcional sem a combinação ideal.

Deck inicial recomendado: 10–12 instâncias de ações simples, com dano, defesa, uma opção de seleção/compra e uma peça flexível. Não iniciar com cinco arquétipos montados. Nomes/tags de classe não restringem uso arbitrariamente.

Proposta de transformações: uma afinidade e até duas posições de suporte identificáveis por carta, com compatibilidades declaradas. Verificar como o ledger de slots atual suporta isso antes de definir o JSON final; não criar uma regra fixa nova na engine só para esse limite.

### 7.4. Inimigos e encontros

Papéis recomendados, com nomes provisórios:

- Duelista: alterna ataque e defesa; ensina turno de investimento versus proteção.
- Preparador: anuncia golpe forte antes de executá-lo; cria janela de burst ou mitigação.
- Enxame: duas ou três ameaças pequenas; valoriza área, multi-hit e ordem de abate.
- Suporte: aumenta a pressão de aliado; decidir eliminá-lo primeiro tem custo imediato.
- Acumulador: aplica efeito residual; exige escolher corrida de dano, defesa ou limpeza.
- Bastião: alterna proteção e exposição; não tem imunidade permanente a uma família inteira.
- Escalador: torna combate longo progressivamente mais perigoso; dá tempo suficiente para tank/DoT.
- Híbrido: combina dois papéis já ensinados, sem estrear três regras novas de uma vez.

Elites testam competências, não rótulos de build: urgência, múltiplas ameaças e sustentação. Evitar resistência total a DoT, punição letal por acerto contra multi-hit e reflect invisível.

Chefe: ciclo com preparação, ataque forte e recuperação; segundo estado aumenta/redistribui a pressão de maneira anunciada. Tank deve conseguir vencer por conversão defensiva, DoT por sustentação/consumo, crítico por janelas e multi-hit por eficiência/sequência. Não exigir uma carta exclusiva para sobreviver.

Lock de intenção preserva a ação escolhida enquanto sua legalidade permite. O preview do impacto ainda pode mudar honestamente se recursos/status forem alterados. A mudança de alvo ou ação após invalidação precisa de regra e comunicação explícitas.

## 8. Balanceamento: valores e método

### 8.1. Referência inicial, não tabela final

Manter 100 de vida como unidade de trabalho. Começar o banco de teste com ataque básico de aproximadamente 7–10 e defesa de 6–9 por energia, antes de sinergias. São âncoras para o modelo, não uma determinação de que todos os efeitos devem caber no mesmo número.

| Tipo de encontro | Duração pretendida com build compatível | Pressão total por rodada para testar |
|---|---:|---:|
| Introdução | 2–3 rodadas | 8–12 de dano antes da defesa do jogador |
| Comum de desenvolvimento | 3–4 rodadas | 12–18, com alternância de preparação/ataque |
| Comum de prova | 3–5 rodadas | 16–24, distribuídos entre ações e alvos |
| Elite | 4–6 rodadas | Picos anunciados; orçamento calibrado pelo papel do encontro |
| Chefe | 6–9 rodadas | Ciclo variável; não replicar o mesmo dano alto em toda rodada |

Pressão é do grupo, não de cada inimigo. Em tank/DoT, permitir lutas um pouco mais longas sem confundir isso com falha de balanceamento. Não escolher a vida do chefe olhando só o dano máximo de uma carta.

Calcular vida e pressão considerando:

- Dano efetivamente jogável por mão e por energia, descontando turnos de setup e defesa.
- Frequência de compra e tamanho do deck; carta forte no fundo do deck não atua em toda rodada.
- Acúmulo e payoff de DoT e condensação ao longo do tempo.
- Combinações comuns, raras e extremas de multiplicadores.
- Excesso de dano perdido e distribuição por alvo.
- Cura, block, controle e efeitos que evitam ativações inimigas.
- Quantidade de lutas, recuperações e serviços por caminho.

### 8.2. Economia e risco

- Gold deve permitir algumas escolhas reais, não comprar toda a loja nem nunca alcançar um serviço.
- Usar no máximo um material de crafting além de gold neste ato; avaliar se `power_points` já basta.
- Recompensa de luta: gold + oferta de carta com opção de recusa; upgrades/suportes aparecem em oportunidades específicas.
- Elite: vantagem real de construção de build, com custo de vida/rota esperado.
- Descanso: escolha entre cura percentual calibrada e melhoria, com limites de uso no nó.
- Loja: cartas, pelo menos uma ferramenta de correção do deck e informação clara de preço; verificar suporte canônico a remoção paga antes de prometê-la.
- Crafting: ofertas compatíveis e preview antes/depois; custo de aplicação/troca/remoção explícito, sem tornar respec gratuito e universal.
- Não usar apenas pesos de raridade para garantir viabilidade. Validar densidade de ferramentas de cada família e chance de encontrar uma direção nas primeiras oportunidades.

### 8.3. O que medir

Por build, seed, caminho e nível de experiência: conclusão do ato, derrota por nó, vida ao entrar/sair, duração, rodadas, uso de cartas, ofertas aceitas/recusadas, frequência de crafting, dano/defesa por energia, contribuição das fontes e mortes antes de a primeira decisão ocorrer.

Distribuições importam: mediana e extremos/percentis, não só média. Registrar cura de stall, loops de compra/energia, dominância de cartas e diversidade de escolhas. Taxa de seleção de uma carta não prova causalmente que ela está forte.

Não fixar uma taxa de vitória arbitrária para perseguir. Primeiro comparar o mesmo conteúdo com coortes de iniciantes e experientes; escolher o alvo após observar aprendizagem, erros e replayabilidade.

## 9. Arquitetura e limites de autoridade

- Engine é autoridade de run, mapa, escolha de recompensa, custos, transformação, alvo, IA, RNG, cálculo, aplicação, recursos persistentes, desbloqueios e resultado.
- Godot apresenta estado/intenções/previews e envia comandos REST; não corrige vida localmente nem recalcula crítico para mostrar um resultado aparentemente exato.
- Conteúdo/configs declaram regras. Evitar condicionais `if setting == ascendant` nos processadores genéricos.
- Reutilizar abstrações de efeitos, pipelines, zonas e ledger; helpers específicos de edição/autoria não podem virar calculadores paralelos.
- Vida persistente e carteira de gold devem ter proprietários claros. Não manter duas reservas canônicas de vida sincronizadas pela interface.
- Política de transporte de recursos declara preservar/resetar/carregar e os boundaries. Vida e derrota continuam configuradas por recurso, não pela interpretação hardcoded do nome `health`.
- Cura fora do combate usa o mesmo contrato de efeito e o recurso do proprietário correto.
- Modificações fora de atividade obedecem às capabilities do modo; cheats/branches não aparecem no modo normal.
- Run fixa revisão de conteúdo e inputs de geração. Desbloqueios elegíveis são capturados no começo da run para que mudanças futuras do perfil não alterem replay/ofertas de uma run existente.
- Atualizar o launch de `ascendant` para a nova definição do ato. Runs antigas permanecem na revisão gravada; não convertê-las ou descartá-las silenciosamente.
- Sem segunda linha de settings obrigatória ao jogador. Manter a vitrine anterior apenas como fixture/documentação se útil.

## 10. Etapas sequenciais de implementação

### Etapa 1 — Baseline reproduzível e especificação de balanceamento

- Compilar o Ascendant e registrar traces reais das seis cartas, dois adversários, influência do modo, relíquia e upgrades.
- Testar orçamento por impacto, defesa, duração de Fractured e fase de limpeza do block.
- Reproduzir duas lutas consecutivas para evidenciar comportamento atual de vida e atributos.
- Criar fichas de função e matriz das cinco famílias.
- Definir unidade de chance, fórmula de tier e semântica increased/more; congelar apenas contratos, não valores de balanceamento.

Gate: cenário de referência repetível com hashes iguais para mesmos inputs; contas estáticas confirmadas ou corrigidas. Relatório não chama testes unitários de playtest.

### Etapa 2 — Recursos do personagem ao longo do ato

- Introduzir ou completar política genérica de ciclo de vida entre run/encounter, com autoridade única e snapshot imutável.
- Inicializar personagem persistente pela definição do ato.
- Materializar vida de entrada e promover o resultado de saída no mesmo commit canônico da resolução.
- Resetar energia/block conforme configuração, sem perpetuar buffs temporários como upgrades de base.
- Permitir cura de descanso/evento através dos efeitos canônicos.
- Cobrir derrota, draw, abandono, retry de sandbox, máximo alterado, comandos duplicados e rollback.

Gate: perder vida na luta A, salvar/reiniciar, recuperar em nó e entrar na luta B preserva valores esperados; replay e branch não compartilham mutação. Dois settings não compartilham vida.

### Etapa 3 — Chance dinâmica e crítico multinível integrado

- Resolver input aleatório com chance calculada por fórmula/pipeline, contexto capturado e unidade explícita; extender o contrato genérico, não criar um executor de dano crítico separado.
- Registrar chance resolvida, scope, sorteio/fato e tier usados por cada impacto.
- Aplicar o multiplicador calculado na etapa correta da pipeline, depois da distribuição quando apropriado.
- Remover crítico automático por uma tag usada como substituto de sorteio no conteúdo novo.
- Testar 0%, frações, 100%, 150%, 200%, buff temporário, múltiplos acertos e limites inválidos.
- Preview produz faixa/probabilidades sem RNG nem alteração de estado; execução registra resultado real.

Gate: N impactos independentes não reutilizam acidentalmente o sorteio; flat/source scaling não é multiplicado N vezes; save/load/replay preservam fatos.

### Etapa 4 — Núcleo de conteúdo e pipelines do mundo 1

- Criar definições próprias do ato, modo e pipelines de magnitude/contagem/DoT conforme necessário.
- Unificar os bônus increased compatíveis; manter more/less explícitos.
- Configurar apenas energia para ações e remover mana vazia da apresentação deste setting.
- Rebater seis cartas atuais e montar o deck inicial simples.
- Produzir uma fatia inicial de cada uma das cinco famílias, compartilhando peças e usando os contratos de condensação/distribuição existentes.
- Auditar efeitos por impacto, DoT e consumo para evitar double dipping.

Gate: as cinco famílias conseguem demonstrar preparação, defesa e payoff em fixtures; nenhuma exige uma ferramenta ainda ausente para funcionar.

### Etapa 5 — Adversários, ciclos e encontros de referência

- Produzir primeiro 4 inimigos comuns e 6 templates que cubram as diferenças de alvo, setup e sustentação.
- Adicionar ações/gambits próprios, recursos de estado/ciclo declarados quando necessários e lock de intenção.
- Encontros multi-inimigo são parte da primeira fatia, não conteúdo para depois.
- Exercitar ataque, proteção, efeito residual, suporte e pressão anunciada.
- Ampliar a 8 comuns / 12 templates só após as primeiras lutas serem agradáveis.

Gate: não há só ataques iguais com vida diferente; preview e ação executada são consistentes, e não ocorre lockout permanente de uma família.

### Etapa 6 — Progressão local: recompensa, crafting, loja e descanso

- Declarar pools próprios, densidade de peças básicas e seleção com recusa.
- Adicionar oportunidades limitadas de transformar/trocar/retirar suportes com preview e custo.
- Preparação e cura competem por oportunidade quando apropriado.
- Completar serviços genéricos ausentes, como remoção paga, só se forem necessários para a economia aprovada.
- Impedir cura/compra/crafting duplicados pelo mesmo comando ou uso repetido indevido do nó.
- Validar moedas e custos usando ResourceSet e processadores canônicos, sem desconto no cliente.

Gate: é possível construir e corrigir uma direção sem receber tudo gratuitamente; nenhum caminho exige tirar exatamente uma rara para ser jogável.

### Etapa 7 — Grafo ramificado do mundo 1

- Criar 12–14 paradas por caminho e templates alternativos com convergências.
- Distribuir a curva de pressão, recuperações, loja e crafting.
- Garantir rota sem elite, recompensas legíveis e oportunidades mínimas para todo caminho.
- Implementar escolha seedada de template apenas se necessário; persistir o resultado na criação e validar reachability/repetições.
- Evitar recalcular mapa a cada consulta ou navegar automaticamente quando existe escolha real.

Gate: todos os caminhos chegam ao chefe, nenhum fica sem ação legal nem depende de UI para completar o grafo.

### Etapa 8 — Elites e chefe

- Criar 3 elites com competências distintas e adicionar risco/recompensa proporcional.
- Criar um chefe com ciclo e dois estados telegráficos; não usar invulnerabilidade como tampa genérica de scaling.
- Dar a cada família uma solução viável e mais de uma ferramenta possível para momentos perigosos.
- Balancear duração, picos e custos de vida considerando uma build razoável, não só a combinação perfeita.

Gate: tank e DoT podem vencer sem stall infinito; burst não elimina todas as interações; multi-hit não dispara procs sem orçamento; chefe ensina por derrota compreensível.

### Etapa 9 — Catálogo completo, híbridos e desbloqueios

- Completar as 30 cartas, 12 transformações, 8 relíquias e 5 eventos com fichas de função.
- Incluir pontes entre crítico/multi-hit, DoT/condensação e defesa/acúmulo, sem prescrever decks fechados.
- Definir pequeno catálogo declarativo de desbloqueios de opções, governado pela engine e separado por setting.
- Pool inicial já sustenta as cinco famílias. Desbloqueios acrescentam alternativas laterais, não +vida/+dano permanente nem uma carta obrigatória.
- Capturar elegibilidade no começo da run; simulações internas/sandbox não concedem progressão sem policy explícita.
- Testar que desbloquear mais cartas não dilui o pool básico a ponto de inviabilizar uma direção.

Gate: mudar setting não mistura progresso; desbloqueios não alteram run em andamento nem tornam replay dependente do perfil atual.

### Etapa 10 — Godot: apresentação voltada às decisões

- Usar os componentes compartilhados de carta, mão e tooltip existentes.
- Mapa exibe tipo de luta, elite, serviço e rotas disponíveis; descrever riscos sem prometer números não calculados.
- Mostrar vida persistente na jornada, cura prevista e custos de oportunidade.
- Inspector diferencia base/upgrades/scaling e resultado por impacto; manter base branco, redução vermelha, aumento azul e borda por raridade.
- Informar crítico por níveis, chances, faixa de resultado, payload residual e consumo sem transformar o corpo da carta em uma planilha.
- Crafting compara antes/depois e motivo de incompatibilidade.
- Intenções mostram multi-hit, dano previsto, status e preparation; avisos não ficam apenas no console.
- Resumo de derrota identifica última ameaça e consequências; histórico segue separado por setting.
- Inglês como fallback/chaves; tradução em português. Testar 1280×720, 1920×1080 e 2560×1080, teclado/mouse e controle.

Gate: regras não foram transferidas para GDScript; nenhum preview altera state/RNG; pause/tooltip/mão não voltam a apresentar regressões conhecidas.

### Etapa 11 — Balanceamento por sementes, builds e sessões humanas

- Banco inicial: pelo menos 50 seeds fixas, cobrindo rotas e combinações diferentes.
- Executar baseline, cinco famílias e alguns híbridos com políticas de teste declaradas; aumentar para centenas de seeds após corrigir erros estruturais.
- Comparar política simples com política mais competente; não chamar vitória de bot uma garantia de diversão ou solvabilidade humana.
- Comparar traces, percentis, duração e uso de peças; ajustar JSONs com pequenas hipóteses por iteração.
- Playtests observados com jogadores novos e experientes, perguntando por escolhas memoráveis, injustiça percebida e trechos repetitivos.
- Proibir ajuste reativo oculto que enfraqueça inimigos conforme a build; dificuldade/seed/regras permanecem declaradas.

Gate: meta de 25–40 minutos se confirma em sessões, cinco famílias possuem caminhos razoáveis e nenhum combo acessível banaliza sistematicamente o chefe.

### Etapa 12 — Gate final e publicação da demo do ato

- Jornada completa via API e Godot: criação, vários encontros, crafting, descanso, derrota/vitória e continuidade.
- Dez execuções do mesmo roteiro com mesma seed, revisão e elegibilidade: comparar hashes, fatos, escolhas e saídas, não só HP final.
- Reiniciar host/cliente no meio do ato e depois de transformação; validar branches/replay conforme capabilities.
- Confirmar abandono/derrota/conclusão e desbloqueios idempotentes.
- Medir latência de comando/resposta útil durante todo o ato: p95 abaixo de 1 segundo na máquina-alvo, distinguindo host frio, processamento, payload e animação da Godot.
- Atualizar launcher, launch do setting, docs de conteúdo/regras e roteiro de teste. Preservar saves históricos e sua revisão.

Gate: primeiro ato navegável, conteúdo validado, sem duplicação de autoridade, sem migração implícita e com relatório de balanceamento/playtests.

## 11. Critérios de conclusão do mundo 1

- Tentativa completa com objetivo e chefe; nada exige criar mundo 2 para funcionar.
- Rotas têm diferenças reais de risco, correção e recompensa.
- Pelo menos cinco famílias sustentadas e combinações híbridas possíveis.
- Todas podem perder por decisões identificáveis e vencer sem ferramenta única obrigatória.
- Vida persiste e recuperação não é gratuita nem infinita.
- Crítico multinível é regra executada e registrada, não flavor de uma carta com multiplicador fixo.
- DoT contínuo e condensado são escolhas distintas; consumo não duplica payload.
- Números refletem custos e oportunidades; raridade não substitui função.
- Chefes interagem com builds fortes; inimigos não são apenas recipientes de vida.
- Engine resolve regras via JSON/REST; UI explica e apresenta.
- Desbloqueios são opções e permanecem por setting, sem aumentar poder inicial permanentemente.
- Mesmos inputs determinísticos geram a mesma jornada de regras; RNG do combate é local à run e nunca ao frame da interface.
- Playtests e medições confirmam compreensão, ritmo e diversidade — testes técnicos sozinhos não cumprem este critério.

## 12. Ordem prática resumida

Primeiro corrigir o fundamento que muda o balanceamento inteiro: persistência de vida, transporte de atributos e crítico real. Depois provar uma fatia pequena das cinco famílias e das ameaças. Em seguida expandir recompensas, grafo, elites/chefe e catálogo. Por fim completar desbloqueios, apresentação e o ciclo de playtests.

Não iniciar preenchendo dezenas de JSONs com inimigos mais resistentes sobre regras ainda inconsistentes. Cada etapa deve ter um commit focado e verificação proporcional quando sua implementação for autorizada; neste momento foi produzido apenas este plano.
