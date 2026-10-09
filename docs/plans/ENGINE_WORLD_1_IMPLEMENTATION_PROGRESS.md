# Implementação dos fundamentos do mundo 1

Guia: [plano de implementação](ENGINE_WORLD_1_FOUNDATIONS_IMPLEMENTATION_PLAN.md).

## Ordem e estado

1. Contratos e baseline: concluída.
2. Recursos persistentes por ator: concluída.
3. Transporte entre encontros e recuperação transacional: pendente.
4. Probabilidades calculadas e fatos de sorteio: pendente.
5. Críticos por níveis e previews: pendente.
6. Catálogo de desbloqueios e contribuições: pendente.
7. Elegibilidade congelada e concessões: pendente.
8. Contratos REST e apresentação: pendente.
9. Hotreload, restart, replay e branches: pendente.
10. Certificação integrada e desempenho: pendente.

## Contratos que não podem regredir

- A carteira da run e os recursos do personagem têm proprietários distintos.
- Dentro do combate, somente o ator é autoridade de seus recursos; fora dele, o personagem persistente.
- Transporte modifica apenas os valores autorizados pela política; buffs de limites não se tornam permanentes.
- Recuperação e seu custo são candidatos de uma única transação; falha não publica efeitos parciais.
- Nenhum identificador de recurso possui semântica hardcoded de dano, vida ou derrota.
- Probabilidades usam cálculos sem settlements; previews não avançam o RNG canônico.
- Elegibilidade é capturada na criação e acompanha replay/branches; consultas não concedem progresso.
- Testes usam armazenamento isolado, nunca saves do jogador.

## Registro

- Baseline recebido: `fe9c8d5`; dois planos e dois identificadores Godot ainda não versionados.
- Baseline: 31 testes aprovados (atributos persistentes, setting Ascendant, inputs aleatórios e perfil).
- Etapa 2: componentes de recursos persistentes, política revisionada opcional por modo, ações explícitas por resultado/retry, validação de identidade/schema/proprietário e limites persistentes. Recursos não listados continuam exclusivos do encontro.
- Engine version 21: novos snapshots de personagem incluem recursos; saves anteriores não são apagados nem reinterpretados silenciosamente.
- Gate da etapa 2: suíte Core completa aprovada (1.537 testes) e 67 testes focados aprovados após a validação adicional do grafo. Integração de saída/recuperação ainda pertence à etapa 3.
- A execução será registrada aqui com testes e limites reais, sem marcar etapas incompletas como concluídas.
