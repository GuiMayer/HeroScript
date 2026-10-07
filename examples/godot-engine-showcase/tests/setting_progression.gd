extends SceneTree
## Offline scope/restore/UI regressions. Does not write user preferences or real runs.
const Session = preload("res://scripts/application/game_session.gd")
const Gateway = preload("res://scripts/engine/engine_gateway.gd")
const ResumeIndex = preload("res://scripts/application/run_resume_index.gd")
var failures := 0

class Transport extends RefCounted:
	signal release_read
	var hold_id := ""
	var fail_projections := false
	var calls: Array = []
	var records := {
		"a-story": {"runId": "a-story", "settingId": "a", "configName": "a", "playerEntityId": "player", "modeId": "story", "lifecycle": "Active", "sequence": 3, "contentRevision": "revision-a", "activeEncounterId": "combat-a"},
		"b-story": {"runId": "b-story", "settingId": "b", "configName": "b", "playerEntityId": "player", "modeId": "story", "lifecycle": "Active", "sequence": 1, "contentRevision": "revision-b", "activeEncounterId": "combat-b"},
		"a-lab": {"runId": "a-lab", "settingId": "a", "configName": "a", "playerEntityId": "player", "modeId": "lab", "lifecycle": "Active", "sequence": 9, "contentRevision": "revision-a", "activeEncounterId": "combat-lab"},
		"a-done": {"runId": "a-done", "settingId": "a", "configName": "a", "playerEntityId": "player", "modeId": "story", "lifecycle": "Completed", "sequence": 99, "contentRevision": "revision-a"},
		"a-other-player": {"runId": "a-other-player", "settingId": "a", "configName": "a", "playerEntityId": "other", "modeId": "story", "lifecycle": "Active", "sequence": 200, "contentRevision": "revision-a"}}
	func ok(data) -> Dictionary: return {"ok": true, "status": 200, "data": data.duplicate(true)}
	func request(_method: int, path: String, _body = null, _timeout := 12.0) -> Dictionary:
		calls.append(path)
		if path.ends_with("/health/ready"): return ok({})
		if path.ends_with("/content/settings"):
			var items: Array = []
			for setting in ["a", "b", "empty"]:
				items.append({"settingId": setting, "displayName": "Setting " + setting, "currentRevision": "revision-" + setting,
					"launch": {"modeId": "story", "playerEntityId": "player", "runDefinitionId": "story"}})
			return ok({"items": items})
		if path.contains("/profiles/"):
			var setting: String = path.get_slice("?settingId=", 1)
			var entries: Array = records.values().filter(func(record): return record.settingId == setting and record.playerEntityId == "player")
			entries.sort_custom(func(a, b): return int(a.sequence) > int(b.sequence))
			return ok({"playerId": "player", "settingId": setting, "runs": entries, "revision": "profile-" + setting})
		if path.ends_with("/available-commands"): return ok({"commands": []})
		if path.ends_with("/capabilities"): return ok({"profile": "normal", "granted": []})
		if path.ends_with("/card-zones"): return ok({"zones": []})
		if path.contains("/legal-actions?"): return ok({"candidates": []})
		if path.contains("/combats/"):
			return ok({"combatId": path.get_file(), "status": "ACTIVE", "activeActorId": "player", "actors": [
				{"instanceId": "player", "definitionId": "spire_adept", "resources": {}, "statuses": []}]})
		var id: String = path.get_file()
		if id == hold_id: await release_read
		return ok(records[id]) if records.has(id) else {"ok": false, "status": 404}
	func request_many(paths: Dictionary) -> Dictionary:
		if fail_projections: return {"commands": {"ok": false, "status": 503}}
		var results := {}
		for key in paths: results[key] = await request(HTTPClient.METHOD_GET, paths[key])
		return results

func _init() -> void: call_deferred("_run")
func check(ok: bool, label: String) -> void:
	print("[PASS] " if ok else "[FAIL] ", label)
	if not ok: failures += 1
