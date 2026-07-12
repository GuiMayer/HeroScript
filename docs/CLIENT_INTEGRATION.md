# Client Integration Guide

**HeroScript API** - Integration guide for game clients

---

## Overview

HeroScript operates as a **REST API service** that game clients consume over HTTP. This architecture provides:

- **5-8ms latency** on localhost (imperceptible for turn-based games)
- **Hot-reload** of game configs without restarting the client
- **Shared engine** across multiple client technologies
- **Web-based tools** running alongside gameplay
- **Headless simulations** for testing and balancing

This guide covers integration with:
- Unity (C# using UnityWebRequest)
- Godot (GDScript using HTTPRequest)
- Web (TypeScript/JavaScript using fetch)
- Python (for simulations and testing)

---

## Prerequisites

### Start the API Server

All clients require the HeroScript API to be running:

```bash
# Option A: Run locally
cd src/API
dotnet run
# API available at http://localhost:5260

# Option B: Docker
docker-compose up -d
# API available at http://localhost:5260
```

Verify the API is running:
```bash
curl http://localhost:5260/api/health
```

---

## Unity Integration (C#)

### 1. Setup

Unity communicates with HeroScript via HTTP using `UnityWebRequest`.

**Recommended architecture:**
```
Unity MonoBehaviours (UI, Animations)
    ↓
HeroScriptClient (wrapper class)
    ↓
UnityWebRequest (HTTP)
    ↓
HeroScript API
```

### 2. Create API Client Wrapper

Create `Assets/Scripts/HeroScriptClient.cs`:

```csharp
using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

public class HeroScriptClient : MonoBehaviour
{
    [SerializeField] private string apiBaseUrl = "http://localhost:5260";
    [SerializeField] private string adminApiKey = ""; // Leave empty for read-only operations

    // Get all available actions
    public IEnumerator GetAllActions(Action<ActionListResponse> onSuccess, Action<string> onError)
    {
        string url = $"{apiBaseUrl}/api/actions";
        
        using (UnityWebRequest request = UnityWebRequest.Get(url))
        {
            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.Success)
            {
                var response = JsonUtility.FromJson<ActionListResponse>(request.downloadHandler.text);
                onSuccess?.Invoke(response);
            }
            else
            {
                onError?.Invoke($"Error: {request.error}");
            }
        }
    }

    // Start a new combat
    public IEnumerator StartCombat(CombatStartRequest combatRequest, Action<CombatStateResponse> onSuccess, Action<string> onError)
    {
        string url = $"{apiBaseUrl}/api/combat/start";
        string jsonPayload = JsonUtility.ToJson(combatRequest);
        
        using (UnityWebRequest request = new UnityWebRequest(url, "POST"))
        {
            byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonPayload);
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");

            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.Success)
            {
                var response = JsonUtility.FromJson<CombatStateResponse>(request.downloadHandler.text);
                onSuccess?.Invoke(response);
            }
            else
            {
                onError?.Invoke($"Error: {request.error}");
            }
        }
    }

    // Execute an action in combat
    public IEnumerator ExecuteAction(Guid combatId, ActionExecutionRequest actionRequest, Action<CombatStateResponse> onSuccess, Action<string> onError)
    {
        string url = $"{apiBaseUrl}/api/combat/{combatId}/execute-action";
        string jsonPayload = JsonUtility.ToJson(actionRequest);
        
        using (UnityWebRequest request = new UnityWebRequest(url, "POST"))
        {
            byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonPayload);
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");

            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.Success)
            {
                var response = JsonUtility.FromJson<CombatStateResponse>(request.downloadHandler.text);
                onSuccess?.Invoke(response);
            }
            else
            {
                onError?.Invoke($"Error: {request.error}");
            }
        }
    }

    // Reload actions (requires admin key)
    public IEnumerator ReloadActions(string configName, Action onSuccess, Action<string> onError)
    {
        if (string.IsNullOrEmpty(adminApiKey))
        {
            onError?.Invoke("Admin API key not configured");
            yield break;
        }

        string url = $"{apiBaseUrl}/api/actions/reload?configName={configName}";
        
        using (UnityWebRequest request = new UnityWebRequest(url, "POST"))
        {
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("X-Admin-Key", adminApiKey);

            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.Success)
            {
                onSuccess?.Invoke();
            }
            else
            {
                onError?.Invoke($"Error: {request.error}");
            }
        }
    }
}

// Data Transfer Objects (DTOs)
[Serializable]
public class ActionListResponse
{
    public ActionSummary[] actions;
}

[Serializable]
public class ActionSummary
{
    public string actionId;
    public string displayName;
    public string actionType;
}

[Serializable]
public class CombatStartRequest
{
    public string heroEntityId;
    public string[] enemyEntityIds;
    public string configName = "default";
}

[Serializable]
public class CombatStateResponse
{
    public string combatId;
    public CombatEntity hero;
    public CombatEntity[] enemies;
    public int currentTurn;
    public string phase;
}

[Serializable]
public class CombatEntity
{
    public string entityId;
    public int health;
    public int maxHealth;
    public int mana;
    public int maxMana;
}

[Serializable]
public class ActionExecutionRequest
{
    public string actionId;
    public string actorEntityId;
    public string[] targetEntityIds;
}
```

### 3. Usage Example

Create `Assets/Scripts/GameManager.cs`:

```csharp
using System;
using UnityEngine;

public class GameManager : MonoBehaviour
{
    private HeroScriptClient client;
    private Guid currentCombatId;

    void Start()
    {
        client = GetComponent<HeroScriptClient>();
        StartNewCombat();
    }

    void StartNewCombat()
    {
        var request = new CombatStartRequest
        {
            heroEntityId = "knight",
            enemyEntityIds = new[] { "goblin", "goblin" },
            configName = "default"
        };

        StartCoroutine(client.StartCombat(
            request,
            OnCombatStarted,
            OnError
        ));
    }

    void OnCombatStarted(CombatStateResponse combat)
    {
        currentCombatId = Guid.Parse(combat.combatId);
        Debug.Log($"Combat started: {combat.combatId}");
        Debug.Log($"Hero HP: {combat.hero.health}/{combat.hero.maxHealth}");
        
        // Update UI
        UpdateCombatUI(combat);
    }

    void ExecutePlayerAction(string actionId, string targetEnemyId)
    {
        var request = new ActionExecutionRequest
        {
            actionId = actionId,
            actorEntityId = "hero",
            targetEntityIds = new[] { targetEnemyId }
        };

        StartCoroutine(client.ExecuteAction(
            currentCombatId,
            request,
            OnActionExecuted,
            OnError
        ));
    }

    void OnActionExecuted(CombatStateResponse combat)
    {
        Debug.Log($"Action executed. Turn: {combat.currentTurn}");
        
        // Play animations
        PlayDamageAnimation();
        
        // Update UI
        UpdateCombatUI(combat);
    }

    void OnError(string error)
    {
        Debug.LogError($"API Error: {error}");
    }

    void UpdateCombatUI(CombatStateResponse combat)
    {
        // Update your UI here
    }

    void PlayDamageAnimation()
    {
        // Trigger animation here
    }
}
```

### 4. Hot-Reload Workflow (Unity Editor)

For rapid iteration during development:

```csharp
// Add to your editor toolbar
#if UNITY_EDITOR
using UnityEditor;

[MenuItem("HeroScript/Reload Actions")]
static void ReloadActions()
{
    var client = FindObjectOfType<HeroScriptClient>();
    EditorCoroutineUtility.StartCoroutine(
        client.ReloadActions("default", 
            () => Debug.Log("Actions reloaded!"),
            (error) => Debug.LogError(error)
        ),
        client
    );
}
#endif
```

**Workflow:**
1. Edit `data/configs/default/Actions/fireball.json` in text editor
2. Click `HeroScript > Reload Actions` in Unity
3. Test immediately without Play/Stop cycle

---

## Godot Integration (GDScript)

### 1. Setup

Create `scripts/heroscript_client.gd`:

```gdscript
extends Node
class_name HeroScriptClient

var api_base_url := "http://localhost:5260"
var admin_api_key := ""  # Leave empty for read-only operations

# Get all actions
func get_all_actions() -> Array:
    var url = api_base_url + "/api/actions"
    var http = HTTPRequest.new()
    add_child(http)
    
    http.request(url)
    var response = await http.request_completed
    http.queue_free()
    
    if response[0] == HTTPRequest.RESULT_SUCCESS:
        var json = JSON.parse_string(response[3].get_string_from_utf8())
        return json.get("actions", [])
    else:
        push_error("Failed to fetch actions: " + str(response[0]))
        return []

# Start combat
func start_combat(hero_id: String, enemy_ids: Array) -> Dictionary:
    var url = api_base_url + "/api/combat/start"
    var http = HTTPRequest.new()
    add_child(http)
    
    var body = JSON.stringify({
        "heroEntityId": hero_id,
        "enemyEntityIds": enemy_ids,
        "configName": "default"
    })
    
    var headers = ["Content-Type: application/json"]
    http.request(url, headers, HTTPClient.METHOD_POST, body)
    
    var response = await http.request_completed
    http.queue_free()
    
    if response[0] == HTTPRequest.RESULT_SUCCESS:
        return JSON.parse_string(response[3].get_string_from_utf8())
    else:
        push_error("Failed to start combat: " + str(response[0]))
        return {}

# Execute action
func execute_action(combat_id: String, action_id: String, actor_id: String, target_ids: Array) -> Dictionary:
    var url = api_base_url + "/api/combat/" + combat_id + "/execute-action"
    var http = HTTPRequest.new()
    add_child(http)
    
    var body = JSON.stringify({
        "actionId": action_id,
        "actorEntityId": actor_id,
        "targetEntityIds": target_ids
    })
    
    var headers = ["Content-Type: application/json"]
    http.request(url, headers, HTTPClient.METHOD_POST, body)
    
    var response = await http.request_completed
    http.queue_free()
    
    if response[0] == HTTPRequest.RESULT_SUCCESS:
        return JSON.parse_string(response[3].get_string_from_utf8())
    else:
        push_error("Failed to execute action: " + str(response[0]))
        return {}

# Reload actions (requires admin key)
func reload_actions(config_name: String) -> bool:
    if admin_api_key.is_empty():
        push_error("Admin API key not configured")
        return false
    
    var url = api_base_url + "/api/actions/reload?configName=" + config_name
    var http = HTTPRequest.new()
    add_child(http)
    
    var headers = [
        "X-Admin-Key: " + admin_api_key
    ]
    http.request(url, headers, HTTPClient.METHOD_POST)
    
    var response = await http.request_completed
    http.queue_free()
    
    return response[0] == HTTPRequest.RESULT_SUCCESS
```

### 2. Usage Example

Create `scripts/game_manager.gd`:

```gdscript
extends Node

@onready var client: HeroScriptClient = $HeroScriptClient
var current_combat_id: String

func _ready():
    start_new_combat()

func start_new_combat():
    var combat = await client.start_combat("knight", ["goblin", "goblin"])
    
    if combat.has("combatId"):
        current_combat_id = combat["combatId"]
        print("Combat started: ", current_combat_id)
        print("Hero HP: ", combat["hero"]["health"])
        
        update_combat_ui(combat)

func execute_player_action(action_id: String, target_enemy_id: String):
    var combat = await client.execute_action(
        current_combat_id,
        action_id,
        "hero",
        [target_enemy_id]
    )
    
    if combat.has("currentTurn"):
        print("Action executed. Turn: ", combat["currentTurn"])
        
        # Play animations
        play_damage_animation()
        
        # Update UI
        update_combat_ui(combat)

func update_combat_ui(combat: Dictionary):
    # Update your UI here
    pass

func play_damage_animation():
    # Trigger animation here
    pass
```

### 3. Hot-Reload (Godot Editor)

Add to `addons/heroscript_tools/reload_menu.gd`:

```gdscript
@tool
extends EditorPlugin

func _enter_tree():
    add_tool_menu_item("Reload HeroScript Actions", _on_reload_actions)

func _exit_tree():
    remove_tool_menu_item("Reload HeroScript Actions")

func _on_reload_actions():
    var client = HeroScriptClient.new()
    client.admin_api_key = "dev-admin-key"  # Set your key
    var success = await client.reload_actions("default")
    
    if success:
        print("Actions reloaded successfully!")
    else:
        push_error("Failed to reload actions")
```

---

## Web Integration (TypeScript/JavaScript)

### 1. Setup

Install dependencies (optional):
```bash
npm install axios  # or use native fetch
```

### 2. Create API Client

Create `src/services/heroScriptClient.ts`:

```typescript
const API_BASE_URL = import.meta.env.VITE_API_URL || 'http://localhost:5260';
const ADMIN_API_KEY = import.meta.env.VITE_ADMIN_API_KEY || '';

// Types
interface ActionSummary {
  actionId: string;
  displayName: string;
  actionType: string;
}

interface CombatStartRequest {
  heroEntityId: string;
  enemyEntityIds: string[];
  configName?: string;
}

interface CombatStateResponse {
  combatId: string;
  hero: CombatEntity;
  enemies: CombatEntity[];
  currentTurn: number;
  phase: string;
}

interface CombatEntity {
  entityId: string;
  health: number;
  maxHealth: number;
  mana: number;
  maxMana: number;
}

interface ActionExecutionRequest {
  actionId: string;
  actorEntityId: string;
  targetEntityIds: string[];
}

// API Client
export class HeroScriptClient {
  private baseUrl: string;
  private adminKey: string;

  constructor(baseUrl: string = API_BASE_URL, adminKey: string = ADMIN_API_KEY) {
    this.baseUrl = baseUrl;
    this.adminKey = adminKey;
  }

  // Get all actions
  async getAllActions(): Promise<ActionSummary[]> {
    const response = await fetch(`${this.baseUrl}/api/actions`);
    if (!response.ok) throw new Error(`HTTP ${response.status}: ${response.statusText}`);
    return response.json();
  }

  // Start combat
  async startCombat(request: CombatStartRequest): Promise<CombatStateResponse> {
    const response = await fetch(`${this.baseUrl}/api/combat/start`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ ...request, configName: request.configName || 'default' }),
    });
    if (!response.ok) throw new Error(`HTTP ${response.status}: ${response.statusText}`);
    return response.json();
  }

  // Execute action
  async executeAction(
    combatId: string,
    request: ActionExecutionRequest
  ): Promise<CombatStateResponse> {
    const response = await fetch(`${this.baseUrl}/api/combat/${combatId}/execute-action`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(request),
    });
    if (!response.ok) throw new Error(`HTTP ${response.status}: ${response.statusText}`);
    return response.json();
  }

  // Reload actions (requires admin key)
  async reloadActions(configName: string = 'default'): Promise<void> {
    if (!this.adminKey) throw new Error('Admin API key not configured');
    
    const response = await fetch(`${this.baseUrl}/api/actions/reload?configName=${configName}`, {
      method: 'POST',
      headers: { 'X-Admin-Key': this.adminKey },
    });
    if (!response.ok) throw new Error(`HTTP ${response.status}: ${response.statusText}`);
  }
}

// Singleton instance
export const heroScriptClient = new HeroScriptClient();
```

### 3. Usage Example (React)

```typescript
// src/components/CombatScreen.tsx
import { useState, useEffect } from 'react';
import { heroScriptClient, CombatStateResponse } from '../services/heroScriptClient';

export function CombatScreen() {
  const [combat, setCombat] = useState<CombatStateResponse | null>(null);
  const [loading, setLoading] = useState(false);

  useEffect(() => {
    startNewCombat();
  }, []);

  async function startNewCombat() {
    setLoading(true);
    try {
      const newCombat = await heroScriptClient.startCombat({
        heroEntityId: 'knight',
        enemyEntityIds: ['goblin', 'goblin'],
      });
      setCombat(newCombat);
    } catch (error) {
      console.error('Failed to start combat:', error);
    } finally {
      setLoading(false);
    }
  }

  async function executeAction(actionId: string, targetId: string) {
    if (!combat) return;
    
    setLoading(true);
    try {
      const updatedCombat = await heroScriptClient.executeAction(combat.combatId, {
        actionId,
        actorEntityId: 'hero',
        targetEntityIds: [targetId],
      });
      setCombat(updatedCombat);
    } catch (error) {
      console.error('Failed to execute action:', error);
    } finally {
      setLoading(false);
    }
  }

  if (!combat) return <div>Loading...</div>;

  return (
    <div className="combat-screen">
      <h2>Turn {combat.currentTurn}</h2>
      
      <div className="hero">
        <h3>Hero</h3>
        <p>HP: {combat.hero.health}/{combat.hero.maxHealth}</p>
        <p>Mana: {combat.hero.mana}/{combat.hero.maxMana}</p>
      </div>

      <div className="enemies">
        {combat.enemies.map((enemy) => (
          <div key={enemy.entityId} className="enemy">
            <h4>{enemy.entityId}</h4>
            <p>HP: {enemy.health}/{enemy.maxHealth}</p>
            <button onClick={() => executeAction('basic_attack', enemy.entityId)} disabled={loading}>
              Attack
            </button>
          </div>
        ))}
      </div>
    </div>
  );
}
```

### 4. Hot-Reload Dev Tool

```typescript
// src/dev-tools/ReloadButton.tsx
import { heroScriptClient } from '../services/heroScriptClient';

export function ReloadButton() {
  async function handleReload() {
    try {
      await heroScriptClient.reloadActions('default');
      alert('Actions reloaded! Refresh the page to see changes.');
    } catch (error) {
      alert(`Failed to reload: ${error}`);
    }
  }

  // Only show in development
  if (import.meta.env.PROD) return null;

  return (
    <button 
      onClick={handleReload}
      style={{ position: 'fixed', bottom: 20, right: 20 }}
    >
      Reload Actions
    </button>
  );
}
```

---

## Python Integration (Simulations)

### 1. Setup

```bash
pip install requests
```

### 2. Create API Client

Create `heroscript_client.py`:

```python
import requests
from typing import List, Dict, Optional

class HeroScriptClient:
    def __init__(self, base_url: str = "http://localhost:5260", admin_key: Optional[str] = None):
        self.base_url = base_url
        self.admin_key = admin_key

    def get_all_actions(self) -> List[Dict]:
        """Get all available actions"""
        response = requests.get(f"{self.base_url}/api/actions")
        response.raise_for_status()
        return response.json()

    def start_combat(self, hero_id: str, enemy_ids: List[str], config_name: str = "default") -> Dict:
        """Start a new combat"""
        response = requests.post(
            f"{self.base_url}/api/combat/start",
            json={
                "heroEntityId": hero_id,
                "enemyEntityIds": enemy_ids,
                "configName": config_name
            }
        )
        response.raise_for_status()
        return response.json()

    def execute_action(self, combat_id: str, action_id: str, actor_id: str, target_ids: List[str]) -> Dict:
        """Execute an action in combat"""
        response = requests.post(
            f"{self.base_url}/api/combat/{combat_id}/execute-action",
            json={
                "actionId": action_id,
                "actorEntityId": actor_id,
                "targetEntityIds": target_ids
            }
        )
        response.raise_for_status()
        return response.json()

    def reload_actions(self, config_name: str = "default") -> None:
        """Reload action definitions (requires admin key)"""
        if not self.admin_key:
            raise ValueError("Admin API key required for reload operations")
        
        response = requests.post(
            f"{self.base_url}/api/actions/reload",
            params={"configName": config_name},
            headers={"X-Admin-Key": self.admin_key}
        )
        response.raise_for_status()
```

### 3. Simulation Example

```python
# simulate_combat.py
from heroscript_client import HeroScriptClient

def run_simulation(iterations: int = 1000):
    """Run combat simulations for balancing"""
    client = HeroScriptClient()
    
    wins = 0
    total_turns = 0
    
    for i in range(iterations):
        # Start combat
        combat = client.start_combat("knight", ["goblin", "goblin"])
        combat_id = combat["combatId"]
        
        # Simulate until combat ends
        while combat["phase"] != "ENDED":
            # Simple AI: attack first alive enemy
            target = next(e for e in combat["enemies"] if e["health"] > 0)
            
            try:
                combat = client.execute_action(
                    combat_id,
                    "basic_attack",
                    "hero",
                    [target["entityId"]]
                )
            except requests.HTTPError:
                break  # Combat ended
        
        # Record results
        if combat["hero"]["health"] > 0:
            wins += 1
        total_turns += combat["currentTurn"]
        
        if (i + 1) % 100 == 0:
            print(f"Progress: {i + 1}/{iterations}")
    
    # Print statistics
    win_rate = (wins / iterations) * 100
    avg_turns = total_turns / iterations
    
    print(f"\nSimulation Results ({iterations} iterations):")
    print(f"Win Rate: {win_rate:.1f}%")
    print(f"Average Turns: {avg_turns:.1f}")
    
    if win_rate < 40:
        print("⚠️ Hero too weak - consider buffing")
    elif win_rate > 80:
        print("⚠️ Hero too strong - consider nerfing")
    else:
        print("✅ Balance looks good")

if __name__ == "__main__":
    run_simulation(1000)
```

Run simulation:
```bash
python simulate_combat.py
```

---

## Error Handling Best Practices

### Retry Logic

```typescript
async function fetchWithRetry<T>(
  fetcher: () => Promise<T>,
  retries: number = 3
): Promise<T> {
  for (let i = 0; i < retries; i++) {
    try {
      return await fetcher();
    } catch (error) {
      if (i === retries - 1) throw error;
      await new Promise(resolve => setTimeout(resolve, 1000 * (i + 1)));
    }
  }
  throw new Error('Max retries exceeded');
}

// Usage
const combat = await fetchWithRetry(() => 
  heroScriptClient.startCombat({ heroEntityId: 'knight', enemyEntityIds: ['goblin'] })
);
```

### CORS Troubleshooting

If you see CORS errors in browser console:

1. **Check API is running**: `curl http://localhost:5260/api/health`
2. **Verify AllowedOrigins**: Check `appsettings.Development.json`
3. **Test CORS headers**:
```bash
curl -H "Origin: http://localhost:5173" \
  -H "Access-Control-Request-Method: GET" \
  -X OPTIONS \
  http://localhost:5260/api/actions -v
```

---

## Performance Tips

### Caching

Cache action definitions to reduce API calls:

```typescript
class CachedHeroScriptClient extends HeroScriptClient {
  private actionsCache: ActionSummary[] | null = null;

  async getAllActions(): Promise<ActionSummary[]> {
    if (!this.actionsCache) {
      this.actionsCache = await super.getAllActions();
    }
    return this.actionsCache;
  }

  clearCache() {
    this.actionsCache = null;
  }
}
```

### Batching

When simulating, reuse connections:

```python
import requests

# Reuse session for connection pooling
session = requests.Session()
client = HeroScriptClient(session=session)
```

---

## Next Steps

- **Production Deployment**: See [PRODUCTION.md](PRODUCTION.md) for deploying the API
- **Content Customization**: See [CUSTOMIZATION.md](CUSTOMIZATION.md) for creating custom cards/enemies
- **API Reference**: See [api/endpoints.md](api/endpoints.md) for complete endpoint documentation

---

## Support

For issues or questions:
- GitHub Issues: `https://github.com/your-org/heroscript/issues`
- Documentation: `docs/README.md`
