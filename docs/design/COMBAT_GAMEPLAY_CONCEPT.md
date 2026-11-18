# Conceito de interface de combate

Este documento registra o conceito visual de combate inspirado no direcionamento de HeroScript: um RPG narrativo de cartas com duelos táticos, personalização de builds e planejamento de turnos. O conceito é uma referência de interface; personagens, inimigos, textos e números da imagem são placeholders.

Referência visual: [Imagem do conceito](combat-gameplay-concept.png).

## Identidade visual

Usar uma estética **preto e branco com programmer art**: formas simples, ícones geométricos, contraste claro e elementos visuais fáceis de substituir. Aplicar essa linguagem aos menus, às animações e à interface de gameplay, priorizando o minimalismo e a legibilidade das informações e decisões de combate. O cenário deve ser discreto e preservar o contraste dos elementos interativos. A arte dos personagens e inimigos será definida posteriormente.

A imagem de referência ilustra a composição e ainda contém cores e artes que não definem a estética final.

## Proporções e espaçamento

A composição deve ser projetada e refinada prioritariamente para **16:9** neste primeiro momento. Em outras resoluções e proporções, inclusive ultrawide, usar **espaçamento uniforme entre os componentes** como solução temporária, mantendo a leitura, a interação e os elementos dentro da área visível. Essa adaptação provisória deverá ser revisada quando houver um projeto específico para outros formatos.

## Cabeçalho

- **Nome do herói:** aparece na região superior esquerda, no lugar do nome do jogo mostrado na imagem conceitual. O conteúdo vem do personagem atual.
- **Rodada:** identificada de forma destacada.
- **Timeline e pausa:** acessíveis no cabeçalho. Os marcadores devem indicar claramente sua função, sem confundir progresso com ordem de ações.

## Campo de batalha

O campo ocupa a região central, com herói e inimigo em lados opostos. As representações visuais são provisórias e não determinam a arte final. Os indicadores de vida e escudo persistente ficam associados ao respectivo combatente. Status e recursos adicionais aparecem quando relevantes.

A intenção do inimigo deve ser visível antes da decisão do jogador. Ícone e valor podem permanecer aparentes; condições e detalhes extensos podem aparecer ao passar o mouse ou selecionar o inimigo.

### Atributos do herói

O painel de atributos é flutuante e recolhível, acessível pela borda esquerda e pelo atalho configurado. Sua abertura não desloca o campo nem a mão. Vida, escudo e recursos necessários para a decisão permanecem acessíveis durante o combate.

### Companion

O companion é representado como presença secundária, sem barra de vida própria. Seu gatilho, estado e recarga são consultáveis; uma apresentação compacta pode mostrar o ícone e a recarga, com detalhes em tooltip. O conceito visual não pressupõe que a funcionalidade já esteja implementada.

## Mão e energia

A energia aparece próxima à mão. As cartas ficam na parte inferior, em leque adaptativo, com sobreposição que preserve a leitura dos elementos importantes. O destaque de hover e seleção deve ser claro e respeitar as configurações de acessibilidade.

Cada carta apresenta:

- Nome no cabeçalho e custo no **canto superior direito**.
- Tags de origem diretamente abaixo do nome.
- Área de arte substituível.
- Função e descrição do efeito com valores legíveis.
- Marcadores relevantes na própria carta, como Brasas acumuladas por uma carta de Fogo retida.

A retenção de cartas e os marcadores de Brasa são transmitidos na própria mão; não há barra de Calor. As cartas e seus efeitos exibidos na imagem são exemplos, não especificações definitivas de conteúdo.

## Pilhas e ações

Os acessos a compra, descarte, exílio e demais zonas publicadas pelo setting ficam em uma coluna à direita da mão, com ícones e contagens. Ao abrir uma pilha, mostrar as cartas em um container de inspeção.

O botão **Encerrar turno** fica destacado nessa região. Controles como cancelar seleção, avançar animação ou escolher um custo aparecem apenas quando pertinentes. O comportamento e as opções disponíveis vêm da engine.

## Cuidados de implementação

- Evitar painéis permanentes extensos que comprimam o campo ou cubram cartas.
- Priorizar a composição em 16:9; nas demais proporções, usar temporariamente espaçamento uniforme entre os componentes.
- Garantir leitura e interação com mouse, teclado e controle, com escala de texto e movimento reduzido.
- Manter o cliente visual responsável pela apresentação e entrada; regras e resultados continuam sob responsabilidade da engine.
- Validar o conceito com diferentes quantidades de cartas, textos longos, status e configurações de cenário.
