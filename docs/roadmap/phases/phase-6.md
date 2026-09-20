# Fase 6 - Modos Especiais

**Status:** 🟡 Seed, modos e daily challenge implementados; modos adicionais permanecem extensões
**Dependências:** Fase 3 (Run), Fase 5 (Save, MetaProgression)

> Os nomes de serviço e endpoints abaixo são o desenho original. Hoje seeds
> fazem parte de `RunStartOptions`, modos são recursos JSON resolvidos por
> `GameModeResolver`, e desafios diários são expostos por
> `DailyChallengeController`/`DailyChallengeService`.

---

## Visão Geral

A Fase 6 implementa modos especiais de jogo: runs com seed fixa (para competição e compartilhamento), daily challenges, e custom runs com regras modificadas. Esta fase adiciona replayability e variedade ao jogo.

## APIs Planejadas

### 1. Seed API

Sistema de runs com seed fixa para determinismo e compartilhamento.

**Endpoints:**
- `POST /api/seed/generate` - Gera seed aleatória
- `POST /api/seed/validate` - Valida seed fornecida
- `POST /api/seed/run` - Inicia run com seed específica
- `GET /api/seed/daily` - Obtém daily challenge atual
- `GET /api/seed/leaderboard` - Leaderboard de daily challenge (futuro)

**Recursos:**
- Determinismo completo (mesma seed = mesma run)
- Compartilhamento de seeds entre jogadores
- Daily challenge (seed única por dia)
- Validação de integridade (prevenir manipulação)
- Leaderboards por seed (futuro)

### 2. Mode API

Modos de jogo customizados com regras modificadas.

**Endpoints:**
- `GET /api/modes` - Lista modos disponíveis
- `GET /api/modes/{name}` - Obtém detalhes de modo
- `POST /api/modes/custom` - Cria custom run com regras modificadas
- `GET /api/modes/{name}/rules` - Obtém regras do modo
- `POST /api/modes/{name}/start` - Inicia run em modo específico

**Modos Planejados:**
- **Normal** - Modo padrão
- **Daily Challenge** - Seed fixa diária, uma tentativa
- **Sandbox** - Recursos ilimitados, teste de builds
- **Infinito** - Run sem fim, dificuldade crescente
- **Custom** - Regras customizadas pelo jogador

**Recursos:**
- Modificadores de dificuldade
- Regras customizadas (energia inicial, ouro, pool de poderes)
- Restrições (sem companions, sem loja, etc.)
- Bônus especiais (dobro de ouro, poderes grátis)

---

## Estrutura de Seed

### Seed Format

```
HERO-2026-05-08-A3F7B2C1
```

**Componentes:**
- `HERO` - Prefixo do jogo
- `2026-05-08` - Data (para daily challenges)
- `A3F7B2C1` - Hash único (8 caracteres hex)

### Seed Data (JSON)

```json
{
  "seed": "HERO-2026-05-08-A3F7B2C1",
  "version": "1.0.0",
  "generatedAt": "2026-05-08T00:00:00Z",
  "type": "DAILY_CHALLENGE",
  "metadata": {
    "difficulty": "normal",
    "allowedRaces": ["human", "orc", "elf"],
    "restrictions": [],
    "bonuses": []
  },
  "runDefinition": {
    "segments": [...],
    "enemyPool": [...],
    "powerPool": [...],
    "shopInventory": [...]
  }
}
```

---

## Estrutura de Custom Mode

### Custom Run Configuration (JSON)

```json
{
  "modeName": "My Custom Run",
  "baseMode": "NORMAL",
  "rules": {
    "startingGold": 200,
    "startingPP": 100,
    "startingEnergy": 5,
    "maxDeckSize": 20,
    "allowCompanions": true,
    "allowShop": true,
    "allowCardSelection": true
  },
  "modifiers": {
    "enemyHealthMultiplier": 1.5,
    "enemyDamageMultiplier": 1.2,
    "goldRewardMultiplier": 2.0,
    "ppRewardMultiplier": 1.5
  },
  "restrictions": {
    "bannedPowers": ["FIREBALL"],
    "bannedCompanions": [],
    "bannedModifiers": ["GO_AGAIN"],
    "allowedRaces": ["human"]
  },
  "bonuses": {
    "startingPowers": ["LIGHTNING_STRIKE"],
    "startingCompanions": ["WOLF"],
    "startingModifiers": {
      "POWER_STRIKE": ["MULTI_HIT"]
    }
  }
}
```

