@tool
extends "res://scripts/ui/screen_view.gd"

const ScreenContext = preload("res://scripts/ui/context/screen_context.gd")
var _router
var _screen_context
var router:
	get: return _router
	set(value):
		_router = value
		_screen_context = ScreenContext.ensure(value)
var screen_context:
	get:
		if _screen_context == null:
			_screen_context = ScreenContext.ensure(_router)
		return _screen_context
	set(value):
		_screen_context = value
		if value != null and value.router != null:
			_router = value.router
var presentation: Dictionary
var selected_mode := "combat_sandbox"
var mode_buttons: Dictionary = {}
var editor: TextEdit
var seed_input: SpinBox
var launch_button: Button
const SYNERGY_PRESETS := "res://data/labs/tag_synergy_scenarios.json"
var scenario_presets: Array = []
var scenario_picker: OptionButton

var _bound := false
func _ready() -> void:
	_bind_nodes()
	if Engine.is_editor_hint():
		mount(ContextType.new()); theme = visual_context.theme
func _bind_nodes() -> void:
	editor = get_node("Columns/ScenarioPanel/Scenario/Editor")
	launch_button = get_node("Columns/ScenarioPanel/Scenario/LaunchControls/LaunchButton")
	scenario_picker = get_node("Columns/ScenarioPanel/Scenario/ScenarioPicker")
	seed_input = get_node("Columns/ScenarioPanel/Scenario/LaunchControls/SeedInput")
func setup(owner, data: Dictionary) -> void:
	screen_context = ScreenContext.ensure(owner)
	if _router == null: _router = owner
	mount(screen_context.visual_context)
	presentation = data
	_bind_nodes(); theme = visual_context.theme
	get_node("Header/Menu").text = I18n.text("MENU")
	get_node("Header/Titles/Title").text = I18n.text("RULES LAB")
	get_node("Header/Titles/Description").text = I18n.text("Keep your cards and scenario, change the game mode and explore a different play style.")
	get_node("Columns/ScenarioPanel/Scenario/LaunchControls/SeedLabel").text = I18n.text("Seed")
	get_node("Columns/ScenarioPanel/Scenario/LaunchControls/LaunchButton").text = I18n.text("START SCENARIO")
	get_node("Columns/ScenarioPanel/Scenario/Heading").text = I18n.text("2. Edit the JSON scenario")
	get_node("Columns/ModesPanel/Modes/Heading").text = I18n.text("1. Choose the rules")
	if not _bound:
		_bound = true
		get_node("Header/Menu").pressed.connect(func(): GameAudio.ui(); screen_context.back_to_menu())
		scenario_picker.item_selected.connect(_select_scenario)
		launch_button.pressed.connect(func(): GameAudio.ui(); _launch())
	var modes = get_node("Columns/ModesPanel/Modes")
	var available_modes: Array = presentation.get("sandbox_modes", []).duplicate(true)
	if GameSession.selected_setting_id == "ascendant":
		available_modes.append({"id": "w1_synergy_sandbox", "name": I18n.text("World-one tag synergies"), "description": I18n.text("Ascendant rules with the expansion test catalog.")})
	var live: Array[String] = []
	for mode in available_modes:
		var id := str(mode.id); live.append(id)
		var choice: Button = mode_buttons.get(id)
		if choice == null:
			choice = preload("res://scenes/ui/components/sandbox_mode.tscn").instantiate()
			modes.add_child(choice); mode_buttons[id] = choice
			choice.pressed.connect(func(): _select_mode(id))
		choice.text = "%s\n%s" % [mode.name, mode.description]
		choice.button_pressed = id == selected_mode
	for id in mode_buttons.keys():
		if id not in live:
			var removed: Button = mode_buttons[id]; modes.remove_child(removed); removed.queue_free(); mode_buttons.erase(id)
	scenario_presets.clear(); scenario_picker.clear(); scenario_picker.hide()
	if GameSession.selected_setting_id == "ascendant":
		var catalog = JSON.parse_string(FileAccess.get_file_as_string(SYNERGY_PRESETS))
		if catalog is Dictionary and catalog.get("presets") is Array:
			scenario_presets = catalog.presets.duplicate(true)
			scenario_picker.show(); scenario_picker.add_item(I18n.text("Custom scenario"))
			for preset in scenario_presets: scenario_picker.add_item(I18n.text(str(preset.name)))
	editor.text = JSON.stringify(presentation.get("default_scenario", {}), "  ")
	launch_button.theme_type_variation = "PrimaryAction" if visual_context.profile.printed_surfaces else "SecondaryAction"

func _select_mode(mode_id: String) -> void:
	selected_mode = mode_id
	for id in mode_buttons:
		mode_buttons[id].button_pressed = id == selected_mode
	GameAudio.ui()

func _select_scenario(index: int) -> void:
	if index < 1 or index > scenario_presets.size(): return
	var preset: Dictionary = scenario_presets[index - 1]
	editor.text = JSON.stringify(preset.scenario, "  ")
	_select_mode("w1_synergy_sandbox")

func _launch() -> void:
	var parsed = JSON.parse_string(editor.text)
	if not parsed is Dictionary:
		screen_context.show_error(I18n.text("The scenario must be a valid JSON object."))
		return
	launch_button.disabled = true
	screen_context.show_toast(I18n.text("Compiling scenario and rules…"))
	var accepted := await GameSession.start_sandbox(selected_mode, parsed, int(seed_input.value))
	if not is_inside_tree():
		return
	if accepted:
		screen_context.open_game()
	else:
		launch_button.disabled = false
