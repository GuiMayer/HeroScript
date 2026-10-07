extends RefCounted
## Formats published definitions and preview facts. Never evaluates a formula or an effect.
const EFFECT_TYPES = ["DAMAGE", "HEAL", "MODIFY_RESOURCE", "APPLY_STATUS", "REMOVE_STATUS", "DISPEL_STATUS", "CARD_ZONE_FLOW", "APPLY_MODIFIER", "REMOVE_MODIFIER", "CONDENSE_STACKS", "MODIFY_ATTRIBUTE"]
const TARGET_TYPES = ["SELF", "TARGET", "ALL_ENEMIES", "ALL_ALLIES", "RANDOM_ENEMY", "LOWEST_RESOURCE_ENEMY", "HIGHEST_RESOURCE_ENEMY"]
const PARAMETER_TYPES = ["Amount", "StatusStacks", "StatusDuration", "ModifierStacks", "ModifierDuration", "CardCount"]
var _i18n
var _appearance: Dictionary
var _actors: Array

func _init(translator, appearance: Dictionary, actors: Array) -> void:
	_i18n = translator
	_appearance = appearance.duplicate(true)
	_actors = actors.duplicate(true)

func sections(inspection: Dictionary, appearance: Dictionary) -> Dictionary:
	var original: Dictionary = inspection.get("compiledContainer", {})
	var effective: Dictionary = inspection.get("effectiveBase", {})
	var container: Dictionary = inspection.get("baseContainer", {})
	var evaluation: Dictionary = inspection.get("evaluation", {})
	var rows: Array = []
	var requirements: Array = []
	var behaviors: Array[String] = []
	for component in effective.get("components", []):
		if component.get("effect") is Dictionary:
			_effect_rows(component.effect, str(component.get("componentId", "")), original, inspection, rows, behaviors)
		elif component.get("type") == "condition":
			var label := str(appearance.get("conditions", {}).get(str(component.componentId), component.get("failureReason", "")))
			if label.is_empty() or label == "Condition not met": label = "Special requirement — inspect for details"
			var passed = null
			for condition in evaluation.get("conditions", []):
				if condition.get("componentId") == component.componentId: passed = condition.get("passed")
			requirements.append({"text": _i18n.text(label), "passed": passed})
		elif component.get("type") == "disposition":
			var flow_id := str(component.get("cardZoneResolutionFlowId", ""))
			var labels: Dictionary = _appearance.get("card_flow_labels", {})
			if labels.has(flow_id): behaviors.append(_i18n.text(str(labels[flow_id])))
	var tags: Array[String] = []
	var labels: Dictionary = _appearance.get("card_tag_labels", {})
	for tag in effective.get("tags", container.get("tags", [])):
		if labels.has(tag): tags.append(_i18n.text(str(labels[tag])))
	return {"rarityId": enum_name(container.get("rarity", appearance.get("rarityId", "")), ["Common", "Uncommon", "Rare", "Legendary"]).to_lower(),
		"identityTags": tags, "requirements": requirements, "effectRows": rows, "behaviors": behaviors,
		"previewNote": _i18n.text("Base values — select a legal action for a preview") if not rows.is_empty() and not inspection.get("previewScope", {}).get("hasExecutablePreview", false) else ""}