---

## Integração com Sistemas Anteriores

### Seed + Run

Seed controla geração determinística:
- Mapa de nós (posições, tipos)
- Pool de inimigos por combate
- Ofertas de cartas
- Inventário de loja
- Eventos aleatórios

### Daily Challenge + MetaProgression

Daily challenges oferecem recompensas especiais:
- Feats exclusivos
- Desbloqueios temporários
- Leaderboards globais (futuro)
- Estatísticas separadas

### Custom Mode + Run

Custom modes modificam regras base:
- Economia (ouro, PP, energia)
- Pool de conteúdo (poderes, companions)
- Dificuldade (multiplicadores de HP/dano)
- Restrições (banir conteúdo específico)

---

## Dependências

### Sistemas Core Necessários

- ✅ RunManager (Fase 3)
- ✅ ContentLoader (Fase 4)
- ✅ SaveManager (Fase 5)
- ✅ MetaProgressionSystem (Fase 5)
- ✅ Seeds explícitas e RNG determinístico em `RunManager`/`DeterministicContext`
- ✅ Modos revisionados em `GameModeResolver`
- ✅ Desafios diários e leaderboard em `DailyChallengeService`

### Ordem de Implementação

1. **Seed Core** (src/Core/Modes/Seed/)
   - SeedGenerator, SeedValidator
   - Deterministic RNG
   - Seed serialization

2. **Seed API** (src/API/Controllers/SeedController.cs)
   - Generate/validate seed
   - Start seeded run
   - Daily challenge

3. **Mode Core** (src/Core/Modes/)
   - ModeDefinition, ModeRules
   - ModeManager, RuleApplier
   - Custom mode builder

4. **Mode API** (src/API/Controllers/ModeController.cs)
   - List modes
   - Create custom run
   - Get rules

5. **Daily Challenge System** (src/Core/Modes/Daily/)
   - DailyChallengeGenerator
   - Leaderboard (futuro)
   - Reward system

---

## Exemplos de Uso

### Gerar Seed

```http
POST /api/seed/generate
Content-Type: application/json

{
  "type": "RANDOM"
}
```

### Iniciar Run com Seed

```http
POST /api/seed/run
Content-Type: application/json

{
  "seed": "HERO-2026-05-08-A3F7B2C1",
  "raceId": "human"
}
```

### Obter Daily Challenge

```http
GET /api/seed/daily
```

### Criar Custom Run

```http
POST /api/modes/custom
Content-Type: application/json

{
  "modeName": "High Risk High Reward",
  "rules": {
    "startingGold": 0,
    "startingPP": 200
  },
  "modifiers": {
    "enemyHealthMultiplier": 2.0,
    "goldRewardMultiplier": 3.0
  }
}
```

### Listar Modos

```http
GET /api/modes
```

---

## Segurança e Validação

### Validação de Seeds

- Verificar formato (regex)
- Validar hash (integridade)
- Verificar versão (compatibilidade)
- Prevenir seeds manipuladas

### Custom Mode Limits

- Limites em multiplicadores (0.1x - 10.0x)
- Validação de regras (não quebrar jogo)
- Sanitização de inputs
- Rate limiting (prevenir spam)

---

## Próximos Passos

Com a conclusão da Fase 6, o roadmap da API está completo. As próximas expansões incluirão:

- **Fase 7** - Expansão de conteúdo (mais raças, companions, mundos)
- **Fase 8** - Features avançadas (modding, multiplayer, etc.)

Para detalhes sobre convenções e padrões da API, consulte:
- [api-conventions.md](../analysis/api-conventions.md)
- [event-integration.md](../analysis/event-integration.md)
