# Execução do MVP da campanha tática

Início: 2026-11-02. Guia: [plano](TACTICAL_CAMPAIGN_MVP_IMPLEMENTATION_PLAN.md).

## Etapa 0 — Baseline e contrato de produto

Estado: concluída.

- Preservadas as alterações pendentes anteriores à execução. Inventário, diff binário e hashes dos arquivos estão em `.runtime/tactical-mvp`.
- Criado o [GDD curto do MVP](../game-design/TACTICAL_CAMPAIGN_MVP.md), com defaults de playtest, limites e matriz de permanência por tentativa. GDDs anteriores permanecem intactos.
- Reutilização confirmada: custos de recurso, CardZoneSystem, CardSelection/Decompose, RestartActivity, lifecycle de recursos, atributos/inscrições, gambits e cenas canônicas da Godot.
- Lacunas sequenciais: composição ativa/reserva/módulos; pagamentos escolhidos com cartas; UX de retry; propriedade/configuração de suporte; integração do capítulo.
- A política de composição não será implementada por grants fictícios nos compartimentos. O companion não terá processador de efeitos nem relógio de gameplay próprios na Godot.

Baseline de testes: **56 aprovados, zero falhas/skips**, cobrindo serialização canônica, zonas, recursos/retry, compartimentos, inscrições e gambits em diretório de build isolado. Evidência local: `.runtime/tactical-mvp/results/baseline.trx`. Nenhuma configuração jogável foi alterada nesta etapa.

## Etapa 1 — Contratos e publicação

Estado: concluída.

Criados contratos de composição com grupos/cotas/catálogos e módulos, suporte/rotinas/loadout e seleção de pagamento por zonas. Kinds novos possuem identidade e validação semântica de referências na publicação; os kinds opacos anteriores não foram reinterpretados. Cadência do suporte pertence a recursos/respostas/gambits normais, não a um novo relógio do companion. Não foi habilitado vínculo de modo antes de existir o executor correspondente. Tentativa e recompensa reutilizarão as políticas/transições atuais, recebendo extensões somente onde demonstradas necessárias.

Schema: `docs/schemas/build-composition-content.schema.json`. Custos de zona são contrato isolado até sua integração na etapa 5; ainda não fazem parte de ActionCosts. Editar composição/suporte em combate é rejeitado como opção não implementada neste recorte.

Testes: **35 aprovados, zero falhas/skips**, incluindo contratos, erros de publicação e serialização canônica. Evidência local: `.runtime/tactical-mvp/results/stage01.trx`.

## Etapa 2 — Composição persistente

Estado: concluída.

Adicionado estado opt-in com associações de grupos/módulos por instância, sem copiar cartas ou alterar zonas. Inicialização determinística, substituições e troca de módulo são planners puros; validam cotas, elegibilidade original, identidade, propriedade, setting/revisão e janela de edição. Captura imutável fornece a seleção da próxima tentativa. Remoção permanente reconcilia referências, sem inventar reposição; uma composição insuficiente fica impedida de iniciar outro encontro.

A regra de propriedade persistente foi compartilhada com os compartimentos, conservando seu comportamento. Ordem visual enviada pelo cliente é normalizada pela identidade/ordem de criação; não altera RNG nem reordena pilhas. Campo ausente é omitido para conservar snapshots históricos.

Testes: **40 aprovados, zero falhas/skips**, incluindo troca integral, rejeição atômica, melhorias preservadas, serialização, identidade adulterada, revisão/ator e regressões de compartimentos/canonicalização. Evidência: `.runtime/tactical-mvp/results/stage02.trx`.

## Etapa 3 — Captura e circulação

Estado: concluída.

