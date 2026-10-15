extends RefCounted
## Offline UI draft only. No authoritative legality, combat math, RNG, API or persistence.
var _fixture: Dictionary = {}
var _items: Dictionary = {}
var _assignments: Dictionary = {}
var selected_item := ""
var selected_slot := ""
var preset_index := 0
var search := ""

func configure(document: Dictionary) -> String:
	if document.get("schemaVersion") != 1 or document.get("authority") != "VisualFixture":
		return "Invalid visual fixture."
	var sample_error: String = preload("res://scripts/labs/loadout_attribute_sample.gd").validate(document)
	if not sample_error.is_empty(): return sample_error
	if not document.get("capacity") is float and not document.get("capacity") is int: return "Invalid visual capacity."
	if float(document.capacity) < 0 or not is_finite(float(document.capacity)): return "Invalid visual capacity."
	var slots = document.get("slots", [])
	if not slots is Array or slots.is_empty() or slots.size() > 64: return "Invalid visual positions."
	var unique := {}
	for id in slots:
		if not id is String or id.is_empty() or unique.has(id): return "Duplicate or invalid visual position."
		unique[id] = true
	var items := {}
	for item in document.get("inscriptions", []):
		if not item is Dictionary or str(item.get("instanceId", "")).is_empty(): return "Invalid visual item."
		var id: String = item.instanceId
		if items.has(id) or not (item.get("cost") is float or item.get("cost") is int): return "Invalid visual item cost or identity."
		if not is_finite(float(item.cost)) or float(item.cost) < 0: return "Invalid visual item cost."
		items[id] = item.duplicate(true)
	if document.get("presets", []).is_empty(): return "Visual fixture requires a preset."
	for preset in document.presets:
		var seen := {}
		for slot in preset.get("assignments", {}):
			var id: String = preset.assignments[slot]
			if slot not in slots or not items.has(id) or seen.has(id): return "Invalid visual preset reference."
			seen[id] = true
	_fixture = document.duplicate(true)
	_items = items
	selected_item = ""; selected_slot = ""; search = ""
	apply_preset(0)
	return ""

func select_item(id: String) -> void:
	if _items.has(id): selected_item = id
func select_slot(id: String) -> void:
	if id in _fixture.get("slots", []): selected_slot = id
func stage_selected() -> void:
	if selected_item.is_empty() or selected_slot.is_empty(): return
	var source := ""
	for slot in _assignments.keys():
		if _assignments[slot] == selected_item: source = slot
	stage_drop(selected_item, selected_slot, source)
func remove_selected() -> void: _assignments.erase(selected_slot)
func stage_drop(item_id: String, target: String, source: String) -> bool:
	if not _items.has(item_id) or target not in _fixture.get("slots", []): return false
	var actual_source := ""
	for slot in _assignments:
		if _assignments[slot] == item_id: actual_source = slot
	if actual_source != source or target == source: return false
	var next := _assignments.duplicate()
	var displaced: String = next.get(target, "")
	if not source.is_empty():
		next.erase(source)
		if not displaced.is_empty(): next[source] = displaced
	next[target] = item_id
	_assignments = next
	selected_item = item_id
	selected_slot = target
	return true
func clear() -> void: _assignments.clear()
func apply_preset(index: int) -> void:
	if index < 0 or index >= _fixture.get("presets", []).size(): return
	preset_index = index
	_assignments = _fixture.presets[index].assignments.duplicate(true)
	selected_item = ""; selected_slot = ""
func restore() -> void: apply_preset(preset_index)
func snapshot() -> Dictionary:
	var used := 0.0
	var contributions: Array = []
	for slot in _fixture.get("slots", []):
		var item: Dictionary = _items.get(_assignments.get(slot, ""), {})
		if item.is_empty(): continue
		# Only sum authored display costs. This is not a loadout validator.
		used += float(item.cost)
		contributions.append(item.duplicate(true))
	return {"authority": "VisualFixture", "slots": _fixture.get("slots", []).duplicate(),
		"items": _items.values().duplicate(true), "assignments": _assignments.duplicate(),
		"selectedItem": selected_item, "selectedSlot": selected_slot,
		"usedCapacity": used, "capacity": _fixture.get("capacity", 0),
		"overBudget": used > float(_fixture.get("capacity", 0)), "contributions": contributions,
		"presets": _fixture.get("presets", []).duplicate(true), "presetIndex": preset_index,
		"character": preload("res://scripts/labs/loadout_attribute_sample.gd").project(_fixture.character, contributions),
		"search": search}
