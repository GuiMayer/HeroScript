extends SceneTree
var failures := 0
func _init() -> void: call_deferred("_run")
func check(ok: bool, label: String) -> void:
	print("[PASS] " if ok else "[FAIL] ", label)
	if not ok: failures += 1
func _run() -> void:
	if root.get_node("Preferences").api_url != "http://127.0.0.1:61987":
		check(false, "numeric REST test requires the isolated API on port 61987"); quit(1); return
	var session = root.get_node("GameSession")
	check(await session.connect_engine("default"), "connect isolated REST setting")
	check(await session.start_campaign(int(Time.get_unix_time_from_system())), "create isolated campaign")
	var count: Dictionary = JSON.parse_string('{"flowId":"run.draw","requestedCount":1}')
	check(await session.execute_run_command("INVOKE_CARD_ZONE_GAMEPLAY_FLOW", count), "JSON-decoded requestedCount is accepted by the real integer command contract")
	var presentation = JSON.parse_string(FileAccess.get_file_as_string("res://data/presentation.json"))
	var scenario: Dictionary = presentation.default_scenario.duplicate(true)
	scenario.initialState.resourcesByActor.player.health = 60.5
	scenario.initialState.effects = [{"targetActorId": "player", "statusId": "burning", "stacks": 2.0}]
	scenario.participants[0].controllerBinding.kind = 0.0
	var before := JSON.stringify(scenario)
	check(await session.start_sandbox("combat_sandbox", scenario, int(Time.get_unix_time_from_system()) + 1), "scenario editor floats become valid stack counts and numeric enums over actual REST")
	check(JSON.stringify(scenario) == before, "real sandbox creation does not mutate the editor document")
	if not failures:
		var players: Array = session.combat.get("actors", []).filter(func(actor): return actor.instanceId == "player")
		check(not players.is_empty() and players[0].resources.health.current == 60.5, "fractional Health survives the real request unchanged")
		var commands: Array = JSON.parse_string('[{"type":"END_TURN","payload":{"actorId":"player","actionType":3}}]')
		var result: Dictionary = await session.simulate(commands)
		check(result.ok, "nested simulation command integer fields pass the actual REST deserializer")
		if not result.ok: print("SIMULATION_DIAGNOSTIC=", result.get("error", result))
		var verified: Dictionary = await session.verify()
		check(verified.get("ok", false) and verified.data.get("isValid", false), "sandbox and command replay remain deterministic")
	root.get_node("GameAudio").shutdown(); quit(1 if failures else 0)