Modo opt-in por `deckCompositionPolicyId`. A criação materializa/valida a composição antes dos fluxos iniciais; encontros capturam ator, política, revisão, sequência, passo e identidades sem copiar cartas. Seleções de fluxo recebem conjuntos derivados (`deck.active`, `deck.reserve`, `deck.group.<id>`) através de `instanceSetId`. A movimentação, propriedade, embaralhamento, retorno e distribuição continuam responsabilidade dos JSONs existentes; o executor recebe apenas conjuntos de IDs. Filtros desconhecidos ou operações que os ignorariam são rejeitados.

Destruição permanente reconcilia as referências persistentes sem alterar a captura da tentativa. Ativação de conteúdo exige compatibilidade integral e `revisionMigration: RebindCompatible`, fora de encontro pendente. Ausência da opção conserva as formas serializadas anteriores; não se habilita composição implicitamente em saves antigos.

Testes: **185 aprovados, zero falhas/skips**, incluindo criação real pelo RunManager, captura no encontro, startup/dispatcher compartilhados, zonas globais/run/ator, reserva excluída, idempotência, preservação de melhorias e RNG, revisão e regressões dos fluxos antigos. Evidência: `.runtime/tactical-mvp/results/stage03.trx`. Builds/testes em diretório isolado; saves do jogador não foram usados.

Documentação de autoria: [composição de baralho](../guides/deck-composition.md). O setting jogável e a edição REST/Godot ainda dependem das próximas etapas.

## Etapa 4 — Edição REST e Godot

Estado: concluída.

GET/preview de composição projetam a seleção e as apresentações canônicas do personagem atual. O comando `EDIT_DECK_LOADOUT` usa o dispatcher/journal/idempotência existentes. Substituição e troca de módulo são gestos completos validados pelo mesmo planner; não há gravação intermediária de grupo incompleto. Módulos usam suas listas iniciais autoradas sobre instâncias já possuídas; escolher novamente o módulo atual preserva personalizações. Propriedade, revisões, elegibilidade, cotas e janela são autoridades do servidor.

A aba Deck → Build usa uma cena editável e CardView canônico, com grupos, módulo, deck ativo e reserva. Clique/teclado ou drag usam a mesma intenção → preview → commit → leitura confirmada. Controles de carta são preservados na sincronização. Falha de preview, setting alterado e resposta tardia não gravam; recibo incerto bloqueia saída/novas operações e usa a recuperação existente, sem repetir a troca.

Testes: **108 testes Core**, **4 testes API** (incluindo contrato servido pelo host real de teste) e **65 verificações Godot em cinco roteiros**, todos aprovados. Incluídos commit duplicado, retomada de snapshot, preservação de zonas/RNG, substituição/módulo, drag, autosave, rejeição, recuperação e contexto tardio. Evidências em `.runtime/tactical-mvp/results/stage04-*.trx` e `.runtime/tactical-mvp/stage04-godot.log`. Nenhum save do jogador foi utilizado.

Contrato e exemplos: [deck-loadouts](../api/deck-loadouts.md). Atualizado o OpenAPI estático com as novas rotas/DTOs, preservando alterações pendentes anteriores.

## Etapa 5 — Custos com cartas

Estado: concluída.

O componente de custo aceita `zonePayments`, mantendo os custos/alternativas de recursos existentes. Cada pagamento referencia um fluxo JSON de transferência explícita, sem segundo processador de zonas; suas restrições são validadas na publicação e novamente no contexto da ação. Custos AND e pagamentos associados às alternativas são avaliados conjuntamente. Uma opção válida em outro componente não pode autorizar ignorar o custo do componente atual.

O planner verifica identidades, quantidade exata, tags efetivas, visibilidade, origem/propriedade, duplicação entre pagamentos e capacidade conjunta sobre snapshots locais. Ordem pode ser SourceOrder ou ChosenOrder. Quantidade zero é inválida; um pagamento sem custo é omitido. Origens ocultas não expõem IDs para pagamento manual. A própria carta é inelegível por padrão; se autorizada e escolhida, a transferência do custo consome sua disposição, preservando a execução dos efeitos e impedindo um segundo descarte.

