Tipo de documento: especificação visual preliminar de layout (HUD) para implementação de interface de jogo.
Base de referência: captura fornecida pelo usuário, resolução original de 1024 × 572 px (proporção próxima de 16:9).
Resolução-alvo: 1920 × 1080 px (16:9).
Precisão: medidas estimadas visualmente. Não constituem dimensões oficiais dos assets ou dados extraídos do código-fonte do jogo.

1. Sobre a interface
A imagem mostra a tela de um jogo de combate por turnos com cartas. O jogador controla o Pyromancer, acompanhado por um Water Elemental, em confronto com um Stone Golem. A interface distribui informações em quatro áreas: cabeçalho de turno, pilhas de cartas à esquerda, campo de batalha central e mão de cartas na parte inferior. À direita, o botão End Turn encerra o turno. As informações visíveis incluem rodada, personagem ativo, ativações restantes, pontos de vida (HP), bloqueio, energia e intenções de ação.
Objetivo deste documento
- Registrar uma aproximação das coordenadas e dimensões dos elementos na tela.
- Padronizar o posicionamento na proporção 16:9.
- Oferecer uma referência para reproduzir ou adaptar o HUD em uma interface própria.
- Identificar limites da medição, especialmente sobreposições, rotação e cortes nas bordas.
2. Convenção de coordenadas
Cada componente é descrito por quatro parâmetros relativos à área útil de 16:9:
- X: distância horizontal da borda esquerda até o canto superior esquerdo do componente.
- Y: distância vertical da borda superior até o canto superior esquerdo do componente.
- L: largura visível aproximada do componente.
- A: altura visível aproximada do componente.
As porcentagens usam a tela inteira como referência, e não a área do personagem ou o painel pai. Em 1920 × 1080, a conversão é:
x_px = X% × 1920 / 100
y_px = Y% × 1080 / 100
largura_px = L% × 1920 / 100
altura_px = A% × 1080 / 100
Os valores em pixels foram arredondados ao inteiro mais próximo.
3. Cabeçalho superior
Componente	X (%)	Y (%)	L (%)	A (%)	X (px)	Y (px)	L (px)	A (px)
Retrato do Pyromancer	1,3	0,9	6,7	11,7	25	10	129	126
Nome e ativações	8,6	1,7	27,9	7,7	165	18	536	83
ROUND 1	39,2	0,3	21,4	10,5	753	3	411	113
Turn: Pyromancer	42,0	10,3	16,3	5,2	806	111	313	56
Combat Log	69,1	1,6	14,1	9,1	1327	17	271	98
Lupa	83,6	1,6	5,2	9,1	1605	17	100	98
Pause	89,4	1,6	10,2	9,3	1716	17	196	100


O que representam os componentes:
- Retrato do Pyromancer: Imagem do personagem atualmente em turno.
- Nome e ativações: Identifica o personagem e mostra as ativações restantes.
- ROUND 1: Exibe o número da rodada atual.
- Turn: Pyromancer: Identifica de quem é o turno.
- Combat Log: Botão de acesso ao registro de acontecimentos do combate.
- Lupa: Botão com ícone de pesquisa ou inspeção; função exata não confirmada apenas pela imagem.
- Pause: Pausa ou abre o menu associado à tecla Escape.
4. Barra lateral esquerda
Componente	X (%)	Y (%)	L (%)	A (%)	X (px)	Y (px)	L (px)	A (px)
Draw	0,9	38,1	9,7	13,5	17	411	186	146
Exile	1,0	52,4	9,5	14,9	19	566	182	161
Discard	1,1	69,2	9,3	13,3	21	747	179	144
Owned Deck	1,0	83,2	9,2	16,3	19	899	177	176


O que representam os componentes:
- Draw: Pilha de compra, com contador visível de 15 cartas.
- Exile: Zona de cartas exiladas, com contador visível de 0.
- Discard: Pilha de descarte, com contador visível de 0.
- Owned Deck: Acesso ou visualização do baralho possuído.
5. Personagens e combate
Componente	X (%)	Y (%)	L (%)	A (%)	X (px)	Y (px)	L (px)	A (px)
Pyromancer — arte	14,4	26,4	13,5	28,1	276	285	259	303
Pyromancer — painel	12,7	53,1	14,2	14,0	244	573	273	151
Water Elemental — arte	26,1	26,9	18,7	26,0	501	291	359	281
Water Elemental — painel	28,8	53,7	14,5	14,5	553	580	278	157
Stone Golem — arte	69,0	21,7	23,1	39,7	1325	234	444	429
Stone Golem — painel	68,6	53,1	18,1	13,8	1317	573	348	149
Indicador de energia	45,7	58,6	7,9	13,1	877	633	152	141


