# Revamp visual: programmer art em marfim

Data: 2026-10-23. Estado: plano para implementação.

## Objetivo e limite

Dar ao protótipo uma linguagem visual simples, consistente e deliberada: formas vetoriais, silhuetas geométricas, superfícies planas e circuitos finos com curvas angulares de 45°. O resultado deve comunicar um projeto de programação de jogos com identidade própria e leitura clara.

O trabalho altera somente aparência. A composição atual de cada tela é a referência obrigatória. Posições, tamanhos dos controles, alinhamentos, espaçamentos, ordem dos elementos, áreas clicáveis, scroll, comportamento responsivo, camadas e navegação permanecem iguais. Conteúdo textual e informações exibidas permanecem iguais.

Também permanecem iguais as animações: gatilhos, duração, velocidade, trajetórias, escala, rotação, easing, fan da mão, hover, seleção, compra, descarte, exílio, pausa e movimento reduzido. Os três shaders recentemente adicionados ao laboratório continuam com seu escopo atual; este plano não os habilita na run.

Engine, API, saves, configurações de gameplay, determinismo e imutabilidade ficam fora das alterações.

## Estado atual verificado

- `VisualContext` já fornece tema, estilo de cartas e resolução de arte por contexto/setting.
- `VisualThemeProfile` e `ViewStyleFactory` concentram cores e parte dos estilos nativos da Godot.
- `ivory_run.tres` e `ivory_theme.tres` já compartilham a direção marfim, preto e vermelhão.
- `CardVisualStyle` mistura propriedades visuais com métricas de layout. Sua alteração exige distinguir os dois grupos.
- `ivory_run_art_manifest.json` já mapeia muitas cartas para famílias SVG, mas há reutilização ampla e pouca diferenciação entre identidades.
- Atores e fundos são resolvidos pelo `SceneryVisualProfile`; demais artes usam o perfil de UI. Atualizar apenas o catálogo da UI não garante a troca do cenário.
- `ArtSlot` e `CardView` são componentes compartilhados entre laboratório e jogo. O revamp deve usar essa fronteira existente.
- O projeto tem ícones e desenho procedural em componentes especializados, além dos arquivos de arte. O inventário precisa alcançar esses caminhos.

Não é necessário reestruturar a arquitetura gráfica para iniciar este trabalho. Eventuais propriedades visuais ausentes podem ser expostas nos Resources existentes, com valores iniciais equivalentes aos atuais.

## Direção visual

### Paleta

| Papel | Diretriz inicial |
| --- | --- |
| Fundo | Marfim atual `#f2ebdd` |
| Superfície clara | `#faf5eb` |
| Superfície secundária | Bege atual `#eee4d1` |
| Texto/silhueta principal | Quase preto `#20201c` |
| Texto secundário | Cinza quente `#5b574f` |
| Destaque de interação | Vermelhão `#b83e25` |
| Cores semânticas | Preservar identificação de recursos, raridades e comparação de valores |

Concentrar os valores em papéis do tema. O marfim deve ser plano e limpo, sem envelhecimento, sujeira ou textura decorativa. Vermelhão aparece em ações e detalhes focais; não deve ocupar todas as bordas.

Manter a convenção existente das cartas: raridade na borda, valor base branco sobre a superfície escura de efeitos, redução vermelha e aumento azul. Não reaproveitar essas cores para comunicar outro significado dentro do mesmo componente.

### Formas e traço

- Vocabulário: círculos, retângulos, losangos, polígonos simples e segmentos a 45°.
- Duas espessuras de traço de referência para arte SVG: 2 e 4 unidades num viewBox de 128 × 128; validar sua leitura no tamanho real.
- Circuitos: grupos de três linhas finas e próximas, segmentos paralelos e uma ou duas mudanças de direção por motivo.
- Decoração restrita às regiões que já recebem arte/ornamento. Preservar suas dimensões e não criar novas faixas ou painéis.
- Evitar contornos duplos ornamentais, filigranas, brilho permanente, relevos e sombras pintadas. Sombras necessárias à leitura conservam sua área atual.
- Traços e preenchimentos devem ter contraste suficiente sobre marfim, inclusive nos estados indisponíveis.

### Tipografia

Preservar inicialmente as fontes, tamanhos, pesos, entrelinhas e capitalização atuais. Trocar a fonte pode mudar quebra de linha, largura mínima e altura dos containers; fica fora deste revamp de geometria fixa. A personalidade técnica virá dos símbolos e materiais visuais.

## Catálogo visual proposto

### Cartas

Preservar a anatomia inteira: título, custo, arte, tipo, raridade, efeitos, condições e modificações em suas posições atuais.

Criar uma família de pictogramas com silhuetas legíveis: impacto, guarda, recuperação, energia, aplicação de stacks, condensação, múltiplos acertos, precisão e recuperação de cartas. Usar os IDs publicados e o catálogo para associar arte; nenhuma regra será deduzida pela UI.

