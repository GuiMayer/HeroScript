# Integração de clientes

O HeroScript é uma engine headless. Um cliente Godot, web ou Unity é responsável
apenas por entrada, apresentação e armazenamento de preferências locais; regras
de gameplay, aleatoriedade e transições pertencem à API.

## Contrato único

Use exclusivamente caminhos sob `/api/v1`. A referência de máquina é
`/openapi/v1.json`, complementada por `docs/api/README.md` e pelos exemplos em
`examples/http/`. Não há aliases sem versão nem caminhos alternativos de
migração.

## Fluxo de uma run

1. Crie uma run: `POST /api/v1/runs` com seed e, quando necessário, revisão de
   conteúdo.
2. Renderize `GET /api/v1/runs/{runId}`, `/map` e `/available-commands`.
3. Envie toda decisão por `POST /api/v1/runs/{runId}/commands`.
4. Durante um encontro, consulte o read model de combate e envie comandos por
   `POST /api/v1/combats/{combatId}/commands`.
5. Depois de cada resposta aceita, atualize a UI somente com o estado e hash
   retornados pelo servidor.

Cada comando inclui um `commandId` novo e os valores observados de
`expectedSequence` e `expectedStep`. Reenvie o mesmo comando após timeout.
Em `409`, descarte a intenção local, recarregue o read model e solicite uma
nova escolha. Em `422`, mostre a regra rejeitada sem tentar reproduzir regras
no cliente.

Seleções de carta, loja, preparação e deck também são comandos da run; suas
rotas específicas são somente de leitura. Para criar ou alterar definições de
ações, entidades, status e gambits, use o fluxo administrativo de drafts e
publicação — a revisão já publicada nunca é alterada em execução.

## Godot

Mantenha um único serviço de rede baseado em `HTTPRequest`. Ele deve preservar
`runId`, `sequence` e `step` do último read model aceito, e expor sinais para a
UI reagir a sucesso, conflito e rejeição de regra. Para o primeiro protótipo,
atualize a tela após cada comando HTTP; SSE em
`/api/v1/runs/{runId}/events/stream` é opcional e serve para projeções e
animações, nunca como fonte de verdade.

O servidor pode ser executado localmente como processo separado durante o
desenvolvimento. Não coloque chave `X-Admin-Key` em um cliente distribuído;
endpoints administrativos não participam do loop de jogo.

### Atualizar sem reconstruir a interface

“Atualizar a tela” significa reconciliar o novo read model, não destruir toda a
árvore visual após cada comando. Preserve a instância da tela enquanto run,
combate ou atividade permanecem no mesmo contexto e atualize componentes por
identidade estável (`runId`, `combatId`, `instanceId`, `cardInstanceId` e IDs de
zona). Troque de tela somente quando o contexto realmente mudar.

Boas práticas para clientes visuais:

- mantenha snapshot autoritativo e estado efêmero da UI separados; seleção,
  foco, scroll e cursor de animação nunca entram no comando;
- construa o próximo estado visual de forma atômica, aplicando tema, escala de
  texto e acessibilidade antes do frame ser desenhado;
- anime entrada apenas para entidades realmente novas; refresh não deve repetir
  todas as animações;
- reserve espaço para controles contextuais ou anime explicitamente o relayout,
  evitando que botões estáveis saltem quando outro aparece;
- associe toda leitura assíncrona à sequência/hash observados ou a uma geração
  local de renderização e descarte respostas obsoletas;
- depois de timeout ou falha de refresh, bloqueie input até ressincronizar em vez
  de continuar sobre uma projeção antiga.

O showcase em `examples/godot-engine-showcase` implementa essa política em
`CombatScreen.refresh_state()` e `ActivityScreen.refresh_state()`.
