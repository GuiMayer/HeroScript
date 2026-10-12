extends RefCounted

static func option_reference(choice: Dictionary) -> String:
	return JSON.stringify([choice.get("type", ""), choice.get("subjectId", ""), choice.get("payload", choice.get("validPayload", {}))]).sha256_text()
## View models join advertised commands with their published offers. No price/rule evaluation.
var _run: Dictionary
var _choices: Array
var _appearance: Dictionary
var _i18n

func _init(run: Dictionary, choices: Array, appearance: Dictionary, translator) -> void:
	_run = run.duplicate(true)
	_choices = choices.duplicate(true)
	_appearance = appearance.duplicate(true)
	_i18n = translator

func current_node() -> Dictionary:
	for node in _run.get("map", {}).get("nodes", []):
		if str(node.get("nodeId", "")) == str(_run.get("currentNodeId", "")): return node.duplicate(true)
	return {}

func choice_label(choice: Dictionary, model: Dictionary = {}) -> String:
	var type := str(choice.get("type", ""))
	var templates := {"ADVANCE_NODE": "TRAVEL TO %s", "PICK_CARD_REWARD": "CHOOSE  •  %s",
		"DECOMPOSE_CARD_REWARD": "DISMANTLE  •  %s", "BUY_SHOP_ITEM": "BUY  •  %s",
		"APPLY_PREPARATION_OPTION": "PREPARE  •  %s", "UPGRADE_CARD": "UPGRADE  •  %s",
		"REMOVE_CARD_TRANSFORMATION": "REMOVE  •  %s", "REPLACE_CARD_TRANSFORMATION": "REPLACE  •  %s"}
	if templates.has(type): return _i18n.text(templates[type]) % str(model.get("name", node_name(str(choice.get("subjectId", "")))))
	return _i18n.text({"START_ENCOUNTER": "ENTER COMBAT", "START_DIALOGUE": "TALK", "RESOLVE_COMBAT": "CONFIRM RESULT",
		"RESOLVE_NODE": "COMPLETE STAGE", "ACQUIRE_RELIC": "CLAIM RELIC", "CREATE_CARD_SELECTION": "REVEAL REWARD",
		"CREATE_SHOP": "ENTER SHOP", "CREATE_PREPARATION": "LIGHT CAMPFIRE", "REROLL_CARD_REWARD": "NEW OPTIONS",
		"REROLL_SHOP": "REFRESH STOCK"}.get(type, type.replace("_", " ")))

func node_name(id: String) -> String:
	for node in _run.get("map", {}).get("nodes", []):
		if str(node.get("nodeId", "")) == id:
			var metadata: Dictionary = node.get("metadata", {})
			if metadata.has("displayName"):
				return _i18n.content_name(id, str(metadata.displayName))
	return str(_appearance.get("nodes", {}).get(id, {}).get("name", _i18n.content_name(id)))

func node_description(node: Dictionary, fallback: String) -> String:
	var metadata: Dictionary = node.get("metadata", {})
	var authored := str(metadata.get("description", fallback))
	# Explicit blank fallback prevents untranslated IDs becoming player-facing prose.
	return _i18n.content_name(str(node.get("nodeId", "")) + ".description", authored if not authored.is_empty() else " ").strip_edges()

func route() -> Array:
	var result: Array = []
	for node in _run.get("map", {}).get("nodes", []):
		var id := str(node.get("nodeId", ""))
		var travel: Dictionary = {}
		for choice in _choices:
			if str(choice.type) == "ADVANCE_NODE" and str(choice.subjectId) == id: travel = choice.duplicate(true)
		result.append({
			"id": id, "name": node_name(id), "kind": str(node.get("activity", {}).get("type", "")),
			"description": node_description(node, ""), "risk": str(node.get("metadata", {}).get("risk", "")),
			"next": node.get("nextNodeIds", []).duplicate(), "travel": travel,
			"current": id == str(_run.get("currentNodeId", "")),
			"resolved": id in _run.get("map", {}).get("resolvedNodeIds", [])
		})
	return result

