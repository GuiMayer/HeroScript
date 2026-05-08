# Arquitetura do Core: Card Roguelike Engine

> Análise técnica de um projeto embrionário, com referências a projetos reais verificáveis.

---

## 1. O que essa engine é, conceitualmente?

O que você está descrevendo é um **Framework de Simulação de Regras** — uma camada de software que separa *o que o jogo sabe* (lógica, estado, regras) de *como o jogo aparece* (UI, animações, sprites). Isso tem um nome no mundo do software:

**Arquitetura Headless** — o "backend" do jogo existe de forma completamente independente de qualquer visualização.

O projeto mais próximo disso no mundo de jogos de tabuleiro/cartas já existe e é open-source:

- **boardgame.io** ([boardgame.io](https://boardgame.io)) — um framework JavaScript onde você escreve funções puras que descrevem como o estado do jogo muda quando um movimento é feito, e o framework cuida de todo o resto (multiplayer, storage, sync). A UI é completamente separada e pode ser feita em qualquer tecnologia.
- **godot-card-game-framework** ([github.com/db0/godot-card-game-framework](https://github.com/db0/godot-card-game-framework)) — framework open-source para Godot 4 com scripting engine para enforcement de regras, desacoplado da camada visual.
- **Slay the Robot** ([github.com/DesirePathGames/Slay-The-Robot](https://github.com/DesirePathGames/Slay-The-Robot)) — framework Godot 4 especificamente para roguelikes estilo Slay the Spire, com API data-driven que cria cartas a partir de JSON, sem tocar em código.
- **DeckBuilderRoguelikeEngine** ([github.com/hoshutakemoto/DeckBuilderRoguelikeEngine](https://github.com/hoshutakemoto/DeckBuilderRoguelikeEngine)) — playground arquitetural focado em clareza de responsabilidades, inspirado no Slay the Spire.
- **RLCard** ([arxiv.org/pdf/1910.04376](https://arxiv.org/pdf/1910.04376)) — toolkit acadêmico (Texas A&M) para Reinforcement Learning em jogos de cartas, com interfaces headless para rodar bilhões de simulações.

Você está, sem saber, reinventando e potencialmente *superando* esses projetos — especialmente pelo seu requisito de mutabilidade de regras em runtime.

---

## 2. Os Padrões de Design do Core

O core desta engine se baseia em **quatro padrões de design clássicos**, documentados no livro canônico *Game Programming Patterns* (Robert Nystrom — disponível gratuito em [gameprogrammingpatterns.com](https://gameprogrammingpatterns.com)):

### 2.1 — Entity Component System (ECS)

O padrão mais importante para engines de jogos modernos. Em vez de herança de classes (`Criatura extends Ser extends Entidade`), tudo é **composição de dados**.

- Uma **Entidade** é apenas um ID único (um inteiro ou string)
- **Componentes** são dados puros, sem lógica (ex: `HealthComponent`, `ManaComponent`)
- **Sistemas** processam todas as entidades que possuem os componentes que ele precisa

No seu contexto: uma carta não é uma classe `CartaDeAtaque`. É uma entidade com componentes `DamageComponent`, `TagComponent(fogo)`, `CostComponent(3)`. O **BucketPipeline** é um Sistema ECS que processa entidades com componentes de dano.

**Referência verificável:** [wikipedia.org/wiki/Entity_component_system](https://en.wikipedia.org/wiki/Entity_component_system) — com histórico de uso desde Dungeon Siege (2002) até jogos modernos.

### 2.2 — Command Pattern (para o Pipeline)

Cada efeito de uma carta é um **objeto Command** — ele encapsula uma ação como dado. Isso dá:

- **Undo/Redo** gratuito (útil para simulações da IA)
- **Serialização** trivial (você salva a lista de commands, não o estado resultante)
- **Reordenação em runtime** (injeta um novo command no meio da fila)

É exatamente isso que é o seu BucketPipeline: uma **fila de Commands ordenada por Resources**. A referência canônica está em [gameprogrammingpatterns.com/command.html](https://gameprogrammingpatterns.com/command.html).

### 2.3 — Observer / EventBus (para desacoplamento)

O seu EventBus central é a implementação do padrão Observer. Nenhum sistema conhece outro diretamente — eles apenas publicam e assinam eventos.

```
CombatSystem  →  publica: "DanoCalculado(valor=30, tipo=fogo)"
                          ↓
ShieldSystem  ←  assina: "DanoCalculado" → intercepta e modifica
VisualSystem  ←  assina: "DanoCalculado" → toca animação
LogSystem     ←  assina: "DanoCalculado" → registra no log da IA
```

Isso é o que garante que a UI pode ser trocada sem tocar na lógica. O artigo *"Nomad Game Engine: Part 7 — The Event System"* (Medium, Niko Savas) documenta exatamente essa implementação em uma engine real.

### 2.4 — Strategy Pattern (para regras intercambiáveis)

Cada "regra" do jogo é uma Strategy — um objeto que implementa uma interface comum mas com comportamento diferente. Como os seus Resources da Godot já fazem isso nativamente, você já usa esse padrão sem perceber.

```gdscript
# Todas as regras implementam a mesma interface
class_name BucketRule extends Resource
func apply(context: CombatContext) -> CombatContext:
    pass

# Implementações concretas são intercambiáveis
class ShieldBeforeDamageRule extends BucketRule: ...
class CriticalMultiplierRule extends BucketRule: ...
class PrimordialRealityBendRule extends BucketRule: ...  # injetada em runtime
```

---

## 3. A Arquitetura em Camadas (o "Stack" completo)

```
┌─────────────────────────────────────────────┐
│              CAMADA DE APLICAÇÃO             │
│   (Alisyum: lore, cartas, sprites, audio)   │
│         Resources específicos do jogo        │
├─────────────────────────────────────────────┤
│              CAMADA DE FRAMEWORK             │
│  RunManager │ CardInterpreter │ GambitEngine │
│  (sistemas serializados, data-driven)        │
├─────────────────────────────────────────────┤
│              CAMADA CORE (O KERNEL)          │
│   BucketPipeline │ EventBus │ StateManager   │
│   (agnóstico de lore, puramente genérico)    │
├─────────────────────────────────────────────┤
│              CAMADA DE PLATAFORMA            │
│         Godot Engine (rendering, I/O)        │
│     (você não toca aqui — é infraestrutura)  │
└─────────────────────────────────────────────┘
         ↕ API (protocolo de comunicação)
┌─────────────────────────────────────────────┐
│              CAMADA DE UI/CLIENT             │
│    (qualquer UI que consuma a API acima)     │
│   UI principal │ UI alternativa │ Headless   │
└─────────────────────────────────────────────┘
```

A separação pela API é o que o **boardgame.io** faz nativamente — e o que permite que a sua IA consuma o jogo sem UI.

---

## 4. O Core em Código (estrutura mínima)

### 4.1 — O StateManager (coração do sistema)

O estado do jogo precisa ser **imutável por convenção** — cada ação cria um novo estado, não modifica o anterior. Isso permite:
- Time-travel debugging (voltar ao estado do turno 3)
- Simulações paralelas (a IA roda 10.000 jogos simultâneos com estados independentes)
- Serialização trivial (estado = dado puro, não objeto com referências)

```gdscript
# O estado é um objeto de dados puro, sem lógica
class_name GameState extends Resource
var turn: int
var player_health: int
var enemy_health: int
var hand: Array[CardData]
var bucket_pipeline: Array[BucketRule]  # ← regras são dados!
var active_tags: Dictionary
```

### 4.2 — O BucketPipeline (o diferencial central)

```gdscript
class_name BucketPipeline extends Node

# A lista de regras é carregada de um Resource, não hardcoded
@export var rules: Array[BucketRule] = []

func process_combat(context: CombatContext) -> CombatContext:
    var current = context
    for rule in rules:
        current = rule.apply(current)  # cada balde transforma o contexto
        EventBus.emit("RuleApplied", {rule: rule, context: current})
    return current

# Mutação de realidade em runtime:
func inject_rule(rule: BucketRule, position: int) -> void:
    rules.insert(position, rule)
    EventBus.emit("RealityBent", {rule: rule})
```

### 4.3 — O EventBus (sistema nervoso)

```gdscript
# Autoload singleton — qualquer sistema pode publicar/assinar
class_name EventBus extends Node

signal rule_applied(data: Dictionary)
signal reality_bent(data: Dictionary)
signal combat_ended(data: Dictionary)

func emit(event_name: String, data: Dictionary) -> void:
    # despacha para todos os assinantes sem que eles se conheçam
```

---

## 5. O Módulo de IA Headless

A arquitetura headless permite usar **Reinforcement Learning** para treinar bots. O projeto **RLCard** (Texas A&M University, 2019) fez exatamente isso para jogos de cartas clássicos, publicando um toolkit acadêmico open-source.

O fluxo para a sua engine seria:

```
[IA Agent]  →  envia: Action("jogar carta 3 no inimigo B")
                          ↓
[GameState] →  processa via BucketPipeline
                          ↓
[GameState] ←  retorna: novo estado serializado (JSON/Dictionary)
                          ↓
[IA Agent]  →  lê o novo estado, calcula próxima ação
```

Sem nenhuma UI envolvida. Isso permite **rodar em modo headless** — milhares de simulações por segundo, usadas para:

1. **Balanceamento automático** — se a IA encontrar um combo que vence 99% das vezes, aquela carta precisa de nerf
2. **Validação de arquitetura** — se rodar 1 bilhão de runs sem crash, o core é sólido
3. **Boss com IA real** — os pesos treinados são injetados num inimigo especial

---

## 6. O Diferencial: Runtime Reality Bending

Nenhum dos projetos listados acima suporta **mutação de regras durante o combate**. É o que separia sua engine de todas elas.

Tecnicamente, é possível porque:
- As regras (BucketRules) são **Resources da Godot** — objetos de dados, não código compilado
- Adicionar, remover ou reordenar um Resource **não requer reload da cena**
- O BucketPipeline lê a lista de regras **a cada execução**, não em cache

```gdscript
# Criatura Primordial que inverte a causalidade:
func primordial_ability(pipeline: BucketPipeline) -> void:
    # move o escudo para depois do dano, em runtime
    var shield_rule = pipeline.rules.filter(func(r): return r is ShieldRule)[0]
    pipeline.rules.erase(shield_rule)
    pipeline.rules.append(shield_rule)  # vai pro final
    EventBus.emit("RealityBent", {"description": "Shield agora é calculado após o dano"})
```

O jogo não crasha. O engine não sabe que a realidade foi "dobrada" — ele só processa o que estiver na lista.

---

## 7. O Sistema de Log Built-in (Event Sourcing)

### 7.1 — O que é, e por que você já tem meio caminho andado

O que você está descrevendo tem um nome técnico preciso na engenharia de software: **Event Sourcing**. A definição canônica vem de Martin Fowler (autor de *Patterns of Enterprise Application Architecture*):

> *Event Sourcing garante que todas as mudanças no estado da aplicação sejam armazenadas como uma sequência de eventos. Não só podemos consultar esses eventos — podemos usar o log de eventos para reconstruir estados passados.*
> — [martinfowler.com/eaaDev/EventSourcing.html](https://martinfowler.com/eaaDev/EventSourcing.html)

A razão pela qual você já tem meio caminho andado é direta: **a estrutura imutável de GameState + EventBus já é Event Sourcing sem nome**. Cada vez que o BucketPipeline emite um evento no bus, ele está implicitamente construindo o log. Falta apenas capturá-lo formalmente.

A Monadical — uma empresa que construiu uma plataforma de jogos de cartas com arquitetura event-driven — documentou exatamente essa experiência: o log de combate deles tinha a estrutura:

```json
"actions": [
  { "subj": "jogador", "action": "JOGAR_CARTA", "args": { "carta": "bola_de_fogo" } },
  { "subj": "inimigo",  "action": "RECEBER_DANO",  "args": { "valor": 30, "tipo": "fogo" } }
]
```

E concluíram que **uma vez que o jogo rodava um milhão de mãos sem erros, raramente viam problemas em produção**. O log era o teste de stress.

### 7.2 — Os três níveis do log

O mesmo stream de eventos brutos pode ser "projetado" em múltiplos formatos, dependendo do consumidor. Isso tem o nome de **CQRS (Command Query Responsibility Segregation)** — separar quem *escreve* estado de quem *lê* estado. A referência está em [learn.microsoft.com/azure/architecture/patterns/event-sourcing](https://learn.microsoft.com/en-us/azure/architecture/patterns/event-sourcing).

```
                    ┌─────────────────────────┐
                    │   RAW EVENT STREAM      │
                    │  (fonte da verdade)      │
                    │                          │
  CombatContext ──► │  [ev1, ev2, ev3, ev4...] │
                    └──────────┬──────────────┘
                               │  projetado em:
              ┌────────────────┼────────────────┐
              ▼                ▼                 ▼
    ┌──────────────┐  ┌──────────────┐  ┌──────────────┐
    │  NOTAÇÃO     │  │   DEBUG      │  │  IA/TREINO   │
    │  DE COMBATE  │  │   VERBOSE    │  │  DATASET     │
    │              │  │              │  │              │
    │ "T3: Jogou   │  │ "Rule:       │  │ { state_t:   │
    │  Bola de     │  │  Shield ran  │  │   ...,       │
    │  Fogo →      │  │  before Dmg  │  │   action: ..,│
    │  Inimigo A,  │  │  delta=-30   │  │   reward: -1 │
    │  30 dano"    │  │  tags:[fogo] │  │   state_t+1: │
    └──────────────┘  └──────────────┘  └──────────────┘
         (humano)          (dev)            (máquina)
```

### 7.3 — Estrutura do LogEntry (o átomo do sistema)

Cada evento emitido pelo EventBus precisa ter uma estrutura canônica bem definida. Isso é o que vai na documentação do SDK:

```gdscript
class_name LogEntry extends Resource

# Metadados obrigatórios
var id: String           # UUID único do evento
var timestamp: float     # tempo de jogo (não relógio real)
var turn: int            # turno do combate
var sequence: int        # posição na sequência do turno

# Classificação
var category: String     # "COMBAT" | "PIPELINE" | "META" | "REALITY_BEND"
var severity: String     # "INFO" | "DEBUG" | "WARN" | "ANOMALY"

# O que aconteceu
var subject: String      # quem fez (carta, entidade, sistema)
var verb: String         # o que fez (DEAL_DAMAGE, APPLY_RULE, BEND_REALITY)
var target: String       # quem recebeu
var payload: Dictionary  # dados específicos do evento

# Contexto de estado
var state_before: Dictionary   # snapshot do estado relevante antes
var state_after: Dictionary    # snapshot do estado relevante depois
var delta: Dictionary          # apenas o que mudou (state_after - state_before)
```

O campo `delta` é o mais importante: em vez de guardar dois estados completos, você guarda apenas a diferença. Isso torna o log compacto o suficiente para rodar em bilhões de simulações de IA.

### 7.4 — As três projeções como parte da API

O `LogSystem` seria um assinante do EventBus que captura todos os eventos e os expõe via três formatadores plugáveis:

```gdscript
class_name LogSystem extends Node

var raw_stream: Array[LogEntry] = []   # fonte da verdade

# Projeção 1: Notação de combate (legível por humanos)
func to_combat_notation() -> String:
    # Turno 1: Jogador jogou [Bola de Fogo] → Goblin A por 30🔥
    # Turno 1: Goblin A atacou Jogador por 8 (bloqueado: 3 por Escudo)
    # Turno 2: [ANOMALIA] Criatura Primordial inverteu BucketPipeline

# Projeção 2: Debug trace (legível por devs)
func to_debug_trace() -> String:
    # [T1][SEQ:003][PIPELINE] ShieldRule.apply()
    #   input:  {damage: 30, type: "fire", target: "player"}
    #   output: {damage: 30, type: "fire", target: "player", shield_pending: true}
    #   delta:  {shield_pending: true}

# Projeção 3: Dataset para IA (legível por máquinas)
func to_rl_dataset() -> Array[Dictionary]:
    # [{state: {...}, action: {...}, reward: -8, next_state: {...}}, ...]

# Replay: reconstrói qualquer estado passado
func replay_to_turn(target_turn: int) -> GameState:
    var state = GameState.new()  # estado inicial
    for entry in raw_stream:
        if entry.turn > target_turn: break
        state = state.apply(entry)  # aplica evento por evento
    return state
```

A função `replay_to_turn()` é particularmente poderosa: ela prova que o log é completo. Se você consegue reconstruir qualquer estado do passado apenas a partir dos eventos, **o log é a fonte da verdade, não o estado final**. Isso é Event Sourcing completo.

### 7.5 — A "Notação PGN" do combate

O xadrez usa **PGN (Portable Game Notation)** — um formato de texto padronizado que registra cada jogada de forma que qualquer software pode reproduzir a partida completa a partir do zero. O design de xadrez online moderno armazena os movimentos como registros imutáveis separados do estado atual, permitindo replay, detecção de trapaça e análise post-mortem.

Você pode fazer o equivalente para o seu combate. Uma notação minimalista seria:

```
# ALISYUM COMBAT LOG v1.0
# run_id: a3f8b2c1
# seed: 48291

T01.P  PLAY  bola_de_fogo  → goblin_a  [dmg:30 type:fire]
T01.E  ATK   goblin_a      → player    [dmg:8  blocked:3]
T02.M  META  REALITY_BEND  pipeline    [op:REORDER rule:shield pos:2→5]
T02.P  PLAY  escudo_arcano → self       [block:+15]
T03.E  DEAD  goblin_a      —           [cause:fire_dot]
```

Esse formato compacto permite que você:
- Reproduza qualquer combate exato dado um `seed` e uma lista de ações
- Exporte bugs como um arquivo `.acl` (Alisyum Combat Log) e abra no debug viewer
- Alimente a IA com datasets de corridas históricas sem guardar os estados completos

### 7.6 — Por que isso é parte da API, não um add-on

O erro clássico de muitos projetos é tratar o log como debugging opcional — algo que se liga quando tem problema e se desliga em produção. Aqui é o oposto:

- O log **é** a fonte da verdade do combate (Event Sourcing puro)
- O estado atual **é derivado** do log, não o contrário
- Desligar o log em produção seria equivalente a apagar a memória do jogo

O Microsoft Azure Architecture Center documenta esse princípio: *"O event store é a fonte permanente de informação. A única forma de atualizar uma entidade ou desfazer uma mudança é adicionar um evento compensatório ao event store."*

Para o SDK, o `LogSystem` seria documentado como **obrigatório** no core, com os três formatadores sendo **opcionais e swappáveis** — um dev que usa a engine pode criar seu próprio formatador de log sem tocar no core.

---

## 8. O Dashboard Web Local (DevTools da Engine)

### 8.1 — Conceito

O dashboard é uma **página web local** que consome a mesma API headless do jogo, funcionando como as DevTools do browser, mas para a engine. Ele não é parte do jogo — é uma ferramenta de desenvolvimento que existe em paralelo, comunicando com o core via HTTP/WebSocket local (`localhost:7777`).

Isso é possível porque a arquitetura headless já expõe tudo como dados. O dashboard é apenas mais um "cliente" da API, como a IA ou a UI do jogo. Nenhuma lógica nova precisa ser adicionada ao core para suportá-lo.

```
[Godot / Core]  ←──────────────────────────────────────┐
       │                                                 │
       │  expõe: localhost:7777/api/*                   │
       │                                                 │
       ├──► [UI do Jogo]   (cliente principal)          │
       ├──► [Agente de IA] (cliente headless)           │
       └──► [Dashboard]    (cliente de desenvolvimento) ┘
```

### 8.2 — Estrutura de Páginas

```
dashboard/
├── index.html          ← entrada, carrega o layout base
├── style/
│   └── theme.css       ← variáveis de cor, tipografia
├── pages/
│   ├── overview.js     ← aba Dashboard (status geral da API)
│   ├── pipeline.js     ← aba Pipeline Editor (drag-and-drop)
│   ├── resources.js    ← aba Resource Creator (formulário + validação)
│   └── log.js          ← aba Event Log (projeções do stream)
└── api/
    └── client.js       ← wrapper das chamadas à API do core
```

### 8.3 — Aba 1: Dashboard (Overview)

Visão geral do estado atual da engine. Consome `GET /api/state` e atualiza em polling ou via WebSocket.

```
┌─────────────┐ ┌─────────────┐ ┌─────────────┐ ┌─────────────┐
│ Rules ativas│ │Eventos/turno│ │ Simulações  │ │  Endpoints  │
│     5 / 6   │ │      7      │ │    idle     │ │  6 online   │
└─────────────┘ └─────────────┘ └─────────────┘ └─────────────┘

┌─────────────────────────┐  ┌─────────────────────────┐
│ Pipeline atual          │  │ API Endpoints           │
│ 00 ■ CriticalMultiplier │  │ GET  /api/state    ● ok │
│ 01 ■ ShieldMitigation   │  │ POST /api/action   ● ok │
│ 02 ■ ElementalTagResolver│  │ GET  /api/log      ● ok │
│ 03 ■ FireDotApplication  │  │ POST /api/pipeline ● ok │
│ 04 □ LifestealConversion │  │ GET  /api/simulate ● ok │
│ 05 ■ RealityBendInjector │  │ POST /api/resource ● ok │
└─────────────────────────┘  └─────────────────────────┘
```

### 8.4 — Aba 2: Pipeline Editor (drag-and-drop)

Permite reordenar as regras do `BucketPipeline` visualmente. Cada reordenação envia um `POST /api/pipeline/reorder` com a nova lista — o core aplica imediatamente (nenhum restart necessário, por conta da arquitetura data-driven).

```
Pipeline Editor
Arraste para reordenar. A ordem define a execução do cálculo de combate.

[≡]  00  ■ CriticalMultiplierRule    [MULTIPLIER]  [ON  ▼]
[≡]  01  ■ ShieldMitigationRule      [MITIGATION]  [ON  ▼]
[≡]  02  ■ ElementalTagResolver      [TAG]         [ON  ▼]
     ↑ arrastando aqui insere antes desta linha
[≡]  03  ■ FireDotApplicationRule    [STATUS]      [ON  ▼]
[≡]  04  □ LifestealConversionRule   [CONVERSION]  [OFF ▼]  (modded)
[≡]  05  ■ RealityBendInjectorRule   [META]        [ON  ▼]

Legenda:  ■ Multiplicador  ■ Mitigação  ■ Status  ■ Conversão  ■ Tag  ■ Meta
```

O drag-and-drop usa a **HTML5 Drag API** nativa — sem dependências externas. O índice de origem e destino são enviados via `POST /api/pipeline/reorder`:

```json
{
  "from_index": 0,
  "to_index": 3,
  "rule_id": "CriticalMultiplierRule"
}
```

### 8.5 — Aba 3: Resource Creator (formulário + validação)

Cria novos `BucketRule` Resources sem abrir a Godot. O formulário possui validações embutidas antes de enviar o `POST /api/resource`.

```
Resource Creator
Crie novos BucketRules com validação integrada.

NOME DO RESOURCE
┌──────────────────────────────────┐
│ MyCustomDamageRule               │  ← validação: /^[A-Z][a-zA-Z]+Rule$/
└──────────────────────────────────┘
  Padrão obrigatório: PascalCaseRule

TIPO
┌──────────────────────────────────┐
│ MULTIPLIER — Multiplicador     ▾ │
└──────────────────────────────────┘

DESCRIÇÃO
┌──────────────────────────────────┐
│ Multiplica o dano baseado em...  │  ← obrigatório, mín. 10 chars
└──────────────────────────────────┘

FÓRMULA / COMPORTAMENTO (GDScript)
┌──────────────────────────────────┐
│ context.damage *= 1 + (          │  ← obrigatório
│   context.tags.count("fire")     │
│   * 0.1)                         │
└──────────────────────────────────┘

AUTOR
┌──────────────────────────────────┐
│ core                             │  ← "core" | "modded" | nome livre
└──────────────────────────────────┘

[ CRIAR RESOURCE ]

─────────────────────────────────────
Preview — JSON output

{
  "class": "MyCustomDamageRule",
  "type": "MULTIPLIER",
  "enabled": false,
  "description": "...",
  "formula": "...",
  "author": "core",
  "created_at": "2025-01-01"
}
```

Regras de validação embutidas:

| Campo | Regra |
|---|---|
| `name` | Obrigatório, regex `/^[A-Z][a-zA-Z]+Rule$/` |
| `desc` | Obrigatório, mínimo 10 caracteres |
| `formula` | Obrigatório, qualquer string não vazia |
| `author` | Obrigatório, valores sugeridos: `core`, `modded` |

### 8.6 — Aba 4: Event Log (projeções)

Exibe o raw event stream do `LogSystem` em três projeções selecionáveis, consumindo `GET /api/log`. A mesma fonte de dados, três visões diferentes:

```
[ NOTAÇÃO ]  [ DEBUG ]  [ RL DATASET ]

─── NOTAÇÃO DE COMBATE ────────────────────────────────────────
T01.C  PLAY_CARD            player → goblin_a   [card:bola_de_fogo cost:3]
T01.P  APPLY                CriticalMultiplier  [delta:{multiplier:1.5}]
T01.C  DEAL_DAMAGE          bola_de_fogo → goblin_a [value:30 type:fire]
T01.C  RECEIVE_DAMAGE       goblin_a            [value:27 blocked:3]
T02.M  REALITY_BEND         primordial_keeper   [op:REORDER rule:Shield 1→4]
T02.C  DEAL_DAMAGE          goblin_a → player   [value:8 type:physical]
T03.C  DEAD                 goblin_a            [cause:fire_dot]
───────────────────────────────────────────────────────────────
```

```
─── DEBUG VERBOSE ─────────────────────────────────────────────
[T1][SEQ:002][PIPELINE] CriticalMultiplierRule.apply()
  input:  {damage: 20, type: "fire", target: "goblin_a"}
  output: {damage: 30, type: "fire", target: "goblin_a"}
  delta:  {damage: +10, multiplier_applied: 1.5}

[T2][SEQ:001][META] RealityBendInjectorRule.inject()
  op:     REORDER
  rule:   ShieldMitigationRule
  before: index 1
  after:  index 4
  effect: "Shield agora é calculado após o dano"
───────────────────────────────────────────────────────────────
```

```
─── RL DATASET (JSON) ─────────────────────────────────────────
[
  {
    "state_t":    { "turn": 1, "seq": 1, "player_hp": 50, "enemy_hp": 40 },
    "action":     { "verb": "PLAY_CARD", "card": "bola_de_fogo" },
    "reward":     0,
    "next_state": { "turn": 1, "seq": 2, "player_hp": 50, "enemy_hp": 40 }
  },
  ...
]
───────────────────────────────────────────────────────────────
```

### 8.7 — Endpoints da API que o dashboard consome

| Método | Rota | Uso no dashboard |
|---|---|---|
| `GET` | `/api/state` | Overview: stat cards, pipeline atual |
| `GET` | `/api/log` | Event Log: raw stream em tempo real |
| `POST` | `/api/action` | (debug) enviar ação manual |
| `POST` | `/api/pipeline/reorder` | Pipeline Editor: aplicar nova ordem |
| `POST` | `/api/pipeline/toggle` | Pipeline Editor: ligar/desligar rule |
| `POST` | `/api/resource` | Resource Creator: criar nova rule |
| `GET` | `/api/simulate` | Overview: disparar simulação headless |

### 8.8 — Por que isso não polui o core

O dashboard é **zero-invasivo** por design. Ele não exige nenhuma mudança na lógica do core — apenas que a API headless já existe (o que é pré-requisito da arquitetura). Especificamente:

- Nenhuma referência ao dashboard existe dentro do `BucketPipeline`, `EventBus` ou `StateManager`
- O dashboard pode ser deletado e o jogo continua funcionando identicamente
- Em builds de release, o servidor local simplesmente não é iniciado — custo zero de performance
- Em builds de desenvolvimento, o servidor sobe automaticamente como um `Autoload` da Godot

```gdscript
# dev_server.gd — Autoload ativo apenas em builds debug
func _ready() -> void:
    if not OS.is_debug_build():
        return   # não faz nada em release
    start_local_server(port = 7777)
```

---

## 9. Modos de Inicialização da API

### 9.1 — A API é sempre instanciada

A API não é um add-on opcional do dashboard — ela é o próprio core em execução. O que muda entre os modos é apenas *quem* a consome e *o que* é iniciado junto com ela.

```
hero-script-engine [flags]

Flags disponíveis:
  --mode=game          inicia o jogo completo (Godot + UI)       [padrão]
  --mode=simulation    inicia só o core, sem UI (para IA/testes)
  --dashboard-active   abre o dashboard no navegador padrão junto
  --port=7777          porta do servidor local                    [padrão]
  --config=alisyum     qual configuração/distribuição carregar
  --seed=48291         seed fixa para runs determinísticas
```

### 9.2 — O arquivo `.bat` (ou `.sh` no Linux/Mac)

Por nunca ter feito isso: um `.bat` é só um arquivo de texto com comandos de terminal, salvo com extensão `.bat`. Ao clicar duas vezes, o Windows abre um terminal e executa as linhas.

Para esta engine, o fluxo seria:

**Passo 1** — compilar o core como um executável de console (não como biblioteca da Godot). Em GDScript isso pode ser feito via `--headless` do próprio Godot; em C# seria um projeto de *Console App*.

**Passo 2** — criar os atalhos:

```bat
:: start-game.bat — inicia o jogo normal
hero-script-engine.exe --mode=game --config=alisyum

:: start-dashboard.bat — só o backend + dashboard no browser
hero-script-engine.exe --mode=simulation --dashboard-active --config=alisyum

:: start-ai.bat — headless puro para treino de IA (velocidade máxima)
hero-script-engine.exe --mode=simulation --config=alisyum --seed=00000
```

**Passo 3** — o código do core lê os argumentos na inicialização:

```gdscript
# main.gd
func _ready() -> void:
    var args = OS.get_cmdline_args()
    var mode = _get_arg(args, "--mode", "game")
    var dashboard = "--dashboard-active" in args
    var config = _get_arg(args, "--config", "alisyum")

    ConfigManager.load(config)        # carrega a distribuição
    APIServer.start(port = 7777)      # API sempre sobe

    if dashboard:
        OS.shell_open("http://localhost:7777/dashboard")

    if mode == "game":
        get_tree().change_scene_to_file("res://ui/main_menu.tscn")
    elif mode == "simulation":
        SimulationLoop.start()        # loop headless, sem UI
```

### 9.3 — O que cada modo permite

| Modo | Jogo abre | Dashboard disponível | Uso típico |
|---|---|---|---|
| `--mode=game` | ✓ | opcional | jogar normalmente |
| `--mode=game --dashboard-active` | ✓ | ✓ | desenvolvimento ativo |
| `--mode=simulation --dashboard-active` | ✗ | ✓ | monitorar IA, editar pipeline |
| `--mode=simulation` | ✗ | ✗ | treino de IA em velocidade máxima |

No modo `simulation` puro, sem UI e sem dashboard, o core processa regras na velocidade máxima do hardware — ordens de magnitude mais rápido do que com qualquer camada gráfica ativa.

---

## 10. Persistência e Estrutura de Dados Local

### 10.1 — Filosofia: pasta como distribuição

Cada configuração carregada (Alisyum, um mod, uma total conversion) ganha sua própria pasta dentro do diretório de dados do usuário. A engine nunca mistura dados de distribuições diferentes.

```
user://                                  ← raiz (AppData no Windows, ~/.local no Linux)
│
├── alisyum/                             ← distribuição principal
│   ├── config.json                      ← metadados (nome, versão, git-hash)
│   ├── runs/
│   │   ├── run_2025-01-01_a3f8b2.acl   ← log de combate completo
│   │   └── run_2025-01-03_c9d1e4.acl
│   ├── saves/
│   │   ├── slot_1.json                  ← upgrades permanentes, posição no mapa
│   │   └── slot_2.json
│   └── personal.json                    ← configs pessoais (volume, keybinds, etc.)
│
├── cyber-cards/                         ← mod/total conversion de outro dev
│   ├── config.json                      ← aponta para o fork do git
│   ├── runs/
│   └── saves/
│
└── shared/
    └── ai-dataset.sqlite                ← resultados de simulações (todos os configs)
```

### 10.2 — O "save boladão" de modders

Como tudo é dado puro, um modder pode distribuir a pasta inteira da sua configuração — incluindo uma pasta `saves/` com um slot pré-preenchido. O jogador baixa, coloca em `user://nome-do-mod/` e a engine detecta automaticamente.

O arquivo de save de um modder é apenas:
- Um `slot.json` com upgrades permanentes e posição no mapa
- Opcionalmente, um `.acl` de log que a engine pode usar para reconstruir o estado via `replay_to_turn()`

Nenhum binário, nenhum código — só dados. O que torna o compartilhamento trivial e a engine agnóstica quanto à origem.

### 10.3 — Três tipos de dado, três destinos

| Tipo de dado | Arquivo | Quem escreve | Quem lê |
|---|---|---|---|
| Progresso da run | `.acl` (log de eventos) | Core (Event Sourcing) | Core (`replay_to_turn`) |
| Upgrades permanentes | `slot.json` | Core (ao fim da run) | Core (ao iniciar nova run) |
| Resultados de IA | `ai-dataset.sqlite` | SimulationLoop | Dashboard (gráficos) |

O SQLite é reservado exclusivamente para os dados de simulação de IA porque eles precisam de consultas rápidas do tipo "qual a taxa de vitória da carta X contra o boss Y em 10.000 runs?". Para tudo mais, arquivos de texto simples são suficientes.

---

## 11. Integração com Git como Plataforma de Mods

### 11.1 — A API expõe um endpoint de Git

Como as regras são arquivos de dados (Resources/JSON), elas são rastreáveis por Git nativamente. A engine expõe um ponto da API que delega operações Git sobre a pasta de configuração da distribuição atual:

```
GET  /api/config/info          → retorna nome, versão e git-hash atual
POST /api/config/load          → carrega um fork pelo URL do repositório
GET  /api/config/log           → histórico de commits da config atual
POST /api/config/checkout      → muda para um branch/commit específico
```

O `POST /api/config/load` recebe um payload simples:

```json
{
  "source": "https://github.com/usuario/alisyum-pirate-mod",
  "branch": "main",
  "slot": "pirate-mod"
}
```

A engine faz o `git clone` ou `git pull` na pasta `user://pirate-mod/`, carrega os Resources e reinicializa o `StateManager` com a nova configuração — sem reiniciar o executável.

### 11.2 — Mod-library in-game (total conversion com um clique)

A partir desse endpoint, construir uma biblioteca de mods dentro do próprio jogo é direto:

```
┌─────────────────────────────────────────────────────┐
│  BIBLIOTECA DE CONFIGURAÇÕES                         │
│                                                     │
│  ● Alisyum (oficial)             v1.2.0  [ATIVO]   │
│  ○ Pirate Cards                  v0.4.1  [Carregar] │
│  ○ Cyber-Noir Deck               v1.0.0  [Carregar] │
│  ○ Alisyum — Modo Caos (fork)    v1.2.3  [Carregar] │
│  + Adicionar por URL do Git                         │
└─────────────────────────────────────────────────────┘
```

Ao clicar em "Carregar", a API faz o pull do repositório, substitui os Resources no `BucketPipeline` e reinicia o `StateManager`. Por conta da arquitetura data-driven, **o executável do jogo não precisa ser recompilado ou reiniciado** — a troca é de dados, não de código.

O log de eventos registra a troca:

```
T00.M  CONFIG_LOADED   pirate-cards-v0.4.1   [git-hash:a3f8b2c]
```

O que garante que qualquer run iniciada após essa entrada seja 100% reprodutível — bastam o hash do commit e o arquivo `.acl`.

### 11.3 — Versionamento das configurações como Git nativo

Para o desenvolvedor, editar as regras no dashboard e salvar é equivalente a um `git commit`:

```
POST /api/config/commit

{
  "message": "nerf: ShieldMitigationRule reduzida de 30% para 20%",
  "author": "dev"
}
```

Isso cria um commit real no repositório da configuração. O histórico fica acessível no dashboard e qualquer mudança que quebre o balanceamento pode ser revertida com `git revert` via API, sem tocar no código do jogo.

---

## 12. Sistema de Epoch + Delta (Save State)

### 12.1 — O que é uma Epoch

A **epoch** é o estado inicial imutável de uma run — o "ponto zero" a partir do qual tudo é calculado. Ela contém:

- O hash do commit da configuração ativa (quais regras estão carregadas)
- A seed aleatória da run (que torna toda geração procedural determinística)
- O estado inicial do jogador (deck base, vida inicial, upgrades permanentes)

```json
{
  "epoch_id": "e_a3f8b2c1",
  "config_hash": "d4e5f6a7",
  "config_name": "alisyum",
  "seed": 48291,
  "player_initial": {
    "health": 80,
    "deck": ["bola_de_fogo", "escudo_arcano", "..."],
    "permanent_upgrades": ["dano_critico_+10"]
  },
  "created_at": "2025-01-01T14:00:00Z"
}
```

A epoch nunca é modificada. Ela é gravada uma vez no início da run e funciona como o `commit` inicial de um repositório Git.

### 12.2 — O save state como sequência de deltas

O arquivo de save (`.acl`) contém apenas os eventos que divergem da epoch — cada escolha do jogador, cada resultado de combate, cada seed consumida:

```
# HERO-ENGINE SAVE v1.0
# epoch: e_a3f8b2c1
# config: alisyum @ d4e5f6a7

[MAP]   NODE_ENTERED    node_id:14  type:combat
[COMBAT] CARD_PLAYED    bola_de_fogo  → goblin_a
[COMBAT] DAMAGE_DEALT   value:30  type:fire
[MAP]   REWARD_CHOSEN   upgrade:vida_maxima_+10
[MAP]   NODE_ENTERED    node_id:21  type:shop
[SHOP]  CARD_BOUGHT     escudo_duplo  cost:50g
...
```

Para reconstruir qualquer estado, a engine parte da epoch e processa os deltas em ordem — o mesmo mecanismo do `replay_to_turn()` já documentado na seção de Event Sourcing.

### 12.3 — Por que isso é superior ao save tradicional

| | Save tradicional | Epoch + Delta |
|---|---|---|
| Tamanho do arquivo | Cresce com o estado do jogo | Cresce só com as ações |
| Resistência a bugs | Difícil rastrear origem | Qualquer estado é reproduzível |
| Compatibilidade com mods | Frágil (muda a estrutura) | Robusta (epoch aponta para o hash exato) |
| Suporte à IA | Requer snapshots pesados | Deltas são o formato nativo de treino |
| Time-travel | Não disponível | Nativo (`replay_to_turn`) |

### 12.4 — Compatibilidade de saves entre versões

O campo `config_hash` na epoch resolve o problema clássico de "save de versão antiga quebrando com patch novo". A engine verifica:

```gdscript
func load_save(file: String) -> void:
    var epoch = parse_epoch(file)
    if epoch.config_hash != ConfigManager.current_hash:
        # a config mudou desde que esse save foi criado
        # oferece ao jogador: continuar com a config antiga (checkout)
        # ou migrar o save para a nova versão (com aviso de incompatibilidade)
        SaveMigrationDialog.show(epoch, ConfigManager.current_hash)
    else:
        StateManager.replay_from_epoch(epoch, file)
```

---

## 13. Timeline no Pause-Menu

### 13.1 — Conceito e intenção de design

A timeline é uma funcionalidade **deliberada e controlada** — não um cheat code. Expô-la sem custo ou contexto destruiria o loop de risco/recompensa do roguelike. A engine suporta tecnicamente desde o início (é consequência direta do Event Sourcing), mas cabe ao designer de cada distribuição decidir se e como oferecê-la.

```
[PAUSA]

  Continuar
  ─────────────────────────────────────
  Timeline da Run           [se habilitado na config]
  Configurações
  Sair
```

### 13.2 — A estrutura da timeline

Os deltas do save formam naturalmente uma hierarquia de granularidade:

```
Run (epoch)
├── ● Nó 1 — Combate (início de batalha = ponto maior)
│   ├── · Turno 1
│   │   ├── · Jogou Bola de Fogo
│   │   └── · Goblin atacou
│   ├── · Turno 2
│   └── · Turno 3 — Vitória
├── ● Nó 2 — Recompensa
│   └── · Escolheu upgrade: Dano Crítico +10
├── ● Nó 3 — Loja
│   └── · Comprou Escudo Duplo
└── ● Nó 4 — Combate (atual)
    └── · Turno 1 ← você está aqui
```

Clicar em qualquer ponto chama `StateManager.replay_from_epoch(epoch, delta_index)` — a engine descarta o estado atual e reconstrói até aquele ponto.

### 13.3 — Modos de uso deliberado por distribuição

A config da distribuição define o comportamento da timeline:

```json
"timeline": {
  "enabled": false,
  "mode": "view_only",
  "jump_allowed": false,
  "cost_per_jump": null
}
```

| Modo | Comportamento | Exemplo de uso |
|---|---|---|
| `disabled` | Não aparece no pause | Roguelike competitivo padrão |
| `view_only` | Jogador vê a timeline, não pode voltar | Análise post-mortem, aprendizado |
| `jump_free` | Pode voltar sem custo | Modo história, acessibilidade |
| `jump_costed` | Voltar consome um recurso do jogo | Item raro que "manipula o tempo" no lore |

O modo `jump_costed` é o mais interessante do ponto de vista de game design: a capacidade técnica da engine de voltar no tempo se torna um **item do jogo** com custo real, integrando o sistema de save ao lore sem expor a mecânica como meta.

### 13.4 — Uso para debug e QA

Mesmo que o jogador nunca acesse a timeline, ela serve ao desenvolvedor:

- **Bug reporting**: jogador encontra comportamento estranho, exporta o `.acl` daquele ponto exato via "Compartilhar log" no pause
- **Análise de IA**: o desenvolvedor usa a timeline para observar em qual turno a IA headless tomou a decisão que a fez ganhar ou perder
- **Validação de balance**: se `replay_to_turn(turn=5)` retorna um estado diferente do que foi salvo, há inconsistência no pipeline

---

## 14. Comparativo com Frameworks Existentes

### 14.1 — O que cada framework faz bem

| Framework | Headless | Event Sourcing | Regras mutáveis em runtime | Mod/fork nativo | Timeline |
|---|---|---|---|---|---|
| **boardgame.io** | ✓ | parcial | ✗ | ✗ | ✗ |
| **godot-card-game-framework** | ✗ | ✗ | ✗ | ✗ | ✗ |
| **Slay the Robot** | ✗ | ✗ | ✗ | ✗ | ✗ |
| **RLCard** | ✓ | ✗ | ✗ | ✗ | ✗ |
| **Hero-Engine (este projeto)** | ✓ | ✓ | ✓ | ✓ | ✓ |

### 14.2 — Por que nenhum deles chega ao mesmo ponto

**boardgame.io** é o mais próximo na filosofia de separar lógica e UI, mas foi projetado para jogos de tabuleiro multijogador via rede — não para roguelikes single-player com pipelines de cálculo complexos. As regras são funções JavaScript fixas, não dados intercambiáveis em runtime.

**godot-card-game-framework** e **Slay the Robot** são excelentes para criar *um* cardgame específico na Godot, mas não têm pretensão de ser um framework genérico. As regras são acopladas ao código da cena.

**RLCard** faz a parte headless/IA muito bem, mas é exclusivamente uma biblioteca de pesquisa — não há camada de aplicação, UI ou sistema de save.

### 14.3 — A analogia com o Geometry Dash

O paralelo mais preciso não é com outros *frameworks de jogos de cartas*, mas com o **Geometry Dash**: um jogo que se tornou uma plataforma porque tratou seu conteúdo como dados manipuláveis.

No GD, a comunidade criou desde plataformers simples até batalhas de boss complexas usando as ferramentas nativas — sem tocar no código do jogo. Na Hero-Engine, a mesma lógica se aplica:

- As **regras** são os objetos/triggers do GD
- O **BucketPipeline** é o loop de física do GD
- O **dashboard** é o editor de fases do GD
- Os **forks de configuração** são os níveis compartilhados pela comunidade

A diferença é que o GD exige que você saiba usar o editor. A Hero-Engine permite que você carregue uma configuração inteira via URL de um repositório Git e jogue imediatamente — a barreira entre jogador, modder e desenvolvedor desaparece progressivamente.

---

## 15. Projetos de Referência para Estudar


| Projeto | Linguagem | O que aprender |
|---|---|---|
| [boardgame.io](https://boardgame.io) | JavaScript | Arquitetura headless, separação de estado e UI |
| [godot-card-game-framework (db0)](https://github.com/db0/godot-card-game-framework) | GDScript | Scripting engine para regras em Godot |
| [Slay the Robot](https://github.com/DesirePathGames/Slay-The-Robot) | GDScript (Godot 4) | API data-driven para roguelike deckbuilder |
| [slaytheweb](https://github.com/oskarrough/slaytheweb) | JavaScript | Reimplementação open-source do Slay the Spire |
| [RLCard](https://arxiv.org/pdf/1910.04376) | Python | RL em jogos de cartas, interface headless |
| [Game Programming Patterns](https://gameprogrammingpatterns.com) | C++ (conceitual) | Command, Observer, ECS — os padrões do core |

---

## 16. O Caminho Progressivo (sem morrer no escopo)

```
Fase 1 — O Kernel (você já está aqui)
  └─ BucketPipeline serializado ✓
  └─ EventBus funcional
  └─ GameState imutável

Fase 2 — O Framework
  └─ CardInterpreter (lê Resource → executa efeito)
  └─ RunManager data-driven (grafo de nós como Resource)
  └─ GambitEngine (IA dos inimigos como árvore de regras)

Fase 3 — A API Headless + Modos de Inicialização
  └─ Flags de linha de comando (--mode, --dashboard-active, --config, --seed)
  └─ Arquivos .bat para cada modo de uso
  └─ Servidor local de desenvolvimento (Autoload, debug-only)

Fase 4 — Dashboard + Persistência
  └─ Dashboard web: overview, pipeline editor, resource creator, log viewer
  └─ Estrutura de pastas user:// por distribuição
  └─ Sistema Epoch + Delta (.acl) substituindo save tradicional
  └─ SQLite para dataset de simulações de IA

Fase 5 — Integração Git + Mod-Library
  └─ Endpoints /api/config/* (load, commit, checkout)
  └─ Mod-library in-game (troca de config com um clique)
  └─ Compatibilidade de saves entre versões via config_hash

Fase 6 — O Jogo (Alisyum)
  └─ UI que consome a API
  └─ Assets, lore, balanceamento
  └─ Runtime Reality Bending com as Criaturas Primordiais
  └─ Timeline no pause-menu (modo view_only por padrão)

Fase 7 — A Engine pública
  └─ Documentação do SDK
  └─ Separação do código de Alisyum do core
  └─ Primeira "total conversion" feita por outra pessoa
  └─ Repositório público de configurações (catálogo de forks)
```

O segredo é: **não existe Fase 5 sem Fase 1**. E a Fase 1 você já começou.
