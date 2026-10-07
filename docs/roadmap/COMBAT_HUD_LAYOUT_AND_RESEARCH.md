# Organização do HUD de combate

## Objetivo

Priorizar a decisão de combate: ler intenções e recursos, escolher uma carta,
escolher o alvo e acompanhar o resultado. Ferramentas de leitura e debug não
devem alterar a composição do campo. Nenhuma regra do jogo migra para a UI.

## Pesquisa e aplicação

### Cardgames: menos ruído e alvos de interação claros

A [Mega Crit descreve ajustes de UI de Slay the Spire 2](https://megacrit.com/news/2024-11-07-neowsletter-issue-4/),
incluindo uma barra superior menos carregada de palavras, alvos de clique maiores
e informações adicionais em painéis de consulta. Aplicação neste projeto:
separar navegação superior, combate central, mão inferior e leitura detalhada.
Não copiar dimensões, recursos ou regras específicos daquele jogo.

A [palestra de Derek Sakamoto sobre a interface de Hearthstone](https://www.gdcvault.com/play/1022036/Hearthstone-How-to-Create-an)
é uma referência adicional de processo de UI para cardgames. A página pública
descreve o tema da palestra; não foi usada como evidência de detalhes do vídeo.

### Navegação e contexto

As [diretrizes XAG 112](https://learn.microsoft.com/en-us/xbox/accessibility/xbox-accessibility-guidelines/112)
recomendam foco previsível, ordem coerente com o layout e adaptação à resolução.
Aplicação: manter a identidade dos componentes da mão, não mover controles com
mudanças de seleção e transferir foco entre abrir/fechar o painel do personagem.

As [diretrizes XAG 114](https://learn.microsoft.com/en-us/xbox/accessibility/xbox-accessibility-guidelines/114)
recomendam associação visual entre rótulos e controles, agrupamentos identificáveis
e ajuda contextual. Aplicação: preview próximo das ações, ferramenta única de
inspeção na mão, botões de pagamento explícitos e tooltips com texto completo
quando um rótulo longo precisa ser abreviado. Isso não representa certificação
de conformidade completa com as XAGs.

### Implementação de overlays

A [documentação de CanvasLayer da Godot](https://docs.godotengine.org/en/stable/tutorials/2d/canvas_layers.html)
explica camadas independentes e sua ordem de desenho. Aplicação: overlays de
consulta/contexto na camada 5, acima do gameplay e das prioridades locais das
cartas, mas abaixo do pause na camada 10. A ausência de clique através do painel
é verificada separadamente por teste de input, não inferida da ordem de desenho.

## Disposição implementada

- Topo: contexto de combate/rodada, timeline, ferramentas disponíveis no modo e pause.
- Campo: cenário de largura inteira, atores/alvos, intenções e recursos junto de cada ator.
- Parte inferior: mão sem moldura de seção, reutilizando `CardHandView` e `CardView`.
- Ferramentas da mão: pilhas publicadas pela engine, paginação, inspeção e visão geral.
- Dock inferior único: preview e fila de apresentação à esquerda, ação contextual,
  passar quando aplicável e encerrar turno à direita. Geometria estável entre estados.
- Pagamentos/alvos alternativos: painel flutuante de opções, acima das ferramentas,
  com rolagem vertical para muitas alternativas; desaparece ao cancelar a seleção.
- Personagem: painel flutuante à direita, inicialmente recolhido para não ocultar
  intenções. Fechado, somente uma seta; aberto, superfície opaca com altura limitada
  e rolagem interna. Abrir/fechar não reduz a largura do campo ou move a mão.
- Pause: permanece acima de ambos os overlays e impede interação com eles.

O painel do personagem é sobreposto, não arrastável. Enquanto aberto pode ocultar
parte do cenário: essa é uma consulta opcional, reversível pela seta. O estado
aberto/recolhido é mantido nas atualizações da mesma tela, sem gravar preferências.
Em telas compactas, a consulta aberta também pode sobrepor cartas à direita;
recolher o painel restitui a visão, sem relayout ou alteração no estado do jogo.

## Fronteiras do código

`floating_hud.gd` cuida somente da camada, tema e retângulo de apresentação.
`combat_screen.gd` conecta intenções da UI aos comandos existentes da sessão.
Presenters continuam traduzindo projeções em modelos visuais. Recursos, pilhas,
custos, candidatos e permissões vêm da engine; o layout não assume quatro recursos,
três pilhas, cinco cartas ou um número fixo de inimigos.

## Verificação

`tests/combat_stage_layout.gd` usa projeções offline e input real em 1280×720,
1280×800, 1920×1080 e 2560×1080; inglês/português; texto em 100%/120%. Verifica
limites, identidade da mão, geometria estável, overlays, foco, muitas alternativas,
cliques bloqueados sob o painel, mão vazia, visibilidade e precedência do pause.

Regressões complementares: `card_play_selection.gd`, `pause_layers.gd` e `hand_ux.gd`.
Capturas nativas permitem conferir a composição ultrawide e a variante compacta
em português com texto ampliado. Estes testes não substituem um playtest com
conteúdo real via REST nem garantem legibilidade de qualquer conteúdo de mod.
