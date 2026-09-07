# ADR 0003 — Fronteira de conteúdo publicado

**Status:** aceito

## Contexto

Loaders opcionais e fallbacks de filesystem tornam uma run sensível ao estado
do processo e a arquivos alterados depois de sua criação.

## Decisão

- `Package` é uma fonte base ou mod data-only, com manifest e dependências.
- `Setting` ordena packages e escolhe o game mode.
- `ContentBundle` é o conteúdo totalmente composto e validado.
- `ContentRevision` é o hash canônico do bundle.
- Gameplay acessa conteúdo exclusivamente por `IContentRuntimeResolver` e pela
  revisão fixada em seu snapshot.
- Patch, herança, hot reload e acesso a arquivos existem apenas na publicação.
- Hot reload de uma run é um comando explícito, permitido pelo modo e registrado
  no histórico.
- Formatos implícitos, schemas legados e fallback silencioso são rejeitados.

