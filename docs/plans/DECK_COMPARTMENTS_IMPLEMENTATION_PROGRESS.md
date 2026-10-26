# Implementação de compartimentos de baralho

Referência: [arquitetura e plano](DECK_COMPARTMENTS_ARCHITECTURE_AND_PLAN.md).

## Etapa 1 — contratos e inventário (2026-10-26)

Decisões técnicas:

- Domínio independente `Core.Run.DeckCompartments`, sem mudanças em `CardZoneDefinition`.
- Perfil vinculado por component tipado da entidade; habilitação por política opcional do modo.
- Estado opcional omitido de JSON quando ausente. Nenhum novo campo nulo deve alterar hashes de runs/modos antigos.
- Concessões usam `CardComponentBundleDefinition`: já aceita a hierarquia genérica de componentes; `CardBundleCompiler.Expand` faz namespace e bindings de efeitos. Não criar outro formato de efeitos.
- Componentes singleton e dependências passam pelo `CardContentCompiler`; conflitos rejeitam a distribuição inteira. Adição não sobrescreve base/ledger.
- Contexto externo tipado entra antes da expansão da composição, com proveniência própria, sem `TransformationId` artificial.
- Janelas: fora de combate/resultado pendente, opcionais restrições a preparação. Sem fallback a outro personagem.
- Alocação usa instâncias persistentes e associação independente das zonas. `$run` só é aceito para o personagem da run, não como permissão universal.

Inventário de consumidores que precisam receber o contexto:

| Consumidor | Integração necessária |
|---|---|
| `RunCardResolver` | Resolução autoritativa por run/instância. |
| `CardPlayExecutor` | Composição antes de legalidade/pagamento/execução. |
| `CardInspectionService` | Condições, custos, alvos e cálculos efetivos. |
| `CardPresentationQueryService` | Projeções contextuais e diffs. |
| `CardZoneRuntimeRuleEvaluator` | Seleção por tags efetivas sem conceder papel de build às zonas. |
| `CardTransformationPlanner` | Validar candidato junto das concessões existentes. |
| `RunContentCompatibilityValidator` | Validar revisão candidata e migração. |
| `CachedEffectiveCardResolver` | Chave inclui contribuição; caminho antigo permanece idêntico. |
| `CombatScenario` | Autoria de cenário não instala concessões implicitamente. |

CardBundleCompiler suporta custos, condições, targeting, disposition, triggers, influences e rule overrides além de efeitos. Os limites de cardinalidade pertencem ao compilador da carta final, não ao compartimento.

## Situação das etapas

1. Contratos e inventário: concluída.
2. Conteúdo e vínculo: pendente.
3. Estado/transições: pendente.
4. Composição: pendente.
5. Consumidores: pendente.
6. Comandos/persistência: pendente.
7. REST: pendente.
8. Conteúdo demonstrativo: pendente.
9. Laboratório: pendente.
10. Integração da run: pendente.
11. Certificação: pendente.