func settle() -> void:
	for _i in 16: await process_frame

func _run() -> void:
	var transport := Transport.new()
	var index = ResumeIndex.new()
	var api := "http://fixture:1234"
	index.remember(api, transport.records["a-story"])
	index.remember(api, transport.records["b-story"])
	index.remember(api, transport.records["a-lab"])
	check(index.candidate(api, "player", "a", "story") == "a-story" and index.candidate(api, "player", "b", "story") == "b-story", "each setting retains its own continue bookmark")
	check(index.candidate(api, "player", "a", "lab") == "a-lab", "sandbox cannot replace the campaign bookmark")
	check(index.candidate("http://other", "player", "a", "story").is_empty() and index.candidate(api, "other", "a", "story").is_empty(), "server and player namespaces are independent")
	check(index.next_counter(api, "player", "a", "story", 5) == 6 and index.next_counter(api, "player", "b", "story", 5) == 6 and index.next_counter(api, "player", "a", "story", 5) == 7, "campaign seed counters are setting-specific and preserve the legacy floor")
	var config := ConfigFile.new()
	index.write_config(config)
	var disk_roundtrip := ConfigFile.new()
	check(disk_roundtrip.parse(config.encode_to_text()) == OK, "namespaced bookmarks serialize as a valid Godot configuration")
	var restarted = ResumeIndex.new()
	restarted.load_config(disk_roundtrip)
	check(restarted.runs == index.runs and restarted.counters == index.counters, "bookmarks and counters survive a config roundtrip/restart")
	var session = Session.new()
	root.add_child(session)
	session.configure(Gateway.new(transport))
	check(await session.connect_engine("a"), "connect to settings catalog")
	var opened := [0]
	session.run_opened.connect(func(_id): opened[0] += 1)
	check(await session.continue_run("a-story", "story") and session.combat.combatId == "combat-a", "A restores A's recorded combat/revision")
	session.select_setting("b")
	var found: Dictionary = await session.find_resume(restarted, api)
	check(found.ok and found.runId == "b-story" and session.run.is_empty() and session.selected_setting_id == "b", "resolving B never activates A or switches the setting")
	check(await session.continue_run(found.runId, "story") and session.combat.combatId == "combat-b", "B restores a different combat")
	session.select_setting("a")
	found = await session.find_resume(restarted, api)
	check(found.ok and found.runId == "a-story", "switching back to A restores its bookmark")
	check(not await session.continue_run("b-story", "story") and session.run.is_empty() and session.selected_setting_id == "a", "cross-setting Continue is rejected before activating state")
	check(not await session.continue_run("a-lab", "story") and not await session.continue_run("a-other-player", "story"), "main-menu resume rejects other modes and players")
	check(not await session.continue_run("a-done", "story"), "completed runs cannot be continued")
	transport.records["a-done"].lifecycle = "Abandoned"
	check(not await session.continue_run("a-done", "story"), "abandoned runs cannot be continued")
	check(await session.continue_run("a-lab") and session.combat.combatId == "combat-lab", "history may explicitly resume another mode within the same setting")
	var old = ResumeIndex.new()
	session.select_setting("b")
	found = await session.find_resume(old, api, "a-lab")
	check(found.ok and found.runId == "b-story" and found.legacyChecked and old.candidate(api, "player", "a", "lab") == "a-lab", "legacy global bookmark is imported by verified metadata, not applied to B")
	check(session.run.is_empty() and session.selected_setting_id == "b", "legacy lookup does not activate or convert saves")
	session.select_setting("empty")
	found = await session.find_resume(old, api)
	check(found.ok and found.runId.is_empty(), "setting without a campaign has no Continue candidate")
	session.select_setting("a")
	var terminal: Dictionary = transport.records["a-story"].duplicate(true)
	terminal.lifecycle = "Completed"
	index.remember(api, terminal)
	check(index.candidate(api, "player", "a", "story").is_empty() and index.candidate(api, "player", "b", "story") == "b-story", "finishing A clears only A's matching bookmark")
	found = await session.find_resume(index, api)
	check(found.ok and found.runId.is_empty(), "a finished bookmark never resurrects an older unfinished campaign")
	var profile: Dictionary = await session.run_history()
	check(profile.ok and profile.data.items.all(func(record): return record.settingId == "a"), "history uses the selected setting's profile")
	transport.hold_id = "a-story"
	var pending := [null]
	_find_later(session, restarted, api, pending)
	await process_frame
	session.select_setting("b")
	transport.release_read.emit()
	await settle()
	check(pending[0] != null and not pending[0].ok and session.selected_setting_id == "b", "late lookup from A cannot overwrite B's menu")
	transport.hold_id = ""
	transport.fail_projections = true
	var before: int = opened[0]
	check(not await session.continue_run("b-story", "story") and opened[0] == before, "failed projection restore never emits a successful run-open bookmark")
	transport.fail_projections = false
	session.queue_free()
	await _menu_test(transport)
	print("SETTING PROGRESSION: ", failures, " failure(s)")
	root.get_node("GameAudio").shutdown()
	quit(0 if failures == 0 else 1)

