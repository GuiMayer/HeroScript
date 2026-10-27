# Implementação das zonas de múltiplos participantes

Guia: `MULTI_ACTOR_CARD_ZONE_PROVISIONING_PLAN.md`.

| Etapa | Resultado | Commit |
|---|---|---|
| 1. Reprodução | Dois testes vermelhos antes da correção, com nomes arbitrários | `09701b8` |
| 2. Provisionador incremental | Cria apenas zonas Actor ausentes; nenhuma carta ou RNG | `c6b8c5a` |
| 3. Abertura canônica | Todos os participantes antes de `encounter.started` | `9d7e7d9` |
| 4. Caminho alternativo/transação | Cobertura estrutural e rollback de fluxo/persistência | `7840680` |
| 5. Pool e consumidores | Fixture JSON, ownership, projeção e testes Godot/web | `1e88ec0` |
| 6. Revisão/determinismo | Reutilização, limpeza explícita, serialização e versão 33 | `7dd1a9b` |
| 7. REST/documentação | Jornada publicada de teste, bot, retry, persistência e replay | Ver documento de validação |

O teste inicial exige zonas vazias; não inclui distribuição implícita. Alterações anteriores não relacionadas do workspace foram preservadas.

A certificação REST revelou uma restrição adicional em `LegalActionResolver`: descoberta e avaliação ainda exigiam que o ator fosse o jogador dono da run. A correção usa as fontes de cartas autorizadas por `CardZonePlaySource`, preservando as verificações de controller, origem e ativação. Isso foi necessário para o bot consumir as cartas reais do pool, sem adicionar outro sistema de IA.

Contrato, exemplos e resultados em `MULTI_ACTOR_CARD_ZONE_PROVISIONING_VALIDATION.md`.
