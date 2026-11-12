# Distribuição da tela de combate do MVP

## Objetivo

Aproximar a distribuição dos componentes da cena de combate do Slay the Robot, preservando o tema e os componentes existentes do MVP.

A referência foi examinada na revisão 4dac5abe0d19382f8ace2cec5ed3fd77e2a0e11d, especialmente em https://github.com/DesirePathGames/Slay-The-Robot/blob/4dac5abe0d19382f8ace2cec5ed3fd77e2a0e11d/scenes/Root.tscn. As posições exatas da referência foram criadas para uma resolução fixa de 1200 × 700 e devem ser adaptadas ao layout responsivo do MVP.

## Modelo proposto

- Faixa superior com o retrato e a identidade do herói à esquerda, rodada e turno alinhados ao ponto médio do espaço entre os blocos do herói e dos inimigos, diário e inspeção à direita, e Pause no extremo direito.
- Campo de batalha no centro. Herói e aliados ocupam a área à esquerda; inimigos, a área à direita. Vida, bloqueio, estados e intenções continuam junto ao respectivo personagem.
- Mão centralizada na borda inferior, com as cartas e suas interações atuais.
- Energia entre o campo de batalha e a mão, alinhada verticalmente com a rodada no ponto médio do espaço entre os blocos do herói e dos inimigos. O eixo deve ser recalculado quando o tamanho ou a distribuição dos blocos mudar. Pilha de compra no canto inferior esquerdo.
- Descarte e exílio agrupados no canto inferior direito. Finalizar turno permanece no lado direito, com área própria e sem sobreposição com as cartas.
- Acesso ao deck no grupo de controles superiores, separado das pilhas transitórias.
- Elementos de seleção de alvos, confirmação, prévia, efeitos e reconexão usam o espaço livre entre o campo e os controles, sem cobrir informações essenciais.

## Execução

1. Identificar os componentes e sinais existentes do cabeçalho, pilhas, energia, campo de batalha, mão e controles de turno.
2. Reorganizar a cena do MVP para distribuir os componentes conforme o modelo, preservando as instâncias e seus sinais. A ordem e a visibilidade das pilhas devem continuar vindo do estado publicado.
3. Ajustar as posições responsivas para resoluções 1280 × 720, 1280 × 800, 1920 × 1080 e ultrawide. Prever várias cartas e vários inimigos sem sobreposição.
4. Verificar mão vazia, arrastar e selecionar cartas, foco por teclado e controle, inspeção de pilhas, estados de resolução de ações e texto ampliado.
5. Gerar capturas reais do MVP e revisar a distribuição visualmente antes de finalizar.

## Critérios

- A interface ocupa a área disponível e mantém os elementos dentro da janela.
- Retrato do herói fica à esquerda, Pause à direita, e energia centralizada entre campo de batalha e mão. Pilhas e finalizar turno permanecem identificáveis e acessíveis.
- A mão e os componentes do campo não mudam de posição ao esvaziar a mão.
- A carta mantém suas proporções e os personagens permanecem legíveis.
- Não se criam recursos de combate nem valores que a engine não tenha publicado.
