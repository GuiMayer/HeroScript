# Sistema de diálogos

Diálogos são atividades da run, definidos em JSON e executados pela engine. A Godot apresenta a fala e envia a resposta escolhida pela REST API. O estado narrativo é imutável e integra o mesmo commit, controle de versão, idempotência, histórico e replay das demais ações.

## Jogar e experimentar

Na campanha **The Ember Path**, inicie uma nova jornada, vença o primeiro combate e receba a relíquia. A próxima parada é **The Keeper / A Guardiã**. Pressione **TALK / CONVERSAR**. Perguntar sobre o topo abre uma informação nova; comprar uma carta custa 20 de ouro e usa o efeito universal `ADD_CARD_TO_HAND`.

Para testar apenas conversas pela API, crie uma run com `modeId: "dialogue_demo"`. Esse modo possui duas visitas à mesma personagem: a memória é compartilhada entre elas, enquanto as escolhas `once` pertencem à instância da conversa. O exemplo pode ser aberto na Godot pelo fluxo de continuar uma run.

Arquivos de exemplo:

- `data/configs/default/Resources/dialogues/ember_keeper.json`
- `data/configs/default/Resources/runs/dialogue_demo.json`
- `data/configs/default/Resources/modes/dialogue_demo.json`

## Definir conteúdo

Registre cada arquivo em `data/configs/default/package.json` com `kind: "dialogues"`. O fluxo normal de publicação valida e revisiona o conteúdo. Adicione ao mapa uma atividade com `type: "Dialogue"`, `definitionId` correspondente e `completionPolicy` explicitamente `Required` ou `Optional`.

```json
{
  "welcome": {
    "dialogueId": "welcome",
    "title": { "text": "At the gate", "translations": { "pt_BR": "No portão" } },
    "startNodeId": "hello",
    "nodes": [{
      "nodeId": "hello",
      "speaker": { "text": "The Keeper", "translations": { "pt_BR": "A Guardiã" } },
      "portraitId": "ember_keeper",
      "text": { "text": "Will you help us?", "translations": { "pt_BR": "Vai nos ajudar?" } },
      "choices": [
        {
          "choiceId": "accept",
          "text": { "text": "Yes.", "translations": { "pt_BR": "Sim." } },
          "setFlags": { "gate.help": "accepted" }
        },
        { "choiceId": "leave", "text": { "text": "Not today.", "translations": { "pt_BR": "Hoje não." } } }
      ]
    }]
  }
}
```

Cada nó tem uma fala, um interlocutor opcional, um retrato opcional, efeitos de entrada e uma lista ordenada de respostas. Uma resposta sem `nextNodeId` encerra a conversa. Para diálogo linear, use uma única resposta `Continue` apontando para o próximo nó. O jogador confirma cada transição: não há execução automática de ciclos.

`text` é o fallback em inglês. `translations` associa códigos como `pt_BR` a textos traduzidos. IDs de nós, escolhas, recursos e flags são estáveis e independentes do idioma. Traduzir ou mudar o visual não altera a decisão enviada à engine.

## Escolhas e condições

Uma escolha pode declarar `costs`, `effects`, `setFlags`, `once`, `condition`, `hideWhenUnavailable` e `unavailableText`. Custos usam qualquer resource existente na run. Condições consultam o estado **antes** da escolha; custos são pagos antes dos efeitos. Flags são strings, têm escopo da run e sobrevivem a outras conversas. Uma flag ausente não satisfaz `Flag`, inclusive se o valor esperado for uma string vazia.

| `kind` | Campos | Significado |
|---|---|---|
| `All` / `Any` | `children` | Todas / pelo menos uma condição |
| `Not` | `children` com exatamente um item | Negação |
| `Flag` | `id`, `value` | Memória narrativa igual ao valor |
| `ResourceAtLeast` | `id`, `amount` | Valor atual do resource suficiente |
| `HasCard` / `HasRelic` | `id` | Possui a definição indicada |
| `VisitedNode` / `ResolvedNode` | `id` | Visitou / concluiu uma parada do mapa |

`once: true` impede repetir uma escolha do mesmo nó na mesma conversa. Para limitar uma recompensa durante a run inteira, combine `Not(Flag(...))` com `setFlags`. `hideWhenUnavailable` esconde respostas indisponíveis; caso contrário a UI mostra o botão desabilitado e o texto explicativo configurado. A API publica somente os IDs atualmente executáveis em `available-commands` e revalida a escolha ao receber o comando.

