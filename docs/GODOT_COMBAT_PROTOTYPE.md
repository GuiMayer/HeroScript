# Protótipo de combate na Godot

Este guia cria uma tela mínima para validar a integração entre a Godot e a engine HeroScript.

O protótipo terá uma run com seed fixa, um herói, um inimigo, mão de cartas, seleção de alvo, execução de cartas e fim de turno. A Godot cuida da entrada e apresentação; a API cuida das regras, custos, dano, turnos, aleatoriedade e persistência.

## 1. Preparar a API

Compile e inicie a API na raiz do repositório:

```powershell
dotnet build src/API/API.csproj
dotnet run --project src/API
```

Confirme que `http://127.0.0.1:5260/api/v1/health/ready` retorna `status: ready`.

## 2. Criar a cena Godot

Crie um projeto Godot 4 vazio e uma cena `CombatPrototype.tscn` com esta estrutura:

```text
CombatPrototype (Control)
├── StatusLabel (Label)
├── HeroPanel (VBoxContainer)
│   ├── HeroHealth (ProgressBar)
│   └── HeroEnergy (Label)
├── EnemyPanel (VBoxContainer)
│   └── EnemyHealth (ProgressBar)
├── Hand (HBoxContainer)
├── TargetButton (Button)
├── DrawButton (Button)
└── EndTurnButton (Button)
```

No primeiro momento, os botões podem mostrar apenas o ID da carta. Arte e animações entram depois que o contrato estiver validado.

## 3. Cliente HTTP como Autoload

Crie `scripts/HeroScriptClient.gd` e registre-o como Autoload com o nome `HeroScriptClient`.

```gdscript
extends Node

signal state_changed(run_state: Dictionary, combat_state: Dictionary)
signal request_failed(code: int, message: String)

const BASE_URL := "http://127.0.0.1:5260"
var run_id := ""
var player_id := ""
var combat_id := ""
var run_state: Dictionary = {}
var combat_state: Dictionary = {}

func request_json(path: String, method: int, payload: Dictionary = {}) -> Dictionary:
    var request := HTTPRequest.new()
    add_child(request)
    var body := "" if payload.is_empty() else JSON.stringify(payload)
    var error := request.request(BASE_URL + path, ["Content-Type: application/json"], method, body)
    if error != OK:
        request.queue_free()
        request_failed.emit(0, "Falha ao iniciar HTTPRequest")
        return {}

    var response = await request.request_completed
    request.queue_free()
    var code: int = response[1]
    var parsed = JSON.parse_string(response[3].get_string_from_utf8())
    if code < 200 or code >= 300:
        var message := str(parsed)
        if parsed is Dictionary and parsed.has("detail"):
            message = str(parsed["detail"])
        request_failed.emit(code, message)
        return {}
    return parsed if parsed is Dictionary else {}

func start_run(seed: int = 20260816) -> bool:
    var result := await request_json("/api/v1/runs", HTTPClient.METHOD_POST, {
        "configName": "default",
        "runDefinitionId": "default_run",
        "playerEntityId": "godot-player",
        "seed": seed
    })
    if result.is_empty():
        return false
    run_id = str(result["runId"])
    player_id = str(result["playerEntityId"])
    run_state = result
    return true

func refresh_run() -> bool:
    var result := await request_json("/api/v1/runs/%s" % run_id, HTTPClient.METHOD_GET)
    if result.is_empty():
        return false
    run_state = result
    return true

func execute_run_command(command_type: String, payload: Dictionary) -> bool:
    var result := await request_json("/api/v1/runs/%s/commands" % run_id, HTTPClient.METHOD_POST, {
        "commandId": new_command_id(),
        "expectedSequence": int(run_state["sequence"]),
        "expectedStep": int(run_state["step"]),
        "type": command_type,
        "payload": payload
    })
    if result.is_empty():
        return false
    run_state = result["state"]
    return true

func start_encounter() -> bool:
    if not await execute_run_command("START_ENCOUNTER", {
        "heroId": player_id,
        "enemyIds": ["godot-enemy"],
        "initialEnergy": 3
    }):
        return false
    combat_id = str(run_state["activeEncounterId"])
    return await refresh_combat()

func refresh_combat() -> bool:
    var result := await request_json("/api/v1/combats/%s" % combat_id, HTTPClient.METHOD_GET)
    if result.is_empty():
        return false
    combat_state = result
    state_changed.emit(run_state, combat_state)
    return true

func draw_cards(count: int = 5) -> bool:
    if not await execute_run_command("DRAW_CARDS", {"count": count}):
        return false
    var refreshed := await refresh_run()
    if refreshed:
        state_changed.emit(run_state, combat_state)
    return refreshed

func play_card(card_id: String, target_id: String = "") -> bool:
    var result := await request_json("/api/v1/combats/%s/commands" % combat_id, HTTPClient.METHOD_POST, {
        "commandId": new_command_id(),
        "expectedSequence": int(run_state["sequence"]),
        "expectedStep": int(combat_state["step"]),
        "type": "EXECUTE_ACTION",
        "payload": {
            "actorId": player_id,
            "actionId": card_id,
            "cardId": card_id,
            "targetId": target_id
        }
    })
    if result.is_empty():
        return false
    await refresh_run()
    return await refresh_combat()

func end_turn() -> bool:
    var result := await request_json("/api/v1/combats/%s/commands" % combat_id, HTTPClient.METHOD_POST, {
        "commandId": new_command_id(),
        "expectedSequence": int(run_state["sequence"]),
        "expectedStep": int(combat_state["step"]),
        "type": "END_TURN",
        "payload": {"actorId": player_id}
    })
    if result.is_empty():
        return false
    await refresh_run()
    return await refresh_combat()

func new_command_id() -> String:
    var bytes := Crypto.new().generate_random_bytes(16)
    bytes[6] = (bytes[6] & 0x0f) | 0x40
    bytes[8] = (bytes[8] & 0x3f) | 0x80
    return "%02x%02x%02x%02x-%02x%02x-%02x%02x-%02x%02x-%02x%02x%02x%02x%02x%02x" % [
        bytes[0], bytes[1], bytes[2], bytes[3], bytes[4], bytes[5], bytes[6], bytes[7],
        bytes[8], bytes[9], bytes[10], bytes[11], bytes[12], bytes[13], bytes[14], bytes[15]
    ]
```

