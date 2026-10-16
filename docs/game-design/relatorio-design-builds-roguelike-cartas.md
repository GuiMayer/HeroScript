# Filosofia de design de sistemas de build em roguelikes de cartas

*Relatório de pesquisa sobre Path of Exile, Warframe, Transistor, Balatro, Slay the Spire e Hades, aplicado a um roguelike de cartas com builds persistentes e atributos que escalam o baralho.*

*Data da pesquisa: 15 de outubro de 2026.*

**Nota sobre as fontes:** a base são entrevistas, palestras e posts de designers (fontes primárias). Em alguns pontos só consegui ler resumos de palestras, ou fóruns e blogs de jogadores. Esses casos estão marcados. A seção "Aplicação ao seu jogo" é análise minha, não das fontes.

---

## Tese central

Os sistemas de build mais bem-sucedidos seguem quatro princípios:

1. **Escassez estruturada.** O jogador sempre quer mais do que cabe.
2. **Sinergia por categorias amplas**, nunca por pares específicos.
3. **Fricção baixa para trocar, profundidade alta para combinar.**
4. **O jogador precisa sentir que a build é dele.**

---

## 1. Escassez estruturada (Warframe)

No Warframe, cada equipamento tem uma capacidade limitada, e cada mod consome parte dela. Quando a polaridade do mod combina com a do slot, o custo cai pela metade. Slots incompatíveis encarecem o mod em cerca de 25%. Mods de aura aumentam a capacidade em vez de consumi-la.

Isso cria duas camadas de decisão: "quais mods eu quero" e "quais eu consigo pagar". Sem o orçamento, o jogador empilharia tudo que é bom. Sem a polaridade, o orçamento seria só uma conta de somar. A polaridade transforma a montagem em quebra-cabeça espacial.

---

## 2. Comprometimento gera propriedade (Path of Exile)

**Respec difícil de propósito.** Chris Wilson, fundador da GGG, explicou que a árvore de passivas dificulta o respec para que o jogador sinta posse sobre o personagem. Ele também comparou a customização do jogo à do Magic: The Gathering.

**Mas sem fricção inútil.** Em 2019, Jonathan Rogers disse que foi um erro prender sockets e cores aos itens. No PoE 2, os sockets passaram para dentro das próprias gemas de skill, e a imprensa registrou que a mudança torna o sistema mais amigável sem cortar opções.

**Itens e build.** O designer Laz, de itens, apontou que itens obrigatórios para uma build e inúteis para as outras não agregam valor, e que os "habilitadores" de build devem ser acessíveis cedo.

**Arquitetura matemática.** Dentro de um mesmo balde (modificadores "increased"), os bônus se somam e têm retorno decrescente. Entre baldes diferentes ("more"), eles se multiplicam. Os "increased" dão bom retorno com pouco investimento, e os "more" vêm principalmente de support gems. Esse desenho faz o jogador buscar variedade de fontes em vez de empilhar uma só.

**Poder e risco.** O objetivo declarado da GGG para o PoE 2 é que builds poderosas deem a sensação de estar quebrando o jogo, mas com inimigos que continuam punindo erros.

---

## 3. Modularidade que se traduz para cartas (Transistor)

O artigo de Amir Rao, diretor de estúdio da Supergiant, é a fonte mais próxima do seu projeto. O sistema de funções nasceu da paixão por card games colecionáveis, com um baralho de habilidades embaralhado a cada fase. O embaralhamento foi abandonado porque não tinha justificativa narrativa natural e atrapalhava o balanceamento de dificuldade.

Duas decisões valem para você:

- **Cada função serve de três formas.** Pode ser ativa, upgrade de outra função ou passiva. Isso permitiu criar 16 conceitos fortes e deixar o jogador combiná-los de forma criativa.
- **Sem power-leveling de uma função só.** O sistema se concentra em pares, trios e na relação entre eles, não em subir o nível de uma habilidade favorita.

