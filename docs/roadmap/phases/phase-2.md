# Fase 2 - Camadas de Combate

**Status:** ⚠️ Parcial / Estabilização  
**Dependências:** Fase 1 (EventBus, Combat, Damage)

---

## Visão Geral

A Fase 2 adiciona camadas de complexidade ao sistema de combate: status effects (buffs/debuffs), modificadores de script (Go Again, Multi-Hit, etc.), e o sistema de Gambits para companions. A auditoria de 2026-05-16 mostrou que Status Effects já existem parcialmente no Core/API/config, mas ainda precisam de estabilização semântica antes de avançar para Run/Shop/CardSelection/Content.

## Estado Atual Verificado (2026-05-16)

### Já implementado
- `src/Core/StatusEffects/` com manager, processor, definitions, instances, timings, behaviors e types
- `src/API/Controllers/StatusEffectController.cs`
- Configuração `UserData/Configs/default/StatusEffects/status_effects.json`
- Integração parcial com `CombatSystem` para controle, DoT/HoT, shield/thorns/intangible, BUFFER e modificadores de pipeline
- Rotas REST compatíveis com `/api/StatusEffect/*` e `/api/combat/{combatId}/entities/{targetId}/status`
- `DamageCalculator` recebe `IStatusEffectManager` via DI

### Correções de estabilização aplicadas
- Build/Core.Tests estabilizados; 524 testes Core passando
- Resource reload/hot reload corrigido
- `*.lscache` ignorado no Git
- `EventsController.ClearHistory` protegido por ambiente de desenvolvimento
- `TestWebApplicationFactory` deixou de alterar o diretório global do processo

### Pendências high priority
- `EffectResolver` ainda é parcialmente esquelético: precisa aplicar dano/cura/recurso/status em estado real, usando `DamageCalculator`, `ResourceManager` e `StatusEffectManager`
- `StatusEffectProcessor` ainda retorna resultados intermediários para DoT/HoT; precisa fechar aplicação real de dano/cura e fórmulas via `MathEngine`/`ExpressionEvaluator`
- `CombatSystem` concentra responsabilidades demais e ainda possui TODOs de integração com `ActionManager`/custos preparados
- API.Tests compila e o host sobe, mas a suíte completa ainda tem falhas legadas de contrato em Config/Resource/Action e timeout nos testes filtrados de StatusEffect

### Data-driven Compliance

A Fase 2 agora inclui uma trilha obrigatória de compliance data-driven antes de Run/Shop/CardSelection/Content:

- `ActionManager` deve carregar ações por discovery/manifest JSON, não por lista fixa em C#.
- `CombatSystem` deve executar `ActionDefinition` e seus `EffectDefinition`, não constantes como `BASIC_ATTACK_DAMAGE` ou `DEFAULT_POWER_COST`.
- `StartCombat` deve preferir `EntityDefinition` JSON para recursos, nomes e stats.
- Status effects devem ser aplicados por comportamento genérico (`DAMAGE_OVER_TIME`, `HEAL_OVER_TIME`, `SHIELD`, `REACTIVE`, etc.), não por nomes específicos como `BURNING`/`POISON`.
- O progresso detalhado fica em [../analysis/data-driven-compliance.md](../analysis/data-driven-compliance.md).

## APIs Planejadas

### 1. Status API

Sistema de buffs, debuffs, e efeitos ao longo do tempo (DoTs).

**Endpoints:**
- `POST /api/StatusEffect/apply` - Aplica status a uma entidade
- `GET /api/StatusEffect/{targetId}/active` - Lista status ativos em uma entidade
- `POST /api/StatusEffect/{targetId}/tick` - Processa duração de status
- `GET /api/StatusEffect/definitions` - Lista definições de status disponíveis
- `DELETE /api/StatusEffect/remove` - Remove status específico via body
- `POST /api/combat/{combatId}/entities/{targetId}/status` - Alias por entidade
- `GET /api/combat/{combatId}/entities/{targetId}/status` - Alias por entidade
- `DELETE /api/combat/{combatId}/entities/{targetId}/status/{instanceId}` - Remove status por instância

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
- ⚠️ StatusSystem (parcial; estabilizar)
- ⏳ ScriptModifierSystem (implementar)
- ⏳ GambitEngine (implementar)

### Ordem de Implementação

1. **Status Core** (`src/Core/StatusEffects/`) - estabilizar
   - Aplicação real de DoT/HoT
   - Fórmulas via MathEngine/ExpressionEvaluator
   - Testes de ciclo de vida, stacks, expiração e modificadores de pipeline

2. **EffectResolver** (`src/Core/Effects/`) - estabilizar
   - Dano via DamageCalculator
   - Cura/recurso via ResourceManager
   - Apply/remove status via StatusEffectManager
   - Condições e fórmulas data-driven

3. **CombatSystem + ActionManager** - integrar
    - Validar `costOptionId`
    - Substituir custos hardcoded por `ApplyCosts`
    - Processar ações via EffectResolver quando aplicável

3.1. **Data-driven Compliance** - remover regras hardcoded remanescentes
   - Carregar todas as ações JSON descobertas
   - Mapear `BASIC_ATTACK`/`POWER` legados para `ActionDefinition`
   - Usar entidades JSON no início de combate
   - Aplicar status por comportamento genérico

4. **ScriptModifier Core** (src/Core/Combat/Modifiers/)
   - ScriptModifier, ModifierDefinition
   - ModifierValidator (compatibilidade)
   - Integration com CombatSystem

5. **ScriptModifier API** (src/API/Controllers/ModifierController.cs)
   - List modifiers
   - Validate compatibility
   - Query by category

6. **Gambit Core** (src/Core/Combat/Gambits/)
   - Gambit, GambitCondition, GambitAction
   - GambitEngine (evaluation)
   - Sincronia com EventBus

7. **Gambit API** (src/API/Controllers/GambitController.cs)
   - Configure gambits
   - Evaluate conditions
   - Execute actions

---

## Exemplos de Uso

### Aplicar Status

```http
POST /api/combat/{combatId}/entities/{targetId}/status
Content-Type: application/json

{
  "statusId": "burning",
  "duration": 3,
  "stacks": 2,
  "sourceId": "00000000-0000-0000-0000-000000000000"
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

Após estabilizar a Fase 2, a Fase 3 implementará o loop completo de run:
- **Run API** - Gerenciamento de runs
- **CardSelection API** - Aprender/decompilar poderes
- **Shop API** - Sistema de loja
- **Preparation API** - Injeção de modificadores

Ver: [phase-3.md](phase-3.md)
