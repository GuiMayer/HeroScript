# Composição MVP — etapa 7: integração e certificação

Data: 2026-11-10. Implementação e verificações abaixo concluídas; **aprovação visual final pendente**. O prosseguimento após a etapa 6 foi solicitado pelo usuário.

## Composição e escala

O canvas mantém cabeçalho, campo, pilhas à esquerda, energia central, mão existente e botão de fim de turno. A cena antiga `mvp_arena.tscn` foi removida após a busca por consumidores; outros settings continuam usando sua composição própria. Na atualização de escala de texto, a mão e suas cartas existentes recebem o contexto atual. No MVP, rótulos de pilhas ampliam a coluna conforme o texto e ícone, e o cabeçalho disponibiliza espaço adicional para log e pausa.

Em 1280 × 720 e 1280 × 800 com texto em 160%, o campo dos atores pode precisar de rolagem vertical. O conteúdo permanece recortado à área do campo e acessível por rolagem, sem ser desenhado sobre a mão. As cartas e sua geometria interna não foram redesenhadas; nessa escala, textos longos em sua face ainda podem exigir a inspeção detalhada.

## Evidências

- Capturas Godot 4.7.2 em `.runtime/mvp-reference-stage7-single/`: 1920 × 1080, 1280 × 720, 1280 × 800 e 2560 × 1080; mãos de 0, 1, 2, 5, 10 e 12 cartas. Capturas em PT-BR e 160% em `.runtime/mvp-reference-stage7-final-pt160c/`. Essas pastas locais são geradas pelo teste e não são parte do commit.
- As capturas e os testes usaram a versão de trabalho do tema MVP, que já tinha alterações locais antes desta etapa. Esse arquivo foi preservado fora do commit; a aparência das capturas depende dessas alterações locais.
- `mvp_reference_full_composition`: **452 verificações**, incluindo EN/PT-BR, múltiplos atores, escala, posições, mão, contexto visual, animações e montagem de outro setting. O manifesto da etapa 1 e o manifesto atual mantêm 0,5 s para voo e 0,5 s para descarte/retenção; exílio por queima central permanece em 0,5 s.
- Regressões: cabeçalho **116**, pilhas **185**, campo **403**, energia/fim de turno **122**, overlays **84** e montagem no editor **55** verificações. `mvp_reference_base` passou com **89**. Uma importação limpa isolada encerrou com código `-1073741819` durante a importação da fonte; a bateria foi executada em cópia de QA já importada e não certifica importação limpa.
- A API foi compilada com .NET 10 sem warnings nem erros e executada em `127.0.0.1:61989`, com diretórios de dados isolados. `mvp_stage7_journey_online` passou **18** verificações: ingresso, combate MVP, vitória, resolução, replay, fingerprint e retomada. A escolha das jogadas após a montagem é automatizada por candidatos legais.
- `mvp_stage7_payment_online` passou **16** verificações: seleção inline da carta em mão, alvo, descarte, custo vindo do descarte, exílio e replay. O caso de custo no descarte invoca o controlador de seleção com o ID publicado; **não comprova uma interação visual para escolher essa carta na coleção**.
- `hand_online` passou **150** verificações em settings Default e Ascendant; `campaign_online` completou a campanha Default e replay (**3** verificações). `pyro_thin_slice_online` passou **23** verificações do MVP, incluindo uso de defesa, turno, bloqueio persistente e replay.

Os testes de seleção de pagamento e jornada não executam operações contra os saves do jogador. As regras, os snapshots e o cálculo permanecem sob autoridade da API. A certificação de capítulo MVP completo e a seleção visual de custos em zonas fora da mão seguem no [TODO](../roadmap/TODO.md).

## Reprodução

Use `tools/test-godot-ui.ps1` com os nomes acima e uma cópia de QA importada. Os testes `*_online` exigem API isolada em `http://127.0.0.1:61989`; use `-ApiUrl` apontando para ela. Para capturas, use `-VisualTest combat_mvp_preview -VisualOutputDirectory <pasta>`; acrescente `-VisualPhase pt_BR_160` para escala de 160%.

## Complemento — seleção no descarte e importação limpa (10/11/2026)

- Uma cópia isolada sem importação prévia passou pela importação e pela montagem de 55 cenas no editor. Outras três importações limpas também passaram durante a investigação. A falha anterior foi uma violação de acesso nativa (`0xC0000005`) no executável da Godot; sem símbolos suficientes, sua causa exata continua desconhecida. A abertura direta do editor ainda pode ser afetada.
- O validador `tools/test-godot-ui.ps1` preserva os registros e tenta uma única vez em outra cópia limpa quando encontra especificamente essa falha nativa. Uma segunda falha ou outros erros continuam reprovando a execução. `tools/test-godot-import-recovery.ps1` verifica a recuperação e o limite de tentativas; isso mitiga a automação, mas não corrige o executável da Godot.
- Com API real isolada, `mvp_stage7_payment_online` passou 25 verificações tanto em execução sem janela quanto gráfica. O teste abre a coleção de descarte, verifica as opções elegíveis, seleciona e desmarca uma carta, usa entrada de ponteiro para selecioná-la e confirma seu envio ao exílio e a validade do replay. A captura da coleção foi inspecionada visualmente.
- Na mesma cópia limpa, passaram `card_collections` (13 verificações), `inline_card_selection` (14), `mvp_reference_overlays` (84) e `editor_tool_scenes` (55). A validação não comprova a interação para todas as demais zonas possíveis.