Pagamentos confirmados viram prefixos CARD_ZONE_FLOW no processador de efeitos existente, com as mesmas observações/respostas e transação dos custos de recurso. Qualquer falha posterior descarta todos os candidatos. Campos opcionais ausentes continuam omitidos dos hashes/snapshots anteriores.

Testes: **168 aprovados, zero falhas/skips**, cobrindo pagamento de duas origens, recursos + cartas, alternativas, tags, ordem, sobreposição, outro ator, origem oculta, destino cheio, disposição e falha posterior. Execução observada e sem observação produziram os mesmos resultados; repetição e snapshots retidos foram comparados. Evidência: `.runtime/tactical-mvp/results/stage05.trx`.

Autoria e limites: [pagamentos com cartas](../guides/card-zone-payments.md). A UI/transporte das escolhas e os exemplos jogáveis são a etapa seguinte; nenhum setting existente recebeu custos novos.

## Etapa 6 — Seleção de pagamentos e prova integrada

Estado: concluída em 2026-11-03.

O comando público/gateway transporta seleções por componente; a inspeção e seus caches incluem essas entradas. POST payment-preview avalia uma única revisão, publica requisitos/rostos visíveis e resultados calculados, sem expor os planos internos ou a ordem das pilhas. Dados fora da visão corrente são filtrados. O contrato de comando retornado é o mesmo DTO público aceito pelo gateway; campos internos de execução não entram no payload. OpenAPI e documentação atualizados de forma aditiva.

A sessão compacta permanece sem inspeção completa de toda a mão: a apresentação composta publica o indicador opcional `hasCardPayments`, e o cliente abre o seletor sob demanda. A cena editável reutiliza CardView, mantém escolhas locais, invalida previews ao editar, ignora respostas antigas e fecha quando muda o contexto. Confirmação usa o dispatcher/recibo existentes; cancelamento não envia gameplay. Resultados exatos/amostrados são distinguidos, sem cálculo de regra na interface. O trânsito/exílio usa o executor atual.

Criado o setting independente **Payment Workshop** (`payment-lab`), com duas cartas de prova (mão → descarte e descarte → exílio), pacote/fluxos JSON e alvo durável para teste. Nenhum setting anterior foi habilitado ou teve custos alterados.

Validação: **71 testes Core**, **2 testes API** e **71 verificações Godot offline**, todos aprovados. Mais **17 verificações na Godot com Kestrel/API real**, porta e stores próprios: abrir combate, receber preview, escolher alvo/pagamento, confirmar uma vez, animar/liberar input e mover a mesma instância entre as três zonas. O teste repetiu POSTs e preservou hashes em previews. No encerramento do host de QA, aguarda-se a liberação do decoder de áudio; não há espera nova no gameplay. Evidências: `.runtime/tactical-mvp/results/stage06-*.trx`, `stage06-godot.log` e `stage06-online.log`. O servidor de QA foi encerrado; saves e API do jogador não foram usados.

Contratos: [API de pagamentos](../api/card-payments.md) e [autoria](../guides/card-zone-payments.md).

## Etapa 7 — Tentativas persistentes

Estado: concluída em 2026-11-03.

Reutilizados RestartActivity, lifecycle por recurso, histórico RunEncounterState/CombatId, captura de build e stream determinístico da campanha. Não foi criado contador/ledger concorrente nem reset implícito de recursos. Documentada a matriz de permanência e autoria em [tentativas](../guides/encounter-attempts.md).

Corrigida a consulta de conclusão: usa a tentativa mais recente e a política de retry vinculada à run. Uma derrota retryable ou uma tentativa atual ativa não é concluída por um combate anterior encerrado. O fluxo de resolução mantém o nó pendente e não executa recompensas/saída na derrota retryable.

Validação: **88 testes Core aprovados**, zero falhas/skips. Incluem quatro resultados, restauração configurada, preservação de materiais/estado permanente, idempotência, restart, dez replays semânticos por jornada, nova tentativa consumindo o próximo seed, abertura inválida sem publicação, captura de loadout e limpeza de zonas/instâncias temporárias já existentes. Testes usam stores próprios; nenhum save do jogador foi tocado. Evidência: `.runtime/tactical-mvp/results/stage07.trx`.