A validação rejeita nós e escolhas duplicados, referências de conteúdo inexistentes, destinos inválidos, nós inalcançáveis, ciclos sem caminho estrutural para um encerramento e custos inválidos. Ela não tenta provar todas as combinações possíveis de estado: o autor deve oferecer uma saída incondicional quando uma conversa não puder ficar bloqueada. Efeitos que dependem de espaço na mão, por exemplo, ainda podem rejeitar uma opção em execução; a transação inteira é revertida.

## Efeitos e atomicidade

A ordem da transação de escolha é: validar atividade/instância/nó/condição → gastar recursos → aplicar flags → executar efeitos da escolha → executar efeitos de entrada do próximo nó → atualizar histórico e cursor → realizar um commit.

Os efeitos são `EffectDefinition` comuns, adaptados pelo `RunActivityEffectExecutor` ao processador universal. Não existe interpretador de dano, recursos ou cartas exclusivo do diálogo. Como nas outras atividades fora do combate, efeitos devem ser garantidos (`chance: 1`), direcionados ao dono da run e persistíveis nela; status de atores de combate não são aceitos nesse contexto. Um erro em qualquer ponto rejeita toda a transação. O ID do comando evita repetir custos e recompensas em retries de rede.

O JSON da conversa e seu grafo ficam capturados no início. Publicar uma revisão nova não reescreve conversas em andamento. Recursos, cartas, fórmulas e modifiers referenciados pelos efeitos continuam seguindo a revisão ativa da run, inclusive quando o modo permite uma ativação explícita de conteúdo durante o desenvolvimento. Essa ativação é journalizada. Remover uma dependência usada pela conversa pode fazer uma escolha falhar atomicamente; o conteúdo deve manter as dependências necessárias. Uma nova conversa usa a definição da revisão então ativa.

## Contrato REST

- `GET /api/v1/content/dialogues`: catálogo revisionado; aceita os mesmos parâmetros de revisão/configuração dos outros conteúdos.
- `GET /api/v1/content/dialogues/{id}`: definição de uma conversa.
- `GET /api/v1/runs/{runId}`: `dialogues` contém projeções com fala, interlocutor, opções visíveis, disponibilidade, custos, transcrição e encerramento; `narrativeFlags` contém a memória.
- `GET /api/v1/runs/{runId}/available-commands`: comandos legais e IDs de respostas executáveis.
- `POST /api/v1/runs/{runId}/commands`: `START_DIALOGUE` com `{ "dialogueId": "ember_keeper" }`; `CHOOSE_DIALOGUE_OPTION` com `{ "dialogueInstanceId": "...", "nodeId": "greeting", "choiceId": "ask" }`.

Use o envelope canônico com `commandId`, `expectedSequence` e `expectedStep`. Após o encerramento, `RESOLVE_NODE` conclui a parada; a navegação usa `ADVANCE_NODE`. Uma atividade opcional também permite `RESOLVE_NODE` antes de encerrar a conversa. Comandos rejeitados não avançam a versão.

Nos commits brutos, o estado persistido fica em `stateAfter.narrative` (definições capturadas, histórico de IDs e flags). Esse campo é omitido enquanto não existe narrativa, preservando os hashes de snapshots anteriores sem diálogos. A projeção REST é distinta do snapshot interno. Replays e branches usam os commits existentes, sem um segundo armazenamento narrativo.

## Godot e substituição visual

`activity_choices.gd` converte apenas os IDs anunciados em payloads. `dialogue_presenter.gd` localiza texto e cria o modelo visual. `dialogue_panel.gd` apresenta esse modelo e emite uma intenção com o payload anunciado. A tela de atividade encaminha a intenção para `GameSession`, que usa o gateway e o transporte REST existentes. O painel não conhece HTTP nem executa condições.

Retratos usam a categoria `portraits` de `data/art_manifest.json`; substitua o slot e o arquivo da arte para trocar o retrato. Textos longos e escolhas ficam na área rolável; botões usam o foco e o tema do jogo. Pause e configurações continuam disponíveis durante a conversa. A UI oferece histórico de falas, com disponibilidade sinalizada por botão e texto, além da cor.

Esta entrega implementa conversas em atividades da run. Interromper uma resolução de combate, diálogos simultâneos, dublagem sincronizada e um editor visual de grafos ainda exigem extensões próprias; não são simulados pela interface.

## Verificação

`DialogueTests` cobre determinismo, serialização, condições, rollback, repetição e validação do grafo. `DialogueFlowTests` executa o fluxo REST com custo e carta real, idempotência, servidor reiniciado, memória entre conversas, uma branch com outra decisão e verificação do replay. `tests/dialogue.gd` exercita a UI com a API real, traduções e resoluções.
