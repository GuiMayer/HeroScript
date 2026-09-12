extends Node

signal changed

const SAVE_PATH := "user://heroscript_showcase.cfg"
const ACTIONS := ["pause_game", "end_turn", "open_timeline", "confirm_action"]
const DEFAULT_KEYS := {
	"pause_game": KEY_ESCAPE,
	"end_turn": KEY_E,
	"open_timeline": KEY_T,
	"confirm_action": KEY_F
}

const DEFAULT_BUTTONS := {"pause_game": JOY_BUTTON_START, "end_turn": JOY_BUTTON_Y,
	"open_timeline": JOY_BUTTON_BACK, "confirm_action": JOY_BUTTON_RIGHT_SHOULDER}
const RESERVED_KEYS := [KEY_ENTER, KEY_KP_ENTER, KEY_TAB, KEY_SPACE, KEY_UP, KEY_DOWN, KEY_LEFT, KEY_RIGHT]
const RESERVED_BUTTONS := [JOY_BUTTON_A, JOY_BUTTON_B, JOY_BUTTON_DPAD_UP, JOY_BUTTON_DPAD_DOWN, JOY_BUTTON_DPAD_LEFT, JOY_BUTTON_DPAD_RIGHT]
var text_scale := 1.0

var master_volume := 0.80
var music_volume := 0.42
var sfx_volume := 0.75
var animation_speed := 1.0
var locale := "en"
var reduced_motion := false
var auto_animations := true
var fullscreen := false
var high_contrast := false
var api_url := "http://127.0.0.1:5271"
var last_run_id := ""
var campaign_counter := 0
var _saved_api_url := ""
var _api_override := false

func _ready() -> void:
	load_settings()
	master_volume = clampf(master_volume, 0.0, 1.0)
	music_volume = clampf(music_volume, 0.0, 1.0)
	sfx_volume = clampf(sfx_volume, 0.0, 1.0)
	animation_speed = clampf(animation_speed, .25, 2.0)
	_saved_api_url = api_url
	for argument in OS.get_cmdline_user_args():
		if argument.begins_with("--api-url="):
			api_url = argument.trim_prefix("--api-url=")
			_api_override = true
	apply_window()
	apply_audio()

func load_settings() -> void:
	var config := ConfigFile.new()
	if config.load(SAVE_PATH) != OK:
		_apply_default_keys()
		return
	text_scale = clampf(float(config.get_value("video", "text_scale", 1.0)), .9, 1.2)
	master_volume = float(config.get_value("audio", "master", master_volume))
	music_volume = float(config.get_value("audio", "music", music_volume))
	sfx_volume = float(config.get_value("audio", "sfx", sfx_volume))
	animation_speed = float(config.get_value("game", "animation_speed", animation_speed))
	locale = str(config.get_value("game", "locale", locale))
	reduced_motion = bool(config.get_value("game", "reduced_motion", reduced_motion))
	auto_animations = bool(config.get_value("game", "auto_animations", auto_animations))
	fullscreen = bool(config.get_value("video", "fullscreen", fullscreen))
	high_contrast = bool(config.get_value("video", "high_contrast", high_contrast))
	api_url = str(config.get_value("network", "api_url", api_url))
	last_run_id = str(config.get_value("session", "last_run_id", last_run_id))
	campaign_counter = int(config.get_value("session", "campaign_counter", campaign_counter))
	for action in ACTIONS:
		var key := int(config.get_value("input", action, DEFAULT_KEYS[action]))
		_set_action_key(action, DEFAULT_KEYS[action] if key in RESERVED_KEYS else key)
		_set_action_button(action, int(config.get_value("gamepad", action, DEFAULT_BUTTONS[action])))

