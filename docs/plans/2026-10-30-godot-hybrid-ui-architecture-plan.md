# Plano de Implementação: Arquitetura Híbrida de UI na Godot (.tscn + Presenters)

> **For Agent:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Objetivo:** Migrar os componentes visuais de interface da Godot (`HeroScript/examples/godot-engine-showcase`) do modelo 100% imperativo em código para uma **Arquitetura Híbrida Profissional** (Cenas visuais `.tscn` editáveis no Viewport 2D com suporte a `@tool` preview + Scripts leves de apresentação/data-binding), permitindo ajustes finos visuais no editor da Godot enquanto preserva 100% da suíte de testes automatizados headless.

**Arquitetura:** O projeto adota o padrão **Dumb Views (.tscn) + Presenters (GDScript)**. Cenas `.tscn` atômicas definem a hierarquia de nós, âncoras, margens, fontes e estilos padrão, tornando-se visíveis e ajustáveis no editor visual do Godot 4. Scripts GDScript anexados vinculam dados do read model da engine via nós com Unique Names (`%NodeName`) e `@onready`, acionando renderização em tempo de design quando `Engine.is_editor_hint()` for verdadeiro. O motor de reconciliação (`view_reconciler.gd`) é preservado para gerenciar coleções dinâmicas de itens (mão de cartas, badges de status).

**Tech Stack:** Godot Engine 4.7.2 (GDScript, `.tscn`, `@tool`, Theme overrides, Unique Node Names), .NET 10 (HeroScript Core & REST API).

---

## Estrutura Alvo de Cenas e Scripts

```text
examples/godot-engine-showcase/
├── scenes/
│   └── ui/
│       ├── components/
│       │   ├── status_badge.tscn        # Badge quadrado visual de status (26x26)
│       │   ├── resource_view.tscn       # Barra de vida e recursos com animação
│       │   ├── actor_panel.tscn         # Painel do combatente (Vida, Intent, Badges, Alvo)
│       │   └── card_view.tscn           # Carta tátil com moldura, cabeçalho e arte
│       └── workbenches/
│           └── ui_catalog_workbench.tscn# Bancada visual para ajustes finos com F6
└── scripts/
    └── ui/
        ├── components/
        │   └── status_badge.gd          # Lógica do badge (cores, ícones, stacks)
        ├── resource_view.gd             # Data-binding e tweens da barra de vida
        ├── actor_panel.gd               # Presenter do combatente e fit de altura
        └── card_view.gd                 # Presenter da carta e interação de clique
```

---

## Tarefas de Execução

### Tarefa 1: Componente Atômico `StatusBadge.tscn` com `@tool` Preview

**Arquivos:**
- Criar: `examples/godot-engine-showcase/scenes/ui/components/status_badge.tscn`
- Modificar: `examples/godot-engine-showcase/scripts/ui/actor_panel.gd`
- Criar/Extrair: `examples/godot-engine-showcase/scripts/ui/components/status_badge.gd`
- Teste: `examples/godot-engine-showcase/tests/actor_panel_polish.gd`

**Passo 1: Criar a cena `status_badge.tscn`**
Criar a cena com raiz `PanelContainer` (`custom_minimum_size = Vector2(26, 26)`), contendo um `MarginContainer` com `IconRect` (`TextureRect`), `GlyphLabel` (`Label`) e `StackLabel` (`Label` com alinhamento inferior-direito e contorno preto de 2px).

**Passo 2: Implementar `status_badge.gd` com `@tool`**
Implementar o script com:
- Anotação `@tool` no topo.
- `apply_status(status_data: Dictionary, visual_context)` aplicando cores, ícone vetorial ou glifo de texto e stacks.
- Em `_ready()`: se `Engine.is_editor_hint()`, aplicar automaticamente um status de teste (`burn`, 4 stacks) para visualização imediata no editor 2D da Godot.

**Passo 3: Atualizar `actor_panel.gd` para instanciar `status_badge.tscn`**
Substituir a criação imperativa em `_create_status_badge()` por:
```gdscript
const StatusBadgeScene = preload("res://scenes/ui/components/status_badge.tscn")
...
var badge = StatusBadgeScene.instantiate()
badge.name = "StatusBadge_" + status_id.validate_node_name()
badge.apply_status(status_data, visual_context)
```

