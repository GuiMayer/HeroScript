# Governança do contrato HTTP

## Fonte de verdade

O contrato público do HeroScript é a especificação OpenAPI `v1` publicada pela
API em `/openapi/v1.json`. O arquivo versionado em
`openapi/heroscript-v1.json` é a cópia revisável usada por CI e por clientes que
precisam trabalhar offline. A documentação em Markdown explica decisões e
fluxos; ela não substitui o contrato de máquina.

## Escopo público

As rotas sob `/api/v1` são classificadas com uma das seguintes estabilidades:

| Classificação | Compromisso |
| --- | --- |
| `stable` | Pode ser consumida por clientes. Mudanças incompatíveis exigem nova versão maior. |
| `experimental` | Pode mudar em versão menor; a resposta explicita essa condição. |
| `planned` | É documentada apenas como intenção e não integra o contrato executável. |
| `admin` | Exige credencial administrativa e não é caminho autoritativo de gameplay. |
| `legacy` | Adaptador temporário em `/api/*`; não recebe capacidades novas. |

O núcleo público estável é formado por consultas e comandos autoritativos de
run e combate, conteúdo publicado, health/capabilities, replay e eventos
duráveis. Perfis, daily challenge, branches, simulações e superfícies TCG são
experimentais até uma revisão explícita desta política.

## Evolução

- Adições compatíveis podem ocorrer em `v1`.
- Campos de resposta existentes nunca mudam de significado; campos novos são
  opcionais para consumidores.
- Remoções, renomes e mudanças semânticas exigem `/api/v2`.
- Um endpoint legado recebe aviso de depreciação antes de ser removido e aparece
  no mapa de migração.
- O changelog registra toda alteração de contrato publicada.

## Disciplina determinística

Mutações de gameplay só entram por gateways de comando. A documentação de cada
comando deve declarar `commandId`, `expectedSequence`, `expectedStep`, revisão
de conteúdo, seed, hash de estado, efeitos idempotentes e eventos emitidos.
Rotas administrativas, diagnósticos e simulações não podem alterar uma run
ativa fora desse fluxo.
