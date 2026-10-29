extends Control
## Presentation fixtures only. Uses exactly the timeline's causal inspector, without rules or saves.
const EventView = preload("res://scripts/ui/gameplay_event_view.gd")
var inspector
var setting_picker: OptionButton
var language_picker: OptionButton
var fixtures: Array = []

func _ready() -> void:
	var background := ColorRect.new()
	background.color = AppTheme.NIGHT
	background.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	background.mouse_filter = Control.MOUSE_FILTER_IGNORE
	add_child(background)
	var margin := MarginContainer.new()
	margin.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	for side in ["left", "right", "top", "bottom"]: margin.add_theme_constant_override("margin_" + side, 24)
	add_child(margin)
	var column := VBoxContainer.new()
	margin.add_child(column)
	column.add_child(AppTheme.title("Gameplay events — presentation laboratory", 26))
	column.add_child(AppTheme.muted("Offline fixtures. The same inspector is available in the authorized timeline."))
	var controls := HBoxContainer.new()
	column.add_child(controls)
	setting_picker = OptionButton.new()
	language_picker = OptionButton.new()
	controls.add_child(setting_picker)
	controls.add_child(language_picker)
	language_picker.add_item("English")
	language_picker.add_item("Português")
	fixtures = JSON.parse_string(FileAccess.get_file_as_string("res://data/labs/gameplay_events.json"))
	for fixture in fixtures: setting_picker.add_item(str(fixture.label))
	setting_picker.item_selected.connect(func(_index): _show_fixture())
	language_picker.item_selected.connect(func(_index): _show_fixture())
	inspector = EventView.new()
	inspector.size_flags_vertical = Control.SIZE_EXPAND_FILL
	column.add_child(inspector)
	_show_fixture()

func _show_fixture() -> void:
	var fixture: Dictionary = fixtures[setting_picker.selected]
	inspector.present(fixture.batch, "pt_BR" if language_picker.selected == 1 else "en", fixture.get("names", {}))