func save() -> void:
	var config := ConfigFile.new()
	config.set_value("video", "text_scale", clampf(text_scale, .9, 1.2))
	config.set_value("audio", "master", master_volume)
	config.set_value("audio", "music", music_volume)
	config.set_value("audio", "sfx", sfx_volume)
	config.set_value("game", "animation_speed", animation_speed)
	config.set_value("game", "locale", locale)
	config.set_value("game", "reduced_motion", reduced_motion)
	config.set_value("game", "auto_animations", auto_animations)
	config.set_value("video", "fullscreen", fullscreen)
	config.set_value("video", "high_contrast", high_contrast)
	config.set_value("network", "api_url", _saved_api_url if _api_override else api_url)
	config.set_value("session", "last_run_id", last_run_id)
	config.set_value("session", "campaign_counter", campaign_counter)
	for action in ACTIONS:
		config.set_value("input", action, action_key(action))
		config.set_value("gamepad", action, action_button(action))
	config.save(SAVE_PATH)
	apply_window()
	apply_audio()
	changed.emit()

func apply_window() -> void:
	DisplayServer.window_set_mode(
		DisplayServer.WINDOW_MODE_FULLSCREEN if fullscreen else DisplayServer.WINDOW_MODE_WINDOWED)

func apply_audio() -> void:
	var bus := AudioServer.get_bus_index("Master")
	AudioServer.set_bus_volume_db(bus, linear_to_db(maxf(master_volume, 0.001)))
	AudioServer.set_bus_mute(bus, master_volume <= 0.001)

func remap(action: String, keycode: Key) -> String:
	var event := InputEventKey.new()
	event.physical_keycode = keycode
	return remap_event(action, event)

func remap_event(action: String, event: InputEvent, persist := true) -> String:
	if action not in ACTIONS: return "Unknown action."
	if event is InputEventKey:
		if event.physical_keycode == KEY_NONE: return "Use a keyboard key or a controller button."
		if event.physical_keycode in RESERVED_KEYS or event.ctrl_pressed or event.alt_pressed or event.meta_pressed or event.shift_pressed:
			return "This input is reserved for interface navigation."
		for other in ACTIONS:
			if other != action and action_key(other) == event.physical_keycode: return "This input is already assigned."
		_set_action_key(action, event.physical_keycode)
	elif event is InputEventJoypadButton:
		if event.button_index in RESERVED_BUTTONS: return "This input is reserved for interface navigation."
		for other in ACTIONS:
			if other != action and action_button(other) == event.button_index: return "This input is already assigned."
		_set_action_button(action, event.button_index)
	else:
		return "Use a keyboard key or a controller button."
	if persist: save()
	return ""

func reset_bindings(persist := true) -> void:
	_apply_default_keys()
	if persist: save()

func set_api_url(value: String) -> bool:
	value = value.strip_edges().trim_suffix("/")
	if not (value.begins_with("http://") or value.begins_with("https://")): return false
	api_url = value
	_saved_api_url = value
	_api_override = false
	save()
	return true

func next_campaign_seed() -> int:
	campaign_counter += 1
	save()
	return 20260911 + campaign_counter

func action_key(action: String) -> int:
	var events := InputMap.action_get_events(action)
	for event in events:
		if event is InputEventKey:
			return event.physical_keycode
	return int(DEFAULT_KEYS.get(action, KEY_NONE))

func action_label(action: String) -> String:
	return OS.get_keycode_string(action_key(action))

func _apply_default_keys() -> void:
	for action in ACTIONS:
		_set_action_key(action, DEFAULT_KEYS[action])
		_set_action_button(action, DEFAULT_BUTTONS[action])

func _set_action_key(action: String, keycode: int) -> void:
	if not InputMap.has_action(action):
		InputMap.add_action(action)
	for previous in InputMap.action_get_events(action):
		if previous is InputEventKey: InputMap.action_erase_event(action, previous)
	var event := InputEventKey.new()
	event.physical_keycode = keycode
	InputMap.action_add_event(action, event)

func action_button(action: String) -> int:
	for event in InputMap.action_get_events(action):
		if event is InputEventJoypadButton: return event.button_index
	return int(DEFAULT_BUTTONS.get(action, -1))

func controller_label(action: String) -> String:
	return "Pad %s" % action_button(action)

func _set_action_button(action: String, button: int) -> void:
	if not InputMap.has_action(action): InputMap.add_action(action)
	for previous in InputMap.action_get_events(action):
		if previous is InputEventJoypadButton: InputMap.action_erase_event(action, previous)
	var event := InputEventJoypadButton.new()
	event.button_index = button
	InputMap.action_add_event(action, event)
