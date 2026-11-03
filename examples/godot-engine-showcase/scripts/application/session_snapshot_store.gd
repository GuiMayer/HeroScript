class_name SessionSnapshotStore
extends RefCounted
## Validates and normalizes compact engine session bundles and builds private view snapshots.

static func belongs_to_run(snapshot: Dictionary, run: Dictionary) -> bool:
	return str(snapshot.get("runId", "")) == str(run.get("runId", "")) \
		and int(snapshot.get("runSequence", -1)) == int(run.get("sequence", 0)) \
		and str(snapshot.get("contentRevision", "")) == str(run.get("contentRevision", ""))


static func input_actor_id(combat: Dictionary) -> String:
	if combat.is_empty():
		return ""
	var priority = combat.get("priorityWindow")
	if priority is Dictionary and not priority.is_empty():
		return str(priority.get("holderActorId", ""))
	var activation = combat.get("activation", {})
	if activation is Dictionary and bool(activation.get("waitingForInput", false)):
		return str(activation.get("activeActorId", combat.get("activeActorId", "")))
	return str(combat.get("activeActorId", ""))


static func evaluation_context(selected_setting_id: String, run_state: Dictionary, combat_state: Dictionary) -> Array:
	return [
		selected_setting_id,
		run_state.get("runId", ""),
		run_state.get("branchId", ""),
		run_state.get("sequence", -1),
		run_state.get("contentRevision", ""),
		combat_state.get("combatId", ""),
		combat_state.get("step", -1),
		input_actor_id(combat_state),
	]


static func capture_view_snapshot(
	run: Dictionary,
	combat: Dictionary,
	legal_actions: Array,
	card_zones: Dictionary,
	card_evaluations: Array,
	evaluations_ready: bool,
	card_presentations: Dictionary,
	character_attributes: Dictionary
) -> Dictionary:
	var value := {
		"run": run,
		"combat": combat,
		"actions": legal_actions,
		"zones": card_zones,
		"evaluations": card_evaluations if evaluations_ready else [],
		"presentations": card_presentations.get("cards", []) if belongs_to_run(card_presentations, run) else [],
		"attributes": character_attributes if belongs_to_run(character_attributes, run) else {},
	}
	return value.duplicate(true)


