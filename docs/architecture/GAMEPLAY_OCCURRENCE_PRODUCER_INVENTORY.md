# Inventário de produtores canônicos — etapa 4

Estado: instrumentação opt-in de candidatos, antes da migração dos consumidores. A autoridade numérica e de estado continua nos processadores/reduções existentes.

| Família | Produtor observado | Composição que usa o fato |
|---|---|---|
| Recursos e settlements de efeitos | `GameplayEffectObservation`, `GameplayResourceFactProducer` | Executor comum, por operando/registro real; vínculos de cálculo e impacto. |
| Custos e reembolsos de combate | `ActionEffectCosts`, resolvedor de legalidade observado | Somente o candidato escolhido; simulações de propostas descartadas não produzem fatos. |
| Carteira da run | `GameplayRunObservation`, `RunResourceTransitions` | Loja, reroll, recompensa, preparação, diálogo, estação de inscrições, transformação e ferramenta de recursos. |
| Atributos e sincronização dependente | `GameplayEntityFactProducer`, executor comum | Atributo primeiro; transporte/restrição dependente como descendente. |
| Aplicação, remoção, dispel e condensação | Projetores de status/modificadores/receita | Donos reais, seleção de stacks e receita canônica, sem recalcular os valores. |
| Remoção sem correspondência | Tentativa `effect.application.resolved` | Resultado explícito sem fabricar uma alteração de stacks. |
| Duração e expiração | Lifecycles observados e `GameplayModifierObservation` | Comando, ativação, rodada, combate, nó e run; ordem e elegibilidade anteriores. |
| Triggers de status e relíquias | Entradas observadas dos lifecycles existentes | Momento, captura, revalidação e idempotência continuam nos lifecycles. |
| Regeneração e refresh | Lifecycle de recurso/fronteiras | Operandos já escolhidos pela política, inclusive Set; recurso sem semântica pelo nome. |
| Restrição de saldo | `GameplayResourcePolicyObservation` | Materialização de relíquias, entrada do executor, materialização do personagem; fato de regra seguido do Set real. |
| Transporte persistente/encontro | `ActorResourceLifecycleTransitions` | Payload próprio para cópia/reset e limites; entrada, saída e sincronização. |
| Cartas e ordenação de zonas | `GameplayCardFlowObservation`, projetor de primitivas | Efeitos, disposição de cartas, fronteiras, concessões, ferramentas e fluxos externos. |
| Fases e fronteiras | `GameplayBoundaryObservation` e adaptadores de fase | Início/fim de combate, ativação, rodada, fase e run. |
| Sequência circular | `SequenceChangePayload` | Mutação real da sequência pelo executor de efeitos. |
| Threshold e derrota | `GameplayRuleFactProducer` | Resultado da política canônica, sem presumir recurso letal. |
| Revisão e rebind | `GameplayContentObservation` | Somente a ativação selecionada; origem da ação separada da revisão de destino. |
| Comandos e continuação automática | Planos observados dos coordenadores e driver | Identidade real, escopo de branch, filhos e fechamento do comando. |

## Limites conceituais

- O bootstrap declarativo de personagens, recursos, cartas e zonas vazias constitui o estado gênese. Não é traduzido em dano, cura ou pagamentos fictícios. Os fluxos configurados que atuam após esse bootstrap são observados.
- Hidratação, replay de snapshots, consultas, previews, alternativas de IA e candidatos descartados não publicam fatos como se fossem ações novas.
- Alterar uma composição de carta/loadout não executa seus componentes. O cálculo de atributos derivados permanece uma consulta; os efeitos executados depois usam o produtor comum. Disponibilidade e registros desses componentes pertencem às etapas de providers/containers.
- `action.completed` encerra o processamento daquele comando interno. Proposta, cancelamento, resolução e continuação da PriorityStack têm sua integração específica na etapa 10; não são simulados por esse nome.
- Os contextos observados e cursores locais não fazem parte do snapshot, dos caches de legalidade ou do event bus operacional. As etapas 11–12 cuidarão do corte de autoria e da persistência das ocorrências/âncoras históricas.
- `Unobserved` conserva lacunas reais de aplicações; não é um mecanismo para confirmar parcialmente uma ação ou ocultar um tipo novo sem produtor.

## Evidência

Resultados e caminhos das execuções estão no [progresso de implementação](../plans/GENERIC_GAMEPLAY_EVENTS_IMPLEMENTATION_PROGRESS.md). A árvore versionada é certificada em uma cópia isolada para não misturar as alterações locais anteriores ao plano com os commits destas etapas.

## Atualização após a etapa 7

A descrição anterior registra a instrumentação da etapa 4. As reações de recurso das relíquias já foram migradas: a leitura de definições capturadas ocorre em `LegacyResourceReactionObserverProvider` e o matching/agendamento ocorre no dispatcher comum de `EffectTriggerExecutor.Responses`. A coleta direta e `EffectReactionPolicies.Matches` foram removidos; `EffectReactionPolicies.Validate` permanece validando o formato histórico. Os cálculos/outcomes e a aplicação continuam exclusivamente no pipeline/executor canônicos.

O executor normal usa fatos locais descartáveis quando há essa família de reações; isso não habilita gravação de journal novo nem publica eventos intermediários. O executor observado exige os IDs reais de setting/root-run/branch. Os templates internos conservam IDs e parent.* históricos, com comparação de hashes completos contra a árvore anterior à migração. Fronteiras, status periódicos/expiração, gatilhos de relíquias e fases continuam aguardando a migração da etapa 8.


## Integração atual dos consumidores (até 9h)

| Caminho | Respostas no executor normal | Limite atual |
|---|---|---|
| Efeitos de recurso, atributo, status, carta/zona e sequência | Sim, com declarações capturadas e providers comuns | Publicação padrão bloqueada até o cutover. |
| Gatilhos capturados de status, relíquias e fases | Sim, pelo dispatcher comum | Templates históricos internos preservam identidades. |
| Fronteiras de combate/rodada/ativação e fases | Sim, uma oportunidade canônica por fronteira | Executor geral deve receber a capacidade de ocorrências (composição API já ligada). |
| Duração/expiração de status | Sim | Snapshot da fronteira impede processamento retroativo de instâncias novas. |
| Duração/expiração de modificadores em combate | Sim | Contadores ainda candidatos, sem durabilidade entre comandos. |
| Refresh primitivo da ativação | Sim | Política original continua decidindo recurso/operando/timing. |
| Movimentos externos de cartas e modificações fora de combate | Instrumentados; integração de respostas ainda pendente | Não considerar esta família migrada apenas porque já produz fatos observados. |
| Intenções, PriorityStack e retomada | Integração anterior do jogo preservada | Nova causação/continuidade de respostas pertence à etapa 10. |
| Journal/REST/mensagens de ocorrências | Pendente | Etapas 12–14; consultar timeline não executa respostas. |

A etapa 8 está concluída. A etapa 9 permanece em execução; 10–15 não foram declaradas concluídas. Evidências por árvore/commit estão no documento de progresso.
