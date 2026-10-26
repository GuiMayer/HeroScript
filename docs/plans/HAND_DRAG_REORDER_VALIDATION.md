# Validação da reorganização visual da mão

Data: 26/10/2026.

## Resultado

Implementadas as oito etapas de `HAND_DRAG_REORDER_ARCHITECTURE_AND_PLAN.md`, com commits separados. Reorganização é uma inserção por identidade de instância, exclusivamente na apresentação. O gesto reutiliza o arraste, a pose e o retorno da mão de produção; não cria outro executor, comando de gameplay ou zona de cartas.

O layout, as proporções do leque e os valores padrão de duração foram preservados. O encaixe usa `hand_transition`; uso, compra e queima continuam consumindo os seus perfis configurados. Movimento reduzido/instantâneo e soltura já na pose final não acrescentam espera fictícia.

## Evidências

| Verificação | Resultado |
|---|---|
| Oito suítes novas na árvore preparada para commit | 64 checks aprovados, além da importação Godot |
| Matriz ampla de mão, ciclo, apresentação e montagem de telas | 21 execuções / 684 checks aprovados, incluindo API renderizada |
| Rodada final após corrigir a conclusão do laboratório | 7 execuções / 322 checks aprovados |
| API real, sem captura | 14 checks de comportamento aprovados |
| API real com captura | 19 checks por execução, incluindo cinco imagens salvas |
| Resoluções capturadas | 1280×720, 1920×1080 e 2560×1080 solicitados ao runner |

Os grupos se sobrepõem: os números não representam testes únicos somados. A rodada final repete laboratório, interrupções, polimento, compra em curva, paridade, seleção e API real após o último ajuste de settlement.

### Contrato com a engine

O teste `hand_reorder_online.gd` cria um sandbox de QA e executa:

1. Captura dos snapshots e simulação de fim de turno na revisão atual.
2. Arraste da primeira instância para o fim da mão.
3. Comparação dos snapshots e do contador do transporte: **nenhuma chamada REST causada pelo gesto**, nenhum estado autoritativo alterado.
4. Repetição da simulação usando a mesma fonte e revisão: **mesmo `finalStateHash`**.
5. Remontagem da tela: ordem visual restaurada no mesmo contexto de UI.
6. Uso da instância movida: exatamente um comando de gameplay, identificado pelo ID publicado.
7. Fim de turno: reconciliação das novas cartas e preservação da ordem relativa das sobreviventes.
8. Verificação canônica do replay: válida; sem ghosts, trânsitos ou bloqueios de apresentação pendentes.

As duas simulações usam a mesma revisão de origem, sem executar a segunda sobre um estado já modificado. O resultado pode ser reaproveitado pelo mecanismo canônico de simulação da engine; a organização visual não faz parte da requisição ou da identidade dessa simulação.

### Interação e ciclo visual

Cobertos inserção no início/fim, cruzamento de múltiplos slots, histerese, ponteiro parado, cancelamento, foco após reparenting, mão vazia e unitária, overflow de 30 cartas, modo reduzido/instantâneo, atualização que remove a carta arrastada e interrupção por modal.

A rolagem automática usa segundos e para ao encerrar o gesto. O teclado/controle mantém a navegação existente; reorganização por novos atalhos não foi adicionada nesta versão.

Foi encontrada uma dependência acidental durante a certificação: os cenários do laboratório esperavam tweens de vizinhas substituídos pelo recálculo do leque. A implementação final observa a conclusão da mão e de seus movimentos atuais por `reorder_settled`. Os testes exigem resultado `completed`, sem watchdog/fallback, e foram repetidos com sucesso na árvore preparada para commit, sem alterações pendentes não relacionadas.

Tooltips de outras cartas são ocultados ao iniciar o arraste; não há som de uso/compra, seleção automática ou clique sintetizado ao reorganizar.

## Isolamento e artefatos

API de QA: `http://127.0.0.1:61990`, com perfil de ferramentas `dev_modder` e os três caminhos de persistência apontando para `.runtime/hand-reorder-qa/`. Foram criadas tentativas novas; nenhum save do jogador foi utilizado. O servidor de QA é encerrado ao concluir a certificação.

Artefatos locais, ignorados pelo Git:

- `.runtime/hand-reorder-qa/online-validation.json`;
- `.runtime/hand-reorder-qa/full-validation.json`;
- `.runtime/hand-reorder-qa/final-validation.json`;
- `.runtime/hand-reorder-qa/committed-validation.json`;
- `.runtime/hand-reorder-qa/visual-1280/`, `visual-1920/`, `visual-2560/`;
- `.runtime/hand-reorder-qa/final-visual-1920/`.

Capturas de prévia e encaixe em 1920×1080 foram inspecionadas. Os arquivos de regras e conteúdo de gameplay não foram alterados por esta implementação.

## Limites

- A preferência visual dura a sessão da interface; não persiste entre reinícios.
- Reorganizar a posição **autoritativa** de uma carta, caso um futuro setting use essa posição como regra, exigirá uma operação da engine própria. Este recurso não realiza essa alteração.
- Avisos de encerramento de recursos de áudio em testes antigos de apresentação permanecem separados desta certificação; a avaliação não equivale a uma certificação completa de teardown do áudio.
- Não foi realizado benchmark de desempenho geral ou certificação de 120 FPS nesta tarefa.

## Reprodução

```powershell
& tools/test-godot-ui.ps1 -Tests @('hand_arrangement','hand_arrangement_session','hand_reorder_preview','hand_reorder_drop','hand_reorder_flow','hand_reorder_overflow','hand_reorder_lab','hand_reorder_interruptions','hand_selection','card_draw_pose','card_animation_parity','card_motion_polish','turn_hand_cycle')
```

Com uma API dedicada e armazenamento de QA:

```powershell
& tools/test-godot-ui.ps1 -Tests @('hand_reorder_online') -ApiUrl 'http://127.0.0.1:61990'
```

Para capturar o fluxo real, passar a API isolada e usar `-Tests @() -VisualTest 'hand_reorder_online' -VisualOutputDirectory '.runtime/hand-reorder-qa/visual-review' -VisualResolution '1920x1080'`. Esse teste cria novas tentativas; não executá-lo contra o armazenamento do jogador.
