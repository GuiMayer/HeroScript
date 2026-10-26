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
2. Conteúdo e vínculo: concluída. Profiles/policies revisionados, grants referenciando bundles genéricos, vínculo tipado da entidade e materialização. 20 testes de conteúdo/entidade aprovados; modo sem política omite o campo novo. Nenhum setting existente foi habilitado.
3. Estado/transições: concluída na camada estrutural. Associação opcional por ator, distribuição final atômica, swap, capacidade, propriedade, janelas e reconciliação de destruição. Validação da composição é conectada na etapa 4 antes de expor comandos. 10 testes de compartimentos aprovados, incluindo round-trip/hash, ordem de inputs e ausência serializada.
4. Composição: concluída. Contexto genérico de fontes, expansão com namespace estável, trace separado do ledger, fingerprint/cache contextual e validação do candidato. Custos, condições, alvos, triggers e efeitos entram pelo compilador comum; duplicatas/singletons incompatíveis são rejeitados. 24 testes focados aprovados, incluindo equivalência estrutural com carta autorada e retorno ao fingerprint original sem alocação.
5. Consumidores: concluída. Resolução da run, execução, inspeção, projeções, transformação e seleção por tags consomem o contexto. Projeção expõe proveniência sem chamar concessões de upgrade. Compatibilidade de conteúdo valida os compartimentos contra revisão/personagem candidatos. 88 testes de cartas/zonas/compatibilidade e um teste integrado de consumidores aprovados.
6. Comandos/persistência: implementada. `APPLY_DECK_COMPARTMENTS` usa codec/dispatcher/commit/journal canônicos, discovery respeita perfil/janela e destruição limpa associações na persistência de run e batches de combate. Snapshot opcional round-trip e cópias de estado independentes testados. 78 testes focados de compartimentos/codec/commit/replay aprovados; jornada semântica específica com API fica para a certificação final.
7. REST: concluída. GET read-only do personagem atual e POST preview; mutação via comando canônico. Projeção antes/depois com componentes efetivos e permissões. Preview rejeita versão antiga e lista ausente. Dois testes HTTP/OpenAPI/controller e teste do serviço aprovados. Contrato em `docs/api/deck-compartments.md`.
8. Conteúdo demonstrativo: pendente.
9. Laboratório: pendente.
10. Integração da run: pendente.
11. Certificação: pendente.