**Passo 4: Executar testes de validação**
Executar: `godot --headless --path examples/godot-engine-showcase --script res://tests/actor_panel_polish.gd`  
Esperado: **10 PASS, 0 FAIL**.

---

### Tarefa 2: Componente `ResourceView.tscn` (Barra de Vida e Recursos)

**Arquivos:**
- Criar: `examples/godot-engine-showcase/scenes/ui/components/resource_view.tscn`
- Modificar: `examples/godot-engine-showcase/scripts/ui/resource_view.gd`
- Testes: `examples/godot-engine-showcase/tests/semantic_cues.gd` e `examples/godot-engine-showcase/tests/ui_reconciliation.gd`

**Passo 1: Criar a cena `resource_view.tscn`**
Criar a cena com raiz `HBoxContainer`, contendo:
- `%ResourceLabel` (`Label`, `custom_minimum_size.x = 78`, `font_size = 12`)
- `%ProgressBar` (`ProgressBar`, `size_flags_horizontal = SIZE_EXPAND_FILL`, `custom_minimum_size.y = 22`, `show_percentage = false`)
- `%AmountLabel` (`Label`, `custom_minimum_size.x = 65`, alinhamento à direita)

**Passo 2: Atualizar `resource_view.gd` para vincular os nós via `@onready`**
Refatorar `resource_view.gd`:
- Adicionar `@tool`.
- Usar referências `@onready var label: Label = %ResourceLabel`, `@onready var bar: ProgressBar = %ProgressBar`, `@onready var amount: Label = %AmountLabel`.
- Preservar o método `present_change(application, duration) -> Tween` com animações suaves de dano/cura.
- Em `_ready()`: se `Engine.is_editor_hint()`, exibir "HEALTH", barra a 75% e texto "75/100" no canvas 2D do editor.

**Passo 3: Executar testes de validação**
Executar:
- `godot --headless --path examples/godot-engine-showcase --script res://tests/semantic_cues.gd`
- `godot --headless --path examples/godot-engine-showcase --script res://tests/ui_reconciliation.gd`  
Esperado: **Todos PASS, 0 FAIL**.

---

### Tarefa 3: Painel do Combatente `ActorPanel.tscn`

**Arquivos:**
- Criar: `examples/godot-engine-showcase/scenes/ui/components/actor_panel.tscn`
- Modificar: `examples/godot-engine-showcase/scripts/ui/actor_panel.gd`
- Teste: `examples/godot-engine-showcase/tests/actor_panel_polish.gd` e `examples/godot-engine-showcase/tests/resolutions.gd -- --layout-smoke`

**Passo 1: Criar a cena `actor_panel.tscn`**
Estruturar a hierarquia visual completa com nomes semânticos e nós únicos:
- `ActorPanel` (`PanelContainer`)
  - `ActorRootContent` (`BoxContainer`)
    - `ActorContent` (`VBoxContainer`, `%ActorContent`)
      - `%ActorTitle` (`Label`, centralizado, 16px)
      - `%ActorIntentRow` (`HBoxContainer`)
        - `%IntentSymbol` (`TextureRect`, 24x24)
        - `%ActorIntention` (`Label`, centralizado, 13px)
      - `%ActorResources` (`VBoxContainer` para barras de vida)
      - `%ActorCounters` (`HFlowContainer` para contadores de texto)
      - `%ActorStatusBadges` (`HFlowContainer` para os badges)
      - `%ActorTarget` (`Button` contendo `%ActorArtworkAnchor`)

**Passo 2: Atualizar `actor_panel.gd`**
- Adicionar `@tool` com dados fictícios para preview imediato no Godot (ex: "Golem", 80/100 HP, Intenção de Ataque 12, badges de Chamas e Escudo).
- O desenvolvedor e artista passam a poder abrir `actor_panel.tscn` na Godot e ajustar qualquer margem, espaçamento ou tipografia no Inspetor Visual.
- Manter o cálculo dinâmico de altura em `fit_battlefield_height(available: float)`.

**Passo 3: Executar testes de validação**
Executar: `godot --headless --path examples/godot-engine-showcase --script res://tests/actor_panel_polish.gd`  
Esperado: **Todos PASS, 0 FAIL**.