static func validate_session_bundle(response: Dictionary, run_id: String, committed_combat: Dictionary = {}, receipt_sequence := -1) -> Dictionary:
	if not response.get("ok", false):
		return {"ok": false, "error": response}
	if not response.get("data") is Dictionary:
		return {"ok": false, "error": {"ok": false, "errorKey": "The engine returned an invalid JSON response."}}
	var data: Dictionary = response.data
	if not data.get("run") is Dictionary or not data.get("version") is Dictionary:
		return {"ok": false, "error": {"ok": false, "errorKey": "The engine returned an invalid JSON response."}}
	var next_run: Dictionary = data.get("run", {})
	var version: Dictionary = data.get("version", {})
	var next_combat: Dictionary = data.combat if data.get("combat") is Dictionary else {}
	if str(next_run.get("runId", "")) != run_id or str(version.get("runId", "")) != run_id or \
		int(version.get("runSequence", -1)) != int(next_run.get("sequence", 0)) or \
		str(version.get("settingId", "")) != str(next_run.get("settingId", next_run.get("configName", ""))) or \
		str(version.get("contentRevision", "")) != str(next_run.get("contentRevision", "")) or \
		int(version.get("runStep", -1)) != int(next_run.get("step", 0)) or \
		str(version.get("engineVersion", "")) != str(next_run.get("engineVersion", "")) or \
		str(version.get("stateFingerprint", "")) != str(next_run.get("stateHash", "")) or \
		str(version.get("stateFingerprint", "")).is_empty() or \
		(receipt_sequence >= 0 and int(next_run.get("sequence", -1)) != receipt_sequence):
		return {"ok": false, "error": {"ok": false, "errorKey": "Could not refresh the game. Reconnect before playing again."}}
	if not next_combat.is_empty() and (str(version.get("combatId", "")) != str(next_combat.get("combatId", "")) or \
		int(version.get("combatStep", -1)) != int(next_combat.get("step", 0))):
		return {"ok": false, "error": {"ok": false, "errorKey": "Could not refresh the game. Reconnect before playing again."}}
	if not committed_combat.is_empty() and receipt_sequence >= 0 and (str(committed_combat.get("combatId", "")) != str(next_combat.get("combatId", "")) or \
		int(committed_combat.get("step", -1)) != int(next_combat.get("step", 0))):
		return {"ok": false, "error": {"ok": false, "errorKey": "Presentation was superseded by a newer game revision."}}
	var responses: Dictionary = {}
	for section in ["commands", "capabilities", "cardZones", "attributes", "cardPresentations", "modifierItems"]:
		if not data.get(section) is Dictionary:
			return {"ok": false, "error": {"ok": false, "errorKey": "The engine returned an invalid JSON response."}}
		responses[section] = {"ok": true, "data": data[section]}
	if not data.get("actions") is Dictionary or not data.get("evaluations") is Dictionary:
		return {"ok": false, "error": {"ok": false, "errorKey": "The engine returned an invalid JSON response."}}
	if not data.actions.get("candidates") is Array or not data.evaluations.get("cards") is Array or \
		not responses.commands.data.get("commands", []) is Array or not responses.capabilities.data.get("granted", []) is Array:
		return {"ok": false, "error": {"ok": false, "errorKey": "The engine returned an invalid JSON response."}}
	var next_actions: Array = data.get("actions", {}).get("candidates", [])
	var next_evaluations: Array = data.get("evaluations", {}).get("cards", [])
	for card in next_evaluations:
		if not card is Dictionary or not card.get("version") is Dictionary:
			return {"ok": false, "error": {"ok": false, "errorKey": "The engine returned an invalid JSON response."}}
		var inspection_version: Dictionary = card.get("version", {})
		if int(inspection_version.get("runSequence", -1)) != int(next_run.get("sequence", 0)) or \
			str(inspection_version.get("runId", "")) != run_id or str(inspection_version.get("combatId", "")) != str(next_combat.get("combatId", "")) or \
			int(inspection_version.get("combatStep", -1)) != int(next_combat.get("step", 0)) or str(inspection_version.get("contentRevision", "")) != str(next_run.get("contentRevision", "")):
			return {"ok": false, "error": {"ok": false, "errorKey": "Could not refresh the game. Reconnect before playing again."}}
	var next_attributes: Dictionary = responses.attributes.data if responses.has("attributes") else {}
	if not next_attributes.is_empty():
		if str(next_attributes.get("runId", "")) != run_id or int(next_attributes.get("runSequence", -1)) != int(next_run.get("sequence", 0)) or str(next_attributes.get("contentRevision", "")) != str(next_run.get("contentRevision", "")):
			return {"ok": false, "error": {"ok": false, "errorKey": "Could not refresh the game. Reconnect before playing again."}}
		if not next_combat.is_empty() and (str(next_attributes.get("combatId", "")) != str(next_combat.get("combatId", "")) or int(next_attributes.get("combatStep", -1)) != int(next_combat.get("step", 0))):
			return {"ok": false, "error": {"ok": false, "errorKey": "Could not refresh the game. Reconnect before playing again."}}
	var next_presentations: Dictionary = responses.cardPresentations.data if responses.has("cardPresentations") else {}
	if not next_presentations.is_empty() and (str(next_presentations.get("runId", "")) != run_id or int(next_presentations.get("runSequence", -1)) != int(next_run.get("sequence", 0)) or str(next_presentations.get("contentRevision", "")) != str(next_run.get("contentRevision", ""))):
		return {"ok": false, "error": {"ok": false, "errorKey": "Could not refresh the game. Reconnect before playing again."}}
	var next_items: Dictionary = responses.modifierItems.data if responses.has("modifierItems") else {}
	if not next_items.is_empty() and (str(next_items.get("runId", "")) != run_id or int(next_items.get("runSequence", -1)) != int(next_run.get("sequence", 0)) or str(next_items.get("contentRevision", "")) != str(next_run.get("contentRevision", ""))):
		return {"ok": false, "error": {"ok": false, "errorKey": "Could not refresh the game. Reconnect before playing again."}}
	var tool_capabilities: Dictionary = {}
	for capability in responses.capabilities.data.get("granted", []):
		tool_capabilities[str(capability)] = true
	return {
		"ok": true,
		"run": next_run.duplicate(true),
		"combat": next_combat.duplicate(true),
		"card_zones": responses.cardZones.data.duplicate(true),
		"available_commands": responses.commands.data.get("commands", []).duplicate(true),
		"legal_actions": next_actions.duplicate(true),
		"card_evaluations": next_evaluations.duplicate(true),
		"character_attributes": next_attributes.duplicate(true),
		"card_presentations": next_presentations.duplicate(true),
		"modifier_items": next_items.duplicate(true),
		"tool_profile": str(responses.capabilities.data.get("profile", "normal")),
		"tool_capabilities": tool_capabilities,
	}
