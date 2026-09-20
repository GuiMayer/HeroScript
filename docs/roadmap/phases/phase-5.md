# Fase 5 - Persistência

**Status:** 🟡 Implementação canônica disponível; expansões de metaprogressão continuam opcionais
**Dependências:** Fase 3 (Run), Fase 4 (Content)

> O desenho abaixo antecede o journal imutável. Não existe uma operação manual
> `save`: cada comando aceito é persistido atomicamente por `FileRunCommitStore`,
> e uma run é retomada pelo seu `runId`. Histórico, estatísticas, unlocks e
> achievements são projeções de perfil, não uma segunda fonte de verdade.

---

## Visão Geral

A Fase 5 implementa persistência de dados: salvar/carregar runs, estatísticas globais, e sistema de meta-progressão com desbloqueios. Esta fase permite que o progresso do jogador seja mantido entre sessões e adiciona objetivos de longo prazo.

## APIs Planejadas

### 1. Save API

Sistema de salvar e carregar runs.

**Endpoints:**
- `POST /api/save/{runId}` - Salva run atual
- `GET /api/save/{saveId}` - Carrega run salva
- `GET /api/save` - Lista saves disponíveis
- `DELETE /api/save/{saveId}` - Deleta save
- `GET /api/save/{saveId}/metadata` - Obtém metadados sem carregar run completa

**Recursos:**
- Serialização completa do estado da run
- Metadados (raça, progresso, tempo de jogo, data)
- Validação de integridade
- Versionamento de saves (compatibilidade futura)
- Auto-save (configurável)

### 2. MetaProgression API

Sistema de estatísticas globais e desbloqueios.

**Endpoints:**
- `GET /api/meta/stats` - Obtém estatísticas globais do jogador
- `GET /api/meta/unlocks` - Lista desbloqueios disponíveis
- `GET /api/meta/feats` - Lista feats disponíveis
- `POST /api/meta/feats/{name}/unlock` - Desbloqueia feat
- `GET /api/meta/history` - Histórico de runs (vitórias/derrotas)
- `POST /api/meta/export` - Exporta RunLog completo

**Estatísticas Rastreadas:**
- Total de runs (vitórias/derrotas)
- Runs por raça
- Poderes mais usados
- Companions favoritos
- Dano total causado
- Inimigos derrotados
- Tempo total de jogo
- Maior streak de vitórias

**Recursos:**
- Feats desbloqueiam poderes, companions, ou modificadores
- RunLog exportável (JSON)
- Leaderboards (futuro)
- Achievements (futuro)

---

## Estrutura de Save

### Save File (JSON)

```json
{
  "saveId": "save-123",
  "version": "1.0.0",
  "metadata": {
    "raceId": "human",
    "runId": "run-456",
    "currentNode": "combat-5",
    "progress": 0.45,
    "playTime": 3600,
    "createdAt": "2026-05-08T10:00:00Z",
    "lastSaved": "2026-05-08T10:30:00Z"
  },
  "runState": {
    "hero": {
      "hp": 80,
      "maxHp": 100,
      "energy": 3,
      "gold": 250,
      "pp": 120,
      "deck": ["FIREBALL", "POWER_STRIKE", "ICE_SHARD"],
      "companions": ["WOLF"]
    },
    "map": {
      "currentSegment": 2,
      "visitedNodes": ["combat-1", "shop-1", "combat-2"],
      "availableNodes": ["combat-3", "elite-1", "rest-1"]
    },
    "modifiers": {
      "FIREBALL": ["GO_AGAIN", "EXPLOSIVO"]
    },
    "gambits": [
      {
        "companionId": "WOLF",
        "priority": 1,
        "condition": { "type": "HERO_HP_BELOW", "threshold": 0.3 },
        "action": { "type": "USE_POWER", "powerId": "HOWL" }
      }
    ]
  },
  "eventHistory": [
    {
      "eventId": "evt-1",
      "type": "COMBAT_STARTED",
      "timestamp": "2026-05-08T10:15:00Z"
    }
  ]
}
```

### MetaProgression Data (JSON)

