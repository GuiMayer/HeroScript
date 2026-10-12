extends Node

signal changed

const SAVE_PATH := "user://heroscript_showcase.cfg"
const Schema = preload("res://scripts/ui/styles/settings_schema.gd")
const DEFAULT_API_URL := "http://127.0.0.1:5271"
const ACTIONS := ["pause_game", "end_turn", "open_timeline", "confirm_action", "inspect_card"]
const DEFAULT_KEYS := {
	"pause_game": KEY_ESCAPE,
	"end_turn": KEY_E,
	"open_timeline": KEY_T,
	"confirm_action": KEY_F, "inspect_card": KEY_I
}

const DEFAULT_BUTTONS := {"pause_game": JOY_BUTTON_START, "end_turn": JOY_BUTTON_Y,
	"open_timeline": JOY_BUTTON_BACK, "confirm_action": JOY_BUTTON_RIGHT_SHOULDER,
	"inspect_card": JOY_BUTTON_X}
const RESERVED_KEYS := [KEY_ENTER, KEY_KP_ENTER, KEY_TAB, KEY_SPACE, KEY_UP, KEY_DOWN, KEY_LEFT, KEY_RIGHT]
const RESERVED_BUTTONS := [JOY_BUTTON_A, JOY_BUTTON_B, JOY_BUTTON_DPAD_UP, JOY_BUTTON_DPAD_DOWN, JOY_BUTTON_DPAD_LEFT, JOY_BUTTON_DPAD_RIGHT]
var text_scale := 1.0
const DEFAULT_RESOLUTION := Vector2i(1280, 800)
const RESOLUTIONS := [Vector2i(1280, 720), Vector2i(1280, 800), Vector2i(1366, 768),
	Vector2i(1440, 900), Vector2i(1600, 900), Vector2i(1920, 1080), Vector2i(1920, 1200),
	Vector2i(2560, 1080), Vector2i(2560, 1440), Vector2i(3440, 1440), Vector2i(3840, 2160)]
var window_resolution := DEFAULT_RESOLUTION
var _applied_resolution := Vector2i.ZERO
var _applied_fullscreen = null

var master_volume := 0.80
var music_volume := 0.42
var sfx_volume := 0.75
var animation_speed := 1.0
var hand_layout_mode := "adaptive"
var hand_emphasis := 1.0
var hand_drag_enabled := false
var locale := "en"
var reduced_motion := false
var auto_animations := true
var fullscreen := false
var high_contrast := false
var api_url := DEFAULT_API_URL
var last_run_id := ""
var selected_setting_id := "default"
var campaign_counter := 0
var resume_index = preload("res://scripts/application/run_resume_index.gd").new()
var _saved_api_url := ""
var _api_override := false

func _ready() -> void:
	load_settings()
	_saved_api_url = api_url
	for argument in OS.get_cmdline_user_args():
		if argument.begins_with("--api-url="):
			var requested := normalize_api_url(argument.trim_prefix("--api-url="))
			if not requested.is_empty():
				api_url = requested
				_api_override = true
	apply_window()
	apply_audio()

func load_settings() -> void:
	var config := ConfigFile.new()
	if config.load(SAVE_PATH) != OK:
		_apply_default_keys()
		return
	text_scale = _read_option(config, "video", "text_scale", "text_scale", 1.0)
	master_volume = _read_option(config, "audio", "master", "master_volume", .80)
	music_volume = _read_option(config, "audio", "music", "music_volume", .42)
	sfx_volume = _read_option(config, "audio", "sfx", "sfx_volume", .75)
	animation_speed = _read_option(config, "game", "animation_speed", "animation_speed", 1.0)
	hand_layout_mode = _read_option(config, "game", "hand_layout", "hand_layout_mode", "adaptive")
	hand_emphasis = _read_option(config, "game", "hand_emphasis", "hand_emphasis", 1.0)
	hand_drag_enabled = _read_option(config, "game", "hand_drag", "hand_drag_enabled", false)
	locale = _read_option(config, "game", "locale", "locale", "en")
	reduced_motion = _read_option(config, "game", "reduced_motion", "reduced_motion", false)
	auto_animations = _read_option(config, "game", "auto_animations", "auto_animations", true)
	fullscreen = _read_option(config, "video", "fullscreen", "fullscreen", false)
	window_resolution = Vector2i(_read_dimension(config, "width", DEFAULT_RESOLUTION.x),
		_read_dimension(config, "height", DEFAULT_RESOLUTION.y))
	if not valid_resolution(window_resolution): window_resolution = DEFAULT_RESOLUTION
	high_contrast = _read_option(config, "video", "high_contrast", "high_contrast", false)
	var saved_url = config.get_value("network", "api_url", DEFAULT_API_URL)
	api_url = normalize_api_url(saved_url) if saved_url is String else ""
	if api_url.is_empty(): api_url = DEFAULT_API_URL
	last_run_id = str(config.get_value("session", "last_run_id", last_run_id))
	selected_setting_id = str(config.get_value("session", "selected_setting_id", selected_setting_id))
	campaign_counter = int(config.get_value("session", "campaign_counter", campaign_counter))
	resume_index.load_config(config)
	var keys := _load_bindings(config, "input", DEFAULT_KEYS, _valid_key)
	var buttons := _load_bindings(config, "gamepad", DEFAULT_BUTTONS, _valid_button)
	for action in ACTIONS:
		_set_action_key(action, keys[action])
		_set_action_button(action, buttons[action])

