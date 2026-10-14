extends RefCounted
## Formats published numbers and contributors; never performs their arithmetic.
var _i18n
func _init(translator) -> void: _i18n = translator
func describe(facts: Dictionary, compact: bool) -> Array[String]:
	var lines: Array[String] = []
	var effects: Array = facts.get("effects", [])
	for effect in effects.slice(0, 3) if compact else effects:
		for parameter in effect.get("parameters", []):
			var values: Dictionary = parameter.get("values", {})
			lines.append(_i18n.content_name(str(parameter.get("parameter", "Amount"))) + ": " + _i18n.text("Original") + " " + _number(values.get("original")) + " → " + _i18n.text("Composed base") + " " + _number(values.get("composedBase")) + (" → " + _i18n.text("Source") + " " + _number(values.get("source")) if str(facts.get("faceValueMode", "")) == "Source" else ""))
			for explanation in facts.get("explanations", []):
				if explanation.get("effectPath") != effect.get("effectPath") or str(explanation.get("parameter")) != str(parameter.get("parameter")): continue
				for attribute in explanation.get("attributes", []):
					lines.append("  " + _i18n.content_name(str(attribute.get("attributeId", ""))) + ": " + _number(attribute.get("currentValue")) + ("" if attribute.get("eligible", false) else " · " + _i18n.text("Not applicable")))
				var names: Array[String] = []
				for source in explanation.get("sources", []):
					if not source.get("applied", false) or str(source.get("kind", "")) == "Actor": continue
					var id := str(source.get("definitionId", "")) if source.get("definitionId") != null else ""
					var name: String = _i18n.content_name(id)
					if not id.is_empty() and name not in names: names.append(name)
				if not names.is_empty(): lines.append(_i18n.text("Contributors") + ": " + ", ".join(names))
				if explanation.get("conditionPassed") != null: lines.append(_i18n.text("Condition met" if explanation.conditionPassed else "Condition not met"))
				if explanation.get("unresolvedReason") != null: lines.append(_i18n.text("Requires execution context"))
				if not compact:
					for bucket in explanation.get("buckets", []):
						lines.append("  " + _i18n.content_name(str(bucket.get("bucketId", ""))) + " · " + str(bucket.get("operation", "")) + ": " + _number(bucket.get("input")) + " → " + _number(bucket.get("output")))
	if lines.is_empty(): lines.append(_i18n.text("No numeric calculation."))
	if str(facts.get("faceValueMode", "")) == "Source": lines.append(_i18n.text("Before-action source snapshot. Target defenses and critical outcomes are separate."))
	return lines.slice(0, 12) if compact else lines
func _number(value) -> String:
	return _i18n.number(float(value)) if value is int or value is float else "—"