func _effect_rows(effect: Dictionary, component_id: String, original: Dictionary, inspection: Dictionary, rows: Array, behaviors: Array[String], depth := 0) -> void:
	if depth > 32: return # The authoritative validator applies the same depth bound.
	var type := enum_name(effect.get("type", effect.get("effectType", "")), EFFECT_TYPES)
	var resource: String = _i18n.content_name(str(effect.get("targetResource", "")))
	var target := _target_text(effect, inspection)
	var template := ""
	var parameter := "Amount"
	match type:
		"DAMAGE": template = "Reduce {value} {resource} on {target}."
		"HEAL": template = "Restore {value} {resource} on {target}."
		"MODIFY_RESOURCE":
			var operation := enum_name(effect.get("operation", "ADD"), ["ADD", "SUBTRACT", "SET"])
			template = {"ADD": "Add {value} {resource} to {target}.", "SUBTRACT": "Reduce {value} {resource} on {target}.", "SET": "Set {resource} to {value} on {target}."}.get(operation, "")
			var field := enum_name(effect.get("resourceField", "Current"), ["Current", "Minimum", "Maximum"])
			if field != "Current": resource += " " + _i18n.text(field)
		"APPLY_STATUS":
			template = "Apply {value} {resource} to {target}."
			resource = _i18n.content_name(str(effect.get("statusId", "")))
			parameter = "StatusStacks"
		"APPLY_MODIFIER":
			template = "Apply {value} {resource} to {target}."
			resource = _i18n.content_name(str(effect.get("modifierId", "")))
			parameter = "ModifierStacks"
		"CARD_ZONE_FLOW":
			template = "Move/create {value} cards: {resource}."
			resource = _i18n.text(str(_appearance.get("card_flow_labels", {}).get(str(effect.get("cardZoneFlowId", "")), "Configured card flow — inspect")))
			parameter = "CardCount"
		"MODIFY_ATTRIBUTE":
			template = "Modify {resource} by {value} on {target}."
			resource = _i18n.content_name(str(effect.get("attributeMutation", {}).get("attributeId", "attribute")))
		"REMOVE_STATUS": rows.append(_plain(_i18n.text("Remove %s from %s.") % [_i18n.content_name(str(effect.get("statusId", ""))), target]))
		"REMOVE_MODIFIER": rows.append(_plain(_i18n.text("Remove %s from %s.") % [_i18n.content_name(str(effect.get("modifierId", ""))), target]))
		"DISPEL_STATUS": rows.append(_plain(_i18n.text("Dispel configured statuses from %s.") % target))
		"CONDENSE_STACKS":
			var recipe_id := str(effect.get("condensationRecipeId", ""))
			var description := str(_appearance.get("condensation_labels", {}).get(recipe_id, "Consume selected stacks in one proc — inspect"))
			rows.append(_plain(_i18n.text(description)))
			_add_unique(behaviors, _i18n.text("Condense"))
		_: rows.append(_plain(_i18n.text("Configured effect — inspect for details")))
	if not template.is_empty():
		var value := _numeric(effect, component_id, parameter, original, inspection, depth == 0)
		var formatted: String = _i18n.text(template).replace("{resource}", resource).replace("{target}", target)
		var parts: PackedStringArray = formatted.split("{value}", true, 1)
		var segment := {"text": str(value.get("text", "—"))}
		if value.has("value"): segment.merge(value)
		rows.append({"componentId": component_id, "segments": [{"text": parts[0]}, segment, {"text": parts[1] if parts.size() > 1 else ""}]})
	# These are qualifiers of this effect, not prerequisites to playing the whole card.
	if effect.get("condition") != null and not str(effect.condition).is_empty():
		rows.append(_plain(_i18n.text(str(effect.get("metadata", {}).get("conditionText", "Conditional effect — inspect for details")))))
	if float(effect.get("chance", 1.0)) < 1.0:
		rows.append(_plain(_i18n.text("Chance: %s%%") % _i18n.number(float(effect.chance) * 100)))
	if int(effect.get("repeat", 1)) > 1: _add_unique(behaviors, _i18n.text("Repeat: %s") % int(effect.repeat))
	for numeric in effect.get("parameters", []):
		if numeric.get("distribution") is Dictionary: _add_unique(behaviors, _i18n.text("Shared sequence budget"))
	if effect.get("continuation") is Dictionary:
		_add_unique(behaviors, _i18n.text("Transfer excess · up to %s hops") % int(effect.continuation.get("maximumHops", 1)))
	for pair in [["statusDuration", "StatusDuration"], ["modifierDuration", "ModifierDuration"]]:
		if effect.get(pair[0]) != null or effect.get("parameters", []).any(func(item): return enum_name(item.get("parameter"), PARAMETER_TYPES) == pair[1]):
			var duration := _numeric(effect, component_id, pair[1], original, inspection, depth == 0)
			rows.append({"segments": [{"text": _i18n.text("Duration") + ": "}, duration]})
	for child in effect.get("chainedEffects", []) if effect.get("chainedEffects") is Array else []:
		var timing := enum_name(child.get("childTiming", "AfterParentImpact"), ["AfterParentImpact", "BeforeParentImpact"])
		rows.append(_plain(_i18n.text("Before impact:" if timing == "BeforeParentImpact" else "After impact:")))
		rows.append(_plain(_i18n.text("Chained effect base — inspect executed impacts")))
		_effect_rows(child, component_id, original, inspection, rows, behaviors, depth + 1)

