# Inventário atual de produtores e respostas canônicas

Atualizado em 2026-10-29. Autoria atual schema 2; conteúdos schema 1 capturados conservam adaptadores históricos. O processador universal continua dono da matemática; fatos apenas descrevem resultados reais. O lote causal gravado no commit é a autoridade do diagnóstico histórico.

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


## Fronteiras e limites

- Bootstrap declarativo é gênese; não inventa custos/dano/cura. Fluxos JSON posteriores produzem fatos.
- Um evento por mudança primitiva real; settlements se relacionam ao cálculo original, não são dano adicional inferido pelo sinal/nome do recurso.
- Status/relíquias/fases ContainerBoundary executam no lifecycle que os captura, uma única vez. Broadcast PostFact não os executa novamente.
- Criação, comandos da run, abertura do encontro e resolução de combate levam suas observações até o commit. Append falho não publica candidata nem recibo. Somente candidatos confirmados se tornam histórico.
- Âncoras de status/propostas e contadores PriorityWindow são estado serializado. Ocorrências/invocações pertencem ao envelope do comando; não são copiadas como comandos novos para branches. Ancestrais autorizados e igualdade por valor sobrevivem à serialização.
- Hidratação, timeline, preview, simulação e alternativas de IA não reaplicam fatos históricos no estado original. O Core mantém o executor único para cálculos especulativos.
- Ativação/rebind de conteúdo conserva revisão de origem da ação e revisão de destino no payload. Sua projeção não equivale a um gatilho arbitrário de hot reload; respostas que trocam revisão durante outra resposta não são uma capacidade concedida.
- Activation/Round/Combat/Run como frequências duráveis e prevenção/cancelamento arbitrários permanecem explicitamente indisponíveis; não há fallback silencioso.
- REST causal exige histórico + inspeção completa de cartas; recibos/timeline comuns não expõem o lote. Não existe endpoint para injetar fatos.
- Autorias novas passam pelo grafo completo. Os oito arquivos de regras migrados e os seis pacotes 1.1.0 são o caminho atual; formatos antigos existem apenas para versões históricas e conversão explícita.

Consulte [certificação](GAMEPLAY_EVENTS_CERTIFICATION_REPORT.md) e [progresso por commit](../plans/GENERIC_GAMEPLAY_EVENTS_IMPLEMENTATION_PROGRESS.md).
