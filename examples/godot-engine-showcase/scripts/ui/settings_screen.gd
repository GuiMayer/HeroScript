extends VBoxContainer
## Categorized, immediately-applied local preferences. This screen never changes game rules.

const FocusNavigation = preload("res://scripts/ui/focus_navigation.gd")
const CATEGORY_IDS := ["general", "audio", "display", "accessibility", "controls", "connection"]

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

func setup(owner, on_close: Callable) -> void:
	router = owner
	close_action = on_close
	add_theme_constant_override("separation", 14)
	_build_header()
	_build_body()
	_build_footer()
	_show_category(selected_category)

func _build_header() -> void:
	var header := HBoxContainer.new()
	header.custom_minimum_size.y = 64
	var titles := VBoxContainer.new()
	titles.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	titles.add_theme_constant_override("separation", 1)
	titles.add_child(AppTheme.title(I18n.text("SETTINGS"), 32))
	titles.add_child(AppTheme.muted(I18n.text("Changes are applied and saved automatically."), 13))
	header.add_child(titles)
	header.add_child(_button(I18n.text("BACK"), _close, 130))
	add_child(header)

func _build_body() -> void:
	var body := HBoxContainer.new()
	body.name = "SettingsBody"
	body.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	body.size_flags_vertical = Control.SIZE_EXPAND_FILL
	body.add_theme_constant_override("separation", 16)
	body.add_child(_category_navigation())
	body.add_child(_page_panel())
	add_child(body)

func _category_navigation() -> Control:
	var content := VBoxContainer.new()
	content.custom_minimum_size.x = 238
	content.size_flags_vertical = Control.SIZE_EXPAND_FILL
	content.add_theme_constant_override("separation", 7)
	var label := AppTheme.title(I18n.text("CATEGORIES"), 12, AppTheme.GOLD)
	label.custom_minimum_size.y = 30
	content.add_child(label)
	for category_id in CATEGORY_IDS:
		var category := AppTheme.button(I18n.text(_category_title(category_id)), 220)
		category.name = "SettingsCategory_" + category_id.capitalize()
		category.toggle_mode = true
		category.alignment = HORIZONTAL_ALIGNMENT_LEFT
		category.custom_minimum_size.y = 52
		category.set_meta("category_id", category_id)
		if category_id == "general":
			category.set_meta("initial_focus", true)
		category.pressed.connect(func(): GameAudio.ui(); _show_category(category_id))
		category_buttons[category_id] = category
		content.add_child(category)
	var spacer := Control.new()
	spacer.size_flags_vertical = Control.SIZE_EXPAND_FILL
	content.add_child(spacer)
	var local_note := AppTheme.muted(I18n.text("Your preferences are saved locally and do not affect game rules."), 12, 220)
	local_note.custom_minimum_size.y = 64
	content.add_child(local_note)
	var panel := AppTheme.panel(content, Color("#171927e8"))
	panel.name = "SettingsCategories"
	panel.size_flags_vertical = Control.SIZE_EXPAND_FILL
	return panel

func _page_panel() -> Control:
	var content := VBoxContainer.new()
	content.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	content.size_flags_vertical = Control.SIZE_EXPAND_FILL
	content.add_theme_constant_override("separation", 8)
	page_title = AppTheme.title("", 27, AppTheme.GOLD)
	page_title.name = "SettingsPageTitle"
	content.add_child(page_title)
	page_description = AppTheme.muted("", 14, 580)
	page_description.name = "SettingsPageDescription"
	page_description.custom_minimum_size.y = 38
	content.add_child(page_description)
	content.add_child(HSeparator.new())
	var scroll := ScrollContainer.new()
	scroll.name = "SettingsPageScroll"
	scroll.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	scroll.size_flags_vertical = Control.SIZE_EXPAND_FILL
	scroll.horizontal_scroll_mode = ScrollContainer.SCROLL_MODE_DISABLED
	scroll.vertical_scroll_mode = ScrollContainer.SCROLL_MODE_AUTO
	page_content = VBoxContainer.new()
	page_content.name = "SettingsPageContent"
	page_content.custom_minimum_size.x = 760
	page_content.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	page_content.add_theme_constant_override("separation", 8)
	scroll.add_child(page_content)
	content.add_child(scroll)
	var panel := AppTheme.panel(content, Color("#202236e8"))
	panel.name = "SettingsPage"
	panel.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	panel.size_flags_vertical = Control.SIZE_EXPAND_FILL
	return panel