O `commandId` identifica a tentativa HTTP. Ele não participa da aleatoriedade da run. Em caso de timeout, guarde e reenvie o mesmo request, com o mesmo ID.

## 4. Controlar a cena

Adicione `scripts/CombatPrototype.gd` à cena principal:

```gdscript
extends Control

@onready var status_label: Label = $StatusLabel
@onready var hero_health: ProgressBar = $HeroPanel/HeroHealth
@onready var hero_energy: Label = $HeroPanel/HeroEnergy
@onready var enemy_health: ProgressBar = $EnemyPanel/EnemyHealth
@onready var hand: HBoxContainer = $Hand

func _ready() -> void:
    HeroScriptClient.state_changed.connect(_render_state)
    HeroScriptClient.request_failed.connect(_on_request_failed)
    await start_prototype()

func start_prototype() -> void:
    status_label.text = "Iniciando run..."
    if not await HeroScriptClient.start_run(20260816):
        return
    if not await HeroScriptClient.start_encounter():
        return
    status_label.text = "Escolha uma carta e um alvo"
    render_hand()

func _render_state(run_state: Dictionary, combat: Dictionary) -> void:
    var hero: Dictionary = combat["hero"]
    var enemy: Dictionary = combat["enemies"][0]
    var energy: Dictionary = hero["resources"]["energy"]
    hero_health.max_value = hero["maxHp"]
    hero_health.value = hero["currentHp"]
    enemy_health.max_value = enemy["maxHp"]
    enemy_health.value = enemy["currentHp"]
    hero_energy.text = "Energia: %d/%d" % [energy["current"], energy["maximum"]]
    render_hand()

func render_hand() -> void:
    for child in hand.get_children():
        child.queue_free()
    for card_id in HeroScriptClient.run_state["deck"]["hand"]:
        var button := Button.new()
        button.text = str(card_id)
        button.pressed.connect(func():
            await HeroScriptClient.play_card(str(card_id), "godot-enemy")
        )
        hand.add_child(button)

func _on_draw_button_pressed() -> void:
    await HeroScriptClient.draw_cards(5)

func _on_end_turn_button_pressed() -> void:
    await HeroScriptClient.end_turn()

func _on_target_button_pressed() -> void:
    await HeroScriptClient.play_card("basic_attack", "godot-enemy")

func _on_request_failed(code: int, message: String) -> void:
    status_label.text = "Erro %d: %s" % [code, message]
```

Conecte os sinais `pressed` dos botões aos métodos correspondentes. A mão inicial contém cinco `basic_attack`. Depois de pressionar `Draw`, o conteúdo padrão traz `defend`, `fireball` e `heal`.

## 5. Regras de sincronização

Depois de cada comando aceito, a UI deve usar o estado retornado pela API. Não recalcule dano, custo, gatilhos ou aleatoriedade na Godot.

- `2xx`: aceite o novo estado;
- `409`: recarregue run e combate; a intenção local ficou desatualizada;
- `422`: mostre a regra rejeitada sem repetir automaticamente;
- timeout: reenvie o mesmo request com o mesmo `commandId`;
- reconexão: use `GET /api/v1/runs/{runId}` e recupere o combate ativo.

As rotas específicas de loja, preparação e seleção são read models. As mutações passam pelos gateways `/runs/{runId}/commands` e `/combats/{combatId}/commands`.

## 6. Validar determinismo

Execute duas runs com seed `20260816` e a mesma sequência: iniciar run, iniciar encontro, jogar `basic_attack` e encerrar turno. Compare os hashes e o estado final. Eles devem ser iguais quando forem iguais a seed, revisão de conteúdo, versão da engine e comandos.

Para auditoria:

```text
GET  /api/v1/runs/{runId}/journal
POST /api/v1/runs/{runId}/verify
```

## 7. Limitações do primeiro protótipo

Comece validando cartas, dano, energia, bloqueio, cura e turnos. A projeção de stacks e duração de status ainda precisa ser exposta de forma mais completa no read model imutável de combate antes de a UI depender dela.

Não inclua `X-Admin-Key` no cliente distribuído. Endpoints administrativos são ferramentas de desenvolvimento, não fazem parte do loop de jogo.

Depois desse fluxo, os próximos incrementos naturais são animações, seleção visual de alvo, log de ações, renderização de status e replay iniciado pela própria UI.
