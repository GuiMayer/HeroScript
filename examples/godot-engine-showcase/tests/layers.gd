extends SceneTree
## Offline contract tests: no running engine, scenes, controls or audio required.

const Session = preload("res://scripts/application/game_session.gd")
const Gateway = preload("res://scripts/engine/engine_gateway.gd")
const Presentation = preload("res://scripts/presentation/playback.gd")
const Choices = preload("res://scripts/application/activity_choices.gd")
var failures := 0

class FakeTransport extends RefCounted:
	signal release_read
	var hold_read := false
	var timeout_once := false
	var receipts := {}
	var calls: Array = []
	var fail_refresh := false
	var fail_after_commit := false
	var state := {"runId": "test-run", "sequence": 1, "step": 3, "activeEncounterId": "test-combat"}
	var snapshot := {"combatId": "test-combat", "step": 3, "status": "ACTIVE", "activeActorId": "player"}
	var legal := {"source": "Card", "command": {"runId": "test-run", "actorId": "player", "actionType": "PLAY_CARD", "cardInstanceId": "card-1", "costOptionId": "mana", "targetIds": ["enemy"], "internalOnly": "excluded"}}

	func request(method: int, path: String, body = null) -> Dictionary:
		calls.append({"method": method, "path": path, "body": body.duplicate(true) if body is Dictionary else body})
		if path.ends_with("/health/ready"):
			return _ok({})
		if path.contains("/content/revisions"):
			return _ok({"currentRevision": "revision-1"})
		if path.ends_with("/commands"):
			if receipts.has(body.commandId):
				return _ok(receipts[body.commandId])
			state.sequence += 1
			snapshot.step += 1
			state.step = snapshot.step
			fail_refresh = fail_after_commit
			var receipt := {"sequence": state.sequence, "state": {"combat": snapshot.duplicate(true), "resolution": {"frames": [{"transitionType": "effect", "applications": []}]}}}
			receipts[body.commandId] = receipt
			if timeout_once:
				timeout_once = false
				return {"ok": false, "status": 0, "error": "Timed out after commit"}
			return _ok(receipt)
		if path.ends_with("/available-commands"):
			if fail_refresh:
				return {"ok": false, "status": 503, "error": "Offline fixture"}
			return _ok({"commands": []})
		if path.contains("/legal-actions?"):
			return _ok({"candidates": [legal]})
		if path.contains("/combats/"):
			return _ok(snapshot)
		if hold_read:
			await release_read
		return _ok(state)

	func request_many(paths: Dictionary) -> Dictionary:
		var results := {}
		for key in paths:
			results[key] = await request(HTTPClient.METHOD_GET, paths[key])
		return results

	func _ok(data: Dictionary) -> Dictionary:
		return {"ok": true, "status": 200, "data": data}

func _init() -> void:
	call_deferred("_run")

func check(value: bool, label: String) -> void:
	print("[PASS] " if value else "[FAIL] ", label)
	if not value:
		failures += 1

