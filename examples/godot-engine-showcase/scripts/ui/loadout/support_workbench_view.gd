@tool
extends VBoxContainer
## Canonical equipment is displayed while autosave runs; local selection is only an intent.
signal equipment_requested(support_id, routine_id)
var visual_context
var _snapshot := {}
var _bound := false
func mount(context) -> void:
	visual_context = context; theme = context.theme
	if not _bound:
		get_node("Equipment/Support").item_selected.connect(_support_selected)
		get_node("Equipment/Routine").item_selected.connect(_routine_selected)
		_bound = true
func present(snapshot: Dictionary, locked: bool) -> void:
	_snapshot = snapshot.duplicate(true)
	var enabled := bool(snapshot.get("enabled", false))
	var text = visual_context.translator
	get_node("Title").text = text.text("COMPANION")
	get_node("Description").text = text.text("Changes are saved automatically. Companions use their own abilities and routines." if enabled else "Supports are not enabled for this character or setting.")
	get_node("Equipment").visible = enabled
	if not enabled: return
	get_node("Equipment/SupportLabel").text = text.text("Equipped companion")
	get_node("Equipment/RoutineLabel").text = text.text("Routine")
	var selected: Dictionary = snapshot.get("selection", {}) if snapshot.get("selection") is Dictionary else {}
	var support: OptionButton = get_node("Equipment/Support")
	var routine: OptionButton = get_node("Equipment/Routine")
	support.clear(); support.add_item(text.text("None")); support.set_item_metadata(0, null)
	routine.clear()
	var entry := {}
	for owned in snapshot.get("owned", []):
		var definition: Dictionary = owned.get("definition", {})
		var id := str(definition.get("supportId", ""))
		support.add_item(text.content_name(id, str(definition.get("displayName", id))))
		support.set_item_metadata(support.item_count - 1, id)
		if id == str(selected.get("supportId", "")):
			support.select(support.item_count - 1); entry = owned
	for option in entry.get("routines", []):
		var id := str(option.get("routineId", ""))
		routine.add_item(text.content_name(id, str(option.get("displayName", id))))
		routine.set_item_metadata(routine.item_count - 1, id)
		if id == str(selected.get("routineId", "")):
			routine.select(routine.item_count - 1)
			get_node("Equipment/RoutineDescription").text = text.text(str(option.get("description", "")))
	get_node("Equipment/SupportDescription").text = text.text(str(entry.get("definition", {}).get("description", "")))
	if entry.is_empty(): get_node("Equipment/RoutineDescription").text = ""
	var interval := int(entry.get("definition", {}).get("ownerActivationInterval", 0))
	get_node("Equipment/Cadence").text = text.text("Acts after every %s completed character activations.") % str(interval) if not entry.is_empty() else ""
	support.disabled = locked or not bool(snapshot.get("canChange", false))
	routine.disabled = support.disabled or entry.is_empty()
func _support_selected(index: int) -> void:
	var button: OptionButton = get_node("Equipment/Support")
	if button.disabled: return
	var id = button.get_item_metadata(index)
	if id == null: equipment_requested.emit(null, null); return
	for owned in _snapshot.get("owned", []):
		if owned.get("definition", {}).get("supportId") == id:
			equipment_requested.emit(id, owned.definition.defaultRoutineId); return
func _routine_selected(index: int) -> void:
	var button: OptionButton = get_node("Equipment/Routine")
	if not button.disabled: equipment_requested.emit(_snapshot.get("selection", {}).get("supportId"), button.get_item_metadata(index))
