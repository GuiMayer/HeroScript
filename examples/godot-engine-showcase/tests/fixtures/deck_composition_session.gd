extends "res://tests/fixtures/unified_loadout_session.gd"
var active := "one"
var module := "initial"
func read_deck_loadout() -> Dictionary:
	var data := snapshot()
	data.merge({"enabled": true, "canChange": true, "selection": {"groups": {"primary": [active]}, "modules": {"primary": module}},
		"policy": {"groups": [{"groupId": "primary", "moduleIds": ["initial", "alternate"]}]}, "cards": []}, true)
	for id in ["one", "two"]:
		data.cards.append({"cardInstanceId": id, "definitionId": "basic_attack", "groupId": "primary" if id == active else null,
			"presentation": {"identity": {"definitionId": "basic_attack", "cardInstanceId": id}, "tags": [], "effects": [], "costs": [], "conditions": []}})
	return {"ok": true, "data": data}
func preview_deck_loadout(source: Dictionary, _intent: Dictionary) -> Dictionary:
	previews += 1
	if hold_preview: await release
	if reject_preview: return {"ok": false, "error": "Rejected by engine"}
	return {"ok": true, "data": {"before": source, "after": source.duplicate(true)}}
func apply_deck_loadout(source: Dictionary, intent: Dictionary) -> bool:
	if not loadout_context_matches(source): return false
	commits.append("composition")
	if intent.has("replacementCardId"): active = str(intent.replacementCardId)
	if intent.has("moduleId"): module = str(intent.moduleId)
	run.sequence += 1
	if uncertain_next: has_pending_command = true; return false
	changed.emit(); return true
