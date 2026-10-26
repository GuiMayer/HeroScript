extends RefCounted
## One passive adapter for base and source facts. No formula, legality, RNG or target simulation.
var _i18n
var _appearance: Dictionary
func _init(translator, appearance: Dictionary) -> void:
	_i18n = translator
	_appearance = appearance.duplicate(true)

func project(facts: Dictionary) -> Dictionary:
	var id := str(facts.get("identity", {}).get("definitionId", ""))
	var appearance: Dictionary = _appearance.get("cards", {}).get(id, {})
	var sections: Dictionary = preload("res://scripts/presentation/card_section_presenter.gd").new(_i18n, _appearance, []).sections({"presentation": facts}, appearance)
	var costs: Array = []
	var alternatives: Array = []
	var costs_known := true
	for component in facts.get("costs", []):
		costs.append_array(component.get("costs", {}).get("costs", []))
		if component.get("costs", {}).get("costs", []).any(func(cost): return cost.get("formula") != null and not str(cost.formula).is_empty()): costs_known = false
		for option in component.get("costs", {}).get("alternativeCosts", []):
			if option.get("costs", []).any(func(cost): return cost.get("formula") != null and not str(cost.formula).is_empty()): costs_known = false
			alternatives.append(tokens(option.get("costs", [])))
	var summary: String = "\n".join(sections.get("effectRows", []).map(func(row): return "".join(row.get("segments", []).map(func(segment): return str(segment.get("text", ""))))))
	var model := {"definitionId": id, "cardInstanceId": facts.get("identity", {}).get("cardInstanceId", ""),
		"name": _i18n.content_name(id, str(appearance.get("name", id))), "tone": appearance.get("tone", "skill"),
		"cardType": _i18n.text(str(appearance.get("tone", "skill")).to_upper()),
		"summary": summary, "effects": summary.split("\n", false), "cost": "", "costs": tokens(costs) if costs_known else [],
		"costsKnown": costs_known, "costUnknownLabel": _i18n.text("Contextual cost — inspect"), "costOptions": alternatives, "alternativeCostCount": alternatives.size(),
		"presentationFacts": facts.duplicate(true)}
	model.merge(sections, true)
	var badges: Array = []
	var seen: Array[String] = []
	for change in facts.get("permanentChanges", []):
		var category := str(change.get("category", "Base"))
		if category in seen: continue
		seen.append(category)
		badges.append({"kind": "upgraded" if category == "Base" else "modified", "label": _i18n.text("UPGRADED" if category == "Base" else "MODIFIED"), "symbol": "↑" if category == "Base" else "~", "sources": []})
	var contributions: Array = facts.get("componentContributions", []) if facts.get("componentContributions") is Array else []
	for source in contributions:
		var label := str(source.get("nameKey", ""))
		if label.is_empty(): label = "COMPONENT GRANT"
		var description := str(source.get("descriptionKey", ""))
		badges.append({"kind": "contribution", "label": _i18n.text(label), "symbol": "+", "sources": [_i18n.text(description)] if not description.is_empty() else []})
	model["changeBadges"] = badges
	model["upgraded"] = not seen.is_empty()
	for influence in facts.get("influences", []):
		var text: String = _i18n.content_name(str(influence.get("channel", ""))) + " · " + _i18n.content_name(str(influence.get("bucket", ""))) + ": "
		text += _i18n.number(float(influence.value)) if influence.get("value") != null and influence.get("formula") == null else _i18n.text("Calculated — inspect")
		model.behaviors.append(text)
	model["rarity"] = _i18n.text(str(model.get("rarityId", "")).to_upper())
	return model

func tokens(costs: Array) -> Array:
	var result: Array = []
	for cost in costs:
		var id := str(cost.get("resourceId", ""))
		var style: Dictionary = _appearance.get("resources", {}).get(id, {})
		result.append({"resourceId": id, "amount": cost.get("amount", 0),
			"symbol": style.get("symbol", id.substr(0, 1).to_upper()), "name": _i18n.content_name(id)})
	return result
