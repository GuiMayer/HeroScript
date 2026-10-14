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
	for component in facts.get("costs", []):
		costs.append_array(component.get("costs", {}).get("costs", []))
		for option in component.get("costs", {}).get("alternativeCosts", []): alternatives.append(tokens(option.get("costs", [])))
	var summary: String = "\n".join(sections.get("effectRows", []).map(func(row): return "".join(row.get("segments", []).map(func(segment): return str(segment.get("text", ""))))))
	var model := {"definitionId": id, "cardInstanceId": facts.get("identity", {}).get("cardInstanceId", ""),
		"name": _i18n.content_name(id, str(appearance.get("name", id))), "tone": appearance.get("tone", "skill"),
		"cardType": _i18n.text(str(appearance.get("tone", "skill")).to_upper()),
		"summary": summary, "effects": summary.split("\n", false), "cost": "", "costs": tokens(costs),
		"costsKnown": true, "costOptions": alternatives, "alternativeCostCount": alternatives.size(),
		"presentationFacts": facts.duplicate(true)}
	model.merge(sections, true)
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