func _numeric(effect: Dictionary, component_id: String, parameter: String, original: Dictionary, inspection: Dictionary, is_root: bool) -> Dictionary:
	var field: String = {"Amount": "flatValue", "StatusStacks": "statusStacks", "StatusDuration": "statusDuration", "ModifierStacks": "modifierStacks", "ModifierDuration": "modifierDuration", "CardCount": "cardCount"}.get(parameter, "flatValue")
	var current = _flat_parameter(effect, parameter, field)
	var baseline = current
	if is_root:
		for component in original.get("components", []):
			if str(component.get("componentId", "")) == component_id and component.get("effect") is Dictionary:
				baseline = _flat_parameter(component.effect, parameter, field)
		var published: Array = []
		for step in inspection.get("previewSteps", []):
			if str(step.get("provenance", {}).get("componentId", "")) != component_id or not step.get("applied", false): continue
			# Execution IDs are NOT definition IDs. The typed parent identity separates
			# this component's root impacts from chained effects and continuation hops.
			if step.get("identity") is Dictionary and step.identity.get("parentProcId") != null: continue
			if step.get("continuation") is Dictionary: continue
			var found_parameter := false
			for resolved in step.get("parameters", []):
				if enum_name(resolved.get("parameter"), PARAMETER_TYPES) == parameter and resolved.get("calculation") is Dictionary:
					published.append(resolved.calculation)
					found_parameter = true
			if parameter == "Amount" and not found_parameter and step.get("calculation") is Dictionary: published.append(step.calculation)
		if not published.is_empty():
			current = published[0].get("value")
			if published.any(func(calculation): return calculation.get("value") != current): return {"text": _i18n.text("Varies — inspect")}
			var distributed: bool = effect.get("parameters", []).any(func(item): return enum_name(item.get("parameter"), PARAMETER_TYPES) == parameter and item.get("distribution") is Dictionary)
			if baseline == null or distributed: baseline = published[0].get("baseValue")
	if not (current is float or current is int) or not is_finite(float(current)): return {"text": _i18n.text("Calculated — inspect")}
	return {"text": _i18n.number(float(current)), "value": current, "baseValue": baseline}

func _flat_parameter(effect: Dictionary, parameter: String, field: String):
	for item in effect.get("parameters", []):
		if enum_name(item.get("parameter"), PARAMETER_TYPES) == parameter:
			return item.get("flatValue") if (item.get("formulaValue") == null or str(item.formulaValue).is_empty()) and item.get("inputQuantityId") == null else null
	if parameter == "Amount" and effect.get("formulaValue") != null and not str(effect.formulaValue).is_empty(): return null
	return effect.get(field)

func _target_text(effect: Dictionary, inspection: Dictionary) -> String:
	var target := enum_name(effect.get("target", "TARGET"), TARGET_TYPES)
	var labels := {"SELF": "yourself", "TARGET": "a selected target", "ALL_ENEMIES": "all enemies", "ALL_ALLIES": "all allies", "RANDOM_ENEMY": "a random enemy", "LOWEST_RESOURCE_ENEMY": "the enemy with the lowest selected resource", "HIGHEST_RESOURCE_ENEMY": "the enemy with the highest selected resource"}
	var selected: Array = inspection.get("previewScope", {}).get("selectedTargetIds", [])
	if target == "TARGET" and selected.size() == 1:
		for actor in _actors:
			if str(actor.get("instanceId", "")) == str(selected[0]): return _i18n.content_name(str(actor.get("definitionId", "")), str(actor.get("name", selected[0])))
	return _i18n.text(str(labels.get(target, "configured targets")))

static func enum_name(value, names: Array) -> String:
	if value is float or value is int:
		var index := int(value)
		return str(names[index]) if index >= 0 and index < names.size() else ""
	return str(value) if value != null else ""

static func _plain(text: String) -> Dictionary: return {"segments": [{"text": text}]}
static func _add_unique(items: Array[String], value: String) -> void:
	if value not in items: items.append(value)
