extends RefCounted
## Published visibility/order and card instances only. No card arithmetic.
static func address(zone: Dictionary) -> Dictionary:
	return {"zoneId": zone.get("zoneId", ""), "ownerId": zone.get("ownerId", ""), "scopeId": zone.get("scopeId", null)}
static func find(snapshot: Dictionary, requested: Dictionary) -> Dictionary:
	for zone in snapshot.get("zones", []):
		if address(zone) == requested: return zone.duplicate(true)
	return {}
static func build(zone: Dictionary, presenter, translator) -> Dictionary:
	var models: Array = []
	if bool(zone.get("contentsVisible", false)):
		for instance in zone.get("cards", []):
			var model: Dictionary = presenter.card_view_model(str(instance.get("cardInstanceId", "")))
			model.availability = translator.text("Inspect card")
			model["collectionTags"] = instance.get("tags", []).duplicate()
			models.append(model)
	if not bool(zone.get("orderVisible", false)):
		models.sort_custom(func(a, b): return str(a.definitionId) + str(a.cardInstanceId) < str(b.definitionId) + str(b.cardInstanceId))
	return {"address": address(zone), "label": translator.text(str(zone.get("presentation", {}).get("labelKey", zone.get("zoneId", "")))),
		"count": int(zone.get("count", 0)), "visible": bool(zone.get("contentsVisible", false)), "ordered": bool(zone.get("orderVisible", false)), "cards": models}
