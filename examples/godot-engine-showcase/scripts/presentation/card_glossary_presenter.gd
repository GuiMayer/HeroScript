extends RefCounted
## Explains only referenced terms, never discovers game rules by parsing prose.
var _i18n
func _init(translator) -> void: _i18n = translator
func describe(model: Dictionary) -> Array:
	var facts: Dictionary = model.get("presentationFacts", {})
	var references: Array = facts.get("referencedRules", []).duplicate(true)
	var known: Array = references.map(func(rule): return str(rule.get("definitionId", "")))
	for effect in facts.get("effects", []):
		var definition: Dictionary = effect.get("definition", {})
		for key in ["statusId", "modifierId", "condensationRecipeId"]:
			var id := str(definition[key]) if definition.get(key) != null else ""
			if id.is_empty() or id in known: continue
			known.append(id)
			references.append({"definitionId": id})
	var result: Array = []
	for rule in references:
		var id := str(rule.get("definitionId", ""))
		var name: String = _i18n.content_name(id, str(rule.get("name", id)))
		var authored := str(rule.get("description", "")).strip_edges()
		var description: String = _i18n.content_name(id + ".description", _i18n.text(authored) if not authored.is_empty() else " ").strip_edges()
		if description.is_empty():
			var contributions: Array[String] = []
			for influence in rule.get("influences", []):
				var value = influence.get("value")
				if value is float or value is int:
					contributions.append("%s: %s%s" % [_i18n.content_name(str(influence.get("channel", ""))), "+" if float(value) >= 0 else "", _i18n.number(float(value))])
			description = "; ".join(contributions)
		if description.is_empty(): description = _i18n.text("No effect description provided by this setting.")
		if not str(model.get("summary", "")).contains(description): result.append({"name": name, "description": description})
	return result
