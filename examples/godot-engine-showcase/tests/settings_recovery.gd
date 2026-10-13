extends SceneTree
## Recovery, sound and connection regressions. Never uses real player preferences or engine commands.
var failures := 0
class ReplayGateway extends RefCounted:
	signal connect_released
	var session
	var connects := 0
	func connect_engine() -> Dictionary:
		connects += 1
		await connect_released
		session.set_available(true)
		return {"ok": true, "data": {"items": [{"settingId": "default", "currentRevision": "fixture"}]}}
	func run_history(_player: String, _setting: String) -> Dictionary:
		return {"ok": true, "data": {"items": []}}
	func run_timeline(_id: String, _cursor: int, _limit: int, _through := 0) -> Dictionary:
		return {"ok": true, "data": {"items": [{"sequence": 1, "stateHash": "fixture"}, {"sequence": 2, "stateHash": "fixture"}, {"sequence": 3, "stateHash": "fixture"}], "nextCursor": 3, "throughSequence": 3, "hasMore": false}}
	func replay_context() -> Array: return ["fixture", 1]
	func begin_run_timeline(id: String, cursor: int, limit: int, through: int):
		var ticket = load("res://scripts/engine/read_ticket.gd").new()
		ticket.finish(await run_timeline(id, cursor, limit, through)); return ticket
	func begin_run_presentation(id: String, sequence: int):
		var ticket = load("res://scripts/engine/read_ticket.gd").new()
		ticket.finish({"ok": true, "data": {"runId": id, "sequence": sequence, "stateHash": "fixture", "stateSummary": {"lifecycle": "Completed"}}}); return ticket
	func run_commit(_id: String, _sequence: int) -> Dictionary:
		return {"ok": true, "data": {"stateAfter": {"lifecycle": "Completed"}}}
func _init() -> void: call_deferred("_run")
func check(ok: bool, label: String) -> void:
	print("[PASS] " if ok else "[FAIL] ", label)
	if not ok: failures += 1
func settle() -> void:
	for _frame in 8: await process_frame
func valid_bindings(prefs) -> bool:
	var keys: Array = []; var buttons: Array = []
	for action in prefs.ACTIONS:
		var key: int = prefs.action_key(action); var button: int = prefs.action_button(action)
		if not prefs._valid_key(key) or key in keys or not prefs._valid_button(button) or button in buttons: return false
		keys.append(key); buttons.append(button)
	return true
