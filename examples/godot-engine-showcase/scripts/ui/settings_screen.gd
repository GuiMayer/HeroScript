@tool
extends "res://scripts/ui/screen_view.gd"
## Categorized, immediately-applied local preferences. This screen never changes game rules.

const FocusNavigation = preload("res://scripts/ui/focus_navigation.gd")
const Schema = preload("res://scripts/ui/styles/settings_schema.gd")
const CATEGORY_IDS := ["general", "audio", "display", "accessibility", "controls", "connection"]
var controller

var router
var close_action: Callable
var capture_action := ""
var capture_button: Button
var binding_buttons := {}
var category_buttons := {}
var selected_category := "general"
var resolution_selector: OptionButton
var page_title: Label
var page_description: Label
var page_content: VBoxContainer
var saved_label: Label

func _ready() -> void:
	if router == null:
		theme = preload("res://scripts/ui/visual_context.gd").new(null, {}, preload("res://data/themes/ivory_theme.tres")).theme

func setup(owner, on_close: Callable) -> void:
	router = owner
	mount(owner.visual_context)
	theme = owner.visual_context.theme
	controller = preload("res://scripts/ui/controllers/settings_controller.gd").new(Preferences, I18n, GameSession)
	close_action = on_close
	page_title = find_child("SettingsPageTitle", true, false)
	page_description = find_child("SettingsPageDescription", true, false)
	page_content = find_child("SettingsPageContent", true, false)
	saved_label = get_node("Footer/SavedStatus")
	_translate(self)
	get_node("Header/Back").text = I18n.text("BACK")
	get_node("Header/Back").pressed.connect(func(): GameAudio.ui(); _close())
	for category_id in CATEGORY_IDS:
		var category: Button = find_child("SettingsCategory_" + category_id.capitalize(), true, false)
		category.text = I18n.text(_category_title(category_id))
		category.pressed.connect(func(): GameAudio.ui(); _show_category(category_id))
		category_buttons[category_id] = category
	_build_general_page()
	_bind_emphasis()
	_build_audio_page()
	_build_display_page()
	_build_accessibility_page()
	_build_controls_page()
	_build_connection_page()
	_show_category(selected_category)

func _translate(node: Node) -> void:
	if node.has_meta("source_text"):
		node.text = I18n.text(str(node.get_meta("source_text")))
	for child in node.get_children(): _translate(child)





func _show_category(category_id: String) -> void:
	if category_id not in CATEGORY_IDS: category_id = "general"
	if not capture_action.is_empty():
		capture_action = ""
		capture_button = null
		_refresh_bindings()
	selected_category = category_id
	for id in category_buttons:
		category_buttons[id].set_pressed_no_signal(id == selected_category)
		page_content.get_node(str(id).capitalize() + "Page").visible = id == selected_category
	page_title.text = I18n.text(_category_title(category_id))
	page_description.text = I18n.text(_category_description(category_id))
	AppTheme.apply_view_preferences(page_content)
	_rewire_focus.call_deferred()
func _build_general_page() -> void:
	var language: OptionButton = find_child("LanguageSelector", true, false)
	language.clear()
	for code in I18n.LOCALES:
		language.add_item("English" if code == "en" else "Português (Brasil)")
	language.selected = I18n.LOCALES.find(I18n.locale)
	language.item_selected.connect(func(index):
		controller.set_option("locale", I18n.LOCALES[index], false)
		controller.commit()
)
	_slider_control(controller.read("animation_speed"), .25, 2.0, .05,
		func(value): return "%s×" % I18n.number(value),
		func(value): controller.set_option("animation_speed", value, false), "AnimationSpeedSlider")
	_toggle_control(controller.read("auto_animations"),
		func(value): controller.set_option("auto_animations", value, false), "AutomaticAnimationsToggle")
	var preset: OptionButton = find_child("AnimationPresetSelector", true, false)
	preset.clear()
	var modes := ["normal", "fast", "instant"]
	for label in ["Normal", "Fast", "Instant"]: preset.add_item(I18n.text(label))
	preset.select(maxi(0, modes.find(controller.read("animation_preset"))))
	preset.item_selected.connect(func(index): controller.set_option("animation_preset", modes[index], false); controller.commit())
	var hand_mode: OptionButton = find_child("HandLayoutSelector", true, false)
	hand_mode.clear()
	hand_mode.add_item(I18n.text("Adaptive fan"))
	hand_mode.add_item(I18n.text("Reading — no overlap"))
	hand_mode.select(1 if controller.read("hand_layout_mode") == "reading" else 0)
	hand_mode.item_selected.connect(func(index):
		controller.set_option("hand_layout_mode", "reading" if index == 1 else "adaptive", false)
		controller.commit(); _mark_saved())

func _bind_emphasis() -> void:
	_slider_control(controller.read("hand_emphasis"), 0, 1, .1, _format_percent,
		func(value): controller.set_option("hand_emphasis", value, false), "HandEmphasisSlider")

func _build_audio_page() -> void:
	for spec in [["master_volume", "MasterVolumeSlider"], ["music_volume", "MusicVolumeSlider"], ["sfx_volume", "SfxVolumeSlider"]]:
		var key: String = spec[0]
		_slider_control(controller.read(key), 0, 1, .01, _format_percent,
			func(value): controller.set_option(key, value, false), spec[1])

