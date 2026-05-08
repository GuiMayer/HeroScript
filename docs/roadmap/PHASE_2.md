# Fase 2 - Camadas de Combate

**Status:** 📋 Planejado  
**Dependências:** Fase 1 (EventBus, Combat, Damage)

---

## Visão Geral

A Fase 2 adiciona camadas de complexidade ao sistema de combate: status effects (buffs/debuffs), modificadores de script (Go Again, Multi-Hit, etc.), e o sistema de Gambits para companions. Estas camadas transformam o combate básico em um sistema rico e estratégico.

## APIs Planejadas

### 1. Status API

Sistema de buffs, debuffs, e efeitos ao longo do tempo (DoTs).

**Endpoints:**
- `POST /api/status/apply` - Aplica status a uma entidade
- `GET /api/status/active` - Lista status ativos em combate
- `POST /api/status/tick` - Processa tick de status (início/fim de turno)
- `GET /api/status/definitions` - Lista definições de status disponíveis
- `DELETE /api/status/{statusId}` - Remove status específico

**Tipos de Status:**
- **Buffs** - Efeitos positivos (força, velocidade, escudo)
- **Debuffs** - Efeitos negativos (fraqueza, lentidão, vulnerabilidade)
- **DoTs** - Dano ao longo do tempo (veneno, queimadura, sangramento)
- **HoTs** - Cura ao longo do tempo (regeneração)
- **Control** - Controle de ações (stun, silence, root)

**Recursos:**
- Duração em turnos
- Stacks (acumulação de efeitos)
- Tick timing (início/fim de turno, on-action)
- Interação entre status (dispel, immunity, synergy)

### 2. ScriptModifier API

Modificadores que alteram comportamento de poderes.

**Endpoints:**
- `GET /api/modifiers` - Lista modificadores disponíveis
- `GET /api/modifiers/{name}` - Obtém detalhes de modificador
- `POST /api/modifiers/validate` - Valida compatibilidade entre modificadores
- `GET /api/modifiers/categories` - Agrupa modificadores por categoria

**Modificadores Principais:**
- **Go Again** - Permite usar outro poder após este
- **Multi-Hit** - Ataca múltiplas vezes
- **Explosivo** - Dano em área (AoE)
- **Eficiente** - Reduz custo de energia
- **Crítico+** - Aumenta chance/multiplicador de crítico
- **Penetração** - Ignora parte da armadura
- **Lifesteal** - Cura baseada em dano causado

**Recursos:**
- Validação de compatibilidade (alguns modificadores são mutuamente exclusivos)
- Custo em PP (Power Points)
- Raridade (Comum, Incomum, Raro, Lendário)
- Tags para filtrar (offensive, defensive, utility)

### 3. Gambit API

Sistema de companions com regras condicionais (IF/THEN).

**Endpoints:**
- `POST /api/gambits/configure` - Configura gambit para companion
- `POST /api/gambits/evaluate` - Avalia se condição de gambit é satisfeita
- `GET /api/gambits/triggers` - Lista triggers disponíveis
- `GET /api/gambits/actions` - Lista ações disponíveis
- `POST /api/gambits/{gambitId}/execute` - Executa ação de gambit

**Triggers (Condições):**
- `HERO_HP_BELOW` - HP do herói abaixo de X%
- `ENEMY_HP_BELOW` - HP do inimigo abaixo de X%
- `ON_HERO_ACTION` - Quando herói usa poder com tag específica
- `TURN_START` - Início do turno
- `TURN_END` - Fim do turno
- `ALWAYS` - Sempre que possível (cooldown permitting)

**Actions (Ações):**
- Usar poder específico
- Aplicar status
- Curar herói
- Buff/debuff

**Recursos:**
- Prioridade de gambits (ordem de avaliação)
- Cooldowns por gambit
- Sincronia (reage a tags de ações do herói)
- Configuração visual (UI futura)

---

## Integração entre Sistemas

### Status + Damage Pipeline

Status effects modificam buckets do pipeline de dano:
- Buff de força → Bucket Aditivo
- Debuff de vulnerabilidade → Bucket Multiplicativo
- Armadura → Bucket Mitigação

### ScriptModifiers + Combat

Modificadores alteram execução de ações:
- Go Again → Permite ação adicional no mesmo turno
- Multi-Hit → Executa ação múltiplas vezes
- Explosivo → Aplica dano a múltiplos alvos

### Gambits + EventBus

Gambits reagem a eventos de combate:
- `ON_HERO_ACTION` → Subscribe a `HeroActionExecutedEvent`
- `HERO_HP_BELOW` → Verifica em `HeroDamagedEvent`
- `TURN_START` → Trigger em `TurnStartedEvent`

---

## Dependências

### Sistemas Core Necessários

- ✅ EventBus (Fase 1)
- ✅ CombatSystem (Fase 1)
- ✅ BucketPipeline (Fase 1)
- ⏳ StatusSystem (implementar)
- ⏳ ScriptModifierSystem (implementar)
- ⏳ GambitEngine (implementar)

### Ordem de Implementação

1. **Status Core** (src/Core/Combat/Status/)
   - StatusEffect, StatusDefinition
   - StatusManager, StatusProcessor
   - Tick timing e stack logic

2. **Status API** (src/API/Controllers/StatusController.cs)
   - Apply/remove status
   - Query active status
   - Process ticks

3. **ScriptModifier Core** (src/Core/Combat/Modifiers/)
   - ScriptModifier, ModifierDefinition
   - ModifierValidator (compatibilidade)
   - Integration com CombatSystem

4. **ScriptModifier API** (src/API/Controllers/ModifierController.cs)
   - List modifiers
   - Validate compatibility
   - Query by category

5. **Gambit Core** (src/Core/Combat/Gambits/)
   - Gambit, GambitCondition, GambitAction
   - GambitEngine (evaluation)
   - Sincronia com EventBus

6. **Gambit API** (src/API/Controllers/GambitController.cs)
   - Configure gambits
   - Evaluate conditions
   - Execute actions

---

## Exemplos de Uso

### Aplicar Status

```http
POST /api/status/apply
Content-Type: application/json

{
  "combatId": "combat-123",
  "targetId": "player-1",
  "statusName": "STRENGTH_BUFF",
  "duration": 3,
  "stacks": 2,
  "source": "companion-1"
}
```

### Validar Modificadores

```http
POST /api/modifiers/validate
Content-Type: application/json

{
  "powerId": "FIREBALL",
  "modifiers": ["GO_AGAIN", "MULTI_HIT", "EXPLOSIVO"]
}
```

### Configurar Gambit

```http
POST /api/gambits/configure
Content-Type: application/json

{
  "companionId": "companion-1",
  "priority": 1,
  "condition": {
    "type": "HERO_HP_BELOW",
    "threshold": 0.5
  },
  "action": {
    "type": "USE_POWER",
    "powerId": "HEAL",
    "target": "HERO"
  },
  "cooldown": 3
}
```

---

## Próximos Passos

Após completar a Fase 2, a Fase 3 implementará o loop completo de run:
- **Run API** - Gerenciamento de runs
- **CardSelection API** - Aprender/decompilar poderes
- **Shop API** - Sistema de loja
- **Preparation API** - Injeção de modificadores

Ver: [PHASE_3.md](PHASE_3.md)
