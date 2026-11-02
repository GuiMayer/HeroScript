extends RefCounted
## Player-facing summary of published numbers. Never evaluates a formula/bucket.
var _i18n
func _init(translator) -> void: _i18n = translator
func describe(facts: Dictionary, _compact: bool) -> Array[String]:
	var lines: Array[String] = []
	for effect in facts.get("effects", []):
		for parameter in effect.get("parameters", []):
			var kind := str(parameter.get("parameter", "Amount"))
			if kind not in ["Amount", "CardCount"]: continue
			var values: Dictionary = parameter.get("values", {})
			var base = values.get("composedBase")
			var value = values.get("source") if str(facts.get("faceValueMode", "")) == "Source" else base
			var details: Array[String] = []
			for explanation in facts.get("explanations", []):
				if explanation.get("effectPath") != effect.get("effectPath") or str(explanation.get("parameter")) != kind: continue
				if explanation.get("conditionPassed") == false:
					_add(details, _i18n.text("Condition not met"))
					continue
				for attribute in explanation.get("attributes", []):
					if not attribute.get("eligible", false) or not attribute.get("applied", true) or str(attribute.get("scope", "Actor")) != "Actor": continue
					if attribute.get("currentValue") == null: continue
					_add(details, _scaling(attribute, explanation))
				for source in explanation.get("sources", []):
					if not source.get("applied", false) or str(source.get("kind", "")) in ["Actor", "Target", "GameMode"]: continue
					var id := str(source.get("definitionId", "")) if source.get("definitionId") != null else ""
					if not id.is_empty(): _add(details, _i18n.text("Affected by %s") % _i18n.content_name(id))
			var changed := _numeric(value) and _numeric(base) and not is_equal_approx(float(value), float(base))
			if details.is_empty() and not changed and _numeric(value): continue
			var heading := _label(effect.get("definition", {}), kind)
			if _numeric(value):
				lines.append("%s: %s%s" % [heading, _number(value), (" (" + _i18n.text("base %s") % _number(base) + ")") if changed else ""])
			else:
				lines.append("%s: %s%s" % [heading, (_i18n.text("base %s") % _number(base) + " · ") if _numeric(base) else "", _i18n.text("Calculated in combat")])
			lines.append_array(details)
	return lines

func _scaling(attribute: Dictionary, explanation: Dictionary) -> String:
	var name: String = _i18n.content_name(str(attribute.get("attributeId", "")))
	var field := str(attribute.get("resourceField", ""))
	if field == "Maximum": name = _i18n.text("Maximum %s") % name
	elif field == "Current": name = _i18n.text("Current %s") % name
	var operation := str(attribute.get("operation", ""))
	if operation.is_empty():
		for bucket in explanation.get("buckets", []):
			if str(bucket.get("bucketId", "")) == str(attribute.get("bucket", "")): operation = str(bucket.get("operation", ""))
	var contribution = attribute.get("contributionValue")
	if operation == "AddPercent" and _numeric(contribution):
		return "%s: %s%s%%" % [name, "+" if float(contribution) >= 0 else "", _i18n.number(float(contribution) * 100)]
	if operation == "Add" and attribute.has("scale") and is_zero_approx(float(attribute.get("offset", 0))):
		return "%s%% %s (%s)" % [_i18n.number(float(attribute.scale) * 100), name, _number(attribute.currentValue)]
	return _i18n.text("Scales with %s") % (name + " (" + _number(attribute.currentValue) + ")")

func _label(effect: Dictionary, parameter: String) -> String:
	if parameter == "CardCount": return _i18n.text("Cards")
	var type := str(effect.get("type", ""))
	if type == "DAMAGE": return _i18n.text("Damage") + " · " + _i18n.content_name(str(effect.get("targetResource", "")))
	if type == "HEAL": return _i18n.text("Healing") + " · " + _i18n.content_name(str(effect.get("targetResource", "")))
	return _i18n.content_name(str(effect.get("targetResource", "Amount")))
func _numeric(value) -> bool: return (value is int or value is float) and is_finite(float(value))
func _number(value) -> String: return _i18n.number(float(value)) if _numeric(value) else "—"
func _add(lines: Array[String], text: String) -> void:
	if not text.is_empty() and text not in lines: lines.append(text)