As cartas terão variações visuais por composição dentro da mesma caixa: direção de um corte, disposição de segmentos, número de elementos decorativos ou combinação de dois símbolos. Essas variações identificam a carta; não afirmam quantidade de acertos, stacks ou efeito calculado. Números continuam vindo dos textos e fatos da engine.

O mapeamento deve ser explícito e estável. Não gerar uma arte diferente a cada renderização nem usar RNG da run. Um gerador de SVG durante a produção dos assets é aceitável; o jogo carrega o catálogo resultante.

### Personagens e inimigos

Substituir desenhos inconsistentes por figuras geométricas construídas com poucos volumes planos. Proposta inicial: personagem como silhueta articulada com núcleo; inimigos leves como núcleos suspensos; defensores como blocos/escudos; chefes como composição maior da mesma família.

São categorias de direção de arte, associadas explicitamente aos IDs existentes. Não implementar classificação de comportamento no cliente.

Preservar canvas, aspect ratio, posição dos pés/pivô visual e envelope opaco de cada arte. A troca não pode aumentar o ator, deslocá-lo ou alterar a área percebida de seleção. Preservar separação entre arte e hitbox.

### Cenário e retratos

Dentro do fundo atual, usar planos discretos, linhas de chão e estruturas geométricas. Preservar distribuição de massas, linha de chão, espaços vazios e contraste atrás dos HUDs. Simplificar sem reposicionar a composição.

Retratos usam recortes da mesma família dos atores, nos slots existentes. Diálogos mantêm retrato, texto e escolhas exatamente onde estão.

### Ícones e objetos

Unificar ícones de pausa, pilhas, mapa, histórico, configurações, recursos, status, relíquias e inscrições na mesma família de traço.

Preservar forma reconhecível de recursos e anatomia do custo: símbolo preenchido escuro, número branco sobreposto, proporção 1:1, tamanho e canto atuais. Ícones não devem conter números decorativos que possam ser confundidos com valores reais.

Relíquias, inscrições e modificadores de cartas precisam de símbolos visualmente distintos, mantendo as três categorias reconhecíveis. Usar o mecanismo de catálogo já disponível em cada consumidor; não criar outro sistema de inventário ou descrição.

## Etapas sequenciais

### 1. Registrar o baseline e o inventário

**Trabalho:** capturar as telas atuais com fixtures estáveis e registrar os assets de fato usados, incluindo fallbacks, overrides por setting, SVGs, desenho procedural e cores locais. Conferir o estado das alterações locais existentes antes de editar.

**Cenas mínimas:** menu, configurações, pausa, laboratório de cartas, combate com mão cheia/duas cartas/vazia, mapa com painel aberto/fechado, diálogo, recompensa, loja, First Forge, loadout, histórico e replay.

**Referência:** 1280 × 720, 1920 × 1080 e 2560 × 1080; EN/PT e um caso de texto ampliado. Registrar o cenário/fixture e as preferências para reproduzir cada captura.

**Entrega:** inventário de propriedade visual e baseline de rects globais, mínimos, clipping, scroll e ordem dos elementos interativos. As capturas só são referência após estabilizar montagem e fontes.

**Aceite:** saber qual recurso controla cada aparência e quais propriedades estão congeladas.

### 2. Consolidar os tokens de aparência

**Alvos:** `VisualThemeProfile`, `ViewStyleFactory`, temas marfim, `CardVisualStyle` e estilo de menus.

**Trabalho:** centralizar cores de superfície, bordas e tintas que hoje escapam do tema; ajustar preenchimentos e ornamentos ao vocabulário definido. Introduzir propriedade visual somente onde houver uso concreto e não existir equivalente.

**Congelar:** fontes/métricas, margens de StyleBox, separações, `screen_margin`, card_size, art_height, cost_symbol_size, dimensões de tooltip, perfis de mapa/mão/combate e limites de arte dos atores. Alterar bordas desenhadas sem mudar o espaço que o controle reserva.

**Entrega:** componentes atuais com superfície marfim plana e estados de interação coerentes.

**Aceite:** todos os controles mantêm seus rects e mínimos; contraste e foco continuam legíveis.

### 3. Produzir a família de símbolos e arte de cartas

**Alvos:** assets SVG, manifestos de arte e consumidores de ícones existentes.

**Trabalho:** desenhar uma amostra de cada família; depois cobrir cartas, atividades, relíquias, inscrições e modificadores. Reutilizar bases geométricas com variações identificáveis. Criar fallback neutro reconhecível.

**Compatibilidade:** manter dimensões intrínsecas, viewBox, aspect ratio e fit equivalentes aos originais. Preservar os IDs do catálogo. Quando houver caminho novo, atualizar apenas o registro visual correspondente.

**Entrega:** catálogo rastreável com família, arquivos e mapeamentos; produção por SVG/código vetorial, sem novas ilustrações por gerador de imagens.

**Aceite:** leitura em tamanho real, diversidade suficiente para reconhecer cartas e ausência de assets faltantes nos settings cobertos.

