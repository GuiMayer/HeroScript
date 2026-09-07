# ADR 0001 — Comando e autoridade de gameplay

**Status:** aceito

## Contexto

Chamadas laterais a serviços de run permitem que regras, persistência e eventos
sejam executados em ordens diferentes. Isso impede que uma request seja uma
unidade transacional clara.

## Decisão

- `RunState` é a única autoridade de gameplay e contém o combate ativo.
- Toda mutação entra por `IRunCommandGateway` como comando tipado.
- Um comando raiz recebe o snapshot, a revisão fixada e o contexto
  determinístico e produz um `RunTransitionPlan` imutável.
- Um comando raiz aceito incrementa `RunState.Sequence` exatamente uma vez.
- Frames internos podem avançar o step determinístico, mas não criam novos
  commits raiz.
- Queries não podem alterar estado nem preencher caches autoritativos.

Métodos públicos de mutação fora do gateway são dívida temporária e devem ser
removidos durante a decomposição de `RunManager`.

