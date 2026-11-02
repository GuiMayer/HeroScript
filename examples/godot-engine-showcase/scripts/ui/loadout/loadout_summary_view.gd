@tool
class_name LoadoutSummaryView
extends ScrollContainer
signal module_requested(id: String)
var visual_context
@export var relics_only := false
var _column: VBoxContainer
var _key := ""
var _bound := false
static func create() -> LoadoutSummaryView:
	return (load("res://scenes/ui/components/loadout/loadout_summary.tscn") as PackedScene).instantiate()
func _ready() -> void:
	if visual_context == null: mount(preload("res://scripts/ui/visual_context.gd").new())
func mount(context) -> void:
	visual_context = context; theme = context.theme; _column = get_node("Body")
	if not _bound:
		for id in ["inscriptions", "deck", "relics"]:
			get_node("Body/Overview/" + id.capitalize()).pressed.connect(func(): module_requested.emit(id))
		_bound = true
	get_node("Body/Overview/SeparateSystems").text = context.translator.text("Equipment, cards and relics keep their own rules and inventories.")
	get_node("Body/Empty").text = context.translator.text("No relics acquired.")
func present(model: Dictionary) -> void:
	var key := JSON.stringify([model, relics_only, visual_context.translator.locale])
	if key == _key: return
	_key = key
	var translator = visual_context.translator
	get_node("Body/Overview").visible = not relics_only
	for entry in [["inscriptions", "Inscriptions", str(model.get("equipped", 0))], ["deck", "Deck", str(model.get("cards", 0))], ["relics", "Relics", str(model.get("relics", []).size())]]:
		get_node("Body/Overview/" + str(entry[0]).capitalize()).text = translator.text(entry[1]) + " · " + entry[2]
	var list = get_node("Body/Relics")
	for child in list.get_children(): list.remove_child(child); child.queue_free()
	list.visible = not model.get("relics", []).is_empty()
	for entry in model.get("relics", []):
		var row: Dictionary = preload("res://scripts/presentation/loadout_presenter.gd").relic(entry, translator)
		var view = preload("res://scenes/ui/components/loadout/relic_summary.tscn").instantiate()
		view.get_node("Title").text = str(row.name) + (" × " + str(row.stacks) if int(row.stacks) > 1 else "")
		view.get_node("Description").text = str(row.description); list.add_child(view)
	get_node("Body/Empty").visible = relics_only and model.get("relics", []).is_empty()