```json
{
  "playerId": "player-1",
  "stats": {
    "totalRuns": 50,
    "victories": 12,
    "defeats": 38,
    "winRate": 0.24,
    "totalPlayTime": 180000,
    "totalDamageDealt": 125000,
    "totalEnemiesKilled": 450,
    "favoriteRace": "human",
    "favoritePower": "FIREBALL",
    "longestWinStreak": 3
  },
  "unlocks": {
    "races": ["human", "orc", "elf"],
    "powers": ["FIREBALL", "POWER_STRIKE", "ICE_SHARD", "LIGHTNING_STRIKE"],
    "companions": ["WOLF", "OWL"],
    "modifiers": ["GO_AGAIN", "MULTI_HIT", "EXPLOSIVO"]
  },
  "feats": [
    {
      "name": "FIRST_VICTORY",
      "unlocked": true,
      "unlockedAt": "2026-05-01T12:00:00Z",
      "rewards": ["LIGHTNING_STRIKE"]
    },
    {
      "name": "DEFEAT_10_BOSSES",
      "unlocked": false,
      "progress": 3,
      "required": 10,
      "rewards": ["DRAGON_COMPANION"]
    }
  ],
  "runHistory": [
    {
      "runId": "run-1",
      "raceId": "human",
      "result": "VICTORY",
      "finalNode": "boss-1",
      "playTime": 3600,
      "completedAt": "2026-05-01T12:00:00Z"
    }
  ]
}
```

---

## Integração com Sistemas Anteriores

### Save + Run

Save captura estado completo da run:
- Estado do herói (HP, energia, deck, companions)
- Mapa e progresso
- Modificadores injetados
- Gambits configurados
- Histórico de eventos

### MetaProgression + Content

Feats desbloqueiam conteúdo:
- Novos poderes
- Novos companions
- Novos modificadores
- Novas raças (futuro)

### RunLog + EventBus

Histórico de eventos é exportável:
- Replay de runs (futuro)
- Análise de estratégias
- Debug de balanceamento
- Dataset para IA (futuro)

---

## Dependências

### Sistemas Core Necessários

- ✅ RunManager (Fase 3)
- ✅ ContentLoader (Fase 4)
- ✅ EventBus (Fase 1)
- ✅ Persistência automática: `FileRunCommitStore` e checkpoints de run
- ✅ Metaprogressão de leitura: `PlayerProfileProjectionReader`
- 📌 Backlog opcional: regras autorais adicionais para concessão de unlocks/achievements

### Ordem de Implementação

1. **Save Core** (src/Core/Persistence/Save/)
   - SaveData, SaveMetadata
   - SaveManager, SaveSerializer
   - Validation e versioning

2. **Save API** (src/API/Controllers/SaveController.cs)
   - Save/load run
   - List/delete saves
   - Get metadata

3. **MetaProgression Core** (src/Core/Persistence/Meta/)
   - PlayerStats, UnlockData
   - MetaProgressionManager
   - Feat system

4. **MetaProgression API** (src/API/Controllers/MetaController.cs)
   - Get stats
   - List unlocks/feats
   - Export RunLog

5. **Storage Backend** (src/Core/Persistence/Storage/)
   - File system storage (JSON)
   - Database storage (futuro)
   - Cloud storage (futuro)

---

## Exemplos de Uso

### Salvar Run

```http
POST /api/save/{runId}
Content-Type: application/json

{
  "saveName": "Human Run - Boss Fight",
  "autoSave": false
}
```

### Listar Saves

```http
GET /api/save
```

### Carregar Run

```http
GET /api/save/{saveId}
```

### Obter Estatísticas

```http
GET /api/meta/stats
```

### Desbloquear Feat

```http
POST /api/meta/feats/FIRST_VICTORY/unlock
```

### Exportar RunLog

```http
POST /api/meta/export
Content-Type: application/json

{
  "format": "json",
  "includeEvents": true,
  "runIds": ["run-1", "run-2"]
}
```

---

## Segurança e Validação

### Validação de Saves

- Verificar integridade (checksum)
- Validar versão (compatibilidade)
- Sanitizar dados (prevenir exploits)
- Verificar referências (poderes, companions existem)

### Backup

- Auto-backup antes de carregar save
- Limite de saves por jogador (configurável)
- Limpeza de saves antigos (opcional)

---

## Próximos Passos

Após completar a Fase 5, a Fase 6 implementará modos especiais:
- **Seed API** - Runs com seed fixa
- **Mode API** - Daily challenge, custom runs

Ver: [phase-6.md](phase-6.md)
