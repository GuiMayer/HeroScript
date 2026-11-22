extends RefCounted
## Projects existing transformation traces. No eligibility or effect calculation.
static var catalog: Dictionary = JSON.parse_string(FileAccess.get_file_as_string("res://data/rune_ui_manifest.json"))

static func equipped(facts: Dictionary, translator) -> Array:
	var result: Array = []
	var seen: Dictionary = {}
	for change in facts.get("permanentChanges", []):
		var category := str(change.get("category", "Base"))
		if category not in ["Affinity", "Behavior"]: continue
		var id := str(change.get("transformationId", ""))
		if id.is_empty() or seen.has(id): continue
		seen[id] = true
		var upgrade := str(change.get("upgradeId", ""))
		var spec: Dictionary = catalog.get(upgrade, {})
		var localized := str(spec.get("name" if translator.locale == "pt_BR" else "english", ""))
		result.append({"transformationId": id, "upgradeId": upgrade, "category": category,
			"name": translator.content_name(upgrade, localized), "symbol": str(spec.get("symbol", "◆" if category == "Affinity" else "◇"))})
	return result