## Etapa 8 — Derrota, preparação e retomada

Estado: implementada e validada por contratos/cenas em 2026-11-03. A jornada jogável completa no setting final será recertificada nas etapas 16–17, após publicação do conteúdo.

Resumo `encounterAttempt` derivado do histórico existente, sem novo estado persistido: identidade, número no nó atual, resultado, regra de resultado quando publicada e fronteiras de confirmação/retry. Consulta não confirma nem modifica gameplay. Política compartilhada com a progressão evita duas interpretações de elegibilidade.

A Godot encaminha derrotas retryable e preparação pendente para a cena de atividade existente, com componente editável próprio para tentativa. Confirmação precede edição; Ajustar build usa permissões publicadas e o loadout do personagem atual; Tentar novamente só executa START_ENCOUNTER publicado. Não há autoavanço da preparação. A saída do loadout também aguarda as leituras canônicas após autosave, além dos bloqueios existentes de gravação/recibo incerto. Comandos dedicados de composição não aparecem como botões genéricos sem payload. Inglês como fallback e textos em português.

Validação: **36 testes Core**, **9 testes API** e **71 verificações Godot** (tentativa 12, reconciliação de atividades 21, loadout 22, composição 12, navegação 4), todos aprovados. Ausência/null do resumo mantém a navegação anterior; tentativa ativa/encerrada e nó resolvido não reutilizam resumo antigo. Evidências: `.runtime/tactical-mvp/results/stage08-*.trx` e `stage08-godot.log`. Nenhum save real foi utilizado.

## Etapa 9 — Suporte configurável na engine

Estado: implementada e validada no Core em 2026-11-03. Catálogo REST, editor e prova no setting jogável pertencem às etapas seguintes.

Modo pode habilitar supportLoadoutPolicyId. Inventário Actor/Run, propriedade e equipamento por ator são associações imutáveis separadas de cartas, inscrições e relíquias. EQUIP_SUPPORT usa o dispatcher/recibos existentes; seleção/rotina, revisão, ator atual e janela são validados. Criação injeta participante normal com entidade, lado do dono, gambits autorizados e captura de equipamento no encontro. Participantes/zonas/ações/efeitos usam as autoridades existentes.

Cadência é uma restrição genérica opcional do controller, não um timer de companion: conta ativações concluídas do ator âncora, participa da seleção canônica PerRound/Circular e reconhece o contador após atuar. Contadores opcionais persistem nos frames/snapshots do combate; nova tentativa inicia novo combate. Sem âncora elegível, não há ativação dependente. Decisions por ator substituem apenas os candidatos de sua política existente; intents e driver automático consomem a mesma seleção.

Participação no resultado é uma opção explícita do participante. Um suporte excluído não mantém a partida aberta quando o dono perde, sem se tornar imune ou receber um executor próprio. Propriedades ausentes continuam omitidas em settings anteriores. Hot reload do equipamento exige RebindCompatible explícito, compatibilidade e ausência de encontro pendente; snapshots históricos não são reescritos.

Validação: **83 testes focados** aprovados, mais a suíte ampliada de **276 testes de regressão** de combate, composições, progressão e jornadas, zero falhas/skips. Incluem sequência real de fases/ativações (3ª e 6ª ativações do dono), três repetições, restauração de counters, dono derrotado com suporte vivo, âncoras inválidas, duas rotinas, dois escopos, isolamento de ator/revisão, comando duplicado/restart e dez replays semânticos do equipamento. Evidências: `.runtime/tactical-mvp/results/stage09*.trx`. Documentação: [suportes e cadência](../guides/support-loadouts.md).

## Próxima etapa

Etapa 10: catálogo REST e Godot para companion. Etapas 10–17 permanecem pendentes.
