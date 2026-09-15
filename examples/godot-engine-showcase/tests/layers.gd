extends SceneTree
## Offline contract tests: no running engine, scenes, controls or audio required.

const Session = preload("res://scripts/application/game_session.gd")
const Gateway = preload("res://scripts/engine/engine_gateway.gd")
const Presentation = preload("res://scripts/presentation/playback.gd")
const Choices = preload("res://scripts/application/activity_choices.gd")
const CardZones = preload("res://scripts/presentation/card_zone_presenter.gd")
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

	func request(method: int, path: String, body = null, _timeout_seconds := 12.0) -> Dictionary:
		calls.append({"method": method, "path": path, "body": body.duplicate(true) if body is Dictionary else body})
		if path.ends_with("/health/ready"):
			return _ok({})
		if path.contains("/content/revisions"):
			return _ok({"currentRevision": "revision-1"})
		if path.contains("/profiles/player"):
			return _ok({"playerId": "player", "totalRuns": 1, "completedRuns": 1, "activeRuns": 0,
				"runs": [{"runId": "archived-run", "sequence": 2, "lifecycle": "Completed", "seed": 42}]})
		if path.contains("/runs/archived-run/timeline"):
			return _ok({"items": [{"sequence": 1, "commandType": "START_RUN", "frames": [{}]}], "nextCursor": 1})
		if path.contains("/runs/archived-run/commits/1"):
			return _ok({"sequence": 1, "stateAfter": {"runId": "archived-run", "lifecycle": "Active"}})
		if path.contains("/runs/archived-run/verify"):
			return _ok({"isValid": true})
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
		if path.ends_with("/capabilities"):
			return _ok({"profile": "dev_modder", "granted": [
				"timeline.read", "timeline.inspect_state", "replay.verify",
				"timeline.branch.read", "timeline.branch.create", "simulation.run"
			]})
		if path.ends_with("/card-zones"):
			return _ok({"topologyHash": "zone-test-hash", "zones": [
				{"zoneId": "prepared", "presentation": {"slot": "playable_cards", "labelKey": "Hand"},
					"count": 1, "contentsVisible": true, "cards": [{"cardInstanceId": "card-1", "definitionId": "strike"}]},
				{"zoneId": "cooldown", "presentation": {"slot": "cooldown_track", "labelKey": "Cooldown"},
					"count": 2, "contentsVisible": false, "cards": []}
			]})
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
	var zone_view = CardZones.new(session.card_zones, i18n)
	check(zone_view.playable_cards().size() == 1 and str(zone_view.playable_cards()[0].definitionId) == "strike",
		"playable cards come from authored presentation slots, not fixed pile names")
	check(zone_view.auxiliary_zones().size() == 1 and zone_view.zone_cards("cooldown").is_empty(),
		"hidden auxiliary zones expose counts without contents")
	var tool_flow := {"flowId": "tool.transfer", "allowedInvocations": ["Tool"],
		"steps": [{"operation": "Move", "sourceZoneId": "prepared", "targetZoneId": "cooldown",
			"selection": {"strategy": "Explicit"}}]}
	var tool_run := {"resolvedMode": {"capabilityPolicy": {"allowCardZoneCheats": true},
		"cardZoneSystem": {"flows": [tool_flow, {"flowId": "boundary.initial", "allowedInvocations": ["Boundary"]}]}}}
	check(CardZones.tool_flows(tool_run).size() == 1 and CardZones.tool_flows(tool_run)[0].flowId == "tool.transfer",
		"zone tools are discovered from revisioned graph invocations")
	var tool_payload: Dictionary = CardZones.tool_payload(tool_flow, "strike, defend, strike", "id-1, id-2", "player")
	check(tool_payload == {"flowId": "tool.transfer", "cardDefinitionIds": ["strike", "defend"],
		"cardInstanceIds": ["id-1", "id-2"], "actorId": "player"},
		"zone tool form sends explicit identifiers without inferring zone purpose")
	tool_run.resolvedMode.capabilityPolicy.allowCardZoneCheats = false
	check(CardZones.tool_flows(tool_run).is_empty(), "normal mode does not display sandbox zone tools")
	var zone_copy: Dictionary = session.card_zones
	zone_copy.topologyHash = "tampered"
	check(session.card_zones.topologyHash == "zone-test-hash", "zone projection is a defensive copy")
	check(session.allows_tool("timeline.branch.create") and session.tool_profile == "dev_modder", "session consumes engine-issued tool capabilities")
	var guarded_calls := transport.calls.size()
	session._tool_capabilities.erase("simulation.run")
	check(not (await session.simulate([])).ok and transport.calls.size() == guarded_calls, "denied tools never reach the transport")
	session._tool_capabilities["simulation.run"] = true
	var copy: Dictionary = session.run
	copy.sequence = 999
	check(session.run.sequence == 1, "interface snapshot mutations do not change session state")
	var live_before_history := JSON.stringify(session.run)
	var history_result: Dictionary = await session.run_history()
	check(history_result.ok and history_result.data.items.size() == 1 and history_result.data.items[0].runId == "archived-run", "profile projection supplies immutable journey history")
	var replay_page: Dictionary = await session.replay_timeline("archived-run")
	var replay_commit: Dictionary = await session.replay_commit("archived-run", 1)
	var replay_check: Dictionary = await session.verify_run("archived-run")
	check(replay_page.ok and replay_commit.ok and replay_check.data.isValid, "arbitrary archived run exposes timeline, commits and verification")
	check(JSON.stringify(session.run) == live_before_history, "history and replay reads do not activate or mutate a run")
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
	var forced := Choices.only_forced_advance([
		{"type": "ABANDON_RUN", "payload": {}}, {"type": "ADVANCE_NODE", "payload": {"targetNodeId": "next"}}])
	check(forced.get("payload", {}).get("targetNodeId") == "next", "single forward route can advance automatically")
	check(Choices.only_forced_advance([{"type": "ADVANCE_NODE"}, {"type": "ADVANCE_NODE"}]).is_empty(), "route branches always require player choice")
	check(Choices.only_forced_advance([{"type": "ADVANCE_NODE"}, {"type": "RESOLVE_NODE"}]).is_empty(), "gameplay decisions prevent automatic travel")
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
	for field in [0.0, 1.0, 2.0, "Current", "Minimum", "Maximum"]:
		var label: String = presenter.application_text({"previousValue": 1, "currentValue": 2, "resourceId": "custom", "resourceField": field})
		check(not label.is_empty(), "resource field presentation accepts numeric and named values: " + str(field))
	_check_boundaries("res://scripts/presentation", ["HTTPRequest", "HTTPClient", "GameSession", "/api/v1/"])
	var prefs = root.get_node("Preferences")
	var Timeline = preload("res://scripts/presentation/timeline_presenter.gd")
	var timeline_model = Timeline.new()
	timeline_model.append_page({"items": [{"runSequence": 3}], "nextCursor": 3})
	timeline_model.append_page({"items": [{"runSequence": 3}, {"runSequence": 7}], "nextCursor": 7})
	check(timeline_model.entries().size() == 2 and timeline_model.next_cursor == 7, "timeline paging preserves sparse sequences without duplicates")
	var lineage: Array = Timeline.lineage({"runId": "origin", "children": [{"runId": "child", "parentRunId": "origin", "children": [{"runId": "leaf", "parentRunId": "child"}]}]})
	check(lineage[2].depth == 2 and lineage[2].parentRunId == "child", "nested branches retain hierarchy instead of flattening")
	var raw_history := {"run": {"deck": {"cardInstances": {"one": {"cardInstanceId": "one"}}}}, "combat": {"actors": {"actor": {"instanceId": "actor", "components": {"pool": {"type": "resources", "state": {"resources": {"custom": {"current": 8}}}}}}}, "actorOrder": ["actor"]}}
	var history := Gateway.normalize_history(raw_history)
	check(history.combat.actors[0].resources.custom.current == 8 and raw_history.combat.actors is Dictionary, "historical DTO adapter preserves generic resources and original aggregate")
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