func _run() -> void:
	await create_timer(.15).timeout
	var i18n = root.get_node("I18n")
	var previous_locale: String = i18n.locale
	i18n.set_locale("unsupported")
	check(i18n.locale == "en" and i18n.text("NEW JOURNEY") == "NEW JOURNEY", "unsupported locale falls back to English")
	var english_keys := true
	for key in i18n.catalogs.en:
		english_keys = english_keys and key == i18n.catalogs.en[key]
	check(english_keys, "every source key is the canonical English message")
	i18n.set_locale("pt_BR")
	check(i18n.text("NEW JOURNEY") == "NOVA JORNADA", "English key resolves Portuguese")
	var saved: String = i18n.catalogs.pt_BR["NEW JOURNEY"]
	i18n.catalogs.pt_BR.erase("NEW JOURNEY")
	check(i18n.text("NEW JOURNEY") == "NEW JOURNEY", "missing Portuguese message uses English")
	i18n.catalogs.pt_BR["NEW JOURNEY"] = saved
	var saved_catalog: Dictionary = i18n.catalogs.pt_BR
	i18n.catalogs.erase("pt_BR")
	check(i18n.text("NEW JOURNEY") == "NEW JOURNEY", "missing locale catalog uses English")
	i18n.catalogs.pt_BR = saved_catalog
	check(i18n._read_catalog("res://data/locales/missing-test.json").is_empty(), "absent catalog is safe to load")
	i18n.content_names["test-fallback"] = {"en": "English content"}
	check(i18n.content_name("test-fallback", "Nome em português") == "English content", "missing content translation prefers English")
	i18n.content_names.erase("test-fallback")
	check(i18n.text("Uncatalogued English message") == "Uncatalogued English message", "unknown key remains English source text")
	check(i18n.error({"errorKey": "Could not start the request (%s).", "errorArgs": [7]}).contains("7"), "error formatting occurs in presentation")
	i18n.set_locale(previous_locale)

	var transport := FakeTransport.new()
	var session = Session.new()
	root.add_child(session)
	check(session is Node, "application session loads without interface scenes")
	session.configure(Gateway.new(transport))
	check(await session.start_campaign(42), "session can run against injected transport without a UI")
	var copy: Dictionary = session.run
	copy.sequence = 999
	check(session.run.sequence == 1, "interface snapshot mutations do not change session state")
	var actions: Array = session.legal_actions
	actions[0].command.targetIds[0] = "tampered"
	check(session.legal_actions[0].command.targetIds[0] == "enemy", "nested choices are defensive copies")
	var playback = Presentation.new()
	root.add_child(playback)
	session.receipt_received.connect(playback.load_receipt)
	check(await session.submit_candidate(session.legal_actions[0]), "semantic card choice reaches gateway")
	var posts: Array = transport.calls.filter(func(call): return str(call.path).ends_with("/commands"))
	var envelope: Dictionary = posts[0].body
	check(envelope.expectedSequence == 1 and envelope.expectedStep == 3, "gateway preserves optimistic concurrency")
	check(str(envelope.commandId).length() == 36 and envelope.type == "PLAY_CARD", "gateway owns envelope and command identity")
	check(envelope.payload.costOptionId == "mana" and envelope.payload.targetIds == ["enemy"] and not envelope.payload.has("internalOnly"), "gateway preserves chosen cost and targets, excludes projection fields")
	check(playback.has_frames() and playback.index == 0, "receipt feeds independent presentation cursor")
	var before := transport.calls.size()
	playback.advance()
	check(transport.calls.size() == before and session.run.sequence == 2, "animation cannot submit commands or mutate run")
	transport.fail_after_commit = true
	check(await session.submit_candidate(session.legal_actions[0]), "successful commit stays successful when refresh fails")
	check(not session.synchronized and session.legal_actions.is_empty(), "failed refresh blocks stale input")
	before = transport.calls.size()
	check(not await session.submit_candidate(transport.legal) and transport.calls.size() == before, "blocked input is not resent")
	transport.fail_refresh = false
	check(await session.refresh(), "session recovers through explicit refresh")
	transport.fail_after_commit = false
	transport.timeout_once = true
	check(not await session.submit_candidate(session.legal_actions[0]) and session.has_pending_command, "uncertain response preserves pending command")
	var committed_sequence: int = transport.state.sequence
	check(not await session.start_campaign(43), "pending command blocks changing runs")
	check(await session.recover_pending() and transport.state.sequence == committed_sequence, "retry returns original receipt without applying twice")
	posts = transport.calls.filter(func(call): return str(call.path).ends_with("/commands"))
	check(posts[-1].body == posts[-2].body, "recovery resends the exact envelope and identity")
	transport.hold_read = true
	session.refresh()
	check(session.busy and not await session.start_campaign(44), "in-flight refresh excludes concurrent run creation")
	session.invalidate()
	transport.release_read.emit()
	check(session.run.is_empty() and not session.synchronized and not session.busy, "late response cannot republish invalidated session")
	var choices := Choices.build({}, [{"type": "UPGRADE_CARD", "validPayload": {"upgradeIds": ["a", "b"], "cardInstanceIds": ["x"]}}])
	check(choices.size() == 2, "progression maps all advertised upgrade choices")
	check(Choices.build({}, [{"type": "UPGRADE_CARD", "validPayload": {"cardInstanceIds": ["x"]}}]).is_empty(), "progression does not invent a missing upgrade ID")
	_check_boundaries("res://scripts/engine", ["I18n", "Preferences", "AppTheme", "Playback", "GameSession", "GameAudio"])
	var Presenter = preload("res://scripts/presentation/combat_presenter.gd")
	var actor_fixture := {"actors": [{"instanceId": "one"}, {"instanceId": "ally"}, {"instanceId": "neutral"}]}
	var presenter = Presenter.new({}, actor_fixture, [transport.legal], i18n)
	actor_fixture.actors.clear()
	check(presenter.actors().size() == 3, "presenter freezes its input and retains every actor")
	var projected: Array = presenter._candidates("card-1")
	projected[0].command.targetIds.clear()
	check(presenter._targets(presenter._candidates("card-1")[0]) == ["enemy"], "indexed candidates cannot be mutated by views")
	check(presenter.input_state(false, true, true, false, "card-1") == Presenter.InputState.ANIMATING, "presentation cursor overrides selection state")
	check(presenter.input_state(false, true, false, true, "") == Presenter.InputState.PAUSED, "pause blocks every presentation input state")
	_check_boundaries("res://scripts/presentation", ["HTTPRequest", "HTTPClient", "GameSession", "/api/v1/"])
	var prefs = root.get_node("Preferences")
	var bindings := {}
	for action in prefs.ACTIONS: bindings[action] = InputMap.action_get_events(action).duplicate()
	prefs.reset_bindings(false)
	var binding := InputEventKey.new()
	binding.physical_keycode = KEY_T
	check(not prefs.remap_event("end_turn", binding, false).is_empty(), "duplicate shortcuts are rejected")
	binding.physical_keycode = KEY_ENTER
	check(not prefs.remap_event("end_turn", binding, false).is_empty(), "UI navigation keys remain reserved")
	binding.physical_keycode = KEY_G
	check(prefs.remap_event("end_turn", binding, false).is_empty() and prefs.action_key("end_turn") == KEY_G, "keyboard binding can be remapped")
	var pad := InputEventJoypadButton.new()
	pad.button_index = JOY_BUTTON_X
	check(prefs.remap_event("end_turn", pad, false).is_empty() and prefs.action_key("end_turn") == KEY_G, "controller binding does not erase keyboard binding")
	pad.button_index = JOY_BUTTON_A
	check(not prefs.remap_event("end_turn", pad, false).is_empty(), "controller confirm remains reserved for UI")
	prefs.reset_bindings(false)
	check(prefs.action_key("end_turn") == KEY_E and prefs.action_button("end_turn") == JOY_BUTTON_Y, "reset restores both input device maps")
	for action in bindings:
		InputMap.action_erase_events(action)
		for event in bindings[action]: InputMap.action_add_event(action, event)
	_check_boundaries("res://scripts/application", ["I18n", "Preferences", "AppTheme", "Playback", "HTTPClient", "HTTPRequest", "/api/v1/"])
	_check_boundaries("res://scripts/ui", ["HTTPClient", "HTTPRequest", "/api/v1/", "HeroAPI", "_command_payload"], true)
	playback.queue_free()
	session.queue_free()
	root.get_node("GameAudio").shutdown()
	print("SHOWCASE_LAYER_TESTS failures=", failures)
	create_timer(.15).timeout.connect(func(): process_frame.connect(
		func(): quit(0 if failures == 0 else 1), CONNECT_ONE_SHOT), CONNECT_ONE_SHOT)

func _check_boundaries(path: String, forbidden: Array, ui_only := false) -> void:
	for filename in DirAccess.get_files_at(path):
		if not filename.ends_with(".gd") or (ui_only and filename == "bootstrap.gd"):
			continue
		var source := FileAccess.get_file_as_string(path.path_join(filename))
		for token in forbidden:
			if source.contains(token):
				check(false, "forbidden dependency %s in %s" % [token, filename])
