extends SceneTree
## Production components with synthetic snapshots, never a live run or REST command.
## Execute through tools/capture-ivory-baseline.ps1 to isolate saved preferences.
var failures := 0
var output_dir := ""
var samples: Array = []
var captures: Array = []
var router
var session
var gateway

class FixtureGateway extends RefCounted:
	var calls: Array[String] = []
	var run: Dictionary = {}
	func inspect_hand(_combat: String, _actor: String, _targets: Array) -> Dictionary:
		calls.append("inspect_hand")
		return {"ok": true, "data": {"cards": []}}
	func run_timeline(_run: String, _cursor: int, _limit: int) -> Dictionary:
		calls.append("run_timeline")
		return {"ok": true, "data": {"items": [{"sequence": 1, "commandType": "START_ENCOUNTER", "step": 1,
			"stateHash": "synthetic-baseline-not-an-engine-hash", "frames": []}], "nextCursor": 0}}
	func run_commit(_run: String, _sequence: int) -> Dictionary:
		calls.append("run_commit")
		return {"ok": true, "data": {"stateAfter": run.duplicate(true)}}
	func connect_engine() -> Dictionary:
		calls.append("connect_engine")
		return {"ok": false, "error": "Offline visual baseline", "status": 503}

func _init() -> void: call_deferred("_run")

func check(condition: bool, label: String) -> void:
	print("[PASS] " if condition else "[FAIL] ", label)
	if not condition: failures += 1

func settle() -> void:
	for _frame in 12: await process_frame

func actor(id: String, definition: String, side: String) -> Dictionary:
	return {"instanceId": id, "definitionId": definition, "sideId": side, "resources": {
		"health": {"current": 48, "minimum": 0, "maximum": 72}, "block": {"current": 8, "minimum": 0, "maximum": 999},
		"energy": {"current": 3, "minimum": 0, "maximum": 3}, "mana": {"current": 5, "minimum": 0, "maximum": 8}}, "statuses": []}

func load_combat() -> void:
	session._run = {"runId": "visual-baseline", "sequence": 1, "playerEntityId": "hero", "lifecycle": "Active"}
	session._combat = {"combatId": "baseline-combat", "activeActorId": "hero", "activation": {
		"round": 1, "activeActorId": "hero", "waitingForInput": true, "intents": [
			{"actorId": "wisp", "actionId": "Attack"}, {"actorId": "sentinel", "actionId": "Poison Spit"}]},
		"relationships": {"sameSide": "Ally", "differentSides": "Enemy"}, "actors": [
			actor("hero", "spire_adept", "player"), actor("wisp", "spire_wisp", "enemy"), actor("sentinel", "spire_sentinel", "enemy")]}
	session._legal_actions = []
	var cards: Array = []
	for index in 5:
		var id := "baseline-%s" % index
		cards.append({"cardInstanceId": id, "definitionId": ["basic_attack", "defend", "fireball", "heal", "arcane_bolt"][index]})
		session._legal_actions.append({"source": "Card", "command": {"cardInstanceId": id, "actionType": "PLAY_CARD", "targetIds": ["wisp"]},
			"costs": [{"resourceId": "energy", "amount": 1}], "applications": [
				{"targetEntityId": "wisp", "resourceId": "health", "previousValue": 48, "currentValue": 42}]})
	session._card_zones = {"zones": [{"zoneId": "play", "allowsCardPlay": true, "contentsVisible": true, "cards": cards, "count": 5},
		{"zoneId": "draw", "count": 8, "presentation": {"labelKey": "Draw pile"}},
		{"zoneId": "discard", "count": 2, "presentation": {"labelKey": "Discard pile"}}]}

func load_journey(reward: bool) -> void:
	session._combat = {}
	session._run = {"runId": "visual-baseline", "sequence": 1, "lifecycle": "Active", "currentNodeId": "encounter",
		"resourceState": {"resources": {"gold": {"current": 40, "maximum": 999}}}, "map": {"nodes": [
			{"nodeId": "encounter", "activity": {"type": "Encounter"}, "nextNodeIds": ["reward"], "metadata": {"displayName": "First battle"}},
			{"nodeId": "reward", "activity": {"type": "CardSelection"}, "nextNodeIds": ["boss"], "metadata": {"displayName": "Build choice"}},
			{"nodeId": "boss", "activity": {"type": "Encounter"}, "nextNodeIds": [], "metadata": {"displayName": "Final battle"}}]},
		"deck": {"cardInstances": session._card_zones.zones[0].cards.duplicate(true)}}
	session._available_commands = [{"type": "START_ENCOUNTER", "validPayload": {}}]
	if reward:
		session._run.currentNodeId = "reward"
		session._available_commands = [{"type": "PICK_CARD_REWARD", "validPayload": {
			"selectionInstanceId": "baseline-offer", "cardIds": ["basic_attack", "fireball", "heal"]}}]

func capture(name: String) -> void:
	await settle()
	check(preload("res://tests/layout_inspector.gd").vertical_text_issues(router).is_empty(), name + ": readable geometry")
	if DisplayServer.get_name() == "headless": return
	await RenderingServer.frame_post_draw
	var path := output_dir.path_join("%s-%sx%s.png" % [name, root.size.x, root.size.y])
	var result := root.get_texture().get_image().save_png(path)
	check(result == OK, "saved " + name)
	if result == OK: captures.append(path.get_file())

