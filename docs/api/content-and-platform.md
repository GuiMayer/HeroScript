# Conteúdo, operação e módulos de plataforma

## Conteúdo imutável

Clientes leem somente conteúdo publicado pelo prefixo `/api/v1/content`.
Uma run fixa `contentRevision` no início; a mesma revisão deve ser usada para
recuperar cartas, ações, entidades e demais definições durante replay.

- `GET /revisions` lista revisões conhecidas;
- `GET /revisions/{revision}` recupera um manifesto;
- `GET /{kind}` e `/{kind}/{definitionId}` consultam definições de uma revisão;
- `POST /validate` valida um bundle ou revisão sem mutar runtime.

Publicação usa `/api/v1/admin/content/drafts` e requer `X-Admin-Key`. Drafts
recebem versão otimista: publicar ou atualizar com `expectedVersion` antigo
retorna `409`. A publicação cria uma revisão nova; nunca altera uma revisão já
referenciada por runs existentes.

## Operação

| Endpoint | Uso |
| --- | --- |
| `/api/v1/health/live` | Processo está respondendo. |
| `/api/v1/health/ready` | Dependências necessárias para aceitar runs. |
| `/api/v1/version` | Versão da API, da engine determinística e assembly. |
| `/api/v1/capabilities` | Recursos efetivamente habilitados no servidor. |

Ferramentas devem consultar `capabilities` antes de usar recursos opcionais.
Diagnósticos e reload são administração, não parte do fluxo de gameplay.

## Recursos experimentais preparados

| Área | Rotas | Estado de contrato |
| --- | --- | --- |
| Perfil | `/api/v1/profiles/{playerId}` | Leitura derivada; sem mutação pública. |
| Daily challenge | `/api/v1/challenges/daily/current` | Tentativa, prova e ranking verificados por replay. |
| Branches | `/api/v1/runs/{runId}/branches` | Branch a partir de commit; experimental. |
| Simulações | `/api/v1/simulations` | Isoladas da run de origem; experimental. |
| Cartas | `/cards/evaluations`, `/cards/{cardInstanceId}/evaluation` | Legalidade e prévia pelo mesmo fluxo de `PLAY_CARD`; experimental. |

Essas superfícies não devem ser usadas para inferir que a engine oferece
multiplayer, economia permanente ou regras completas de TCG. Cada capacidade
precisa aparecer em `/capabilities` e no changelog antes de se tornar estável.

Reações, pilha e prioridade usam o mesmo gateway autoritativo das demais ações.
Elas só ficam ativas em modos cuja política de combate as habilite; modos sem
essa política continuam com resolução imediata.