func _find_later(session, index, api: String, pending: Array) -> void:
	pending[0] = await session.find_resume(index, api)

func _menu_test(transport) -> void:
	var prefs = root.get_node("Preferences")
	var session = root.get_node("GameSession")
	var bootstrap = root.get_node("GameServices")
	var saved := {"index": prefs.resume_index, "last": prefs.last_run_id, "setting": prefs.selected_setting_id,
		"gateway": session._gateway, "settings": session._settings, "selected": session.selected_setting_id,
		"available": session.available, "run": session._run, "combat": session._combat, "revision": session.content_revision}
	# Pre-populated bookmarks make menu lookup read-only. Disconnect disk persistence
	# for the fixture's explicitly accepted Continue; production callbacks are restored.
	session.run_opened.disconnect(bootstrap._remember_run)
	session.changed.disconnect(bootstrap._remember_progress)
	prefs.resume_index = ResumeIndex.new()
	prefs.last_run_id = ""
	for id in ["a-story", "b-story", "a-lab"]: prefs.resume_index.remember(prefs.api_url, transport.records[id])
	session.configure(Gateway.new(transport))
	session.available = true
	await session.connect_engine("a")
	var router = load("res://main.tscn").instantiate()
	root.add_child(router)
	await settle()
	check(not router.host.find_child("ContinueButton", true, false).disabled and router.continuation_run_id == "a-story", "main menu resolves A's Continue, not the last global run")
	var selector: OptionButton = router.host.find_child("SettingSelector", true, false)
	session._operation = "opening"
	selector.select(1)
	selector.item_selected.emit(1)
	check(str(selector.get_item_metadata(selector.selected)) == "a" and session.selected_setting_id == "a", "a blocked setting switch restores the selector instead of lying about the active setting")
	session._operation = "idle"
	session.select_setting("b")
	router._show_main_menu()
	await settle()
	check(router.continuation_run_id == "b-story", "main-menu Continue changes with the selected setting")
	await router._continue_campaign()
	await settle()
	check(router.current_screen == "combat" and session.combat.combatId == "combat-b", "actual Continue opens B's combat through the canonical session")
	session.select_setting("empty")
	router._show_main_menu()
	await settle()
	check(router.host.find_child("ContinueButton", true, false).disabled and router.continuation_run_id.is_empty(), "actual menu disables Continue for an empty setting")
	router.queue_free()
	await settle()
	prefs.resume_index = saved.index; prefs.last_run_id = saved.last; prefs.selected_setting_id = saved.setting
	session.configure(saved.gateway); session._settings = saved.settings; session.selected_setting_id = saved.selected
	session.available = saved.available; session._run = saved.run; session._combat = saved.combat; session.content_revision = saved.revision
	session.run_opened.connect(bootstrap._remember_run)
	session.changed.connect(bootstrap._remember_progress)
