# Conteúdo, operação e módulos de plataforma

## Conteúdo imutável

Clientes leem somente conteúdo publicado pelo prefixo `/api/v1/content`.
Uma run fixa `contentRevision` no início; a mesma revisão deve ser usada para
recuperar cartas, ações, entidades e demais definições durante replay.

- `GET /settings` lista os settings jogáveis publicados, com metadados,
  revisão atual e ponto de entrada canônico (`runDefinitionId`,
  `playerEntityId` e `modeId`);
- `GET /revisions` lista revisões conhecidas;
- `GET /revisions/{revision}` recupera um manifesto;
- `GET /{kind}` e `/{kind}/{definitionId}` consultam definições de uma revisão;
- `POST /validate` valida um bundle ou revisão sem mutar runtime.

Publicação usa `/api/v1/admin/content/drafts` e requer `X-Admin-Key`. Drafts
recebem versão otimista: publicar ou atualizar com `expectedVersion` antigo
retorna `409`. A publicação cria uma revisão nova; nunca altera uma revisão já
referenciada por runs existentes.

### Settings jogáveis

Um setting é a composição completa de configurações e conteúdo que produz um
jogo. O launcher consulta `/api/v1/content/settings` e não mantém IDs de run ou
modo embutidos no cliente. Ao iniciar uma jornada, envia exatamente o
`settingId`, a revisão e o objeto `launch` anunciados pelo catálogo.

Por padrão, o host descobre, compila, valida e publica todos os settings em
`data/configs`. Uma implantação pode restringir a lista com
`Content:StartupSettings`; settings sem `launch` continuam válidos para autoria,
mas não aparecem no catálogo jogável. Uma run já iniciada permanece presa à sua
revisão imutável mesmo que outro setting seja selecionado depois.

## Operação

| Endpoint | Uso |
| --- | --- |
| `/api/v1/health/live` | Processo está respondendo. |
| `/api/v1/health/ready` | Dependências necessárias para aceitar runs. |
| `/api/v1/version` | Versão da API, da engine determinística e assembly. |
| `/api/v1/capabilities` | Recursos efetivamente habilitados no servidor. |

`/api/v1/capabilities` descreve o host. Ferramentas ligadas a uma run devem
consultar também `/api/v1/runs/{runId}/capabilities`, que cruza o perfil
operacional confiável com o teto imutável do game mode. Diagnósticos e reload
são administração, não parte do fluxo de gameplay. Veja
[Hot reload e perfis de ferramentas](../content/hot-reload-and-tool-profiles.md).

## Recursos experimentais preparados

| Área | Rotas | Estado de contrato |
| --- | --- | --- |
| Perfil | `/api/v1/profiles/{playerId}?settingId=...` | Leitura derivada por jogador e setting; sem mutação pública. |
| Daily challenge | `/api/v1/challenges/daily/current` | Tentativa, prova e ranking verificados por replay. |
| Branches | `/api/v1/runs/{runId}/branches` | Branch a partir de commit; experimental. |
| Simulações | `/api/v1/simulations` | Isoladas da run de origem; experimental. |
| Cartas | `/cards/evaluations`, `/cards/{cardInstanceId}/evaluation` | Legalidade e prévia pelo mesmo fluxo de `PLAY_CARD`; experimental. |

Essas superfícies não devem ser usadas para inferir que a engine oferece
multiplayer, economia permanente ou regras completas de TCG. Cada capacidade
precisa aparecer em `/capabilities` e no changelog antes de se tornar estável.

O `settingId` é obrigatório também em `/profiles/{playerId}/stats`, `/unlocks`,
`/achievements` e `/runs`. Não há perfil permanente global por omissão. A projeção
filtra as runs pelo jogador e pela identidade gravada do setting antes de calcular
contadores, conquistas, desbloqueios e revisão; alterações em outro setting não
afetam essa revisão. Versões de conteúdo do mesmo setting compartilham o perfil,
mas cada run mantém sua própria revisão imutável. Uma configuração sem runs
correspondentes retorna um perfil vazio, sem herdar conquistas de outra.

Desbloqueios usam [políticas declarativas](../content/profile-progress-policies.md)
e provas no commit canônico, separadas dos badges de histórico. O perfil completo
retorna `progressSequence`, `progressRevision` e `unlockProofs`. Não há bônus
permanente de atributos: os alvos são opções de cartas/upgrades/relíquias.
A captura de elegibilidade e sua aplicação em ofertas pertencem à etapa 7.

Reações, pilha e prioridade usam o mesmo gateway autoritativo das demais ações.
Elas só ficam ativas em modos cuja política de combate as habilite; modos sem
essa política continuam com resolução imediata.
