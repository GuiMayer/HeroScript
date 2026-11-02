extends Control
## Presentation fixtures only. Uses exactly the timeline's causal inspector, without rules or saves.
const EventView = preload("res://scripts/ui/gameplay_event_view.gd")
var inspector
var setting_picker: OptionButton
var language_picker: OptionButton
var fixtures: Array = []

func _ready() -> void:
	theme = AppTheme.build(Preferences.high_contrast)
	get_node("Background").color = AppTheme.NIGHT
	setting_picker = get_node("Margin/Column/Controls/Setting")
	language_picker = get_node("Margin/Column/Controls/Language")
	language_picker.add_item("English")
	language_picker.add_item("Português")
	fixtures = JSON.parse_string(FileAccess.get_file_as_string("res://data/labs/gameplay_events.json"))
	for fixture in fixtures: setting_picker.add_item(str(fixture.label))
	setting_picker.item_selected.connect(func(_index): _show_fixture())
	language_picker.item_selected.connect(func(_index): _show_fixture())
	inspector = get_node("Margin/Column/Inspector")
	_show_fixture()

func _show_fixture() -> void:
	var fixture: Dictionary = fixtures[setting_picker.selected]
	inspector.present(fixture.batch, "pt_BR" if language_picker.selected == 1 else "en", fixture.get("names", {}))