func _build_footer() -> void:
	var footer := HBoxContainer.new()
	footer.custom_minimum_size.y = 28
	saved_label = AppTheme.caption("●  " + I18n.text("SAVED AUTOMATICALLY"), 12, 220)
	saved_label.add_theme_color_override("font_color", AppTheme.TEAL)
	footer.add_child(saved_label)
	var spacer := Control.new()
	spacer.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	footer.add_child(spacer)
	footer.add_child(AppTheme.caption(I18n.text("Arrows / D-pad: navigate. Enter / A: confirm. Escape / B: close."), 12, 480))
	add_child(footer)

func _show_category(category_id: String) -> void:
	if category_id not in CATEGORY_IDS:
		category_id = "general"
	if not capture_action.is_empty():
		capture_action = ""
		capture_button = null
	selected_category = category_id
	for id in category_buttons:
		category_buttons[id].set_pressed_no_signal(id == selected_category)
	page_title.text = I18n.text(_category_title(category_id))
	page_description.text = I18n.text(_category_description(category_id))
	for child in page_content.get_children():
		page_content.remove_child(child)
		child.queue_free()
	binding_buttons.clear()
	resolution_selector = null
	match category_id:
		"general": _build_general_page()
		"audio": _build_audio_page()
		"display": _build_display_page()
		"accessibility": _build_accessibility_page()
		"controls": _build_controls_page()
		"connection": _build_connection_page()
	AppTheme.apply_view_preferences(page_content)
	_rewire_focus.call_deferred()

func _build_general_page() -> void:
	_add_section(I18n.text("PRESENTATION"))
	var language := OptionButton.new()
	language.name = "LanguageSelector"
	language.custom_minimum_size = Vector2(300, 44)
	for code in I18n.LOCALES:
		language.add_item("English" if code == "en" else "Português (Brasil)")
	language.selected = I18n.LOCALES.find(I18n.locale)
	language.item_selected.connect(func(index):
		Preferences.locale = I18n.LOCALES[index]
		Preferences.save()
		I18n.set_locale(Preferences.locale))
	_add_option(I18n.text("Interface language"), I18n.text("Choose the language used by interface and content."), language)
	var speed := _slider_control(Preferences.animation_speed, .25, 2.0, .05,
		func(value): return "%s×" % I18n.number(value),
		func(value): Preferences.animation_speed = value, "AnimationSpeedSlider")
	_add_option(I18n.text("Animation playback speed"),
		I18n.text("Controls presentation speed only; simulation results never change."), speed)
	var automatic := _toggle_control(Preferences.auto_animations,
		func(value): Preferences.auto_animations = value, "AutomaticAnimationsToggle")
	_add_option(I18n.text("Automatic animation playback"),
		I18n.text("Advance the action queue automatically after the engine resolves a command."), automatic)
	_add_section(I18n.text("CARD HAND"))
	var hand_mode := OptionButton.new()
	hand_mode.name = "HandLayoutSelector"
	hand_mode.add_item(I18n.text("Adaptive fan"))
	hand_mode.add_item(I18n.text("Reading — no overlap"))
	hand_mode.select(1 if Preferences.hand_layout_mode == "reading" else 0)
	hand_mode.item_selected.connect(func(index):
		Preferences.hand_layout_mode = "reading" if index == 1 else "adaptive"
		Preferences.save(); _mark_saved())
	_add_option(I18n.text("Hand layout"), I18n.text("Keep card text size; choose overlap or horizontal reading."), hand_mode)
	_add_option(I18n.text("Card highlight lift"), I18n.text("Highlight intensity does not change card values or text size."),
		_slider_control(Preferences.hand_emphasis, 0.0, 1.0, .1, _format_percent,
			func(value): Preferences.hand_emphasis = value, "HandEmphasisSlider"))
	_add_option(I18n.text("Drag cards to play"), I18n.text("Optional. Click selection and target choices remain available."),
		_toggle_control(Preferences.hand_drag_enabled, func(value): Preferences.hand_drag_enabled = value, "HandDragToggle"))

func _build_audio_page() -> void:
	_add_section(I18n.text("VOLUME"))
	_add_option(I18n.text("Master volume"), I18n.text("Overall output level."),
		_slider_control(Preferences.master_volume, 0.0, 1.0, .01, _format_percent,
			func(value): Preferences.master_volume = value, "MasterVolumeSlider"))
	_add_option(I18n.text("Music"), I18n.text("Background music level."),
		_slider_control(Preferences.music_volume, 0.0, 1.0, .01, _format_percent,
			func(value): Preferences.music_volume = value, "MusicVolumeSlider"))
	_add_option(I18n.text("Sound effects"), I18n.text("Cards, hits and interface feedback."),
		_slider_control(Preferences.sfx_volume, 0.0, 1.0, .01, _format_percent,
			func(value): Preferences.sfx_volume = value, "SfxVolumeSlider"))

