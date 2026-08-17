# Changelog do contrato público

## v1 — 2026-08-16

- Publicado contrato OpenAPI em `/openapi/v1.json`.
- Definidos gateways idempotentes de comandos de run e combate.
- Documentadas projeções SSE por AsyncAPI.
- Classificadas rotas históricas `/api/*` como adaptadores legados.
- Respostas de adaptadores legados agora anunciam a depreciação em tempo de
  execução e apontam para o contrato sucessor em `/openapi/v1.json`.

Mudanças incompatíveis futuras serão anunciadas aqui antes da remoção de uma
rota estável ou de um campo público.
