# Certificação — compartimentos de construção do baralho

Data: 2026-10-26. Referência: [plano](DECK_COMPARTMENTS_ARCHITECTURE_AND_PLAN.md).

## Evidências

- Core completo no workspace: 1.893 testes aprovados antes do refinamento final de metadados/cache.
- Jornada específica HTTP: três stores/hosts novos, mesma seed e comandos com identidades estáveis. Compara hashes de todos os recibos/commits; executa efeitos concedidos de block e health, custo/condição adicionais, resultado de combate, volta à preparação e nova distribuição.
- Duplicatas de comandos retornam recibo idempotente. Preview conserva hash da run. Gravação durante combate é bloqueada pela política/planner.
- Reinício do host preserva snapshot e alocações; branch neutralizada não modifica o pai; replay semântico é válido para ambos.
- Testes focados API: 12 aprovados (compartimentos, controller de inscrições e projeção de interação).
- Tempos observados da jornada em HTTP in-process: maior comando de 560,91 ms no primeiro host; nos dois hosts seguintes, máximos de 60,13 e 60,36 ms. Preview: 94,36 / 18,95 / 18,83 ms. Uma execução concorrente da suíte ampla registrou previews de até 231,68 ms. Não são garantia universal nem medição de animação/renderização; criação do host e verificação integral do replay ficam fora desse orçamento.
- Godot offline: 20 checks finais do editor (incluindo recuperação de recibo inválido); regressões de coleção (9), modificadores (33), inscrições da run (24), câmera do mapa (51), além de topologia (30) e reconciliação de atividades (21) em execução anterior.
- Godot renderizada com API isolada na porta 61988: 13 checks aprovados, incluindo gesto nativo de drag/drop, preview real, commit por GameSession, retorno confirmado e reentrada descartando draft não salvo. Capturas inspecionadas: estilo marfim, CardView compartilhado, efeitos/custos efetivos dentro das colunas corretas. Nenhuma run do usuário foi usada.

## Refinamentos encontrados na certificação

- Perfil também precisa ser materializado no personagem persistente, não só no ator de combate.
- Arrays concretos de condições/disposição/rule override na projeção REST não incluem necessariamente `type`; o adaptador visual agora reconstitui esse discriminador somente na cópia de apresentação.
- Contribuições recebem nome/descrição de apresentação opcionais; o badge é separado de upgrades permanentes.
- Cache de validação por runtime imutável e profile ID tem chaves fracas e limite de 32 entradas por runtime; cache de carta inclui o contexto estrutural. Nenhum cache é autoridade de gameplay.
- O teste de drag precisa enviar deslocamento relativo e input ao viewport; a primeira tentativa sem esse deslocamento não acionava o gesto da Godot. Depois da correção do harness, o arraste nativo passou.

## Limitações e resultados não atribuídos à mecânica

- Suíte API ampla no workspace: 241 aprovados, uma falha e interrupção pelo timeout global de 300 segundos. A falha de `PublishedAct_CanTraverseAllNineEncountersServicesAndReplay` espera 65 de vida e obtém 58.
- Controle independente: exportado `cf349c1` (antes da implementação), copiando somente o conteúdo Ascendant pendente para a cópia. O mesmo teste falha com exatamente 65 esperado / 58 obtido. Portanto essa divergência já acompanha o conteúdo pendente e não demonstra regressão causada pelos compartimentos. O teste não foi relaxado ou rebaselined neste trabalho.
- No workspace visual há também um teste antigo de carta esperando custo no topo direito, embora uma solicitação anterior tenha movido o custo para baixo do título. As alterações visuais pendentes foram preservadas; essa expectativa não foi modificada nesta entrega.
- Tipos de componente são os canônicos. Targeting e trigger têm prova de composição estrutural; este trabalho não cria um scheduler novo para boundaries de triggers que o jogo ainda não execute. Conceder um componente não implementa uma semântica inexistente no processador original.
- Perfil demonstrativo habilitado apenas no setting novo `compartment-lab`; não foi habilitado em Ascendant/default/Volatile Core.

## Verificação do índice independente

Exportados somente os arquivos do índice para uma árvore temporária fora do workspace, sem as alterações antigas não commitadas:

- Core completo: 1.891 testes aprovados.
- API: 13 testes aprovados, incluindo a jornada nova repetida e o teste original de nove combates do Ascendant, com a expectativa original de 65 de vida. Isso confirma que a mecânica opcional não altera esse resultado no conteúdo commitado.
- Godot: editor (20 na execução final), apresentação de cartas original (62), reconciliação de atividades (11), topologia (30), câmera (51) e inscrições da run (17), todos aprovados.
- A primeira importação da Godot encerrou com uma falha nativa durante reimportação da fonte; fonte conferida com hash idêntico à do workspace. Reimportação seguinte completou e todos os testes acima passaram sem erros de script. Não foi necessário alterar assets de terceiros.

Refinamento final adicional: validação de gameplay do container final impede instalar componentes cuja semântica ainda não é executável (incluindo triggers não suportados pelo validador canônico). Teste de hotreload com redução de capacidade rejeita atomicamente a migração, conserva hash e não contamina cache da revisão original.

Após esses refinamentos: 19 testes de compartimentos aprovados na árvore do índice e nova execução dos três testes API específicos aprovada, incluindo a jornada repetida três vezes. A Godot renderizada foi repetida com os metadados finais: 13 checks aprovados, sem diagnósticos.