func offer(choice: Dictionary) -> Dictionary:
	var type := str(choice.get("type", ""))
	var subject := str(choice.get("subjectId", ""))
	var definition := subject
	var costs: Array = []
	var has_cost := false
	if choice.has("costs"):
		costs = choice.costs.duplicate(true)
		has_cost = true
	if type == "BUY_SHOP_ITEM":
		for shop in _run.get("shops", []):
			if str(shop.get("shopInstanceId", "")) != str(choice.payload.get("shopInstanceId", "")): continue
			for item in shop.get("items", []):
				if str(item.get("itemId", "")) == subject:
					definition = str(item.get("cardId", subject)) if item.get("cardId") != null else subject
					costs = item.get("costs", []).duplicate(true)
					has_cost = item.has("costs")
	elif type == "APPLY_PREPARATION_OPTION":
		for preparation in _run.get("preparations", []):
			if str(preparation.get("preparationInstanceId", "")) != str(choice.payload.get("preparationInstanceId", "")): continue
			for option in preparation.get("options", []):
				if str(option.get("optionId", "")) == subject:
					costs = option.get("costs", []).duplicate(true)
					has_cost = option.has("costs")
	elif type in ["REROLL_SHOP", "REROLL_CARD_REWARD"]:
		var shop := type == "REROLL_SHOP"
		var identity := "shopInstanceId" if shop else "selectionInstanceId"
		for offer_state in _run.get("shops" if shop else "cardSelections", []):
			if str(offer_state.get(identity, "")) == str(choice.payload.get(identity, "")):
				costs = offer_state.get("rerollCosts", []).duplicate(true)
				has_cost = offer_state.has("rerollCosts")
	var card: Dictionary = _appearance.get("cards", {}).get(definition, {})
	var is_card := str(choice.get("subjectType", "")) == "card" or not card.is_empty()
	var name: String = _i18n.content_name(definition, str(card.get("name", definition)))
	if str(choice.get("subjectType", "")) == "node": name = node_name(subject)
	var summary := str(card.get("text", _i18n.text("Choose your next action.")))
	if str(choice.get("subjectType", "")) == "node":
		for node in _run.get("map", {}).get("nodes", []):
			if str(node.get("nodeId", "")) == subject: summary = node_description(node, summary)
	if choice.has("variantId"): summary = _i18n.content_name(str(choice.variantId))
	var price: Array[String] = []
	for cost in costs: price.append("%s %s" % [_i18n.number(float(cost.get("amount", 0))), _i18n.content_name(str(cost.get("resourceId", "")))])
	return {"name": name, "definitionId": definition, "tone": card.get("tone", "skill"),
		"summary": summary, "category": "cards" if is_card else "activities",
		"cost": (_i18n.text("Free") if costs.is_empty() else " + ".join(price)) if has_cost else "",
		"choice": choice.duplicate(true)}

func transformation_text(assessment: Dictionary) -> String:
	var lines: Array[String] = []
	if not bool(assessment.get("isCompatible", false)):
		for diagnostic in assessment.get("diagnostics", []):
			lines.append(str(diagnostic.get("message", diagnostic)))
		return "\n".join(lines)
	for side in ["before", "after"]:
		var card: Dictionary = assessment.get(side, {}) if assessment.get(side) is Dictionary else {}
		lines.append(_i18n.text("Before" if side == "before" else "After"))
		lines.append(", ".join(card.get("tags", [])))
		for component in card.get("components", []):
			if component.get("effect") is Dictionary:
				var effect: Dictionary = component.effect
				lines.append("%s · %s · ×%s" % [str(component.get("componentId", "")), _i18n.content_name(str(effect.get("type", effect.get("effectType", "effect")))), effect.get("repeat", 1)])
				for parameter in effect.get("parameters", []):
					lines.append("%s: %s" % [str(parameter.get("parameter", parameter.get("parameterId", "value"))), parameter.get("flatValue", "—")])
	return "\n".join(lines)