O que representam os componentes:
- Pyromancer — arte: Representação do personagem aliado/jogável.
- Pyromancer — painel: Nome, HP 100/100, bloqueio e energia.
- Water Elemental — arte: Representação da unidade aliada mostrada ao lado do Pyromancer.
- Water Elemental — painel: Nome, HP 20/20, bloqueio e intenção.
- Stone Golem — arte: Representação do inimigo.
- Stone Golem — painel: Nome, HP 55/55, bloqueio, energia e intenção de ataque.
- Indicador de energia: Orbe central e marcador ENERGY 3/3, indicando o recurso disponível no turno.
6. Mão de cartas e ação de turno
Componente	X (%)	Y (%)	L (%)	A (%)	X (px)	Y (px)	L (px)	A (px)
Incinerate	19,3	72,2	14,4	27,8	371	780	276	300
Flame Blade	30,8	71,2	15,6	28,8	591	769	300	311
Rest	44,0	71,3	12,1	28,7	845	770	232	310
Void Release	56,0	71,9	13,5	28,1	1075	777	259	303
Infernal Ember	67,4	73,1	13,0	26,9	1294	789	250	291
End Turn	83,7	86,7	16,3	12,1	1607	936	313	131


O que representam os componentes:
- Incinerate: Carta da mão: aplica queimadura.
- Flame Blade: Carta da mão: dano e queimadura.
- Rest: Carta da mão: recupera vida e fornece bloqueio.
- Void Release: Carta da mão: cancela bônus e causa dano de caos.
- Infernal Ember: Carta da mão: causa dano e aplica queimadura.
- End Turn: Botão de encerramento do turno atual.
7. Organização e relações de layout
1. Cabeçalho: acompanha as bordas superiores; rodada e turno ficam centralizados, enquanto ações secundárias ficam à direita.
2. Pilhas de cartas: ficam ancoradas na borda esquerda, em coluna. Seus contadores são partes internas sobrepostas e não foram medidos separadamente.
3. Unidades em combate: as artes ficam acima dos painéis de informações. Não é necessário que ambos tenham a mesma largura.
4. Energia: fica aproximadamente no centro horizontal, logo acima da mão de cartas.
5. Mão: cartas se sobrepõem e possuem pequenas inclinações. O retângulo informado corresponde à área visível projetada na captura, não ao arquivo original da carta sem rotação.
6. Fim de turno: é ancorado no canto inferior direito.
8. Orientações de implementação
Estratégia recomendada
Utilize uma área lógica de 1920 × 1080, escalada de forma uniforme, para preservar a proporção de cartas, tipografia, molduras e personagens. Quando a tela física não for 16:9, prefira centralizar essa área e usar faixas laterais/superiores ou uma estratégia específica de adaptação, em vez de esticar os elementos individualmente.
Exemplo simples de posicionamento por porcentagem
.combat-screen {
  position: relative;
  width: 100%;
  aspect-ratio: 16 / 9;
}

.end-turn {
  position: absolute;
  left: 83.7%;
  top: 86.7%;
  width: 16.3%;
  height: 12.1%;
}
Atenção: porcentagens independentes de largura/altura preservam posições relativas, mas não garantem preservação do tamanho dos elementos caso o contêiner não permaneça em 16:9. Para um HUD final, escale a área inteira por um único fator.
Camadas sugeridas (do fundo para a frente)
1. Cenário / background.
2. Efeitos ambientais e partículas.
3. Artes dos personagens e inimigos.
4. Painéis de HP, bloqueio, energia e intenção.
5. Indicador de energia central e mão de cartas.
6. Controles de HUD, botões e textos sobrepostos.
9. Limitações e cuidados
- A captura tem 1024 × 572, proporção de aproximadamente 1,790:1, ligeiramente diferente do 16:9 = 1,778:1. A normalização para 1920 × 1080 implica um pequeno ajuste vertical.
- Áreas de arte com brilho, sombra e partículas dificultam a determinação exata do limite de cada sprite.
- As cartas inferiores estão parcialmente cortadas pela borda da captura; a altura informada é visível, não necessariamente total.
- A inclinação das cartas pode fazer suas caixas delimitadoras variar conforme a animação ou o estado do hover.
- Ícones menores, barras internas, contadores, efeitos e espaçamentos tipográficos não foram medidos individualmente.
- As posições representam esta captura específica, e não garantem o comportamento do layout em outros estados do jogo.
10. Próximo nível de detalhamento
Para transformar a especificação preliminar em um guia de produção, vale medir também os elementos internos de cada painel (barra de HP, nome, ícones, intenção, bordas e padding), além do tamanho de carta sem rotação e dos seus estados de hover, seleção e arraste.