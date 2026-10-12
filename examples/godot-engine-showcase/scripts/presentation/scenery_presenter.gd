extends RefCounted
## Resolve visual mappings using published IDs, not inferred gameplay rules.
static func build(run: Dictionary, combat: Dictionary, setting: String, mappings: Dictionary):
	var result: Dictionary = mappings.get("defaults", {"backgroundId": "combat"}).duplicate(true)
	var specific: Dictionary = mappings.get("settings", {}).get(setting, {})
	result.merge(specific.get("defaults", {}), true)
	var node := str(run.get("currentNodeId", combat.get("runNodeId", "")))
	result.merge(mappings.get("nodes", {}).get(node, {}), true)
	result.merge(specific.get("nodes", {}).get(node, {}), true)
	var encounter := str(combat.get("encounterDefinitionId", ""))
	if not encounter.is_empty():
		result.merge(mappings.get("encounters", {}).get(encounter, {}), true)
		result.merge(specific.get("encounters", {}).get(encounter, {}), true)
	result["settingId"] = setting
	result["nodeId"] = node
	return preload("res://scripts/presentation/view_models.gd").Scenery.new(result)
