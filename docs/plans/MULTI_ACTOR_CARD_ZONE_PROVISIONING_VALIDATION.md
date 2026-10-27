# Zonas por participante: contrato e validação

## Comportamento implementado

Antes de `encounter.started`, a abertura canônica materializa os participantes e garante todas as definições `ownerScope: Actor` para seus IDs de instância. Inclui jogador, adversários e aliados, independentemente de facção ou controller.

`CardZoneParticipantProvisioner.Ensure` recebe a topologia imutável existente e o grafo resolvido da run. Acrescenta somente recipientes vazios ausentes, em ordem ordinal canônica. Preserva zonas, cartas, ordem e instâncias existentes. Não cria cartas, não executa fluxos e não recebe nem avança o contexto determinístico. Chamadas repetidas retornam o mesmo estado quando não há nada a acrescentar.

O endereço mantém o escopo anterior. Exemplos: `$run::hero::hand` e `$run::dealer::hand`. Os nomes são conteúdo; o mesmo contrato funciona com `pocket` ou qualquer outro identificador válido.

O caminho alternativo `RunManager.AttachEncounter` usa o mesmo provisionador antes de seus fluxos. Se receber um candidato previamente inicializado sem as zonas necessárias, rejeita esse candidato em vez de repetir distribuição ou alterar silenciosamente seu trace. Fluxos e persistência precisam concluir antes de publicar o novo estado.

## O que continua nos JSONs

Distribuição, shuffle, descarte, recolhimento e limpeza permanecem configurados nos fluxos. Um pool RunOwner continua sendo um único monte físico; Actor cria recipientes distintos, sem duplicar cartas. `ActorScope.Any` habilita a invocação para qualquer ator elegível; não executa um loop por participantes.

Exemplo de distribuição de um pool para a zona do ator da invocação:

```json
{
  "flowId": "deal-active",
  "triggers": ["encounter.started", "activation.started"],
  "actorScope": "Any",
  "allowedInvocations": ["Boundary"],
  "steps": [{
    "stepId": "deal",
    "operation": "Move",
    "sourceZoneId": "draw",
    "sourceOwner": "RunOwner",
    "targetZoneId": "hand",
    "targetOwner": "ActiveActor",
    "selection": { "strategy": "First", "targetZoneCount": 2 },
    "onInsufficient": "AllowPartial"
  }]
}
```

Nesse exemplo, `draw` é RunOwner e `hand` é Actor. A abertura invoca `encounter.started` no contexto já existente do jogador. As ativações seguintes invocam `activation.started` no contexto do respectivo ator. A inicialização da primeira ativação continua usando o fluxo de abertura; não foi inserida uma nova distribuição no ciclo. Para não distribuir na abertura, omita esse gatilho.

Para mover a carta jogada da mão do ator para descarte compartilhado, o fluxo de resolução deve declarar `sourceOwner: ActiveActor` e `targetOwner: RunOwner`. A origem do fluxo e os dois lados da movimentação não devem ser confundidos. A movimentação não reescreve a proveniência `CardInstanceState.OwnerId`.

Reutilizar o mesmo ID de ator preserva suas zonas e cartas. Somente um fluxo explícito recolhe essas cartas. Um ator novo recebe zonas ao abrir seu encontro, sem antecipar todos os adversários do mapa. Invocações de atores durante um combate não fazem parte desta alteração.

## Fontes jogáveis e projeção

`CardZonePlaySource` é a autoridade de posse: Actor autoriza seu proprietário; RunOwner autoriza o jogador da run; Global mantém o compartilhamento configurado. Descoberta, preview e execução usam essa autoridade, inclusive para cartas físicas de bots. Origem do comando, controller, turno, fase, custos e demais regras continuam sendo verificados.

`GET /api/v1/runs/{runId}/card-zones` acrescenta `playerCanPlayFrom`. `allowsCardPlay` continua descrevendo a capacidade da definição; o novo campo descreve a permissão do jogador nessa zona. Godot e web usam o novo campo para formar a mão local. Uma mão adversária pública pode ser inspecionada sem aparecer como carta jogável do jogador.

As projeções de zonas respeitam `visibility.contents` e `visibility.order`: a mão privada adversária expõe sua contagem, sem listar suas cartas. Isso é o contrato da coleção; não constitui autenticação multiplayer nem uma política de sigilo para ferramentas de debug, consultas explícitas de outros atores ou journals históricos.

## Versão e dados históricos

A versão determinística passa de 32 para **33**, pois zonas adicionais alteram hashes de estado e candidatos legais por ator. A política estrita já existente impede continuar runs de versões incompatíveis. Nenhum save/journal antigo foi migrado, apagado ou reescrito. A reexecução semântica é certificada entre jornadas da mesma versão e revisão, sem promessa de replay entre versões incompatíveis.

## Regressões e certificação

- Provisionamento: idempotência, ordem dos participantes, inputs intocados, ausência de cartas/RNG novos, nomes arbitrários, ator posterior e configuração sem Actor.
- Abertura: jogador, aliado e adversário antes dos fluxos; preservação ao repetir IDs; candidato incompleto rejeitado; falha após uma movimentação bem-sucedida e falha de gravação sem estado parcial publicado.
- Pool JSON: cartas físicas únicas distribuídas por contexto, fonte própria jogável e carta privada alheia rejeitada diretamente pelo executor.
- Revisão e estado: grafo vinculado à run, limpeza somente por fluxo, serialização com hash preservado e três repetições equivalentes com mesmo hash.
- Clientes: regressão Godot `participant_zones` e `card_collections`; regressão web `participantZones.test.ts`.
- REST real em host de integração: pacote publicado em diretório temporário, com serviços reais e persistência isolada. Duas execuções para cada uma das variantes pública/privada com mesmos inputs; bot compra e joga sua carta; retry de lançamento/comando não distribui novamente; jogador não pode usar carta alheia nem assumir o controller da IA; reinício preserva a coleção; commits equivalentes e replay semântico validado.

Os testes não alteram os arquivos de conteúdo publicados do jogo nem seus saves. A fixture mínima de domínio está em `tests/Fixtures/participant-pool.json`; a jornada REST está em `tests/API.Tests/Integration/MultiActorPoolJourneyTests.cs`.

Resultados finais (26/10/2026):

| Validação | Resultado |
|---|---|
| Suíte completa `Core.Tests` após a correção de descoberta de ações | 1.922 aprovados |
| API: `MultiActorPoolJourneyTests`, `CombatSandboxIntegrationTests`, `RunControllerTests`, `DeckCompartmentJourneyTests`, `RoguelikeGameFlowTests` | 44 aprovados |
| Godot: `participant_zones` e `card_collections`, cópia isolada | 10 verificações aprovadas |
| Web: `participantZones.test.ts` | 1 aprovado |

A suíte completa da API não foi executada nesta certificação; os 44 testes acima são a seleção de integração executada.