---

### Tarefa 4: Componente de Carta Tátil `CardView.tscn`

**Arquivos:**
- Criar: `examples/godot-engine-showcase/scenes/ui/components/card_view.tscn`
- Modificar: `examples/godot-engine-showcase/scripts/ui/card_view.gd`
- Testes: `examples/godot-engine-showcase/tests/card_presentation.gd` e `examples/godot-engine-showcase/tests/ui_contracts.gd`

**Passo 1: Criar a cena `card_view.tscn`**
Configurar visualmente no Godot Editor:
- `CardView` (`Button`)
  - `CardFaceMargin` (`MarginContainer`, margem de 12px)
    - `Column` (`VBoxContainer`)
      - `%CardHeader` (`HBoxContainer` com `%Title` e `%UpgradeIcon`)
      - `%CostZone` (`HBoxContainer` para símbolos de custo)
      - `%ArtFrame` (`PanelContainer` contendo `%ArtSlot`)
      - `%TypeLine` (`HBoxContainer` com Tipo e Raridade)
      - `%RulesText` (`RichTextLabel` com bbcode e autowrap)

**Passo 2: Refatorar `card_view.gd`**
- Reduzir as atuais 397 linhas imperativas para um Presenter conciso e declarativo.
- `@tool` exibe uma carta de exemplo ("Golpe Flamejante", Custo 1 Calor, "Causa 8 de dano. Aplica 2 Chamas.") no 2D Canvas sem dar Play.
- Designers podem testar texturas de moldura, efeitos de relevo e bordas de raridade diretamente no editor visual.

**Passo 3: Executar testes de validação**
Executar:
- `godot --headless --path examples/godot-engine-showcase --script res://tests/card_presentation.gd`
- `godot --headless --path examples/godot-engine-showcase --script res://tests/ui_contracts.gd`  
Esperado: **Todos PASS, 0 FAIL**.

---

### Tarefa 5: Bancada Visual de Ajustes Finos (`ui_catalog_workbench.tscn`)

**Arquivos:**
- Criar: `examples/godot-engine-showcase/scenes/ui/workbenches/ui_catalog_workbench.tscn`
- Criar: `examples/godot-engine-showcase/scripts/ui/workbenches/ui_catalog_workbench.gd`
- Criar teste de validação: `examples/godot-engine-showcase/tests/ui_workbench_smoke.gd`

**Passo 1: Criar a cena de bancada visual**
Uma cena isolada contendo:
- Lado esquerdo: Exemplos de cartas lado a lado (comum, rara, aprimorada, custo composto).
- Centro: Combatentes de teste (Herói Piromaníaco com termômetro, Elemental da Água, Golem de Pedra com intenção e status badges).
- Lado direito: Fileira de todos os badges de status para teste de cores e contrastes.

**Passo 2: Rodar com F6 no Godot**
O desenvolvedor/designer pode simplesmente selecionar a cena `ui_catalog_workbench.tscn` e apertar `F6` no Godot para testar todos os ajustes finos em 1 segundo, sem precisar carregar o jogo inteiro nem autenticar no backend.

**Passo 3: Executar suíte completa de regressão**
Executar:
- `godot --headless --path examples/godot-engine-showcase --script res://tests/layers.gd`
- `godot --headless --path examples/godot-engine-showcase --script res://tests/resolutions.gd -- --layout-smoke`  
Esperado: **66/66 layers aprovados, todas as resoluções aprovadas, 0 falhas**.

---

## Critérios de Sucesso e Verificação

1. **Ajustes Finos no Editor:** Abrir qualquer `.tscn` de componente no Godot Editor exibe o nó visualmente montado com `@tool` preview, permitindo ajustar margens, cores, âncoras e fontes pelo Inspetor.
2. **Zero Quebra de Testes:** Todos os 66 testes da suíte `layers.gd` e testes de resolução continuam passando com código de saída 0.
3. **Preservação do Reconciler:** O reconciler continua funcionando perfeitamente para listas dinâmicas sem reconstruir nós desnecessariamente.
4. **Mesmo Repositório:** Nenhuma alteração estrutural na árvore git fora do showcase Godot.
