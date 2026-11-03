# MVP da campanha tática — contrato de produto

Data: 2026-11-02. Escopo: setting opt-in `prototype-test`.

Fonte: direcionamento de 2026-10-30 e plano `docs/plans/TACTICAL_CAMPAIGN_MVP_IMPLEMENTATION_PLAN.md`. Os GDDs anteriores continuam referências históricas; não são requisitos cumulativos deste MVP.

## Estado em 2026-11-03

Capítulo implementado e validado tecnicamente em fonte limpa, com três encontros,
preparação, diálogos, recompensa exclusiva, build persistente e retry. Não está
integralmente certificado: encerramento de turno ainda supera o alvo de 100 ms de
p95 e falta playtest externo de compreensão/balanceamento. Os critérios de produto
abaixo continuam critérios de aceite, não resultados automaticamente comprovados.

## Hipótese a validar

O jogador entende a derrota, altera sua composição/atributos/rotina de suporte e percebe uma diferença na próxima tentativa. A campanha não é descartada ao perder. Código compartilhado da engine não reconhece nomes ou números específicos deste jogo.

## Recorte

Um herói de Calor; um companion de suporte; duas rotinas selecionáveis; dois módulos alternativos; três encontros, sendo o último um chefe; diálogos curtos; escolha guardar/decompilar; preparação entre tentativas; save/retomada. Cartas repetidas são permitidas: 20 posições não exigem 20 designs únicos. Arte e áudio atuais permanecem placeholders.

## Preset inicial de playtest

São escolhas de conteúdo reversíveis, não novos defaults globais da engine. Seus IDs, números e referências estão publicados em JSON. Veja as [regras executáveis](PROTOTYPE_TEST_RULES.md) para o preset implementado e o [relatório de validação](../validation/TACTICAL_CAMPAIGN_MVP.md) para evidências e gates ainda abertos.

| Regra | Preset |
| --- | --- |
| Composição | 15 cartas elegíveis do herói e 5 do módulo selecionado; reserva fora da compra |
| Módulos | Void paga com cartas da mão; necromântico paga com cartas do descarte |
| Troca | Fora do combate, substituição atômica de instâncias; manter melhorias/propriedade |
| Mão | Cinco iniciais, duas por ativação posterior do herói, limite oito; reter cartas comuns |
| Mão cheia | Mover apenas a quantidade que cabe; não perder silenciosamente a próxima carta |
| Banimento | Zona temporária de combate; retorno ao estado persistente após encerramento/retry |
| Sacrifício | Jogador escolhe as instâncias; carta em resolução não pode pagar seu próprio custo neste preset |
| Calor | 0–100; ganho/queda e curvas são parâmetros de conteúdo; não há energia universal acrescentada implicitamente |
| DoT | Potência capturada ao aplicar; ticks sem crítico no preset |
| Condensação | Consumir stacks com o processador existente; não reaplicar scaling já capturado |
| Crítico | Bônus linear por tier; acertos independentes; multiplicadores e chance definidos na política global do setting |
| Molhado | Não apaga Queimadura; concede um bônus explícito de 25% à liberação condensada de fogo, por dois avanços do contador de duração configurado do alvo; validar o timing em teste |
| Suporte | A cada três ativações do herói; rotinas priorizam maior/menor valor de um recurso configurado do adversário; desempate canônico |
| Retry | Nó continua pendente; próxima tentativa captura a build atual; vida inicial cheia e Calor/defesa temporária reiniciados |
| RNG no retry | Continuidade da sequência determinística da campanha; não prometer a mesma mão a cada tentativa |
| Derrota/empate/abandono | Tentativa registrada; volta à preparação sem recompensa e sem perder progresso permanente |
| Recompensa | Guardar na reserva OU decompilar em materiais; oportunidade terminal e idempotente |
| Persistência | Uma sessão de campanha do personagem no setting; nenhum inventário duplicado em perfil paralelo |

Molhado/condensação é hipótese de sinergia para playtest, não reivindicação de coerência física/lore. Se o suporte existente exigir ajustes de duração, registrar a unidade final antes de publicar o setting, sem fazer a Godot contar turnos.

## Permanência por tentativa

| Estado | Após derrota retryable | Ao abrir a próxima tentativa |
| --- | --- | --- |
| Nós concluídos, diálogo/flags permanentes | Preservar | Não repetir consequências |
| Cartas possuídas e mutações permanentes | Preservar | Reusar as mesmas instâncias válidas |
| Composição/módulo, inscrições e rotina | Preservar; permitir edição na preparação | Capturar a seleção confirmada |
| Materiais e recompensas já confirmadas | Preservar | Sem nova concessão automática |
| Mão/compra/descarte/banimento | Encerrar pelo fluxo JSON | Distribuir somente a composição ativa |
| Vida/Calor/defesa transitórios | Resolver conforme lifecycle | Aplicar valores iniciais configurados |
| Status e cooldowns da batalha | Encerrar pelo escopo configurado | Inicializar estado transitório novo |
| RNG e journal | Preservar/avançar pelas transições normais | Não reverter commits anteriores |
| Tentativa anterior | Preservar como histórico | Criar identidade determinística nova |

Custos permanentes de preparo não são cobrados novamente por voltar ao mesmo menu; somente um novo comando confirmado pode gastá-los. Efeitos de combate não destroem permanentemente cartas possuídas neste preset.

## Autoridades e limites

- Inscrição equipa o personagem e altera seus atributos.
- Modificador é item aplicado ao container de uma carta.
- Compartimento concede componentes genéricos; não é a seleção de deck ativo.
- Zona governa entrada/saída/visibilidade; não contém regras universais de mochila ou descarte.
- Companion é configuração de suporte persistente com comportamento da engine, não timer da interface.
- Game over, targetabilidade e resultado do grupo vêm das políticas; o suporte não deve impedir a derrota do herói.
- Regras numéricas pertencem aos JSONs; layout/estilo e animações usam as cenas e recursos existentes.

## Critério de aceite de produto

Um capítulo pode ser concluído pelo menu normal, com ao menos uma derrota seguida de ajuste e retry. O jogador consegue explicar custo, retenção, Calor, função do companion e guardar/decompilar. Playtest e balanceamento ainda são necessários: passar em testes determinísticos não prova diversão.

Editor livre de gambits, outros heróis, campanha longa, arcade infinito, Workshop, monetização e arte final ficam fora deste ciclo.