**Trocar de build sob pressão.** Quando a vida acabava, o jogador perdia a função mais valiosa. Isso levava muitos a testar combinações novas, e frequentemente eles continuavam com elas mesmo depois de recuperar as antigas. Rao reconheceu o erro de querer "obrigar" o jogador a experimentar. A equipe passou a apostar em riqueza suficiente para convidar à experimentação, sem punir quem prefere o que conhece.

---

## 4. Sinergia por categoria (Balatro e Slay the Spire)

O LocalThunk, criador do Balatro, publicou as regras que usa para desenhar Jokers. As que importam aqui:

- **Sinergizar com palavras-chave e atributos gerais, não com cartas específicas.** Isso evita que as sinergias fiquem raras ou diluídas à medida que o pool cresce, e gera combinações emergentes entre cartas que nunca foram pensadas juntas.
- **Trabalhar com as mecânicas existentes**, em vez de criar uma mecânica nova a cada carta.
- **Efeitos poderosos devem ser condicionais**, para que as escolhas A/B continuem difíceis.
- **A carta nem sempre deve ser a escolha certa** numa run comum.
- **Descrições curtas e simples.** Uma ideia que não cabe em poucas linhas costuma ter pouco potencial de sinergia.

**Slay the Spire.** Segundo resumos da palestra de Anthony Giovannetti (GDC 2019), toda carta deve ter seu lugar, e o fato de ser single-player permite combos raros muito fortes sem prejudicar outros jogadores. Ele também alerta que métricas podem enganar se forem mal interpretadas. *(Só consegui ler resumos, não o vídeo.)*

**Hades.** Greg Kasavin comentou que você pode empurrar uma run para certa build, mas a aleatoriedade luta contra isso. Em outra entrevista, ele disse que o jogo precisa de combinações realmente poderosas que levam a runs memoráveis.

---

## 5. Quem é o seu jogador (Rosewater, Magic)

Mark Rosewater descreve o perfil "Johnny": quem gosta de deckbuilding tanto quanto (ou mais que) de jogar, e quer mostrar criatividade vencendo com cartas que ninguém usa. O "Spike" quer provar competência.

O seu jogo é feito principalmente para o Johnny, mas funciona melhor se tiver um teto de otimização (Spike) e espaço para builds estranhas que funcionam (Johnny).

---

## 6. Meta-progressão: um debate ainda aberto

O sentimento informal da comunidade é que progressão **horizontal** (mais opções) é mais saudável que **vertical** (mais poder). Um blog técnico resume: um jogador na centésima run deveria ter mais opções, não ser dez vezes mais forte. Uma discussão de jogadores faz uma crítica relacionada: a meta-progressão borra a autoavaliação, porque você não sabe se venceu por habilidade ou por poder acumulado.

*Atenção: essas são fontes fracas (blog e fórum), não pesquisa formal de design. Servem como indício do sentimento dos jogadores.*

---

## 7. Estudo de caso: Shards of Order

O jogo lançou em 28 de julho de 2026. Tem três heróis que compartilham um único baralho, e cada carta adicionada altera as chances de compra. O combate usa contadores de tempo no lugar de turnos e mana.

Uma análise agregada das reviews da Steam aponta, entre os pontos negativos, que os jogadores se sentem forçados a usar builds específicas para vencer os chefes, o que torna a experimentação pouco recompensadora. Uma review também menciona que dá para refundir pontos de habilidade e resetar a árvore para achar combos contra chefes difíceis.

É o risco clássico: se o chefe exige uma build, o jogo vira um quebra-cabeça com resposta única, e a liberdade vira ilusão. Em um sistema de builds, a pergunta de design mais importante é se existem vários caminhos viáveis para cada desafio.

---

## Aplicação ao seu jogo (análise minha, não das fontes)

Você escolheu build flexível durante o combate. Isso conversa bem com o Transistor e com o orçamento do Warframe, e resolve o problema das runs direcionadas.