func _read_option(config: ConfigFile, section: String, key: String, option: String, fallback):
	var value = config.get_value(section, key, fallback)
	var spec: Dictionary = Schema.option(option)
	if spec.has("values"): return value if value in spec["values"] else fallback
	if spec.get("type") == "bool": return value if value is bool else fallback
	if not (value is int or value is float) or not is_finite(float(value)): return fallback
	return clampf(float(value), float(spec.min), float(spec.max))

func _read_dimension(config: ConfigFile, key: String, fallback: int) -> int:
	var value = config.get_value("video", key, fallback)
	return value if value is int and value >= 0 and value <= 7680 else fallback

func _load_bindings(config: ConfigFile, section: String, defaults: Dictionary, valid: Callable) -> Dictionary:
	var result := {}
	var used: Array = []
	# Reserve valid custom bindings before allocating replacement defaults.
	for action in ACTIONS:
		var value = config.get_value(section, action, defaults[action])
		if valid.call(value) and value not in used:
			result[action] = value
			used.append(value)
	for action in ACTIONS:
		if result.has(action): continue
		var candidates: Array = [defaults[action]] + defaults.values()
		for candidate in candidates:
			if candidate not in used:
				result[action] = candidate
				used.append(candidate)
				break
	return result

static func _valid_key(value) -> bool:
	if not value is int or value <= KEY_NONE or value in RESERVED_KEYS: return false
	# Reject modifier masks and undefined special keys, not just KEY_NONE.
	var label := OS.get_keycode_string(value)
	return not label.is_empty() and OS.find_keycode_from_string(label) == value

static func _valid_button(value) -> bool:
	return value is int and value >= 0 and value < JOY_BUTTON_MAX and value not in RESERVED_BUTTONS

func save(notify := true) -> void:
	var config := ConfigFile.new()
	config.set_value("video", "text_scale", clampf(text_scale, .9, 1.2))
	config.set_value("audio", "master", master_volume)
	config.set_value("audio", "music", music_volume)
	config.set_value("audio", "sfx", sfx_volume)
	config.set_value("game", "animation_speed", animation_speed)
	config.set_value("game", "hand_layout", hand_layout_mode)
	config.set_value("game", "hand_emphasis", hand_emphasis)
	config.set_value("game", "hand_drag", hand_drag_enabled)
	config.set_value("game", "locale", locale)
	config.set_value("game", "reduced_motion", reduced_motion)
	config.set_value("game", "auto_animations", auto_animations)
	config.set_value("video", "fullscreen", fullscreen)
	config.set_value("video", "width", window_resolution.x)
	config.set_value("video", "height", window_resolution.y)
	config.set_value("video", "high_contrast", high_contrast)
	config.set_value("network", "api_url", _saved_api_url if _api_override else api_url)
	config.set_value("session", "last_run_id", last_run_id)
	config.set_value("session", "selected_setting_id", selected_setting_id)
	config.set_value("session", "campaign_counter", campaign_counter)
	resume_index.write_config(config)
	for action in ACTIONS:
		config.set_value("input", action, action_key(action))
		config.set_value("gamepad", action, action_button(action))
	config.save(SAVE_PATH)
	if notify:
		apply_window()
		apply_audio()
		changed.emit()

func save_session() -> void:
	# Bookmark updates must not reapply video/audio or rebuild the UI.
	save(false)

func apply_window() -> void:
	# Audio/input saves must not reset a manually resized or maximized window.
	if _applied_fullscreen == fullscreen and _applied_resolution == window_resolution: return
	_applied_fullscreen = fullscreen
	_applied_resolution = window_resolution
	var window := get_tree().root
	window.content_scale_mode = Window.CONTENT_SCALE_MODE_CANVAS_ITEMS
	window.content_scale_aspect = Window.CONTENT_SCALE_ASPECT_EXPAND
	window.content_scale_size = Vector2i(1440, 900)
	window.min_size = Vector2i(960, 540)
	window.mode = Window.MODE_FULLSCREEN if fullscreen else Window.MODE_WINDOWED
	if not fullscreen:
		var requested := window_resolution
		if DisplayServer.get_name() != "headless":
			var area := DisplayServer.screen_get_usable_rect(window.current_screen)
			requested = fit_window(requested, area.size - Vector2i(32, 64))
			window.size = requested
			window.position = area.position + (area.size - requested) / 2
		else:
			window.size = requested

static func valid_resolution(value: Vector2i) -> bool:
	return value.x >= 960 and value.y >= 540 and value.x <= 7680 and value.y <= 4320

