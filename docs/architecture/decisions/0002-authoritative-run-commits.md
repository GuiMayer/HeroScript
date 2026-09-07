# ADR 0002 — Histórico autoritativo por `RunCommit`

**Status:** aceito

## Contexto

Checkpoints, snapshots e journal representam hoje a mesma transição de formas
parcialmente sobrepostas. Replay e branch precisam de uma origem inequívoca.

## Decisão

- `RunCommit` é a única unidade persistente autoritativa do histórico.
- O commit contém envelope do comando, hashes anterior/posterior, steps,
  frames, fatos e `stateAfter`.
- `IRunCommitStore.AppendAsync` é a única escrita persistente de gameplay.
- latest state, journal, timeline, eventos duráveis e índice de branches são
  projeções reconstruíveis dos commits.
- Replay executa o mesmo gateway live em runtime isolado e compara o resultado
  com o commit armazenado.
- Branch referencia um commit pai imutável; nunca modifica nem trunca o pai.

Snapshots materializados podem existir somente como cache descartável.

