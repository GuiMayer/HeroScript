# Validação — atributos e política global

Data: 2026-10-14. Plano: [HERO_ATTRIBUTE_SCALING_GLOBAL_RULES_PLAN.md](HERO_ATTRIBUTE_SCALING_GLOBAL_RULES_PLAN.md).

## Entrega por etapa

| Etapa | Entrega |
| --- | --- |
| 1 | Contratos percent/factor, tabelas de chances e bônus lineares, curva com K configurável e constraints de parâmetros. |
| 2 | Catálogo `effect-resolution-policies`, referência no modo, fingerprints revisionados, validação de referências/captures/perfis habilitados, defaults declarados nos bindings. |
| 3 | Resolver puro no executor comum, `effect_rule_override`, seleção por contexto, proibição prevalente e recibo da política por impacto. |
| 4 | Captures efetivos de chance/bônus; Armor por binding Target; mesma curva na projeção, antes de Block, sem consumo de Armor e sem nome de recurso implícito. |
| 5 | Inspeção, legalidade e IA usam o executor existente; previews incluem inputs herdados e contribuições de buckets. Replay usa revisão/versão fixadas; etapa executável `29`. |
| 6 | Serviço de projeção com providers comuns; duas rotas REST read-only; até 64 contribuições/atributo; integração paralela e publicação atômica no gateway/GameSession. |
| 7 | Mundo 1 migrou 15 templates críticos repetidos e 27 efeitos de inimigos para perfis comuns em pontos. Removidos 12 multipliers obsoletos de inimigos; `w1_pierce` exemplifica proibição de crítico. |
| 8 | Sidebar com atributos, resumo dos tiers, fontes/contexto, chips com contraste, paleta de valores compartilhada e EN/PT. Face continua separada do preview alvo/RNG. |
| 9 | Testes matemáticos, imutabilidade, produção REST, jornada completa/replay, UI e renderização em duas resoluções; documentação/OpenAPI atualizados. |

## Evidências

- Suíte completa do núcleo: **1.701 testes aprovados**.
- `GlobalEffectResolutionPolicyTests`: herança, override, canais excluídos, conflitos, várias chances (0/15/99/100/150/200/250/300) e bônus (0/50/100/150/200), repetição determinística e ausência de mutação.
- `HeroAttributeScalingTests`: Armor -25/0/25/100/300, K 25/100/300, K inválido, default crítico 100%, ordem Armor/Block, bypass, subtração com semântica explícita de dano e projeção sem RNG.
- Integrações de produção: todas as cartas publicadas do ato, todas as habilidades de inimigos, carta exilada, projeção persistente/ativa, buffs e leitura sem avanço da run.
- Contratos/OpenAPI: cinco testes aprovados; rotas novas experimentais documentadas.
- `AscendantWorldOneJourneyTests`: jornada publicada de **nove encontros**, serviços e replay aprovada duas vezes durante a implementação. O teste é uma política automática de regressão, não um gate de balanceamento humano.
- Leitura de atributos em teste REST local: primeira consulta 124,2 ms; consultas aquecidas **7,7–10,3 ms**, resposta de 2.616 caracteres. Não extrapolar esse benchmark em memória para todas as máquinas/implantações.
- UI: testes de cores, reconciliação, isolamento de setting, cartas/previews, atualizações e layout aprovados. `combat_stage_layout` cobre 1280×720, 1280×800, 1920×1080 e 2560×1080, EN/PT e escalas de texto. Execução nativa também aprovou **443 checks**, incluindo capturas em 1280×720 e 2560×1080; capturas revisadas visualmente em `.runtime/hero-attribute-scaling/visual/`.
- A suíte padrão completa do cliente Godot passou: **40 suítes / 988 checks**, além dos testes específicos do painel/layout acima.
- Verificação de whitespace do diff aprovada.

## Decisões de implementação

- Defaults e projeções pertencem à política/perfis do setting, sem uma segunda autoridade de atributos na UI.
- Perfis de dano simples e de crítico permanecem dados separados para que um efeito proibido não avalie fórmulas de sorteio ausentes. O código de execução/cálculo é único.
- Contexto da projeção é publicado: atributos condicionais não prometem um valor universal para todas as cartas ou todos os canais. Contribuições usam unidades do pipeline; valores formatados do atributo usam a unidade de apresentação declarada.
- Um problema de reconciliação apareceu ao remover a seção de atributos: nós com identidade semântica podiam ser reaproveitados como labels anônimos. A reconciliação agora preserva essa fronteira, e um teste garante que dados de outro owner desaparecem.
- Launcher exige as capabilities `global-effect-policies` e `actor-attribute-projections`, evitando reutilizar um host antigo sem as novas rotas.

## Limites e próxima validação humana

O trabalho não certifica duração/dificuldade/diversão de 25–40 minutos. Aumentar o bônus inicial de 50% para 100% muda dano esperado; playtests devem orientar ajustes de conteúdo. Não se adicionaram evasão, resistência elemental, crítico automático de cura/DoT ou poder permanente.

Para experimentar as regras novas, reinicie pelo launcher e inicie uma run nova do Ascendant Mundo 1. Saves/revisões antigas não são reescritos; execução semântica histórica requer uma versão compatível. Não foi feito commit/push neste pedido, e alterações pendentes anteriores foram preservadas.