### 4. Aplicar e validar primeiro no laboratório

**Alvos:** os mesmos `ArtSlot`, `CardView`, ícones de custo e temas usados pelo combate.

**Trabalho:** carregar o catálogo novo pelo contexto visual do laboratório. Exibir cartas curtas/longas, recursos diferentes, raridades, buffs/debuffs, upgrades e casos desconhecidos nas ferramentas existentes.

**Entrega:** primeiro marco visual para aprovação, com capturas comparáveis ao baseline.

**Aceite:** nova aparência com a mesma anatomia, área de seleção, fan e tooltip. Usar as animações existentes apenas para verificar compatibilidade; não alterar seus parâmetros. Se um SVG novo mudar demais a silhueta percebida da queima, corrigir a arte, não o shader.

### 5. Trocar atores, retratos e fundo

**Alvos:** assets de cenário e `SceneryVisualProfile`, `CombatSceneryView` e catálogo de retratos, somente nos pontos de aparência.

**Trabalho:** produzir as silhuetas geométricas e simplificar o fundo mantendo sua composição. Conferir atores repetidos, chefes e fallback.

**Entrega:** palco de combate e retratos pertencentes à mesma linguagem das cartas.

**Aceite:** pés, centros visuais, tamanhos aparentes, HUDs e hitboxes preservados. Atores continuam reconhecíveis com flash, seleção e sobreposição de indicadores existentes.

### 6. Aplicar o acabamento nas telas da run

**Alvos:** combate, mapa, atividades, diálogo, loadout, coleções de cartas e inspeções.

**Trabalho:** substituir ícones inconsistentes, tintas locais e ornamentos dentro das áreas existentes. Unificar superfícies de painel, controles e estados. Manter distinções visuais entre atual/concluído/disponível/bloqueado e entre equipado/vazio/rascunho.

**Entrega:** percurso visual completo do mapa ao combate/recompensa e de volta.

**Aceite:** painel flutuante continua sem afetar layout; nós preservam coordenadas/centralização; mão e custo mantêm geometria; slots e textos não mudam de lugar. Nenhuma informação é removida para reduzir ruído.

### 7. Aplicar o acabamento nos menus e ferramentas

**Alvos:** menu principal, pausa, configurações, histórico, replay e laboratórios.

**Trabalho:** usar a mesma família de ícones, bordas e superfícies, inclusive dropdowns, sliders, tooltips, modais e estados desabilitados. Conferir contextos derivados para evitar reaparecimento do tema antigo.

**Entrega:** consistência entre ferramentas técnicas e experiência jogável.

**Aceite:** controles mantêm seu comportamento, foco, tamanho e posição. Nenhuma preferência nova é necessária para acessar a aparência padrão.

### 8. Certificar e documentar a entrega

**Trabalho:** repetir capturas e comparações geométricas com as mesmas fixtures do baseline. Executar as suítes existentes relevantes de tema, cartas, custo, layout, seleção, mapa e paridade de animações. Acrescentar verificações somente para lacunas concretas: catálogo ausente ou deslocamento involuntário, por exemplo.

Conferir importação SVG e execução no renderer Compatibility, nitidez em tamanhos pequenos, texto ampliado, alto contraste, EN/PT, overlays e fallbacks. Verificar animações existentes em compra, uso, descarte e exílio; pausa e movimento reduzido continuam com seus contratos atuais.

**Entrega:** relatório com antes/depois, inventário atualizado e limites conhecidos. Documentar quais assets são placeholders vetoriais e como substituí-los pelo catálogo.

**Aceite final:** geometria e contratos de animação preservados; identidade marfim coerente em todas as telas cobertas; nenhum novo erro de importação/script/shader; nenhuma alteração em regras/API ou dados da run.

## Estratégia de implementação e revisão

Um commit por etapa concluída, com alterações pré-existentes preservadas fora do escopo. A etapa 1 documenta o estado real; as etapas seguintes registram assets e código visual estritamente necessários. As etapas 6 e 7 podem ser divididas por tela caso o diff fique grande.

Não usar comparação pixel a pixel como único aceite: ela deve mudar por definição. Comparar geometria e contratos separadamente da revisão da aparência. Nas animações, comparar eventos, endpoints, durações configuradas e lifecycle; tolerar apenas diferenças de frames da execução já existentes.

Se um ajuste visual exigir mudar dimensões, conteúdo, input ou tempo de animação, registrar como proposta separada. A alternativa desta entrega é adaptar a arte à caixa atual.

## Ordem e marcos

1. Baseline e tokens estabelecem a segurança da troca.
2. Catálogo e laboratório demonstram a linguagem no componente real de cartas.
3. Atores/cenário completam a identidade do combate.
4. Telas da run e menus estendem o acabamento.
5. Certificação encerra o revamp.

O primeiro marco para validar a aparência é o fim da etapa 4. Não é necessário redesenhar telas ou aumentar o escopo do jogo para chegar a ele.