func measure_refresh(screen, changed: bool) -> void:
	var call_start: int = gateway.calls.size()
	var times: Array[float] = []
	for _sample in 20:
		if changed: session._run.sequence += 1
		var started := Time.get_ticks_usec()
		screen.refresh_state(router.presentation)
		times.append((Time.get_ticks_usec() - started) / 1000.0)
		await settle()
	times.sort()
	samples.append({"resolution": "%sx%s" % [root.size.x, root.size.y], "screen": router.current_screen,
		"logical_viewport": str(root.get_visible_rect().size), "content_scale_size": str(root.content_scale_size),
		"changed_sequence": changed, "samples": times.size(), "synchronous_refresh_median_ms": times[times.size() / 2],
		"synchronous_refresh_p95_ms": times[ceili(times.size() * .95) - 1], "fixture_gateway_reads": gateway.calls.size() - call_start,
		"real_http_requests": root.get_node("GameServices").diagnostics().size()})

func _run() -> void:
	for argument in OS.get_cmdline_user_args():
		if argument.begins_with("--baseline-output="): output_dir = argument.trim_prefix("--baseline-output=")
	if output_dir.is_empty():
		push_error("Pass --baseline-output=<absolute directory>."); quit(1); return
	check(DirAccess.make_dir_recursive_absolute(output_dir) == OK, "output directory ready")
	var prefs = root.get_node("Preferences")
	var saved_before := FileAccess.get_file_as_string(prefs.SAVE_PATH) if FileAccess.file_exists(prefs.SAVE_PATH) else ""
	prefs.text_scale = 1.0; prefs.high_contrast = false; prefs.reduced_motion = true
	prefs.fullscreen = false; prefs.hand_layout_mode = "adaptive"
	root.get_node("I18n").set_locale("en")
	session = root.get_node("GameSession")
	gateway = FixtureGateway.new()
	session.configure(gateway)
	session.available = false # Avoid menu resume resolution and any persisted profile changes.
	router = load("res://main.tscn").instantiate()
	root.add_child(router)
	for resolution in [Vector2i(1280, 720), Vector2i(2560, 1080)]:
		prefs.set_resolution(resolution, false) # Use the same 1440x900 expand/stretch policy as the real game.
		root.size = resolution # Capture exact canvas dimensions, not the desktop's safe-window fit.
		check(root.size == resolution and root.content_scale_size == Vector2i(1440, 900), "exact capture size with production stretch policy")
		router.back_to_menu()
		await capture("menu")
		load_combat()
		router.show_combat()
		var combat = router.host.get_child(0)
		await capture("combat-hand")
		var hand_top: float = combat.hand.scroll.global_position.y + combat.hand.visual_style.padding
		check(combat.target_buttons.values().all(func(button): return button.get_parent().get_global_rect().end.y <= hand_top), "actor information remains above hand")
		combat._choose_card("baseline-0")
		combat._show_target_preview("wisp")
		await capture("combat-preview")
		combat._cancel_selection()
		combat._toggle_character_sidebar()
		await capture("combat-sidebar")
		router.toggle_pause()
		await capture("pause")
		router.toggle_pause()
		combat._toggle_character_sidebar()
		await measure_refresh(combat, false)
		await measure_refresh(combat, true)
		session._card_zones.zones[0].cards = []; session._card_zones.zones[0].count = 0
		combat.refresh_state(router.presentation)
		await capture("combat-empty")
		load_combat()
		load_journey(false)
		router.show_activity()
		await capture("journey")
		await measure_refresh(router.host.get_child(0), false)
		await measure_refresh(router.host.get_child(0), true)
		load_journey(true)
		router.show_activity()
		await capture("offer")
		router.show_settings()
		await capture("settings")
		gateway.run = session.run
		router.show_run_replay("visual-baseline")
		await capture("replay")
		check(router.host.get_child(0).entries.size() == 1, "replay renders synthetic recording")
	check(root.get_node("GameServices").diagnostics().is_empty(), "no real HTTP requests")
	check(gateway.calls.all(func(call): return call in ["inspect_hand", "run_timeline", "run_commit"]), "only passive fixture reads")
	check((FileAccess.get_file_as_string(prefs.SAVE_PATH) if FileAccess.file_exists(prefs.SAVE_PATH) else "") == saved_before, "preferences untouched")
	var report := {"fixture": true, "engine_version": Engine.get_version_info().string, "display_driver": DisplayServer.get_name(),
		"locale": "en", "text_scale": 1.0, "reduced_motion": true, "captured_at_utc": Time.get_datetime_string_from_system(true),
		"captures": captures, "measurements": samples, "gateway_reads": gateway.calls, "failures": failures,
		"measurement_scope": "Synchronous refresh only; excludes network, rendering and subsequent deferred layout. Synthetic gateway reads are not REST requests."}
	var file := FileAccess.open(output_dir.path_join("metrics.json"), FileAccess.WRITE)
	check(file != null, "metrics file writable")
	if file:
		file.store_string(JSON.stringify(report, "\t") + "\n"); file.close()
	router.queue_free()
	await settle()
	root.get_node("GameAudio").shutdown()
	print("VISUAL BASELINE: ", failures, " failure(s)")
	quit(0 if failures == 0 else 1)
