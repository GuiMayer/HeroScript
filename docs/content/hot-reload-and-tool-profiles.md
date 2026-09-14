# Hot reload e perfis de ferramentas

O hot reload do HeroScript não altera objetos já carregados. Ele recompila um
setting completo, valida seu grafo e publica uma nova revisão imutável. Uma run
em andamento continua vinculada à revisão anterior até receber um comando
explícito de ativação. Assim, o journal sempre informa exatamente sob quais
regras cada comando foi processado.

## Duas autoridades independentes

O acesso efetivo a uma ferramenta é a interseção de duas políticas:

1. O perfil operacional confiável do host, definido em `ToolAccess` na
   configuração da API. Conteúdo e mods não podem elevá-lo.
2. As políticas imutáveis do `GameMode`, publicadas no JSON e capturadas pela
   run. Elas limitam timeline, replay, cheats, sandbox e ativação de conteúdo.

`GET /api/v1/runs/{runId}/capabilities` retorna essa interseção. Clientes devem
usar `granted` para mostrar ou ocultar controles e ainda tratar `403` como a
decisão final do servidor. `GET /api/v1/capabilities` informa o perfil do host,
mas não substitui a consulta contextual da run.

## Perfis operacionais

| Perfil | Ferramentas liberadas pelo host |
| --- | --- |
| `normal` | Timeline somente para leitura, inspeção histórica e verificação de replay. |
| `experimental` | Tudo de `normal`, mais cheats de recursos e zonas de cartas. |
| `sandbox` | Tudo de `experimental`, mais criação/leitura de branches, simulação, autoria de cenários e inspeção completa de cartas. |
| `dev_modder` | Todas as ferramentas, incluindo restore de head, ativação de revisão e operações administrativas. |
| `custom` | Somente as capacidades listadas em `CustomCapabilities`. |

Em `custom`, `timeline.branch.create` também concede
`timeline.branch.read`; `timeline.inspect_state` também concede
`timeline.read`. IDs desconhecidos impedem a inicialização da API.

Exemplo operacional:

```json
{
  "ToolAccess": {
    "Profile": "custom",
    "CustomCapabilities": [
      "timeline.read",
      "timeline.inspect_state",
      "simulation.run"
    ]
  }
}
```

O ambiente `Development` usa `dev_modder`; produção usa `normal`. Isso não
transforma um game mode normal em sandbox: a política do modo continua sendo o
teto da run.

## Fluxo de autoria e publicação

Pré-requisitos para o atalho de reload:

- `AllowConfigReload=true`;
- administração habilitada e `X-Admin-Key` válido;
- perfil `dev_modder` ou `custom` com `admin.operations`.

Fluxo recomendado:

1. Edite os packages em `data/configs/{configName}`.
2. Valide o setting com
   `POST /api/v1/admin/settings/{settingId}/validate`.
3. Recompile, valide, publique e pré-aqueça atomicamente com
   `POST /api/v1/admin/content/reload` e o corpo
   `{ "settingId": "default" }`.
4. Leia a revisão retornada ou confirme-a em
   `GET /api/v1/content/revisions?configName=default`.
5. Para uma run já existente, consulte
   `GET /api/v1/runs/{runId}/content/activation-preview?targetRevision={revision}`.
6. Se o preview estiver liberado, envie `ACTIVATE_CONTENT_REVISION` pelo
   endpoint canônico de comandos da run, com sequência e step esperados.

O reload é uma transação completa. Schema inválido, referência ausente,
políticas incompatíveis ou falha de publicação deixam a revisão anterior
ativa. O resultado operacional é publicado como `ContentReloadedEvent`, tanto
para sucesso quanto para rejeição, com revisão, warnings e invalidação de cache.

## Ativação numa run existente

O preview não modifica estado. Ele informa:

- revisão atual e revisão alvo;
- artefatos adicionados, removidos ou modificados;
- warnings de compatibilidade;
- bloqueios de política e de lifecycle.

A ativação exige `content.activate` nas duas autoridades. Ela é rejeitada
durante combate ativo e nunca reescreve proveniência de combates concluídos.
Somente comandos futuros passam a consumir a nova revisão; commits anteriores
continuam reproduzíveis com o conteúdo original.

## Regras para clientes Godot

- Trate a API como autoridade; não derive permissões apenas do nome do modo.
- Atualize `/capabilities` da run junto com as demais projeções após cada recibo.
- Oculte branches e simulação quando não constarem em `granted`.
- Invalide permissões locais ao trocar ou abandonar uma run.
- Não envie ferramentas negadas localmente, mas sempre aceite que a API ainda
  pode responder `403` por mudança operacional.
- Timeline visível não implica branch, restore ou mutação histórica.

Esse modelo permite uma interface única para jogo normal, experimentação,
theorycraft, sandbox e ferramentas de modding sem tornar conteúdo moddável capaz
de conceder privilégios ao próprio processo.
