extends ScrollContainer
signal module_requested(id: String)
var visual_context
var relics_only := false
var _column: VBoxContainer
var _key := ""
func mount(context) -> void:
	visual_context = context; horizontal_scroll_mode = ScrollContainer.SCROLL_MODE_DISABLED; follow_focus = true
	_column = VBoxContainer.new(); _column.size_flags_horizontal = Control.SIZE_EXPAND_FILL; _column.add_theme_constant_override("separation", 18); add_child(_column)
func present(model: Dictionary) -> void:
	var key := JSON.stringify(model)
	if key == _key: return
	_key = key
	for child in _column.get_children(): _column.remove_child(child); child.queue_free()
	var translator = visual_context.translator
	if not relics_only:
		for entry in [["inscriptions", "Inscriptions", str(model.get("equipped", 0))], ["deck", "Deck", str(model.get("cards", 0))], ["relics", "Relics", str(model.get("relics", []).size())]]:
			var id: String = entry[0]
			var button: Button = visual_context.styles.button(translator.text(entry[1]) + " · " + entry[2])
			button.pressed.connect(func(): module_requested.emit(id)); _column.add_child(button)
		_column.add_child(visual_context.styles.muted(translator.text("Equipment, cards and relics keep their own rules and inventories.")))
	for entry in model.get("relics", []):
		var row: Dictionary = preload("res://scripts/presentation/loadout_presenter.gd").relic(entry, translator)
		_column.add_child(visual_context.styles.title(str(row.name) + (" × " + str(row.stacks) if int(row.stacks) > 1 else ""), 22))
		_column.add_child(visual_context.styles.muted(str(row.description), 16, 0))
	if relics_only and model.get("relics", []).is_empty(): _column.add_child(visual_context.styles.muted(translator.text("No relics acquired.")))
