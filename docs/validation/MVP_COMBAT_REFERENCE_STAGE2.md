# Composição MVP — etapa 2: cabeçalho

Data: 2026-11-05. **Etapa 1 aprovada. Etapa 2 implementada, aguardando aprovação visual. Etapas 3–7 não iniciadas.**

Guia: [plano de composição](../plans/MVP_COMBAT_REFERENCE_HUD_REBUILD_PLAN.md). Evidências: [testes, arquivos protegidos e limites](mvp-reference-stage2.json).

## Entrega visual

O cabeçalho usa três regiões independentes e editáveis em `mvp_combat_header.tscn`:

- `ActiveIdentity`: retrato do catálogo atual, nome e contador identificado do suporte quando publicado.
- `RoundAndTurn`: rodada centralizada independentemente dos textos laterais; ator ativo abaixo.
- `HeaderActions`: registro de combate, lupa de inspeção e pausa com atalhos remapeados.

A variante agora possui script próprio `mvp_combat_header_view.gd`. Não existem nós fictícios de timeline, ferramentas, spacer ou nomes antigos para manter a árvore anterior. Sinais fixos são conexões das cenas. Os controles genéricos dos outros settings mantêm sua composição anterior; o Label real de suporte é exposto como propriedade pública em ambas as variantes, evitando caminhos internos nos consumidores/testes.

Tema, fontes, cores e ícones continuam os existentes. As placas usam o Theme atual; lupa e pause reutilizam CombatChrome. Não foi criado tema, shader, fonte ou arte nova. A inspeção anterior no dock fica oculta na instância MVP, pois a mesma ação agora está no cabeçalho; as ferramentas de navegação da mão permanecem no componente existente.

Nome, retrato e rodada também acompanham snapshots intermediários publicados para reprodução. Essa apresentação não substitui o snapshot autoritativo. O cliente não calcula ativações: o contador `remainingOwnerActivations` pertence explicitamente ao suporte e só aparece quando há identidade/valor correspondentes. Na ausência de ativação publicada, a identidade do ator de input é o fallback e a linha de turno fica oculta.

## Validação

Godot 4.7.2, cópia de QA isolada. O relatório reúne a rodada completa e a última repetição focada após ajuste de âncoras: **22 scripts distintos, 1.178 verificações aprovadas**, todos com exit 0. Uma execução adicional de captura em português aprovou mais 23 verificações.

O teste `mvp_reference_header` tem 116 verificações: inglês/português, escalas 1,0/1,6, larguras 960/1280/1920, nomes longos, centralização sem sobreposição, fallback de retrato, atalhos remapeados, sinais únicos após refresh, troca de ator, associação do contador de suporte, snapshots intermediários, inspeção real e seu fechamento, pause/log e preview equivalente a F6. As entradas, o tema e a sessão permanecem inalterados.

As regressões existentes incluem mão e hover, dimensões/custos de carta, compra, ciclo de turno, pagamentos inline, pause, reconciliação e 515 verificações da composição genérica. Onze arquivos de carta/mão/movimento foram comparados com a etapa anterior e permanecem iguais, normalizando apenas finais de linha.

Capturas renderizadas em 1920×1080, 1280×800 e 2560×1080 estão em `.runtime/mvp-reference-stage2/after/` e `pt-BR/`. Foram inspecionadas visualmente as capturas 1920×1080 em ambos os idiomas. O restante do campo continua provisório, aguardando suas etapas.

## Diagnósticos e limites

- A montagem inicial do novo cabeçalho expôs um aviso sobre redimensionar um Control ancorado antes de ready. O ajuste usa os offsets das âncoras e as execuções finais de montagem/captura não apresentam esse aviso.
- `combat_stage_layout` passou suas 515 verificações, mas registrou quatro instâncias ObjectDB e dois recursos retidos no encerramento. O mesmo diagnóstico foi reproduzido na base `4b71c177`, com os arquivos de produção anteriores restaurados apenas na cópia isolada. Não é anunciado como corrigido nesta etapa; o relatório preserva esse diagnóstico. Um aviso semelhante apareceu numa execução inicial de `card_draw_pose`, sem repetição na execução focada final.
- São projeções locais e interações de componentes reais, não uma nova jornada com API real ou certificação de importação limpa. Os bindings dos testes online de suporte foram atualizados para a propriedade pública, mas esses dois testes online não foram executados nesta entrega.
- Alterações locais anteriores em tema, idiomas e arquivos não relacionados foram preservadas e ficam fora do commit.

## Reprodução e próximo gate

Executar `tools/test-godot-ui.ps1 -Tests mvp_reference_header,mvp_reference_base,editor_tool_scenes,combat_mvp_layout` para a seleção focada. Para capturas, acrescentar `-VisualTest combat_mvp_preview -VisualOutputDirectory <pasta>`; `-VisualPhase pt_BR` seleciona a captura em português.

Reexecutar o jogo/cena na Godot para carregar a nova árvore do cabeçalho. Aguardar aprovação do usuário antes da etapa 3, que moverá as pilhas para a coluna esquerda.
