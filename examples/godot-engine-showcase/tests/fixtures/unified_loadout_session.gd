extends RefCounted
signal changed
signal release
var has_pending_command := false
var busy := false
var hold_preview := false
var reject_preview := false
var uncertain_next := false
var run := {"runId": "run", "playerEntityId": "hero", "settingId": "setting", "sequence": 2, "step": 5, "contentRevision": "revision"}
var assignments := {}
var commits: Array = []
var previews := 0
var character_attributes := {}
var modifier_items := {}
var allocations: Array = []
func activity_choices() -> Array: return []
func load_collection_presentations() -> Dictionary:
	return {"ok": true, "data": {"cards": [{"identity": {"definitionId": "basic_attack", "cardInstanceId": "card-one"}, "tags": [], "costs": [], "effects": [], "conditions": []}]}}
func read_deck_compartments() -> Dictionary:
	var data := snapshot()
	data.merge({"enabled": true, "canChange": true, "allocations": allocations.duplicate(true),
		"profile": {"compartments": [{"compartmentId": "utility", "nameKey": "Utility", "capacity": 2}]},
		"cards": [{"cardInstanceId": "card-one"}]}, true)
	return {"ok": true, "data": data}
func preview_deck_compartments(source: Dictionary, candidate: Array) -> Dictionary:
	previews += 1
	if hold_preview: await release
	if reject_preview: return {"ok": false, "error": "Rejected by engine"}
	var after := source.duplicate(true); after.allocations = candidate.duplicate(true)
	return {"ok": true, "data": {"before": source, "after": after}}
func apply_deck_compartments(source: Dictionary, candidate: Array) -> bool:
	if not loadout_context_matches(source): return false
	commits.append("compartments"); allocations = candidate.duplicate(true); run.sequence += 1
	if uncertain_next: has_pending_command = true; return false
	changed.emit(); return true
func loadout_context_matches(value: Dictionary) -> bool:
	return value.get("runId") == run.runId and value.get("runSequence") == run.sequence and value.get("expectedStep") == run.step and value.get("contentRevision") == run.contentRevision
func snapshot() -> Dictionary:
	return {"runId": run.runId, "actorId": run.playerEntityId, "runSequence": run.sequence, "expectedStep": run.step, "contentRevision": run.contentRevision,
		"inventoryId": "actor:hero", "canEquip": true, "usedCapacity": assignments.size() * 3,
		"policy": {"capacity": 12, "slots": [{"slotId": "one"}]}, "assignments": assignments.duplicate(true),
		"items": [{"instance": {"instanceId": "owned"}, "definition": {"inscriptionId": "focus", "equipmentCost": 3, "presentation": {"name": "Actual inscription"}}}],
		"attributes": {"attributes": [{"attributeId": "critical_chance", "displayName": "Critical chance", "unit": "percent", "baseValue": 0, "effectiveValue": 150}]}}
func read_loadout(_actor: String) -> Dictionary: return {"ok": true, "data": snapshot()}
func preview_loadout(source: Dictionary, draft: Dictionary) -> Dictionary:
	previews += 1
	if hold_preview: await release
	if reject_preview: return {"ok": false, "error": "Rejected by engine"}
	var after := source.duplicate(true); after.assignments = draft.duplicate(true)
	return {"ok": true, "data": {"before": source, "after": after}}
func apply_loadout(source: Dictionary, draft: Dictionary) -> bool:
	if not loadout_context_matches(source): return false
	commits.append("hero"); assignments = draft.duplicate(true); run.sequence += 1
	if uncertain_next: has_pending_command = true; return false
	changed.emit(); return true
func recover_pending() -> bool: has_pending_command = false; changed.emit(); return true

