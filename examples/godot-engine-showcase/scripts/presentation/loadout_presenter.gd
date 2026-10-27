extends RefCounted
## Formatting only. Values/contributions are supplied by canonical queries, never recomputed.
static func character(projection: Dictionary, name: String) -> Dictionary:
	var rows: Array = []
	for stat in projection.get("attributes", []):
		rows.append({"id": str(stat.attributeId), "label": str(stat.displayName), "group": "Character attributes",
			"base": stat.baseValue, "value": stat.effectiveValue, "change": float(stat.effectiveValue) - float(stat.baseValue),
			"format": "percent" if stat.get("unit") in ["percent", "%"] else "number", "sources": [], "contextTags": stat.get("contextTags", [])})
	return {"name": name, "attributes": rows, "remote": true}
static func relic(entry: Dictionary, translator) -> Dictionary:
	var id := str(entry.get("definitionId", ""))
	var presentation: Dictionary = entry.get("presentation", {})
	if presentation.is_empty() and entry.get("definition") is Dictionary: presentation = entry.definition.get("presentation", {})
	return {"name": translator.content_name(id, str(presentation.get("name", id))),
		"description": translator.content_name(id + ".description", translator.text(str(presentation.get("description", "Effect description unavailable.")))),
		"stacks": entry.get("stacks", 1)}
