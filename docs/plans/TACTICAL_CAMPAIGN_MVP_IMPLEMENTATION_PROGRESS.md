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

## Próxima etapa

Etapa 6: avaliação pública e seletor de pagamentos na Godot. Etapas 6–17 permanecem pendentes. O plano possui 18 etapas numeradas de 0 a 17; não contém uma etapa 18 adicional.
