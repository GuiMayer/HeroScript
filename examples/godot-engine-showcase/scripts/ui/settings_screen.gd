extends VBoxContainer

var router
var close_action: Callable
var capture_action := ""
var capture_button: Button

func setup(owner, on_close: Callable) -> void:
	router = owner
	close_action = on_close
	add_theme_constant_override("separation", 18)
	var header := HBoxContainer.new()
	header.add_child(AppTheme.title(I18n.text("SETTINGS"), 34))
	var push := Control.new()
	push.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	header.add_child(push)
	header.add_child(_button(I18n.text("BACK"), _close, 130))
	add_child(header)
	var columns := HBoxContainer.new()
	columns.size_flags_vertical = Control.SIZE_EXPAND_FILL
	columns.add_theme_constant_override("separation", 20)
	add_child(columns)
	for panel in [_audio_panel(), _control_panel()]:
		var scroll := ScrollContainer.new()
		scroll.size_flags_horizontal = Control.SIZE_EXPAND_FILL
		scroll.horizontal_scroll_mode = ScrollContainer.SCROLL_MODE_DISABLED
		panel.size_flags_horizontal = Control.SIZE_EXPAND_FILL
		scroll.add_child(panel)
		columns.add_child(scroll)

func _audio_panel() -> Control:
	var content := VBoxContainer.new()
	content.custom_minimum_size = Vector2(440, 0)
	content.add_theme_constant_override("separation", 12)
	content.add_child(AppTheme.title(I18n.text("Audio and video"), 24, AppTheme.GOLD))
	var language := OptionButton.new()
	language.name = "LanguageSelector"
	for code in I18n.LOCALES:
		language.add_item("English" if code == "en" else "Português (Brasil)")
	language.selected = I18n.LOCALES.find(I18n.locale)
	language.item_selected.connect(func(index):
		Preferences.locale = I18n.LOCALES[index]
		Preferences.save()
		I18n.set_locale(Preferences.locale))
	content.add_child(AppTheme.muted(I18n.text("Language")))
	content.add_child(language)
	content.add_child(_slider(I18n.text("Master volume"), Preferences.master_volume, func(v): Preferences.master_volume = v))
	content.add_child(_slider(I18n.text("Music"), Preferences.music_volume, func(v): Preferences.music_volume = v))
	content.add_child(_slider(I18n.text("Sound effects"), Preferences.sfx_volume, func(v): Preferences.sfx_volume = v))
	content.add_child(_slider(I18n.text("Animation speed"), Preferences.animation_speed / 2.0,
		func(v): Preferences.animation_speed = maxf(.25, v * 2.0)))
	var fullscreen := CheckButton.new()
	fullscreen.text = I18n.text("Fullscreen")
	fullscreen.button_pressed = Preferences.fullscreen
	fullscreen.toggled.connect(func(value): Preferences.fullscreen = value; Preferences.save())
	content.add_child(fullscreen)
	var contrast := CheckButton.new()
	contrast.text = I18n.text("High contrast")
	contrast.button_pressed = Preferences.high_contrast
	contrast.toggled.connect(func(value): Preferences.high_contrast = value; Preferences.save())
	content.add_child(contrast)
	var reduced := CheckButton.new()
	reduced.text = I18n.text("Reduced motion")
	reduced.button_pressed = Preferences.reduced_motion
	reduced.toggled.connect(func(value): Preferences.reduced_motion = value; Preferences.save())
	content.add_child(reduced)
	var automatic := CheckButton.new()
	automatic.text = I18n.text("Automatic animations")
	automatic.button_pressed = Preferences.auto_animations
	automatic.toggled.connect(func(value): Preferences.auto_animations = value; Preferences.save())
	content.add_child(automatic)
	content.add_child(AppTheme.muted(I18n.text("Your preferences are saved locally and do not affect game rules.")))
	return AppTheme.panel(content)

func _control_panel() -> Control:
	var content := VBoxContainer.new()
	content.custom_minimum_size = Vector2(520, 0)
	content.add_theme_constant_override("separation", 12)
	content.add_child(AppTheme.title(I18n.text("Controls and connection"), 24, AppTheme.TEAL))
	var labels := {
		"pause_game": I18n.text("Pause"),
		"end_turn": I18n.text("End turn"),
		"open_timeline": I18n.text("Open timeline"),
		"confirm_action": I18n.text("Next animation")
	}
	for action in Preferences.ACTIONS:
		var row := HBoxContainer.new()
		var name := Label.new()
		name.text = labels[action]
		name.size_flags_horizontal = Control.SIZE_EXPAND_FILL
		row.add_child(name)
		var button := AppTheme.button(Preferences.action_label(action), 150)
		button.pressed.connect(func(): GameAudio.ui(); _capture(action, button))
		row.add_child(button)
		content.add_child(row)
	var api_label := AppTheme.muted(I18n.text("HeroScript address"))
	content.add_child(api_label)
	var api := LineEdit.new()
	api.text = Preferences.api_url
	api.text_submitted.connect(func(value): Preferences.api_url = value; Preferences.save())
	api.focus_exited.connect(func(): Preferences.api_url = api.text; Preferences.save())
	content.add_child(api)
	content.add_child(AppTheme.muted(I18n.text("Godot connects to HeroScript to run the game. All rules are handled by the engine.")))
	return AppTheme.panel(content)

func _slider(label_text: String, value: float, setter: Callable) -> Control:
	var row := VBoxContainer.new()
	var label := Label.new()
	label.text = label_text
	row.add_child(label)
	var slider := HSlider.new()
	slider.min_value = 0.0
	slider.max_value = 1.0
	slider.step = .01
	slider.value = value
	slider.value_changed.connect(func(v): setter.call(float(v)); Preferences.save())
	row.add_child(slider)
	return row

func _capture(action: String, button: Button) -> void:
	capture_action = action
	capture_button = button
	button.text = I18n.text("PRESS A KEY")
	set_process_unhandled_key_input(true)

func _unhandled_key_input(event: InputEvent) -> void:
	if capture_action.is_empty() or not event.pressed or event.echo:
		return
	Preferences.remap(capture_action, event.physical_keycode)
	capture_button.text = Preferences.action_label(capture_action)
	capture_action = ""
	set_process_unhandled_key_input(false)
	get_viewport().set_input_as_handled()

func _close() -> void:
	Preferences.save()
	close_action.call()

func _button(text: String, action: Callable, width := 0) -> Button:
	var value := AppTheme.button(text, width)
	value.pressed.connect(func(): GameAudio.ui(); action.call())
	return value