1. **Troca de build com custo.** Se a troca for gratuita, o jogador sempre vai para a configuração ótima contra cada inimigo e a escolha some. O custo pode ser tempo, energia ou um turno. O orçamento de capacidade do Warframe é a base: como não dá para ter tudo equipado, a troca vira decisão tática.
2. **Poucos baldes de multiplicação, bem visíveis.** Dois ou três, e a interface mostra o valor final já calculado, com a origem de cada bônus ao passar o mouse. Complexidade escondida vira frustração.
3. **Tags amplas e sinergia por tag**, seguindo as regras do LocalThunk. Evite cartas ou mods que dependem de um par específico.
4. **Teste cada chefe contra vários arquétipos.** Se só uma configuração vence, o sistema falhou. Métricas de run ajudam, mas leia com cuidado.
5. **Respec barato fora do combate e custo dentro dele.** Isso concilia a "propriedade" do Wilson com a baixa fricção que a GGG buscou ao mover os sockets.
6. **Teto de poder contido na persistência.** Se builds persistentes crescem sem limite, você cria o problema vertical que os jogadores criticam. Prefira mods que dão opções novas a mods que só dão números maiores.

---

## Fontes

**Warframe**
- Wiki oficial, sistema de mods: https://wiki.warframe.com/w/Mod
- Guia de mods (Digital Extremes): https://www.warframe.com/news/mods-guide

**Path of Exile**
- Entrevista com Chris Wilson (Kotaku, via GameBanshee): https://gamebanshee.com/ur63
- Entrevista com o designer Laz (IncGamers, via GameBanshee): https://gamebanshee.com/9cw4
- ExileCon 2019, sistema de skills do PoE 2 (MassivelyOP): https://massivelyop.com/2019/11/21/exilecon-2019-path-of-exile-2s-skill-system-reboot-and-ascendancy-classes/
- Meta gems e simplificação no PoE 2 (PC Gamer): https://tech.yahoo.com/general/articles/path-exile-2-pushes-action-235124462.html
- Builds "quebradas" no PoE 2 (Mein-MMO, sobre entrevista à GamesRadar): https://mein-mmo.de/poe2-skills-broken/
- Escalonamento de dano (Maxroll): https://maxroll.gg/poe2/getting-started/damage-scaling

**Transistor e Hades (Supergiant)**
- Game Design Deep Dive: The Functions of Transistor (Amir Rao): https://www.gamedeveloper.com/design/game-design-deep-dive-the-functions-of-i-transistor-i-
- Entrevista com Greg Kasavin (GameSpot): https://gamespot.com/articles/hades-changes-what-it-means-to-be-a-roguelike/1100-6483420/
- Boons do Hades 2 (GamesRadar): https://www.inkl.com/news/hades-2-lead-says-the-best-boons-from-early-access-weren-t-killed-just-reskinned-we-think-it-s-important-not-to-be-too-precious-about-these-things

**Balatro e Slay the Spire**
- Guidelines de design de Jokers (LocalThunk, post no Reddit): https://lr.sudovanilla.org/r/balatro/comments/1czo9g0/guidelines_for_joker_design
- GDC 2019, Slay the Spire: Metrics Driven Design and Balance (resumos): https://glasp.co/youtube/7rqfbvnO_H0 e https://podwise.ai/episodes/1459444

**Magic: The Gathering**
- Timmy, Johnny and Spike (Mark Rosewater): https://magic.wizards.com/en/news/making-magic/timmy-johnny-and-spike

**Meta-progressão (fontes de comunidade)**
- Blog sobre arquitetura de roguelikes: https://www.strayspark.studio/blog/building-roguelike-ue5-procedural-progression
- Discussão de jogadores sobre meta-progressão: https://scribe.disroot.org/post/7032582

**Shards of Order**
- Página do jogo (Time Extension): https://www.timeextension.com/games/pc/shards-of-order
- Análise agregada de reviews da Steam (Vaporlens): https://vaporlens.app/app/3432480/shards_of_order
- Review (Turn Based Lovers): https://turnbasedlovers.com/review/shards-of-order-impressions/
- Matéria original que mencionou a comparação com PoE e Slay the Spire (Game Rant): https://gamerant.com/steam-games-like-path-of-exile-slay-the-spire-rpg/
