# Auditoria de montagem e retorno das telas

Data: 2026-10-16. Auditoria inicial seguida pelas duas correções solicitadas. As seções de achados registram o estado anterior; o fechamento e os testes atuais estão em “Correções e regressões”.

## Método

- Inspeção de setup/apply_projection, consultas de viewport/tree, captura/restauração de foco, callbacks adiados e continuations após await.
- Novo teste run_screen_lifecycle usa main.tscn, router, screens e componentes de produção. Portanto executa setup antes de host.add_child, diferente de testes isolados que montavam a View primeiro.
- Snapshots e gateway de teste são isolados. A evolução dos snapshots é controlada pela fixture, não simula regras na Godot nem representa uma campanha real.
- Campanha REST é verificada separadamente por PublishedAct_CanTraverseAllNineEncountersServicesAndReplay, com factory/store temporários, sem usar o servidor ou os saves pessoais.

## Fluxo principal

Três ciclos completos, 107 verificações aprovadas:

1. Run ativa sem combate → mapa com inspector construído.
2. Snapshot de combate ativo → montagem do combate e mão de cinco cartas.
3. Pausa → retomar combate.
4. Consulta ao mapa → retorno canônico ao combate ativo.
5. Combate encerrado → recompensa.
6. Recompensa, loja, preparação e upgrade → botão MAP → mapa → open_game restaura atividade pendente e escolhas inalteradas.
7. Diálogo → botão MAP → Retomar conversa → diálogo.
8. Diálogo resolvido → mapa, preservando a barreira de retorno.
9. Seis substituições rápidas de telas antes de terminar callbacks adiados.

Sem novos erros de montagem nesses caminhos. O teste permitiu somente seis leituras inspect_hand atendidas pelo gateway sintético, sem comandos ou rede real. O retorno de atividades não narrativas foi exercitado pelo método canônico do router; não se deve apresentar isso como existência de um botão Voltar na tela de mapa.

Resultado consolidado: **301 checks Godot aprovados**, incluindo 30 de continuar/progressão por setting, contratos/leitura de diálogo, ingresso de atividades, replay progressivo, apresentação histórica e arquitetura gráfica. O teste REST da campanha publicada também passou (88 s), incluindo replay e retomada persistida. A reprodução dirigida do bug de fechamento de replay falhou como esperado e é contabilizada separadamente, não como parte da suíte verde.

## Achados confirmados

### 1. Replay pode acessar SceneTree nula depois de fechar a tela

Local: examples/godot-engine-showcase/scripts/ui/run_replay_screen.gd, _toggle_play, linhas 73–74.

Depois de await _watch(), a rotina cria um timer sem revalidar is_inside_tree, controller.valid, playing e play_epoch. dispose cancela o estado do controller, mas a continuation que já aguardava a reprodução visual ainda pode retornar a esse ponto.

Reprodução controlada: manter _watch pendente, remover a tela da árvore (disparando dispose), liberar a espera. A rotina de reprodução original, sem alteração, produziu:

    Parameter "data.tree" is null.
    Cannot call method 'create_timer' on a null value.

Probe localizado apenas na cópia isolada .runtime/ui-refactor/e06d5e48523b4338b25745e3ea140f48/tests/replay_detach_probe.gd. Ele usa uma subclasse temporária para controlar a espera; é uma reprodução de falha, não um teste aprovado. Não foi incluído na suíte de regressão verde.

Correção recomendada: revalidar montagem, validade e geração após cada await antes de usar árvore, controles ou agendar timer; cobrir fechamento enquanto _watch e navigate aguardam respostas.

### 2. Falta retorno explícito do mapa às atividades não narrativas

MapView cria MapResumeDialogue e MapScreen trata resume_dialogue. Não existe intenção/controle de retomar atividade equivalente para recompensa, loja, preparação ou upgrade. A consulta não perde o snapshot e open_game retorna corretamente; o jogador não dispõe desse retorno direto na interface.

