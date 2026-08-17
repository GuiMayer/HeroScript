# Compatibilidade e migração

## Rotas v1 e adaptadores legados

`/api/v1` é a superfície pública versionada. As rotas históricas sob `/api`
permanecem temporariamente para clientes existentes, mas são adaptadores: não
definem novos recursos nem novas regras de gameplay.

| Legado | Contrato v1 | Situação |
| --- | --- | --- |
| `POST /api/run/start` | `POST /api/v1/runs` | Compatível; prefira v1. |
| `GET /api/run/{runId}/state` | `GET /api/v1/runs/{runId}` | Compatível; prefira v1. |
| `POST /api/combat/{combatId}/action` | `POST /api/v1/combats/{combatId}/commands` | Migração obrigatória para clientes autoritativos. |
| `POST /api/run/{runId}/draw` e afins | `POST /api/v1/runs/{runId}/commands` | Migração obrigatória para comandos idempotentes. |
| `GET /api/events` | `GET /api/v1/runs/{runId}/events` ou combate equivalente | Feed de sessão; não usar para recuperação. |

## Janela de depreciação

Uma rota só pode ser removida depois de:

1. existir alternativa estável em v1;
2. receber aviso de depreciação por pelo menos uma versão menor publicada;
3. constar em `docs/api/changelog.md` com instrução de migração;
4. ter cobertura de cliente/exemplo para a alternativa.

Durante a janela, o servidor pode emitir os cabeçalhos `Deprecation` e `Link`
com a documentação de substituição. Clientes devem migrar pelo mapa acima, não
inferir compatibilidade por nomes semelhantes.