static func fit_window(requested: Vector2i, available: Vector2i) -> Vector2i:
	var ratio := minf(1.0, minf(float(available.x) / requested.x, float(available.y) / requested.y))
	return Vector2i(Vector2(requested) * maxf(ratio, .1))

func resolution_options() -> Array:
	var options := RESOLUTIONS.duplicate()
	var native := DisplayServer.screen_get_size() if DisplayServer.get_name() != "headless" else Vector2i.ZERO
	for value in [native, window_resolution]:
		if valid_resolution(value) and value not in options: options.append(value)
	options.sort_custom(func(a, b): return a.x < b.x or (a.x == b.x and a.y < b.y))
	return options

func set_resolution(value: Vector2i, persist := true) -> bool:
	if not valid_resolution(value): return false
	window_resolution = value
	if persist: save()
	else: apply_window()
	return true

func apply_audio() -> void:
	var bus := AudioServer.get_bus_index("Master")
	AudioServer.set_bus_volume_db(bus, linear_to_db(master_volume))
	AudioServer.set_bus_mute(bus, master_volume <= 0.0)

func remap(action: String, keycode: Key) -> String:
	var event := InputEventKey.new()
	event.physical_keycode = keycode
	return remap_event(action, event)

func remap_event(action: String, event: InputEvent, persist := true) -> String:
	if action not in ACTIONS: return "Unknown action."
	if event is InputEventKey:
		if event.physical_keycode in RESERVED_KEYS or event.ctrl_pressed or event.alt_pressed or event.meta_pressed or event.shift_pressed:
			return "This input is reserved for interface navigation."
		if not _valid_key(int(event.physical_keycode)): return "Use a keyboard key or a controller button."
		for other in ACTIONS:
			if other != action and action_key(other) == event.physical_keycode: return "This input is already assigned."
		_set_action_key(action, event.physical_keycode)
	elif event is InputEventJoypadButton:
		if event.button_index in RESERVED_BUTTONS: return "This input is reserved for interface navigation."
		if not _valid_button(int(event.button_index)): return "Use a keyboard key or a controller button."
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
	value = normalize_api_url(value)
	if value.is_empty(): return false
	api_url = value
	_saved_api_url = value
	_api_override = false
	save()
	return true

static func normalize_api_url(value: String) -> String:
	value = value.strip_edges()
	for character in value:
		if character.unicode_at(0) <= 32 or character.unicode_at(0) == 127: return ""
	if value.contains("\\"): return ""
	var parser := RegEx.new()
	parser.compile("(?i)^(https?)://(\\[[0-9a-f:.]+\\]|[a-z0-9.-]+)(?::([0-9]{1,5}))?(/[^?#]*)?$")
	var parsed := parser.search(value)
	if parsed == null: return ""
	var host := parsed.get_string(2)
	if host.begins_with("["):
		if not host.contains(":") or not host.substr(1, host.length() - 2).is_valid_ip_address(): return ""
	else:
		if host.length() > 253: return ""
		if host.contains(".") and host.replace(".", "").is_valid_int() and not host.is_valid_ip_address(): return ""
		var label := RegEx.new()
		label.compile("^[a-zA-Z0-9](?:[a-zA-Z0-9-]{0,61}[a-zA-Z0-9])?$")
		for part in host.trim_suffix(".").split("."):
			if label.search(part) == null: return ""
	var port := parsed.get_string(3)
	if not port.is_empty() and (int(port) < 1 or int(port) > 65535): return ""
	var path := parsed.get_string(4)
	while path.ends_with("/"): path = path.trim_suffix("/")
	return parsed.get_string(1).to_lower() + "://" + host.to_lower() + (":" + port if not port.is_empty() else "") + path

func next_campaign_seed() -> int:
	var setting: Dictionary = GameSession.selected_setting
	var launch: Dictionary = setting.get("launch", {})
	var counter: int = resume_index.next_counter(api_url, str(launch.get("playerEntityId", "player")),
		GameSession.selected_setting_id, str(launch.get("modeId", "")), campaign_counter)
	save_session()
	return 20260911 + counter

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
	var event := InputEventKey.new()
	event.physical_keycode = keycode
	_replace_action_event(action, event)

func action_button(action: String) -> int:
	for event in InputMap.action_get_events(action):
		if event is InputEventJoypadButton: return event.button_index
	return int(DEFAULT_BUTTONS.get(action, -1))

func controller_label(action: String) -> String:
	return "Pad %s" % action_button(action)

func _set_action_button(action: String, button: int) -> void:
	var event := InputEventJoypadButton.new()
	event.button_index = button
	_replace_action_event(action, event)

func _replace_action_event(action: String, event: InputEvent) -> void:
	if not InputMap.has_action(action): InputMap.add_action(action)
	var retained: Array = InputMap.action_get_events(action).filter(func(previous): return previous.get_class() != event.get_class())
	# Removing a malformed empty key individually can fail Godot's event matching.
	InputMap.action_erase_events(action)
	for previous in retained: InputMap.action_add_event(action, previous)
	InputMap.action_add_event(action, event)