Correção recomendada: intenção de retorno visual para o router canônico, sem enviar comandos, escolher recompensas, recriar atividade ou alterar progresso. Não usar uma segunda interpretação das regras no mapa.

## Risco latente, não reproduzido no fluxo normal

CombatView.capture_local_state consulta get_viewport().gui_get_focus_owner sem guarda. A chamada interna em _try_apply_composition está protegida por is_inside_tree. Portanto não foi identificado outro crash de entrada nesse ponto. O contrato público de captura antes da montagem permanece frágil e merece uma guarda caso esse uso seja suportado.

CharacterAttributesView conecta size_changed em _ready, quando há viewport. FocusNavigation.wire verifica is_inside_tree. MapView._inspect já permite construção sem viewport. ActivityScreen e DialogueScreen adiam workflows até a montagem e invalidam callbacks durante dispose.

## Limites

Não houve playtest humano nem automação de cliques físicos. Os botões MAP/Retomar conversa e o roteador reais foram acionados por sinais e métodos em processo Godot isolado. O teste REST separado certifica a engine, não uma sessão única Godot+REST de ponta a ponta. Nenhuma regra, RNG, persistência, gameplay ou snapshot de produção foi alterado.

## Reprodução da suíte verde

    ./tools/test-godot-ui.ps1 -Tests @('run_screen_lifecycle','activity_reconciliation','dialogue_coordination','map_selection','combat_collaborators')
    dotnet test tests/API.Tests --no-restore --filter 'FullyQualifiedName~AscendantWorldOneJourneyTests.PublishedAct_CanTraverseAllNineEncountersServicesAndReplay' --verbosity quiet

## Correções e regressões

Os dois achados confirmados foram corrigidos sem mudar regras, contratos REST, progresso ou persistência.

- Replay: _playback_active centraliza montagem, validade, playing e play_epoch. A rotina revalida após settlement da animação, navegação, leitura visual e timer. Continuações antigas não agendam outro timer, navegam nem interrompem uma reprodução reiniciada.
- Mapa: botão global MapResumeRun, traduzido, retoma atividade, combate ou conversa. Permanece acessível com inspector recolhido ou outro nó selecionado; fica oculto quando a run já está no contexto de mapa.
- O retorno usa open_game. A classificação de rota existente foi extraída sem mudança de critérios para gameplay_route, reutilizada na abertura e na visibilidade do botão. Nenhuma elegibilidade de gameplay é calculada pelo mapa e nenhuma consequência é executada ao voltar.
- replay_lifecycle: 11 checks com o loop e dispose reais, controlando somente esperas e cursor. Cobre fechar durante inspeção, navegação, animação e timer; parar durante inspeção; invalidar contexto; impedir reinício desmontado; preservar a nova geração depois de resposta tardia.
- run_screen_lifecycle: ampliado de 107 para 149 checks. O retorno não narrativo agora acontece pelo botão real, não por chamada direta ao router no teste. Verifica EN/PT, retorno ao combate, seleção de outro nó, inspector recolhido, ausência de botão enganoso quando só há mapa e snapshots/escolhas inalterados.
- Os dois scripts fazem parte da execução padrão de tools/test-godot-ui.ps1.
- Validação Godot das correções: **290 checks aprovados**, incluindo as suítes de mapa, replay progressivo, reconciliação de atividades, diálogo, histórico, progressão por setting e arquitetura gráfica. Nenhum erro de script nesses testes.
- Campanha REST reexecutada após as correções: **PublishedAct_CanTraverseAllNineEncountersServicesAndReplay aprovado**, incluindo replay determinístico e retomada persistida (85 s nesta execução), em store temporário.

Reproduzir diretamente:

    ./tools/test-godot-ui.ps1 -Tests @('replay_lifecycle','run_screen_lifecycle','map_selection','replay_progressive','activity_reconciliation','dialogue_coordination','historical_presentation','setting_progression','run_graphics_architecture')
