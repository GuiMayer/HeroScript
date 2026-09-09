# Sistemas transversais da engine

Este documento define como recursos genéricos devem atravessar todos os módulos
da HeroScript. O objetivo é impedir que cada supermódulo crie sua própria versão
de cache, logging, matemática, conteúdo, mutação ou tratamento de erro.

## Fontes de verdade

| Camada | Papel | Pode decidir regra? |
| --- | --- | --- |
| Journal e commits | Histórico autoritativo append-only | Registra a decisão já aceita |
| Snapshot imutável | Projeção atual de uma run | Sim, como entrada da próxima transição |
| `ContentManifest` | Conteúdo publicado e endereçado por revisão | Sim |
| Gateway de comandos | Única entrada de mutações de gameplay | Valida e coordena |
| EventBus | Telemetria operacional correlacionada e durável | Não |
| Cache | Projeção descartável de dados derivados | Não |
| API REST | Tradução de intenção, leitura e erros | Não |
| Godot | Visual e input do jogador | Não |

O journal pode reconstruir a run. EventBus, cache, logs, SSE e snapshots de
consulta podem ser recriados e nunca substituem o journal.

## Ciclo de um comando

```text
intenção REST
  -> envelope idempotente (commandId + versão esperada + hash do payload)
  -> IGameplayCommandGateway
  -> resolução da mesma contentRevision fixada na run
  -> transição pura sobre estado imutável
  -> persistência atômica do `RunCommit`
  -> publicação do novo snapshot
  -> projeções, EventBus e logs correlacionados
```

Uma alteração nova de gameplay que contorne esse caminho não é uma extensão
válida da engine. Controllers traduzem HTTP; eles não calculam regras nem alteram
managers diretamente.

## Ciclo de conteúdo

```text
arquivos JSON efetivos
  -> draft canônico
  -> validação estrutural e semântica do grafo
  -> bundle imutável identificado por hash
  -> publicação atômica
  -> invalidação ordenada de caches dependentes
  -> ativação explícita para novas runs ou comando registrado na run ativa
```

Uma publicação só se torna visível por inteiro. Referências de modo, cartas,
inimigos, regras de combate, pipelines e fórmulas são resolvidas na mesma revisão.
Falha de leitura, validação ou publicação preserva integralmente a revisão ativa.

## Decisões transversais

### Imutabilidade e concorrência

- Estados e definições publicados usam coleções imutáveis e cópias defensivas.
- Catálogos singleton publicam um novo snapshot atômico; não expõem dicionários
  mutáveis nem alteram uma coleção compartilhada em partes.
- O contexto de comando e de observabilidade usa escopo assíncrono, isolando
  requisições e simulações concorrentes.
- A serialização por agregado protege a ordem das transições; não transforma
  serviços em depósitos de estado parcial de gameplay.

### Conteúdo e cache

- Toda regra de gameplay é resolvida por `configName` e `contentRevision`.
- `ICacheCoordinator` registra dependências e invalida em ordem quando uma nova
  revisão é publicada.
- Cache é uma otimização. Chaves incluem a revisão e uma perda completa de cache
  não muda o resultado de uma run.
- Um cache novo deve se registrar no coordenador; invalidações locais dispersas
  não são permitidas.

### Matemática

- Fórmulas de gameplay entram pelo avaliador central vinculado à revisão.
- Pipelines de dano referenciam fórmulas por identidade de conteúdo, sem compilar
  expressões ocultas em managers.
- Precisão, arredondamento e operações disponíveis fazem parte do contrato
  versionado e precisam de testes de mesmo input/mesmo resultado.

### Eventos e logging

- `correlationId`, `commandId`, run, combate, revisão e versão esperada são
  propagados pelo contexto de execução.
- Eventos operacionais são gravados antes de o publish retornar e preservam sua
  sequência após reinício.
- A projeção SSE deriva do journal e usa a sequência da run como cursor estável.
- Logging usa `Microsoft.Extensions.Logging` por injeção de dependência. Não há
  fábrica estática nem service locator global.

### Validação e erros HTTP

- A publicação valida hashes, tipos e todas as referências do grafo de conteúdo.
- Qualquer resposta HTTP não bem-sucedida usa `application/problem+json`, código
  estável e `correlationId`; conflitos incluem a versão autoritativa quando útil.
- Exceções não atravessam o contrato público e controllers não inventam formatos
  de erro particulares.

## Limites operacionais deliberados

Autoria administrativa, datas de cache, logs e calculadoras isoladas podem usar
tempo real ou entropia porque não pertencem ao estado da run. Para que um resultado
dessas ferramentas afete gameplay, ele precisa virar conteúdo publicado ou um
comando explícito com entradas determinísticas.

Hot reload não edita uma revisão. Ele publica outra revisão e, quando permitido
pela política do modo, registra sua ativação como uma transição da run.

## Responsabilidade da Godot

A Godot consulta catálogos e snapshots, envia comandos e exibe recibos, timeline,
logs e branches. Ela pode antecipar animações, mas deve reconciliar a tela com o
snapshot devolvido. Dano, custo, alvo legal, IA, sorteio, fase, turno, replay,
branch e ativação de conteúdo permanecem na engine.

## Checklist para qualquer novo módulo

1. Seus dados publicados são profundamente imutáveis?
2. Toda regra é resolvida pela revisão da run?
3. Toda mutação de gameplay entra por `IGameplayCommandGateway`?
4. IDs, aleatoriedade e tempo de domínio vêm de `DeterministicContext`?
5. Fórmulas passam pelo avaliador revisionado?
6. O conteúdo novo participa da validação semântica do grafo?
7. O cache, se existir, está registrado no coordenador e usa revisão na chave?
8. Eventos e logs carregam correlação suficiente para reconstruir a causa?
9. Falhas REST saem como Problem Details?
10. Há testes de determinismo, isolamento de mutação, concorrência e reinício?

Se uma resposta for “não”, o módulo ainda não está pronto para integrar uma run
autoritativa.

## Entregas que estabeleceram estas garantias

| Garantia | Commit |
| --- | --- |
| Guardrails arquiteturais | `a443f84` |
| Imutabilidade profunda | `3342d30` |
| Regras vinculadas à revisão | `389cf02` |
| Cache e invalidação coordenados | `dc20cc7` |
| Hot reload atômico | `cb68116` |
| Matemática revisionada | `e24ec9e` |
| Gateway único de comandos | `dd48742` |
| Validação semântica do grafo | `a089c75` |
| Telemetria durável e correlacionada | `d920f40` |
| Problem Details universal | `38ca9a8` |
| DI e concorrência endurecidas | `188eb84` |