func _run() -> void:
	if not OS.get_user_data_dir().contains("HeroScript-ui-refactor-"):
		check(false, "recovery test requires an isolated preference directory"); quit(1); return
	var prefs = root.get_node("Preferences")
	var session = root.get_node("GameSession")
	var audio = root.get_node("GameAudio")
	var services = root.get_node("GameServices")
	var original := ConfigFile.new()
	if original.load(prefs.SAVE_PATH) != OK: prefs.save(false); original.load(prefs.SAVE_PATH)
	var saved := {"gateway": session._gateway, "url": prefs.api_url, "saved_url": prefs._saved_api_url, "override": prefs._api_override}
	var requests: int = services.diagnostics().size()
	var malformed := ConfigFile.new()
	malformed.set_value("game", "locale", "unsupported")
	malformed.set_value("game", "animation_speed", 999.)
	malformed.set_value("audio", "music", 9.)
	malformed.set_value("audio", "sfx", -1.)
	malformed.set_value("audio", "master", "invalid")
	malformed.set_value("game", "reduced_motion", "false")
	malformed.set_value("video", "width", "invalid")
	malformed.set_value("network", "api_url", "http://?invalid")
	malformed.set_value("input", "pause_game", KEY_NONE)
	malformed.set_value("input", "end_turn", KEY_Q)
	malformed.set_value("input", "open_timeline", KEY_Q)
	malformed.set_value("input", "inspect_card", KEY_F1)
	malformed.set_value("gamepad", "pause_game", -1)
	malformed.set_value("gamepad", "end_turn", JOY_BUTTON_A)
	malformed.set_value("gamepad", "inspect_card", JOY_BUTTON_PADDLE1)
	malformed.save(prefs.SAVE_PATH); prefs.load_settings()
	check(prefs.locale == "en", "unsupported saved language becomes canonical English")
	check(prefs.animation_speed == 2. and prefs.music_volume == 1. and prefs.sfx_volume == 0. and prefs.master_volume == .8, "loading clamps numeric options and rejects invalid types")
	check(not prefs.reduced_motion and prefs.window_resolution == prefs.DEFAULT_RESOLUTION and prefs.api_url == prefs.DEFAULT_API_URL, "invalid boolean/resolution/URL use safe defaults")
	check(valid_bindings(prefs), "empty, conflicting and reserved saved bindings recover to unique valid controls")
	check(prefs.action_key("end_turn") == KEY_Q and prefs.action_key("inspect_card") == KEY_F1 and prefs.action_button("inspect_card") == JOY_BUTTON_PADDLE1, "recovery preserves valid custom keyboard and gamepad mappings")
	# A custom key can occupy another action's default: replacement must not steal it.
	malformed.set_value("input", "pause_game", KEY_E); malformed.set_value("input", "end_turn", KEY_NONE)
	malformed.save(prefs.SAVE_PATH); prefs.load_settings()
	check(valid_bindings(prefs) and prefs.action_key("pause_game") == KEY_E, "recovery reserves all valid custom bindings before choosing defaults")
	malformed.set_value("audio", "music", NAN)
	check(prefs._read_option(malformed, "audio", "music", "music_volume", .42) == .42, "non-finite saved number uses finite fallback")
	# Simulate a malformed legacy InputMap already in memory, not just the config file.
	InputMap.action_erase_events("pause_game")
	InputMap.action_add_event("pause_game", InputEventKey.new())
	prefs.reset_bindings(false)
	check(valid_bindings(prefs) and prefs.action_key("pause_game") == KEY_ESCAPE, "Reset Controls removes even unmatchable empty key events")
	for action in prefs.ACTIONS:
		check(prefs.action_key(action) == prefs.DEFAULT_KEYS[action] and prefs.action_button(action) == prefs.DEFAULT_BUTTONS[action] and InputMap.action_get_events(action).size() == 2, "reset restores exactly one keyboard/gamepad event: " + action)
	for button in [-1, JOY_BUTTON_MAX, JOY_BUTTON_MAX + 1]:
		var event := InputEventJoypadButton.new(); event.button_index = button
		check(not prefs.remap_event("pause_game", event, false).is_empty() and valid_bindings(prefs), "out-of-range gamepad button cannot change controls: " + str(button))
	prefs.save(false); prefs.load_settings()
	check(valid_bindings(prefs) and prefs.locale == "en", "recovered preferences persist through a second load")
	for invalid in ["", "http://", "http://?invalid", "https://#fragment", "http:///path", "ftp://localhost", "http://localhost:0", "http://localhost:65536", "http://localhost:abc", "http://user:pass@localhost", "http://bad..host", "http://-host", "http://localhost/path?query", "http://localhost/\\bad", "http://localhost/\nheader", "http://[bad]", "http://[127.0.0.1]", "http://999.999.999.999"]:
		check(prefs.normalize_api_url(invalid).is_empty(), "invalid endpoint rejected: " + invalid.replace("\n", "\\n"))
	for pair in [[" HTTP://LOCALHOST:5271/ ", "http://localhost:5271"], ["https://example.test/api///", "https://example.test/api"], ["http://[::1]:5271/", "http://[::1]:5271"], ["http://127.0.0.1:65535", "http://127.0.0.1:65535"]]:
		check(prefs.normalize_api_url(pair[0]) == pair[1] and prefs.normalize_api_url(pair[1]) == pair[1], "valid endpoint normalized idempotently: " + pair[0])
	# Apply the reloaded preferences before testing a change from that endpoint.
	prefs.save()
	var controller = load("res://scripts/ui/controllers/settings_controller.gd").new(prefs, root.get_node("I18n"), session)
	var before: String = prefs.api_url
	check(not controller.save_connection("http://?invalid").is_empty() and prefs.api_url == before, "invalid connection never reaches preferences or transport")
	session._operation = "connecting"
	check(not controller.save_connection("http://127.0.0.1:59998").is_empty() and prefs.api_url == before, "busy operation still blocks endpoint changes")
	session._operation = "idle"; session._pending = {"test": true}
	check(not controller.save_connection("http://127.0.0.1:59998").is_empty(), "pending uncertain command still blocks endpoint changes")
	session._pending = {}; session.available = true
	services._transport._set_available(true)
	session._card_zones = {"zones": [{"zoneId": "old-server"}]}
	check(controller.save_connection("http://127.0.0.1:59998").is_empty() and not session.available and not services._transport.available and session.card_zones.is_empty() and session.settings.is_empty() and services._transport.base_url == prefs.api_url, "new endpoint clears old content and availability in session and transport")
	for values in [[0., 0., 0.], [.4, .2, .7], [1., 1., 1.], [1., 0., .5]]:
		prefs.master_volume = values[0]; prefs.music_volume = values[1]; prefs.sfx_volume = values[2]; prefs.save()
		check(AudioServer.is_bus_mute(0) == (values[0] == 0.) and is_equal_approx(db_to_linear(AudioServer.get_bus_volume_db(AudioServer.get_bus_index("Music"))), values[1]) and is_equal_approx(db_to_linear(AudioServer.get_bus_volume_db(AudioServer.get_bus_index("SFX"))), values[2]), "independent music/SFX bus gains reach exact zero: " + str(values))
	check(audio._music.stream == null and not audio._music.playing, "no synthetic drone is retained when no authored music is configured")
	# Actual replay view and its existing read-only gateway/cursor, not a duplicate scheduler.
	var gateway := ReplayGateway.new(); gateway.session = session
	session.configure(gateway); session.available = false
	var router = load("res://main.tscn").instantiate(); root.add_child(router); await settle()
	check(router.host.find_child("EngineStatus", true, false).text == root.get_node("I18n").text("●  ENGINE OFFLINE") and router.host.find_child("ReconnectButton", true, false) != null, "offline menu honestly exposes reconnect after an endpoint change")
	router.host.find_child("ReconnectButton", true, false).pressed.emit()
	await settle()
	check(router.host.find_child("EngineStatus", true, false).text == root.get_node("I18n").text("●  CONNECTING") and router.host.find_child("ReconnectButton", true, false).disabled, "Connecting is displayed only while a real connection attempt is pending")
	gateway.connect_released.emit(); await settle()
	check(gateway.connects == 1 and session.available and session.run.is_empty() and router.host.find_child("EngineStatus", true, false).text == root.get_node("I18n").text("●  ENGINE ONLINE"), "menu reconnect reloads settings without starting a journey")
	router.show_run_replay("recovery-fixture", true); await settle()
	var replay = router.host.get_child(0)
	check(replay.selected_index == 0 and replay.entries.size() == 3, "replay initializes from a read-only timeline fixture")
	for motion in [false, true]:
		prefs.reduced_motion = motion
		for speed in [.25, .5, 1., 2.]:
			prefs.animation_speed = speed
			check(is_equal_approx(replay.playback_delay(), (.18 if motion else .7) / speed), "replay delay honors motion=%s speed=%s" % [motion, speed])
	prefs.reduced_motion = false
	var durations: Array[float] = []
	for speed in [1., 2.]:
		prefs.animation_speed = speed
		await replay._restart()
		var started := Time.get_ticks_msec()
		replay.play_button.pressed.emit()
		while replay.playing and Time.get_ticks_msec() - started < 5000: await process_frame
		durations.append(float(Time.get_ticks_msec() - started))
		check(not replay.playing and replay.selected_index == 2, "real replay finishes at configured speed: " + str(speed))
	check(durations[1] < durations[0] * .85, "2x replay is observably faster without changing recorded state")
	check(requests == services.diagnostics().size(), "recovery tests send no real engine requests or gameplay commands")
	router.queue_free(); await settle()
	session.configure(saved.gateway)
	original.save(prefs.SAVE_PATH); prefs.load_settings()
	prefs.api_url = saved.url; prefs._saved_api_url = saved.saved_url; prefs._api_override = saved.override
	prefs.save(); root.get_node("I18n").set_locale(prefs.locale)
	AppTheme.configure_preferences(prefs.text_scale, prefs.high_contrast)
	audio.shutdown(); await create_timer(.08).timeout
	quit(0 if failures == 0 else 1)
