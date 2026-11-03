class_name SessionInspectionService
extends RefCounted
## Read-only loadout, payment preview, transformation preview, and card presentation helper for GameSession.

static func loadout_context_matches(synchronized: bool, run: Dictionary, snapshot: Dictionary, client_context: Array) -> bool:
	return synchronized and not run.is_empty() \
		and str(snapshot.get("runId", "")) == str(run.get("runId", "")) \
		and int(snapshot.get("runSequence", -1)) == int(run.get("sequence", 0)) \
		and int(snapshot.get("expectedStep", -1)) == int(run.get("step", 0)) \
		and str(snapshot.get("contentRevision", "")) == str(run.get("contentRevision", "")) \
		and snapshot.get("_clientContext", client_context) == client_context


static func payment_context_matches(synchronized: bool, run: Dictionary, combat: Dictionary, content_revision: String, version: Dictionary, client_context: Array) -> bool:
	return synchronized and not run.is_empty() and not combat.is_empty() \
		and str(version.get("runId", "")) == str(run.get("runId", "")) \
		and str(version.get("combatId", "")) == str(combat.get("combatId", "")) \
		and int(version.get("runSequence", -1)) == int(run.get("sequence", 0)) \
		and int(version.get("combatStep", -1)) == int(combat.get("step", 0)) \
		and str(version.get("contentRevision", "")) == content_revision \
		and version.get("_clientContext", client_context) == client_context


static func preview_transformation(gateway, run_getter: Callable, choice: Dictionary) -> Dictionary:
	var initial_run: Dictionary = run_getter.call()
	var captured_sequence := int(initial_run.get("sequence", -1))
	var captured_run := str(initial_run.get("runId", ""))
	var operation: String = {
		"UPGRADE_CARD": "Apply",
		"REMOVE_CARD_TRANSFORMATION": "Remove",
		"REPLACE_CARD_TRANSFORMATION": "Replace",
	}.get(str(choice.get("type", "")), "")
	if str(choice.get("type", "")) == "APPLY_CARD_MODIFIER_ITEM":
		var item_payload: Dictionary = choice.get("payload", {})
		var preview: Dictionary = await gateway.modifier_item_preview(captured_run, str(item_payload.get("itemInstanceId", "")), str(item_payload.get("cardInstanceId", "")))
		var current_run: Dictionary = run_getter.call()
		if not preview.ok or captured_run != str(current_run.get("runId", "")) or captured_sequence != int(current_run.get("sequence", -1)) or int(preview.data.get("runSequence", -2)) != captured_sequence:
			return {}
		var data: Dictionary = preview.data.duplicate(true)
		data["isCompatible"] = true
		return data
	if operation.is_empty():
		return {}
	var payload: Dictionary = choice.get("payload", {})
	var upgrade_id := str(payload.upgradeId) if payload.get("upgradeId") != null else ""
	var response: Dictionary = await gateway.transformation_preview(captured_run, str(payload.get("cardInstanceId", "")), operation, upgrade_id, payload.get("transformationId"))
	var latest_run: Dictionary = run_getter.call()
	if not response.ok or captured_run != str(latest_run.get("runId", "")) or captured_sequence != int(latest_run.get("sequence", -1)):
		return {}
	if int(response.data.get("expectedSequence", -2)) != captured_sequence:
		return {}
	return response.data.duplicate(true)


static func validate_loadout_info(response: Dictionary, run: Dictionary) -> Dictionary:
	if not response.get("ok", false):
		return response
	var data = response.get("data")
	if not data is Dictionary or not data.get("relics") is Dictionary or not data.get("attributes") is Dictionary:
		return {"ok": false, "error": "Invalid loadout information."}
	if not data.relics.get("relics") is Array or not data.attributes.get("attributes") is Array or str(data.attributes.get("runId", "")) != str(run.get("runId", "")):
		return {"ok": false, "error": "Invalid loadout information."}
	if str(data.relics.get("runId", "")) != str(run.get("runId", "")) \
		or int(data.relics.get("sequence", -1)) != int(run.get("sequence", 0)) \
		or int(data.attributes.get("runSequence", -1)) != int(run.get("sequence", 0)) \
		or str(data.attributes.get("actorId", "")) != str(run.get("playerEntityId", "")) \
		or str(data.attributes.get("contentRevision", "")) != str(run.get("contentRevision", "")):
		return {"ok": false, "status": 409, "error": "Equipment changed. Synchronizing…"}
	return response