func _build_display_page() -> void:
	_add_section(I18n.text("WINDOW"))
	var fullscreen := _toggle_control(Preferences.fullscreen, func(value):
		Preferences.fullscreen = value
		Preferences.save()
		_mark_saved()
		if is_instance_valid(resolution_selector): resolution_selector.disabled = value,
		"FullscreenToggle", false)
	_add_option(I18n.text("Fullscreen"), I18n.text("Use the current monitor's full display area."), fullscreen)
	resolution_selector = OptionButton.new()
	resolution_selector.name = "ResolutionSelector"
	resolution_selector.custom_minimum_size = Vector2(300, 44)
	var resolutions := Preferences.resolution_options()
	for resolution in resolutions:
		resolution_selector.add_item("%s × %s" % [resolution.x, resolution.y])
		resolution_selector.set_item_metadata(resolution_selector.item_count - 1, resolution)
	resolution_selector.select(resolutions.find(Preferences.window_resolution))
	resolution_selector.disabled = Preferences.fullscreen
	resolution_selector.item_selected.connect(func(index):
		Preferences.set_resolution(resolution_selector.get_item_metadata(index))
		_mark_saved())
	_add_option(I18n.text("Window resolution"),
		I18n.text("Used in windowed mode. Ultrawide resolutions are supported."), resolution_selector)

func _build_accessibility_page() -> void:
	_add_section(I18n.text("READABILITY AND MOTION"))
	_add_option(I18n.text("Text size"), I18n.text("Adjusts menu and gameplay text from 90% to 120%."),
		_slider_control(Preferences.text_scale, .9, 1.2, .05, _format_percent,
			func(value): Preferences.text_scale = value, "TextScaleSlider"))
	_add_option(I18n.text("High contrast"),
		I18n.text("Uses darker surfaces and stronger outlines throughout the interface."),
		_toggle_control(Preferences.high_contrast, func(value): Preferences.high_contrast = value, "HighContrastToggle"))
	_add_option(I18n.text("Reduced motion"), I18n.text("Disables decorative motion and shortens transitions."),
		_toggle_control(Preferences.reduced_motion, func(value): Preferences.reduced_motion = value, "ReducedMotionToggle"))

func _build_controls_page() -> void:
	_add_section(I18n.text("GAMEPLAY ACTIONS"))
	var labels := {
		"pause_game": I18n.text("Pause"),
		"end_turn": I18n.text("End turn"),
		"open_timeline": I18n.text("Open timeline"),
		"confirm_action": I18n.text("Next animation"),
		"inspect_card": I18n.text("Inspect card"), "hand_overview": I18n.text("Hand overview")
	}
	for action in Preferences.ACTIONS:
		var button := AppTheme.button(_binding_label(action), 235)
		button.name = "Binding_" + action
		button.pressed.connect(func(): GameAudio.ui(); _capture(action, button))
		binding_buttons[action] = button
		_add_option(labels[action], I18n.text("Select a binding to replace it. Press Escape to cancel."), button)
	var reset := _button(I18n.text("RESET CONTROLS"), _reset_bindings, 235)
	reset.name = "ResetControlsButton"
	_add_option(I18n.text("Restore default bindings"),
		I18n.text("Returns keyboard and gamepad actions to their original mapping."), reset)

func _build_connection_page() -> void:
	_add_section(I18n.text("ENGINE SERVICE"))
	var editor := HBoxContainer.new()
	editor.custom_minimum_size.x = 430
	editor.add_theme_constant_override("separation", 8)
	var api := LineEdit.new()
	api.name = "ApiAddressInput"
	api.text = Preferences.api_url
	api.placeholder_text = "http://127.0.0.1:5271"
	api.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	api.custom_minimum_size.y = 44
	api.text_submitted.connect(func(value): _save_connection(value))
	editor.add_child(api)
	var apply := _button(I18n.text("APPLY"), func(): _save_connection(api.text), 110)
	apply.name = "ApplyConnectionButton"
	editor.add_child(apply)
	_add_option(I18n.text("HeroScript address"),
		I18n.text("Used only by the Godot client to communicate with HeroScript."), editor)
	_add_notice(I18n.text("Connection changes are validated before being saved."))

func _add_section(text: String) -> void:
	var title := AppTheme.title(text, 12, AppTheme.GOLD)
	title.custom_minimum_size.y = 30
	title.vertical_alignment = VERTICAL_ALIGNMENT_BOTTOM
	page_content.add_child(title)

