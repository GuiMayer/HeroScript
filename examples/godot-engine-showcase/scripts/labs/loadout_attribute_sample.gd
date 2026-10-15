extends RefCounted
## LAB ONLY: display-unit additions to authored sample attributes, NOT combat math.
## Replace this adapter with the engine's attribute projection on REST integration.
static func validate(document: Dictionary) -> String:
	var character = document.get("character", {})
	if not character is Dictionary or not character.get("attributes") is Array: return "Invalid sample character."
	var ids := {}
	for stat in character.attributes:
		if not stat is Dictionary: return "Invalid sample attribute."
		var id: String = str(stat.get("id", ""))
		if id.is_empty() or ids.has(id) or not stat.get("base") is float and not stat.get("base") is int: return "Invalid sample attribute."
		if not is_finite(float(stat.base)) or stat.get("format") not in ["number", "percent"]: return "Invalid sample attribute unit."
		if not stat.get("label") is String or not stat.get("group") is String: return "Invalid sample attribute label."
		ids[id] = true
	for item in document.get("inscriptions", []):
		if not item is Dictionary or not item.get("attributeBonuses") is Array: return "Invalid sample bonuses."
		for bonus in item.attributeBonuses:
			if not bonus is Dictionary or not ids.has(bonus.get("attributeId")): return "Unknown sample attribute."
			if not bonus.get("amount") is float and not bonus.get("amount") is int: return "Invalid sample bonus."
			if not is_finite(float(bonus.amount)): return "Invalid sample bonus."
	return ""

static func project(character: Dictionary, equipped: Array) -> Dictionary:
	var result: Dictionary = character.duplicate(true)
	for stat in result.attributes:
		stat.value = float(stat.base)
		stat.sources = []
		for item in equipped:
			for bonus in item.attributeBonuses:
				if bonus.attributeId != stat.id: continue
				stat.value += float(bonus.amount)
				stat.sources.append({"name": item.name, "amount": bonus.amount})
		stat.change = stat.value - float(stat.base)
	return result
