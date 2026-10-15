extends RefCounted
## REST workspace: local intentions only. Never computes equipment legality or effective attributes.
var source: Dictionary = {}
var assignments: Dictionary = {}
var selected_item := ""
var selected_slot := ""
var search := ""
var preview: Dictionary = {}
var generation := 0
var message := ""

func accept(snapshot: Dictionary) -> bool:
	if not snapshot.get("items") is Array or not snapshot.get("policy") is Dictionary or not snapshot.get("assignments") is Dictionary: return false
	source = snapshot.duplicate(true)
	assignments = source.assignments.duplicate(true)
	selected_item = ""; selected_slot = ""; preview = {}; message = ""
	generation += 1
	return true
func dirty() -> bool: return assignments != source.get("assignments", {})
func select_item(id: String) -> void: selected_item = id
func select_slot(id: String) -> void: selected_slot = id
func invalidate_preview() -> void:
	generation += 1; preview = {}; message = "Preview required. Attributes are not calculated locally."
func stage_selected() -> void:
	if selected_item.is_empty() or selected_slot.is_empty(): return
	var origin := str(assignments.find_key(selected_item)) if selected_item in assignments.values() else ""
	stage_drop(selected_item, selected_slot, origin)
func stage_drop(item: String, slot: String, origin: String) -> bool:
	if not source.get("canEquip", false): return false
	if not source.items.any(func(row): return str(row.instance.instanceId) == item): return false
	if not source.policy.slots.any(func(row): return str(row.slotId) == slot): return false
	var actual := str(assignments.find_key(item)) if item in assignments.values() else ""
	if actual != origin or slot == origin: return false
	var displaced: String = assignments.get(slot, "")
	if not origin.is_empty():
		assignments.erase(origin)
		if not displaced.is_empty(): assignments[origin] = displaced
	assignments[slot] = item
	selected_item = item; selected_slot = slot; invalidate_preview()
	return true
func remove_selected() -> void:
	if source.get("canEquip", false): assignments.erase(selected_slot); invalidate_preview()
func clear() -> void:
	if source.get("canEquip", false): assignments.clear(); invalidate_preview()
func restore() -> void:
	assignments = source.get("assignments", {}).duplicate(true); selected_item = ""; selected_slot = ""; invalidate_preview()
func accept_preview(value: Dictionary, ticket: int, captured: Dictionary) -> bool:
	if ticket != generation or captured != assignments or not value.get("before") is Dictionary or not value.get("after") is Dictionary: return false
	for key in ["runId", "actorId", "inventoryId", "runSequence", "expectedStep", "contentRevision"]:
		if value.before.get(key) != source.get(key) or value.after.get(key) != source.get(key): return false
	if value.after.get("assignments") != assignments: return false
	preview = value.duplicate(true); message = "Authoritative preview ready."
	return true
func can_confirm() -> bool: return dirty() and not preview.is_empty() and source.get("canEquip", false)
func snapshot() -> Dictionary:
	var current: Dictionary = preview.get("after", source)
	var items: Array = []
	for row in source.get("items", []):
		var definition: Dictionary = row.definition
		var presentation: Dictionary = definition.get("presentation", {})
		items.append({"instanceId": str(row.instance.instanceId), "name": presentation.get("name", definition.inscriptionId),
			"family": "Character inscription", "rarity": presentation.get("rarity", "common"), "cost": definition.equipmentCost,
			"tags": definition.get("tags", []), "attributeBonuses": [], "tradeoffs": [],
			"description": presentation.get("description", ""), "equippedActorId": row.get("equippedActorId", "")})
	var attributes: Array = []
	for stat in current.get("attributes", {}).get("attributes", []):
		var baseline: float = float(stat.baseValue)
		attributes.append({"id": stat.attributeId, "label": stat.displayName, "group": "Character attributes",
			"base": baseline, "value": stat.effectiveValue, "change": float(stat.effectiveValue) - baseline,
			"format": "percent" if stat.unit == "percent" else "number", "sources": []})
	return {"authority": "REST", "contextKey": [source.get("runId"), source.get("actorId"), source.get("inventoryId"), source.get("contentRevision")],
		"slots": source.get("policy", {}).get("slots", []).map(func(slot): return str(slot.slotId)), "items": items,
		"assignments": assignments.duplicate(true), "selectedItem": selected_item, "selectedSlot": selected_slot,
		"usedCapacity": current.get("usedCapacity", 0), "capacity": source.get("policy", {}).get("capacity", 0),
		"overBudget": false, "presets": [], "presetIndex": -1, "search": search,
		"status": message, "canEdit": source.get("canEquip", false), "attributesReady": not dirty() or not preview.is_empty(),
		"character": {"name": source.get("actorId", ""), "attributes": attributes, "remote": true}}
