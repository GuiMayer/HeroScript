# Contratos dos fundamentos do mundo 1

Engine mínima nesta entrega: `28` (etapa 10). Commit schema `4`, meta schema `1`.
O launcher verifica `canonical-effect-identities`, `foundation-reload-safety` e as capabilities anteriores;
saves históricos incompatíveis são preservados, não convertidos automaticamente.

## Ativação e proveniência

Preview e comando validam também o modo da revisão de destino: ele não pode
substituir os gates da elegibilidade capturada nem exigir recursos persistentes
ausentes. Um encontro terminal ainda precisa ser resolvido antes da ativação.
Encontros resolvidos preservam integralmente atores, recursos e revisão histórica;
somente estado operacional/futuro é rebindado.

`progressProvenance` registra a origem conservadora da run. A ordem monotônica é
Published → Experimental → Sandbox → DevModder → Branch. Desabilitar cheats ou
trocar de regras não apaga a origem anterior. A política continua definindo quais
origens podem contribuir; branches podem ser autorizadas explicitamente e
simulações nunca contribuem. Valores inválidos e downgrade são rejeitados antes
de persistir. O campo é opcional para modos sem política; sua classificação
efetiva também considera cenário, capabilities e linhagem.

## Recursos no GET da run

`resources` continua sendo a carteira da run; `runResourceOwnerId` identifica
seu proprietário. `characterResources` contém `ownerId`, `authority`,
`combatId` e `resources` (`current`, `minimum`, `maximum` por ID genérico).

- `PersistentPlayer`: reserva fora de encontro ativo.
- `ActiveEncounter`: ator do encontro é a autoridade, inclusive se o combate
  acabou mas sua resolução ainda não foi promovida.
- `Unavailable`: sem reserva operacional; não usa a carteira como fallback.

`playerEntity` permanece o baseline persistente. Não somar/sincronizar essas
reservas no cliente. Recuperação escolhe uma atividade legal; não envia valor
arbitrário de vida. Cheats continuam sujeitos às capabilities do modo/host.

## Perfil e disponibilidade

GET de perfil e `/unlocks` exigem jogador e `settingId`. Ambos expõem
`progressSequence`, `progressRevision`, `unlockProofs` e `availability`.
Esta última é uma leitura do catálogo **atualmente publicado** do setting,
com `contentRevision`, `progressRevision` e `options`:

- `policyId`, `unlockId`, `displayName`, árvore `condition`;
- `conditionSatisfied` e contagens por caminho em `evidenceCounts`;
- `targets`, `availableTargets` e `proof` histórica, quando existe.

Condição satisfeita não é uma concessão: somente prova autoritativa disponibiliza
alvos. Reutilizar um unlock ID para outro alvo não fabrica uma prova.
Disponibilidade atual do perfil não substitui `run.eligibility`, capturada
anteriormente. Se o conteúdo atual estiver indisponível, `availability` é nula;
as provas/histórico ainda podem ser consultados. Nenhum GET produz progresso.

Políticas `actor-resource-lifecycle-policies` e `profile-progress-policies`
usam o GET genérico de conteúdo e a validação/publicação existente.

## Probabilidade e certeza

Inspections/intents publicam `randomOutcomes.impacts`, limitado a 64 inputs:
`probability`, `probabilityUnit: probability`, `probabilitySource:
Literal|Calculated`, `probabilityCalculation`, revisão/hash de snapshot,
captures, scope e alternativas numéricas locais. `conditionalOnReachingImpact`
e `validity` explicam o limite. `isGlobalOutcomeRange` é falso e `truncated`
sinaliza o orçamento: não apresentar média/faixa global como garantia.
Leituras não avançam RNG, sequência ou hash de gameplay.

## Godot

O gateway traduz contratos, sem fórmulas ou redução de condições. Campanhas
enviam `commandId` e não `schemaVersion`; retry após resposta incerta conserva
inputs/ID. Sucesso encerra essa janela; outra tentativa recebe nova chave.
Sidebar/atividades mostram recursos projetados pela engine, separados da
carteira. Histórico mostra opções disponíveis/bloqueadas do setting atual.
Inglês continua fallback, com chaves inglesas e tradução portuguesa.

Teste offline de comunicação: `tests/foundations.gd`. Os testes REST e Godot
usam stores/transportes isolados e não alteram saves reais.
