# Laboratório de efeitos de arte — Marfim

2026-10-20. Protótipo visual isolado, sem comandos REST ou alterações de gameplay.

## Acesso e escopo

No laboratório de cartas, abrir **Art effects lab / Laboratório de efeitos**. O popup usa o ArtSlot real, o catálogo de atores do combate Marfim e o mesmo executor que poderá ser chamado pelo combate. As animações novas **não são habilitadas automaticamente na run**: a integração visual será aprovada no laboratório primeiro. O flash anterior de combate permanece inalterado nesta etapa.

## Etapa 1 — impacto

- Seletor extensível, comparação sem efeito/prévia, duração em segundos e intensidade.
- Pausa de prévia, movimento reduzido e reset/fechamento com restauração de material.
- Shader CanvasItem local, sem screen texture, SubViewport ou leitura de pixels para CPU.
- Tinta vermelha Marfim, sem flash branco de tela inteira ou deslocamento de hitboxes.
- `ArtSlot.play_art_effect()` usa `ArtEffectPlayer`; shader compartilhado, material independente por arte ativa, referência fraca ao alvo.
- Efeito ausente, duração/intensidade zero, alvo sem textura, modo instantâneo ou movimento reduzido retornam imediatamente, sem Tween/material/espera.
- Paleta e valores iniciais pertencem ao Resource `scripts/ui/styles/art_effect_style.gd`, não à engine.

Validação inicial: `art_effects.gd` (13 checks headless, 14 com captura renderizada) e `animation_contracts.gd` (8). São testes de apresentação, não certificação de latência REST ou benchmark de GPU.

## Etapa 2 — contorno de alvo

- Seletor com alvo disponível (tinta escura, 1,5 px locais) e selecionado (vermelho Marfim, 3 px locais).
- Estado persistente, estático e independente do flash, inclusive com movimento reduzido. Não seleciona um alvo nem torna uma ação legal; apenas representa o estado visual solicitado.
- Amostragem dos vizinhos do alpha e margem visual no quad da arte contain; não altera o Control, anchors ou hitbox. Artes opacas serão contornadas como retângulo: máscaras de silhueta precisam de transparência.
- Tamanho da textura desenhada acompanha resize. Fontes, HUD e bordas da carta não recebem o material.
- Limpeza do contorno restaura o material anterior; terminar um flash não apaga um contorno ativo.
- O teste renderizado também compara a arte antes/depois de material neutro, protegendo contra multiplicação duplicada da textura ou alteração indevida da cor original.

Validação da etapa: 23 verificações por execução renderizada, duas execuções aprovadas em Compatibility, sem erros de shader/script. O catálogo de cenário é explicitamente o mesmo Marfim usado no combate.

## Etapa 3 — reflexo de melhoria

- Seletor **Improvement sheen / Reflexo de melhoria**, com duração inicial de 0,32 s e intensidade de 0,65. Faixa diagonal em dourado discreto; a duração pode ser editada de 0 a 2 s.
- A comparação usa duas `CardView` reais e o modelo da carta selecionada no laboratório (ou a primeira amostra se não houver seleção). O shader fica exclusivamente no ArtSlot da arte: custo, regras, moldura e raridade permanecem intactos.
- `CardView.artwork()` expõe o componente compartilhado para que a futura integração de combate use o mesmo executor. Não há desenho ou cópia de shader específicos do laboratório.
- Movimento reduzido e duração zero omitem o reflexo sem espera. O contorno continua disponível por ser um estado estático.
- Checkbox **Pause preview / Pausar prévia** congela a animação local; a pausa real da SceneTree também congela seu Tween. Não é usado `TIME` para progresso.
- Selecionar outro efeito, reset, fechar/reabrir o popup ou substituir a arte limpam os efeitos. Atualizar a mesma CardView não reinicia o progresso.
- Materiais independentes podem compartilhar o mesmo Shader sem contaminar outra arte. Quando possível, o popup desenha um frame de material neutro na abertura para aquecimento; isso não garante ausência de stutter em toda GPU.
- Popup redimensionável. A comparação de cartas tem scroll utilizável para manter os controles externos alcançáveis em 1280×720. Nenhum controle altera preferências salvas ou aplica automaticamente o efeito ao combate.

## Validação e limites

`art_effects.gd` cobre executor/material/alpha neutro, pausa explícita e real, resize, concorrência, zero/instantâneo, movimento reduzido, reconciliação, preservação de conteúdo e limpeza. As capturas são produzidas apenas pelos testes renderizados, não pelo caminho de gameplay.

`art_effect_lab.gd` abre o laboratório real, escolhe uma carta, usa o botão do popup, alterna os quatro itens (três efeitos, com duas variantes de contorno) em EN/PT, verifica dimensões em 1280×720 e reabertura, e confere ausência de requests/mutações da sessão e de gravação de preferências. Os dois testes foram incluídos na suíte padrão.

O combate continua com seu flash anterior e as animações existentes. **Não foi implementado disparo automático dos novos efeitos a partir dos receipts**, nem alterada a regra de seleção/targeting, o determinismo ou a imutabilidade. Depois da aprovação visual, a integração deve chamar estes mesmos componentes, com estados e confirmações autoritativos já publicados, sem nova fila de playback.

Capturas de referência: [impacto](art-effects/impact.png), [contorno](art-effects/outline.png), [reflexo](art-effects/sheen.png). São quadros congelados dos efeitos, não um benchmark. Não foram medidos overhead de GPU, latência REST ou desempenho em outros hardwares.

Certificação final: **277 verificações aprovadas**, sem diagnósticos de script/shader, somando suíte offline (222: efeitos, integração do laboratório, contratos, paridade das cartas, apresentação ao vivo com gateway de teste, laboratório Marfim e fronteiras gráficas) e rodada final de integração/renderização (55). A parte renderizada usa Godot 4.7.2 Compatibility/OpenGL na GPU local. Relatórios: [offline](art-effects/offline-validation.json) e [renderização/integração](art-effects/rendered-validation.json).
