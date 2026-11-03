class_name SessionReplayService
extends RefCounted
## History, resume bookmark resolution, timeline, and replay read operations for GameSession.

static func tool_denied(capability: String) -> Dictionary:
	return {
		"ok": false,
		"status": 403,
		"errorKey": "This tool is not available in the current mode.",
		"error": "Tool capability denied: %s" % capability,
	}


static func find_resume(
	gateway,
	index,
	api: String,
	legacy_id: String,
	setting: String,
	launch: Dictionary,
	generation: int,
	state_checker: Callable
) -> Dictionary:
	var player := str(launch.get("playerEntityId", "player"))
	var mode := str(launch.get("modeId", ""))
	var ids: Array[String] = []
	var bookmarked: String = index.candidate(api, player, setting, mode)
	if bookmarked.is_empty() and index.has_scope(api, player, setting, mode):
		return {"ok": true, "runId": "", "legacyChecked": false}
	if not bookmarked.is_empty():
		ids.append(bookmarked)
	if not legacy_id.is_empty() and legacy_id not in ids:
		ids.append(legacy_id)
	var legacy_checked := false
	for id in ids:
		var response: Dictionary = await gateway.read_run(id)
		if not state_checker.call(generation, setting):
			return {"ok": false, "errorKey": "The selected setting changed. Try again."}
		if not response.ok:
			if int(response.get("status", 0)) != 404:
				return response
			index.forget(api, player, setting, mode, id)
			if index.has_scope(api, player, setting, mode):
				return {"ok": true, "runId": "", "legacyChecked": legacy_checked}
			continue
		var record: Dictionary = response.data
		if str(record.get("runId", "")) != id:
			return {"ok": false, "errorKey": "This journey does not match the selected setting, player or mode, or is no longer active."}
		if id == legacy_id:
			legacy_checked = true
		index.remember(api, record)
		if str(record.get("settingId", record.get("configName", ""))) == setting \
			and str(record.get("playerEntityId", "")) == player \
			and str(record.get("modeId", "")) == mode \
			and str(record.get("lifecycle", "")).to_lower() == "active":
			return {"ok": true, "runId": id, "legacyChecked": legacy_checked}
		index.forget(api, player, setting, mode, id)
		if index.has_scope(api, player, setting, mode) and index.candidate(api, player, setting, mode).is_empty():
			return {"ok": true, "runId": "", "legacyChecked": legacy_checked}
	var profile: Dictionary = await gateway.run_history(player, setting)
	if not state_checker.call(generation, setting):
		return {"ok": false, "errorKey": "The selected setting changed. Try again."}
	if not profile.ok:
		return profile
	for record in profile.data.get("items", []):
		if str(record.get("settingId", record.get("configName", ""))) != setting \
			or str(record.get("modeId", "")) != mode \
			or str(record.get("lifecycle", "")).to_lower() != "active":
			continue
		var metadata: Dictionary = record.duplicate(true)
		metadata["playerEntityId"] = player
		index.remember(api, metadata)
		return {"ok": true, "runId": str(record.get("runId", "")), "legacyChecked": legacy_checked}
	return {"ok": true, "runId": "", "legacyChecked": legacy_checked}
