extends "res://scripts/ui/screen_view.gd"

var router
var presentation: Dictionary
var selected_mode := "combat_sandbox"
var mode_buttons: Dictionary = {}
var editor: TextEdit
var seed_input: SpinBox
var launch_button: Button
const SYNERGY_PRESETS := "res://data/labs/tag_synergy_scenarios.json"
var scenario_presets: Array = []
var scenario_picker: OptionButton

func setup(owner, data: Dictionary) -> void:
	router = owner
	mount(owner.visual_context)
	presentation = data
	add_theme_constant_override("separation", 16)
	var header := HBoxContainer.new()
	var titles := VBoxContainer.new()
	titles.add_child(AppTheme.title(I18n.text("RULES LAB"), 34))
	titles.add_child(AppTheme.muted(I18n.text("Keep your cards and scenario, change the game mode and explore a different play style."), 15, 360))
	header.add_child(titles)
	var push := Control.new()
	push.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	header.add_child(push)
	header.add_child(_button(I18n.text("MENU"), router.back_to_menu, 120))
	add_child(header)
	var columns := HBoxContainer.new()
	columns.size_flags_vertical = Control.SIZE_EXPAND_FILL
	columns.add_theme_constant_override("separation", 18)
	add_child(columns)
	var modes := VBoxContainer.new()
	modes.custom_minimum_size = Vector2(390, 0)
	modes.add_theme_constant_override("separation", 10)
	modes.add_child(AppTheme.title(I18n.text("1. Choose the rules"), 22, AppTheme.GOLD))
	var available_modes: Array = presentation.get("sandbox_modes", []).duplicate(true)
	if GameSession.selected_setting_id == "ascendant":
		available_modes.append({"id": "w1_synergy_sandbox", "name": I18n.text("World-one tag synergies"), "description": I18n.text("Ascendant rules with the expansion test catalog.")})
	for mode in available_modes:
		var choice := Button.new()
		choice.custom_minimum_size = Vector2(0, 82)
		choice.text = "%s\n%s" % [mode.name, mode.description]
		choice.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
		choice.alignment = HORIZONTAL_ALIGNMENT_LEFT
		choice.toggle_mode = true
		choice.button_pressed = str(mode.id) == selected_mode
		choice.pressed.connect(func(): _select_mode(str(mode.id)))
		mode_buttons[str(mode.id)] = choice
		modes.add_child(choice)
	columns.add_child(AppTheme.panel(modes))
	var scenario := VBoxContainer.new()
	scenario.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	scenario.add_theme_constant_override("separation", 10)
	scenario.add_child(AppTheme.title(I18n.text("2. Edit the JSON scenario"), 22, AppTheme.TEAL))
	if GameSession.selected_setting_id == "ascendant":
		var catalog = JSON.parse_string(FileAccess.get_file_as_string(SYNERGY_PRESETS))
		if catalog is Dictionary and catalog.get("presets") is Array:
			scenario_presets = catalog.presets.duplicate(true)
			scenario_picker = OptionButton.new()
			scenario_picker.add_item(I18n.text("Custom scenario"))
			for preset in scenario_presets:
				scenario_picker.add_item(I18n.text(str(preset.name)))
			scenario_picker.item_selected.connect(_select_scenario)
			scenario.add_child(scenario_picker)
	editor = TextEdit.new()
	editor.size_flags_vertical = Control.SIZE_EXPAND_FILL
	editor.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	editor.text = JSON.stringify(presentation.get("default_scenario", {}), "  ")
	editor.wrap_mode = TextEdit.LINE_WRAPPING_BOUNDARY
	scenario.add_child(editor)
	var launch_row := HBoxContainer.new()
	launch_row.add_child(AppTheme.title(I18n.text("Seed"), 15))
	seed_input = SpinBox.new()
	seed_input.min_value = 1
	seed_input.max_value = 99999999
	seed_input.value = 424242
	seed_input.custom_minimum_size.x = 170
	launch_row.add_child(seed_input)
	var stretch := Control.new()
	stretch.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	launch_row.add_child(stretch)
	launch_button = _button(I18n.text("START SCENARIO"), _launch, 210)
	if visual_context.profile.printed_surfaces: launch_button.theme_type_variation = "PrimaryAction"
	launch_row.add_child(launch_button)
	scenario.add_child(launch_row)
	var scenario_panel := AppTheme.panel(scenario)
	scenario_panel.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	columns.add_child(scenario_panel)

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
		router.show_error(I18n.text("The scenario must be a valid JSON object."))
		return
	launch_button.disabled = true
	router.show_toast(I18n.text("Compiling scenario and rules…"))
	var accepted := await GameSession.start_sandbox(selected_mode, parsed, int(seed_input.value))
	if not is_inside_tree():
		return
	if accepted:
		router.open_game()
	else:
		launch_button.disabled = false

func _button(text: String, action: Callable, width := 0) -> Button:
	var value := AppTheme.button(text, width)
	value.pressed.connect(func(): GameAudio.ui(); action.call())
	return value
