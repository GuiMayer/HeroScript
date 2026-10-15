extends Control
## Isolated visual harness. No engine commands, calculations or preference writes.
signal back_requested
@export_file("*.json") var fixture_path := "res://data/labs/inscription_loadout_lab.json"
var draft = preload("res://scripts/labs/inscription_loadout_draft.gd").new()
var view
var visual_context
var fixture_error := ""

func _ready() -> void:
	size_flags_horizontal = Control.SIZE_EXPAND_FILL
	size_flags_vertical = Control.SIZE_EXPAND_FILL
	visual_context = preload("res://scripts/ui/visual_context.gd").from_nodes(Preferences, I18n).derive(
		preload("res://data/themes/ivory_theme.tres"))
	var paper := ColorRect.new()
	paper.color = visual_context.styles.NIGHT
	paper.mouse_filter = Control.MOUSE_FILTER_IGNORE
	paper.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	add_child(paper)
	var margin := MarginContainer.new()
	margin.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	for side in ["left", "right", "top", "bottom"]: margin.add_theme_constant_override("margin_" + side, 20)
	add_child(margin)
	var scroll := ScrollContainer.new()
	scroll.name = "LoadoutPageScroll"
	scroll.horizontal_scroll_mode = ScrollContainer.SCROLL_MODE_DISABLED
	scroll.follow_focus = true
	margin.add_child(scroll)
	var document = JSON.parse_string(FileAccess.get_file_as_string(fixture_path))
	fixture_error = draft.configure(document) if document is Dictionary else "Invalid visual fixture."
	if not fixture_error.is_empty():
		var message := Label.new()
		message.text = fixture_error
		scroll.add_child(message)
		return
	view = preload("res://scripts/ui/loadout/loadout_view.gd").new()
	view.mount(visual_context)
	scroll.add_child(view)
	view.item_requested.connect(func(id): draft.select_item(id); _refresh())
	view.slot_requested.connect(func(id): draft.select_slot(id); _refresh())
	view.equip_requested.connect(func(): draft.stage_selected(); _refresh())
	view.drop_requested.connect(func(item, slot, source): draft.stage_drop(item, slot, source); _refresh())
	view.remove_requested.connect(func(): draft.remove_selected(); _refresh())
	view.clear_requested.connect(func(): draft.clear(); _refresh())
	view.restore_requested.connect(func(): draft.restore(); _refresh())
	view.preset_requested.connect(func(index): draft.apply_preset(index); _refresh())
	view.search_requested.connect(func(value): draft.search = value; _refresh())
	view.back_requested.connect(back_requested.emit)
	_refresh()
func _refresh() -> void:
	view.present(draft.snapshot())
	preload("res://scripts/ui/focus_navigation.gd").wire(view)
