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
