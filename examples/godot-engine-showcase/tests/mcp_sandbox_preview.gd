extends Node
## Read-only QA scene. Never launch in the player's user directory.

var qa_evidence: Dictionary = {}
@export var sandbox_run_override: String = ""

func _ready() -> void:
	call_deferred("_load_sandbox")

func _load_sandbox() -> void:
	if not OS.get_user_data_dir().get_file().begins_with("HeroScript-mcp-qa-"):
		push_error("MCP preview requires an isolated Godot user directory")
		return
	var origin := OS.get_environment("HEROSCRIPT_MCP_QA_API")
	var run_id := sandbox_run_override if not sandbox_run_override.is_empty() else OS.get_environment("HEROSCRIPT_MCP_QA_RUN")
	if not origin.begins_with("http://127.0.0.1:") or run_id.is_empty():
		push_error("MCP preview requires an explicit sandbox API and run")
		return
	var transport = preload("res://scripts/engine/http_transport.gd").new()
	add_child(transport)
	transport.base_url = origin
	var marker: Dictionary = await transport.request(HTTPClient.METHOD_GET, "/api/v1/diagnostics/mcp-sandbox")
	if not marker.ok or not marker.data.get("isolated", false):
		push_error("MCP preview refuses a non-sandbox API")
		return
	var gateway = preload("res://scripts/engine/engine_gateway.gd").new(transport)
	Preferences.api_url = origin
	Preferences.changed.emit()
	GameSession.configure(gateway)
	var read: Dictionary = await gateway.read_run(run_id)
	if not read.ok:
		push_error("MCP preview could not read sandbox run")
		return
	var snapshot: Dictionary = read.data.get("run", read.data)
	if not await GameSession.connect_engine(str(snapshot.get("settingId", "default"))):
		push_error("MCP preview could not connect to sandbox")
		return
	# Sandbox participants use aliases, not campaign launch identities. Hydrate the
	# same authoritative read bundle without asking the campaign resume UI to open it.
	GameSession._run = snapshot
	if not await GameSession.refresh():
		push_error("MCP preview could not refresh the sandbox combat")
		return
	var router = load("res://main.tscn").instantiate()
	add_child(router)
	router.show_combat()
	qa_evidence = {"runId": GameSession.run.get("runId"), "combatId": GameSession.combat.get("combatId"),
		"contentRevision": GameSession.combat.get("contentRevision"), "stateHash": GameSession.combat.get("stateHash"),
		"actors": GameSession.combat.get("actors", []), "screen": router.current_screen}
	print("[MCP QA] ", JSON.stringify(qa_evidence))
