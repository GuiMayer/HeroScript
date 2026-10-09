# Implementação dos fundamentos do mundo 1

Guia: [plano de implementação](ENGINE_WORLD_1_FOUNDATIONS_IMPLEMENTATION_PLAN.md).

## Ordem e estado

1. Contratos e baseline: concluída.
2. Recursos persistentes por ator: concluída.
3. Transporte entre encontros e recuperação transacional: concluída.
4. Probabilidades calculadas e fatos de sorteio: implementada; gate REST em verificação.
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
- Suíte API completa aprovada após a etapa 2: 193 testes.
- Etapa 3: promoção no commit da resolução (incluindo política de retry), owner binding em mapa/preparação/diálogo, cura via processador/pipeline comum e consequência de derrota fora de combate. Ascendant passa a preservar vida e restaurar energia por JSON.
- Testes de jornada: recuperação + custo com o mesmo resource ID em proprietários distintos; rollback por pagamento/efeito/durabilidade; restart, receipt duplicado, branch isolada e 10 replays por roteiro de recuperação/vitória/retry.
- Gate da etapa 3: 1.546 testes Core aprovados antes do ajuste de identidade; 50 testes focados aprovados após o ajuste. A suíte API revelou um encontro com ID fixo; corrigido com `identityBinding: RunPlayer` e confirmado por dois testes REST (incluindo identidade personalizada). Suíte API completa será repetida no próximo gate.
- Launcher exige a capability `persistent-actor-resources` para não reutilizar um processo anterior sem os novos contratos. Nenhum processo/save real foi alterado pelos testes.
- Etapa 4: probabilidade numérica pelo `CalculationResolver`, unidade explícita, CaptureOnly, validação de contratos compartilhados, captura Action/ParentProc/Impact e fatos com probabilidade/cálculo/revisão/snapshots. Disponível para qualquer efeito, inclusive stacks; não há calculador específico de dano/crítico.
- Engine version 22: fingerprints e traces incorporam novos fatos. Launcher passa a exigir `calculated-random-inputs`; versões anteriores/saves reais não foram reiniciados ou apagados.
- Gate Core após etapa 4: 1.569 testes aprovados, incluindo 22 casos novos de probabilidade, publicação, rollback e repetição/serialização. Sintaxe do launcher validada.
- A jornada REST antiga dependia de ordenação lexical de IDs e eliminava alvos antes de exercer overflow. O jogador de teste agora prioriza a instância transformada e usa a projeção legal para preservar alvos até demonstrar uma continuação; as regras da engine e a exigência de executar o salto não foram enfraquecidas. O gate REST completo será registrado após sua execução.
- A execução será registrada aqui com testes e limites reais, sem marcar etapas incompletas como concluídas.
