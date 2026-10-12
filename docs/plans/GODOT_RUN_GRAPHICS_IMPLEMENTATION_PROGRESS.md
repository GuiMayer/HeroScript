# Implementação da arquitetura visual da run

Iniciada em 2026-10-11, atualizada em 2026-10-12. Plano: [GODOT_RUN_GRAPHICS_ARCHITECTURE_PLAN.md](GODOT_RUN_GRAPHICS_ARCHITECTURE_PLAN.md).

## Estado atual

A implementação principal de combate, jornada, cenário e mapa foi entregue. **A certificação integral da etapa 10 ainda não está encerrada:** falta executar o fluxo REST com servidor de QA isolado. Não confundir aprovação das fixtures com validação online de uma run real.

| Etapa | Estado | Evidência |
| --- | --- | --- |
| 0 | Baseline e regressões permanentes entregues | 72 capturas anteriores, 72 posteriores; EN/PT e 1280×720, 1920×1080, 2560×1080. Fixtures de terceira equipe, recursos desconhecidos, laboratório com conteúdo variado e mapas grandes/cíclicos. |
| 1 | Implementada | Identidade/textura de ArtSlot atômicas; risco alto→normal→alto não conserva override antigo. |
| 2 | Implementada | Componentes com script reconciliam seu próprio modelo; nomes semânticos para interativos; substituição segura quando não há contrato. |
| 3 | Implementada para os consumidores da run | Factory de estilo única com instâncias por VisualContext; modelos de cabeçalho/interação/atores/cenário; perfis de cenário e mapa separados da UI. |
| 4 | Implementada | CombatView passiva; CombatScreen adapta coordenação; opções enviam referências contextuais. FloatingHUD possui slots de reconexão, alternativas, preview, sidebar e alça. Composições inválidas são rejeitadas antes de reparentar. |
| 5 | Implementada | Coordenador de inspeção com alvo/revisão na chave, cache limitado e coalescência; respostas descartadas após dispose. Driver usa exclusivamente Playback, com pausa e modos manual/automático. |
| 6 | Implementada para o renderer Control | Ambiente e artes pertencem ao RunSceneryHost do shell. HUD/alvos separados; relação e equipe completas; neutros não são YOUR SIDE. |
| 7 | Implementada | JourneyView e ActivityOptionsView passivas; updates regionais; nenhuma instância temporária de ActivityScreen. Recuperação/verificação/avanço não são iniciados pelo desenho. |
| 8 | Implementada | MapLayout puro/cacheado, DAG por camadas/faixas, fallback estável para ciclos/desconexões, traçados direcionais, scroll/foco e diagnóstico de IDs inválidos. |
| 9 | Implementada no escopo combate/jornada | Componentes migrados para contextos locais; estilos especializados de carta/mão preservados. Fachada AppTheme permanece para outras telas/menus ainda existentes. |
| 10 | Verificação offline/visual realizada; integração online pendente | Rodada ampla de 20 suites/1.218 checks, seguida de rodadas focadas nos ajustes finais. Limites descritos abaixo e no relatório de comparação. |

## Decisões de implementação

- A geometria inicial de atores continua conduzida pelo layout Control já existente. O adaptador de cenário segue **anchors públicos em coordenadas globais da viewport**, sem buscar descendants privados nem capturar input. Isto preserva a composição atual e não pressupõe um renderer Node2D/Node3D.
- Atividades que compartilham anatomia usam ActivityOptionsView: ofertas de cartas usam CardView; diálogo usa DialoguePanel; outras escolhas publicadas usam o fallback genérico. Não foram criadas classes duplicadas para cada nome de atividade.
- Templates passivos de regiões pequenas continuam permitidos na reconciliação. Não há segunda tela operacional ou controller temporário.
- A revisão de cenário é separada da chave de UI. Trocar apenas o perfil de cenário não recria a mão/HUD nem requisita inspeção.
- Um encontro só participa do mapping visual quando seu **definition ID** está publicado; nunca se interpreta um instance ID como definição.
- As regras, envelopes REST e identidade determinística da engine não foram alterados.

## Verificação e limites

Runner: `tools/test-godot-ui.ps1`, com cópia/user directory isolados. Regressões permanentes novas: `run_graphics_updates`, `run_visual_contexts`, `combat_collaborators`, `map_geometry`, `map_graph_view` e `run_graphics_architecture`.

As suites verificaram seleção após uso, pause, composição lateral, descarte, configurações, progressão por setting, mão, custos, recursos desconhecidos, equipes e atualização de atividades. Vinte refreshes idênticos tiveram **zero substituições de nodes, zero relayouts da mão e zero leituras de inspeção adicionais**. Tempo local não representa rede/GPU nem clique→resultado.

O teste online `dialogue` não conectou à API (status 0); não foi contabilizado como aprovado. A tentativa de iniciar um servidor QA foi rejeitada pela ferramenta antes da execução. Não se usou a API das partidas pessoais como substituto. Ainda falta executar os testes online e o percurso completo menu→jornada→atividade→combate→recompensa→resultado, nos settings default/ascendant/volatile, com persistência isolada.

Avisos antigos de ObjectDB/importações nativas continuam separados dos asserts. O relatório não promete ausência de avisos nativos apenas porque um processo terminou com código 0.

## Artefatos

- Referência anterior: [run-graphics-before/2026-10-11](../design/run-graphics-before/2026-10-11/README.md).
- Comparação posterior: [run-graphics-after/2026-10-12](../design/run-graphics-after/2026-10-12/README.md).
- Contratos: [GODOT_RUN_GRAPHICS_CONTRACTS.md](../design/GODOT_RUN_GRAPHICS_CONTRACTS.md).

Alterações anteriores do working tree foram preservadas. Nenhum commit/push foi realizado neste pedido.
