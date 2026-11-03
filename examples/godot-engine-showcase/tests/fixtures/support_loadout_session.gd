extends "res://tests/fixtures/unified_loadout_session.gd"
var routine := "offense"
var equipped = "sentinel"
var editable_support := true
func read_support_loadout() -> Dictionary:
	var data := snapshot()
	data.merge({"enabled": true, "canChange": editable_support, "selection": {"actorId": run.playerEntityId, "supportId": equipped, "routineId": routine},
		"owned": [{"definition": {"supportId": "sentinel", "displayName": "Sentinel", "description": "Own abilities", "ownerActivationInterval": 3, "defaultRoutineId": "offense"},
		"routines": [{"routineId": "offense", "displayName": "Offense", "description": "Use an attack."}, {"routineId": "defense", "displayName": "Defense", "description": "Gain Block."}]}]}, true)
	return {"ok": true, "data": data}
func preview_support_loadout(source: Dictionary, _equipment: Dictionary) -> Dictionary:
	previews += 1
	if hold_preview: await release
	if reject_preview: return {"ok": false, "error": "Rejected by engine"}
	return {"ok": true, "data": {"before": source, "after": source.duplicate(true)}}
func apply_support_loadout(source: Dictionary, equipment: Dictionary) -> bool:
	if not loadout_context_matches(source): return false
	commits.append("support"); equipped = equipment.supportId; routine = equipment.routineId; run.sequence += 1
	if uncertain_next: has_pending_command = true; return false
	changed.emit(); return true