func _build_display_page() -> void:
	_toggle_control(controller.read("fullscreen"), func(value):
		controller.set_option("fullscreen", value, false)
		controller.commit()
		_mark_saved()
		if is_instance_valid(resolution_selector): resolution_selector.disabled = value,
		"FullscreenToggle", false)
	resolution_selector = find_child("ResolutionSelector", true, false)
	resolution_selector.clear()
	var resolutions: Array[Vector2i] = controller.resolution_options()
	for resolution in resolutions:
		resolution_selector.add_item("%s × %s" % [resolution.x, resolution.y])
		resolution_selector.set_item_metadata(resolution_selector.item_count - 1, resolution)
	resolution_selector.select(resolutions.find(controller.read("window_resolution")))
	resolution_selector.disabled = controller.read("fullscreen")
	resolution_selector.item_selected.connect(func(index):
		controller.set_resolution(resolution_selector.get_item_metadata(index))
		_mark_saved())

func _build_accessibility_page() -> void:
	_slider_control(controller.read("text_scale"), .9, 1.2, .05, _format_percent,
		func(value): controller.set_option("text_scale", value, false), "TextScaleSlider")
	for spec in [["high_contrast", "HighContrastToggle"], ["reduced_motion", "ReducedMotionToggle"], ["reveal_text_gradually", "RevealTextGraduallyToggle"]]:
		var key: String = spec[0]
		_toggle_control(controller.read(key), func(value): controller.set_option(key, value, false), spec[1])

func _build_controls_page() -> void:
	for action in controller.actions():
		var button: Button = find_child("Binding_" + action, true, false)
		button.text = _binding_label(action)
		button.pressed.connect(func(): GameAudio.ui(); _capture(action, button))
		binding_buttons[action] = button
	var reset: Button = find_child("ResetControlsButton", true, false)
	reset.text = I18n.text("RESET CONTROLS")
	reset.pressed.connect(func(): GameAudio.ui(); _reset_bindings())

func _build_connection_page() -> void:
	var api: LineEdit = find_child("ApiAddressInput", true, false)
	api.text = controller.read("api_url")
	api.text_submitted.connect(_save_connection)
	var apply: Button = find_child("ApplyConnectionButton", true, false)
	apply.text = I18n.text("APPLY")
	apply.pressed.connect(func(): GameAudio.ui(); _save_connection(api.text))

func _slider_control(value: float, minimum: float, maximum: float, step: float,
		formatter: Callable, setter: Callable, node_name: String) -> Control:
	var slider: HSlider = find_child(node_name, true, false)
	var spec: Dictionary = Schema.for_control(node_name)
	slider.min_value = float(spec.get("min", minimum))
	slider.max_value = float(spec.get("max", maximum))
	slider.step = float(spec.get("step", step))
	slider.set_value_no_signal(value)
	var amount: Label = find_child(node_name + "Value", true, false)
	amount.text = str(formatter.call(value))
	slider.value_changed.connect(func(next_value):
		amount.text = str(formatter.call(float(next_value)))
		setter.call(float(next_value))
		controller.commit()
		_mark_saved())
	return slider.get_parent()
func _toggle_control(value: bool, setter: Callable, node_name: String, persist := true) -> CheckButton:
	var toggle: CheckButton = find_child(node_name, true, false)
	toggle.set_pressed_no_signal(value)
	toggle.text = I18n.text("ON") if value else I18n.text("OFF")
	toggle.toggled.connect(func(enabled):
		toggle.text = I18n.text("ON") if enabled else I18n.text("OFF")
		setter.call(enabled)
		if persist: controller.commit()
		_mark_saved())
	return toggle
func _format_percent(value: float) -> String:
	return "%s%%" % roundi(value * 100.0)

func _category_title(category_id: String) -> String:
	return str(Schema.CATEGORIES.get(category_id, Schema.CATEGORIES.general)[0])

func _category_description(category_id: String) -> String:
	return str(Schema.CATEGORIES.get(category_id, Schema.CATEGORIES.general)[1])

func _binding_label(action: String) -> String:
	return controller.action_label(action) + "   /   " + controller.controller_label(action)

func _capture(action: String, button: Button) -> void:
	capture_action = action
	capture_button = button
	button.text = I18n.text("PRESS A KEY OR BUTTON")

func _input(event: InputEvent) -> void:
	if capture_action.is_empty() or not (event is InputEventKey or event is InputEventJoypadButton) or not event.is_pressed() or event.is_echo():
		return
	get_viewport().set_input_as_handled()
	if event is InputEventKey and event.physical_keycode == KEY_ESCAPE:
		capture_action = ""
		_refresh_bindings()
		return
	var error: String = controller.remap_event(capture_action, event)
	if not error.is_empty():
		router.show_error(I18n.text(error))
		return
	capture_action = ""
	_refresh_bindings()
	_mark_saved()

func _unhandled_input(event: InputEvent) -> void:
	if capture_action.is_empty() and event.is_action_pressed("ui_cancel") and not event.is_echo():
		get_viewport().set_input_as_handled()
		_close()

func _refresh_bindings() -> void:
	for action in binding_buttons:
		if is_instance_valid(binding_buttons[action]):
			binding_buttons[action].text = _binding_label(action)

func _reset_bindings() -> void:
	capture_action = ""
	controller.reset_bindings()
	_refresh_bindings()
	_mark_saved()

func _save_connection(value: String) -> void:
	var error: String = controller.save_connection(value)
	if not error.is_empty(): router.show_error(I18n.text(error))
	else: _mark_saved()

func _mark_saved() -> void:
	if not is_instance_valid(saved_label):
		return
	saved_label.text = "●  " + I18n.text("SAVED AUTOMATICALLY")
	saved_label.modulate = Color.WHITE

func _rewire_focus() -> void:
	if not is_inside_tree():
		return
	var tree := get_tree()
	if tree == null:
		return
	await tree.process_frame
	if is_inside_tree():
		FocusNavigation.wire(self)

func _close() -> void:
	capture_action = ""
	controller.commit()
	close_action.call()
