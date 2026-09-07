# Arquitetura da HeroScript

**Status:** arquitetura headless canônica

**Atualizado em:** 2026-09-06

## Propósito

HeroScript é uma engine de regras para jogos configuráveis. Ela recebe comandos
por REST, calcula toda a transição e devolve estado, timeline e uma fila de
resolução visual. Godot ou outro cliente cuida apenas de input, apresentação e
ritmo das animações.

```text
Godot / cliente
  -> intenção REST + controle de concorrência
  -> API (tradução HTTP)
  -> coordenador de run/combate
  -> transições puras + conteúdo versionado
  -> journal atômico
  -> snapshot + resolução visual + eventos projetados
```

## Fonte de verdade

`RunState` é o agregado autoritativo. Combate, deck, recursos de run, cartas,
relíquias e modifiers pertencem ao snapshot da run. Serviços podem coordenar uma
transição, mas não armazenam estado de gameplay paralelo.

Cada comando aceito:

1. valida ID, sequência, step e payload;
2. resolve regras contra a revisão de conteúdo fixada;
3. calcula um snapshot candidato e avança o contexto determinístico;
4. gera hash canônico, frames e registro do comando;
5. persiste um checkpoint atômico;
6. só então publica o novo estado e eventos de observação.

Falha em qualquer ponto anterior ao commit mantém estado, deck e RNG originais.

## Camadas

| Camada | Responsabilidade |
| --- | --- |
| API | Contrato HTTP, validação de transporte e Problem Details. |
| Aplicação | Idempotência, coordenação e commit do agregado. |
| Domínio | Snapshots imutáveis, validação e transições puras. |
| Conteúdo | Definições JSON validadas e publicadas por revisão. |
| Infraestrutura | Journal, snapshots, cache e telemetria operacional. |
| Cliente | Input e visualização; nenhuma regra autoritativa. |

Controllers são finos: não calculam custo, dano, legalidade, ordem de turno ou
resultado. Eles traduzem requests para comandos/queries e projetam a resposta.

## Sistemas canônicos

- `RunManager`: serializa e persiste comandos da run;
- `CombatRunCoordinator`: coordena comandos do encontro dentro da run;
- `CombatFlowPlanner`: única autoridade de fases, ativações e lifecycles;
- `EffectTriggerExecutor`: condição, chance, alvos, repetição e encadeamento;
- `ImmutableEffectProcessor`/`RunEffectReducer`: mutações atômicas comuns;
- `CalculationResolver`/`CalculationEngine`: cálculo por buckets;
- `ResourceMutationReducer`: toda alteração numérica de recursos;
- `ContentRuntimeResolver`: acesso somente à revisão fixada;
- `RunSemanticReplayService`: reexecução autoritativa do journal.

Não existem motores paralelos de dano, status, modifiers ou fases. A origem de um
efeito muda a proveniência, não o algoritmo.

## Conteúdo e hot reload

Arquivos JSON formam um candidato. A publicação valida referências, fórmulas,
pipelines, políticas, ownership e capacidades executáveis, então cria uma revisão
imutável identificada por hash. Runs publicadas continuam na revisão fixada.

O modo de desenvolvimento pode autorizar `ACTIVATE_CONTENT_REVISION` para migrar
explicitamente uma run. Editar um arquivo nunca altera uma timeline por efeito
colateral.

## Determinismo

Todo input ambiental de gameplay vive em `DeterministicContext`: seed, estado do
RNG versionado, cursor de IDs, step, relógio lógico, revisão e versão da engine.
Coleções com impacto em regra usam ordenação explícita. Snapshots, cálculos e
resoluções recebem hashes/fingerprints canônicos.

Retry do mesmo comando é idempotente. Replay reexecuta os comandos, em runtime
isolado, e compara sequência, step e hash; não confia no snapshot armazenado como
resultado da verificação.

## Efeitos e componentes

Cartas, status, relíquias, habilidades, modo e encontro usam os mesmos
`EffectDefinition` e `ContextualInfluenceDefinition`.

- carta: custos, targeting, efeitos, influências, destino e upgrades;
- status: stacks/duração, triggers, influências e restrições;
- relíquia: ownership/stacks, triggers e influências;
- modifier: ownership/duração/stacks e influências.

`DAMAGE` subtrai do recurso explícito; `HEAL` adiciona. Vitória/derrota vem das
políticas da definição do recurso, nunca do nome `health`.

## Timeline e visualização

Cada comando produz pontos na timeline e, quando aplicável, uma resolução com
frames ordenados, traces de efeitos/cálculos/aplicações, hashes e fingerprint.
Branches derivam de checkpoints e nunca reescrevem a origem.

A Godot pode pausar entre frames, consultar snapshots compactos e navegar pela
árvore de branches. Essa espera é visual: a engine já concluiu a transação.

## Capacidades deliberadamente indisponíveis

- reações/pilha/prioridade: opções reservadas, validadas apenas como `Disabled`;
- grafos de fase mais ricos: estruturalmente representáveis, mas o executor atual
  exige exatamente `Start`, `Middle` e `End`;
- término da run por derrota: depende de futura política de progressão do modo;
- compatibilidade legado: não é mantida nesta fase pré-produção.

## Próximas leituras

- [Runs determinísticas](deterministic-runs.md)
- [Sistemas transversais](cross-cutting-systems.md)
- [Timeline](timeline-system.md)
- [Combate](../systems/combat/combat-system.md)
- [Efeitos](../systems/effects/effect-system.md)
- [Cálculos](../systems/calculations/calculation-system.md)
- [Recursos](../systems/resources/resource-system.md)