func _add_option(title_text: String, description_text: String, control: Control) -> void:
	var row := HBoxContainer.new()
	row.custom_minimum_size.y = 78
	row.add_theme_constant_override("separation", 24)
	var copy := VBoxContainer.new()
	copy.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	copy.add_theme_constant_override("separation", 3)
	var title := AppTheme.title(title_text, 16)
	title.custom_minimum_size.x = 360
	copy.add_child(title)
	var description := AppTheme.muted(description_text, 12, 360)
	description.tooltip_text = description_text
	copy.add_child(description)
	row.add_child(copy)
	control.custom_minimum_size.x = maxf(control.custom_minimum_size.x, 300)
	# Keep controls easy to scan on ultrawide displays instead of stretching
	# sliders and toggles across the entire page.
	control.size_flags_horizontal = Control.SIZE_SHRINK_END
	control.tooltip_text = description_text
	row.add_child(control)
	var panel := PanelContainer.new()
	panel.add_theme_stylebox_override("panel", AppTheme.box(Color("#171927a8"), 9, Color("#34364d"), 1))
	panel.add_child(row)
	page_content.add_child(panel)

func _add_notice(text: String) -> void:
	var notice := AppTheme.muted(text, 12, 500)
	notice.add_theme_color_override("font_color", AppTheme.TEAL)
	page_content.add_child(notice)

func _slider_control(value: float, minimum: float, maximum: float, step: float,
		formatter: Callable, setter: Callable, node_name: String) -> Control:
	var row := HBoxContainer.new()
	row.custom_minimum_size = Vector2(330, 44)
	row.add_theme_constant_override("separation", 12)
	var slider := HSlider.new()
	slider.name = node_name
	slider.min_value = minimum
	slider.max_value = maximum
	slider.step = step
	slider.value = value
	slider.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	var amount := Label.new()
	amount.name = node_name + "Value"
	amount.text = str(formatter.call(value))
	amount.custom_minimum_size.x = 58
	amount.horizontal_alignment = HORIZONTAL_ALIGNMENT_RIGHT
	amount.add_theme_color_override("font_color", AppTheme.GOLD)
	slider.value_changed.connect(func(next_value):
		amount.text = str(formatter.call(float(next_value)))
		setter.call(float(next_value))
		Preferences.save()
		_mark_saved())
	row.add_child(slider)
	row.add_child(amount)
	return row

func _toggle_control(value: bool, setter: Callable, node_name: String, persist := true) -> CheckButton:
	var toggle := CheckButton.new()
	toggle.name = node_name
	toggle.custom_minimum_size = Vector2(150, 44)
	toggle.button_pressed = value
	toggle.text = I18n.text("ON") if value else I18n.text("OFF")
	toggle.alignment = HORIZONTAL_ALIGNMENT_RIGHT
	toggle.toggled.connect(func(enabled):
		toggle.text = I18n.text("ON") if enabled else I18n.text("OFF")
		setter.call(enabled)
		if persist: Preferences.save()
		_mark_saved())
	return toggle

func _format_percent(value: float) -> String:
	return "%s%%" % roundi(value * 100.0)

func _category_title(category_id: String) -> String:
	return {
		"general": "GENERAL", "audio": "AUDIO", "display": "DISPLAY",
		"accessibility": "ACCESSIBILITY", "controls": "CONTROLS", "connection": "CONNECTION"
	}.get(category_id, "GENERAL")

func _category_description(category_id: String) -> String:
	return {
		"general": "Language and basic gameplay presentation.",
		"audio": "Balance music and interface feedback.",
		"display": "Window mode, resolution and presentation.",
		"accessibility": "Readability and motion preferences.",
		"controls": "Keyboard and gamepad bindings.",
		"connection": "HeroScript service connection."
	}.get(category_id, "Language and basic gameplay presentation.")

func _binding_label(action: String) -> String:
	return Preferences.action_label(action) + "   /   " + Preferences.controller_label(action)

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
	var error := Preferences.remap_event(capture_action, event)
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
	Preferences.reset_bindings()
	_refresh_bindings()
	_mark_saved()

func _save_connection(value: String) -> void:
	if value.strip_edges().trim_suffix("/") == Preferences.api_url:
		return
	if GameSession.busy or GameSession.has_pending_command:
		router.show_error(I18n.text("Finish or recover the current operation before changing the connection."))
	elif not Preferences.set_api_url(value):
		router.show_error(I18n.text("Use an HTTP or HTTPS address."))
	else:
		_mark_saved()

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
	Preferences.save()
	close_action.call()

func _button(text: String, action: Callable, width := 0) -> Button:
	var value := AppTheme.button(text, width)
	value.pressed.connect(func(): GameAudio.ui(); action.call())
	return value
