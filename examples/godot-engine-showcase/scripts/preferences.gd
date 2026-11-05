extends Node

signal changed
signal video_preview_changed
var _video_previous := {}
var _video_ticket := 0
var video_deadline := 0
var _applied_monitor := -2
func video_snapshot() -> Dictionary:
	return {"fullscreen": fullscreen, "lock_aspect_16_9": lock_aspect_16_9, "window_resolution": window_resolution, "window_monitor": window_monitor, "window_monitor_position": window_monitor_position}
func preview_video(values: Dictionary) -> void:
	if _video_previous.is_empty():
		_video_previous = video_snapshot()
		_video_previous["actual_size"] = get_tree().root.size
		_video_previous["actual_position"] = get_tree().root.position
	for key in values:
		if key in ["fullscreen", "lock_aspect_16_9", "window_resolution", "window_monitor"]: set(key, values[key])
	if values.has("window_monitor") and DisplayServer.get_name() != "headless":
		window_monitor_position = DisplayServer.screen_get_position(window_monitor)
	apply_window()
	_video_ticket += 1
	var ticket := _video_ticket
	video_deadline = Time.get_ticks_msec() + 15000
	video_preview_changed.emit()
	await get_tree().create_timer(15.0, true, false, true).timeout
	if ticket == _video_ticket: revert_video()
func confirm_video() -> void:
	if _video_previous.is_empty(): return
	_video_previous.clear(); _video_ticket += 1; video_deadline = 0
	save()
	video_preview_changed.emit()
func revert_video() -> void:
	if _video_previous.is_empty(): return
	var previous := _video_previous.duplicate()
	_video_previous.clear(); _video_ticket += 1; video_deadline = 0
	for key in video_snapshot(): set(key, previous[key])
	apply_window()
	if not fullscreen:
		get_tree().root.size = previous.actual_size
		get_tree().root.position = previous.actual_position
	video_preview_changed.emit()

const SAVE_PATH := "user://heroscript_showcase.cfg"
const Schema = preload("res://scripts/ui/styles/settings_schema.gd")
const DEFAULT_API_URL := "http://127.0.0.1:5271"
const ACTIONS := ["pause_game", "end_turn", "open_timeline", "confirm_action", "inspect_card", "open_character"]
const DEFAULT_KEYS := {
	"pause_game": KEY_ESCAPE,
	"end_turn": KEY_E,
	"open_timeline": KEY_T,
	"confirm_action": KEY_F, "inspect_card": KEY_V, "open_character": KEY_I
}

const DEFAULT_BUTTONS := {"pause_game": JOY_BUTTON_START, "end_turn": JOY_BUTTON_Y,
	"open_timeline": JOY_BUTTON_BACK, "confirm_action": JOY_BUTTON_RIGHT_SHOULDER,
	"inspect_card": JOY_BUTTON_X, "open_character": JOY_BUTTON_LEFT_STICK}
const RESERVED_KEYS := [KEY_ENTER, KEY_KP_ENTER, KEY_TAB, KEY_SPACE, KEY_UP, KEY_DOWN, KEY_LEFT, KEY_RIGHT]
const RESERVED_BUTTONS := [JOY_BUTTON_A, JOY_BUTTON_B, JOY_BUTTON_DPAD_UP, JOY_BUTTON_DPAD_DOWN, JOY_BUTTON_DPAD_LEFT, JOY_BUTTON_DPAD_RIGHT]
var text_scale := 1.0
const DEFAULT_RESOLUTION := Vector2i(1280, 800)
const RESOLUTIONS := [Vector2i(1280, 720), Vector2i(1280, 800), Vector2i(1366, 768),
	Vector2i(1440, 900), Vector2i(1600, 900), Vector2i(1920, 1080), Vector2i(1920, 1200),
	Vector2i(2560, 1080), Vector2i(2560, 1440), Vector2i(3440, 1440), Vector2i(3840, 2160)]
var window_resolution := DEFAULT_RESOLUTION
var lock_aspect_16_9 := false
var _applied_aspect = null
var _applied_resolution := Vector2i.ZERO
var _applied_fullscreen = null
var window_monitor := -1
var window_monitor_position = null

var master_volume := 0.80
var music_volume := 0.42
var sfx_volume := 0.75
var animation_speed := 1.0
var animation_preset := "normal"
var hand_layout_mode := "adaptive"
var hand_emphasis := 1.0
var locale := "en"
var reduced_motion := false
var reveal_text_gradually := false
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
	_migrate_legacy_preferences()
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
	if DisplayServer.get_name() != "headless":
		get_tree().root.close_requested.connect(_remember_monitor)
		var monitor_watch := Timer.new()
		monitor_watch.wait_time = 0.75
		monitor_watch.process_mode = Node.PROCESS_MODE_ALWAYS
		monitor_watch.timeout.connect(_remember_monitor)
		add_child(monitor_watch)
		monitor_watch.start()

func _migrate_legacy_preferences() -> void:
	# Direct editor launches need the same first-run preference migration as the
	# launcher. Isolated QA projects must never read or write the player's files.
	if not OS.has_feature("windows") or OS.get_user_data_dir().get_file() != "HeroScript": return
	var old_directory := OS.get_environment("APPDATA").path_join("Godot/app_userdata/HeroScript- Ember Archive")
	for filename in ["heroscript_showcase.cfg", "card_animation_profile.json"]:
		var destination := OS.get_user_data_dir().path_join(filename)
		var old_path := old_directory.path_join(filename)
		if FileAccess.file_exists(destination) or not FileAccess.file_exists(old_path): continue
		var temporary := destination + ".migration.tmp"
		if DirAccess.copy_absolute(old_path, temporary) != OK:
			push_error("Could not copy legacy preferences; original retained: " + old_path)
			continue
		if FileAccess.get_sha256(old_path) != FileAccess.get_sha256(temporary):
			push_error("Preference migration verification failed; original retained: " + old_path)
			continue
		if DirAccess.rename_absolute(temporary, destination) != OK:
			push_error("Could not publish migrated preferences; original retained: " + old_path)

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
	animation_preset = _read_option(config, "game", "animation_preset", "animation_preset", "normal")
	hand_layout_mode = _read_option(config, "game", "hand_layout", "hand_layout_mode", "adaptive")
	hand_emphasis = _read_option(config, "game", "hand_emphasis", "hand_emphasis", 1.0)
	locale = _read_option(config, "game", "locale", "locale", "en")
	reduced_motion = _read_option(config, "game", "reduced_motion", "reduced_motion", false)
	reveal_text_gradually = _read_option(config, "accessibility", "reveal_text_gradually", "reveal_text_gradually", false)
	auto_animations = _read_option(config, "game", "auto_animations", "auto_animations", true)
	fullscreen = _read_option(config, "video", "fullscreen", "fullscreen", false)
	lock_aspect_16_9 = _read_option(config, "video", "lock_aspect_16_9", "lock_aspect_16_9", false)
	var saved_monitor = config.get_value("video", "monitor", -1)
	window_monitor = saved_monitor if saved_monitor is int and saved_monitor >= -1 and saved_monitor < 256 else -1
	var saved_position = config.get_value("video", "monitor_position") if config.has_section_key("video", "monitor_position") else null
	window_monitor_position = saved_position if saved_position is Vector2i else null
	window_resolution = Vector2i(_read_dimension(config, "width", DEFAULT_RESOLUTION.x),
		_read_dimension(config, "height", DEFAULT_RESOLUTION.y))
	if not valid_resolution(window_resolution): window_resolution = DEFAULT_RESOLUTION
	if lock_aspect_16_9 and not is_16_9(window_resolution): window_resolution = Vector2i(1280, 720)
	high_contrast = _read_option(config, "video", "high_contrast", "high_contrast", false)
	var saved_url = config.get_value("network", "api_url", DEFAULT_API_URL)
	api_url = normalize_api_url(saved_url) if saved_url is String else ""
	if api_url.is_empty(): api_url = DEFAULT_API_URL
	last_run_id = str(config.get_value("session", "last_run_id", last_run_id))
	selected_setting_id = str(config.get_value("session", "selected_setting_id", selected_setting_id))
	campaign_counter = int(config.get_value("session", "campaign_counter", campaign_counter))
	resume_index.load_config(config)
	# Transfer the former default inspection key only for pre-drawer preferences.
	if not config.has_section_key("input", "open_character") and config.get_value("input", "inspect_card", KEY_V) == KEY_I:
		config.set_value("input", "inspect_card", KEY_V)
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
	if lock_aspect_16_9 and not is_16_9(window_resolution): window_resolution = Vector2i(1280, 720)
	_capture_monitor()
	var config := ConfigFile.new()
	config.set_value("video", "text_scale", clampf(text_scale, .9, 1.2))
	config.set_value("audio", "master", master_volume)
	config.set_value("audio", "music", music_volume)
	config.set_value("audio", "sfx", sfx_volume)
	config.set_value("game", "animation_speed", animation_speed)
	config.set_value("game", "animation_preset", animation_preset)
	config.set_value("game", "hand_layout", hand_layout_mode)
	config.set_value("game", "hand_emphasis", hand_emphasis)
	config.set_value("game", "locale", locale)
	config.set_value("game", "reduced_motion", reduced_motion)
	config.set_value("accessibility", "reveal_text_gradually", reveal_text_gradually)
	config.set_value("game", "auto_animations", auto_animations)
	config.set_value("video", "fullscreen", fullscreen)
	config.set_value("video", "lock_aspect_16_9", lock_aspect_16_9)
	config.set_value("video", "monitor", window_monitor)
	if window_monitor_position is Vector2i: config.set_value("video", "monitor_position", window_monitor_position)
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
	# Audio/session saves during the confirmation countdown retain confirmed video.
	if not _video_previous.is_empty():
		for pair in [["fullscreen", "fullscreen"], ["lock_aspect_16_9", "lock_aspect_16_9"], ["monitor", "window_monitor"], ["monitor_position", "window_monitor_position"]]:
			if _video_previous[pair[1]] != null: config.set_value("video", pair[0], _video_previous[pair[1]])
			elif config.has_section_key("video", pair[0]): config.erase_section_key("video", pair[0])
		config.set_value("video", "width", _video_previous.window_resolution.x)
		config.set_value("video", "height", _video_previous.window_resolution.y)
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
	if lock_aspect_16_9 and not is_16_9(window_resolution): window_resolution = Vector2i(1280, 720)
	if _applied_fullscreen == fullscreen and _applied_resolution == window_resolution and _applied_aspect == lock_aspect_16_9 and _applied_monitor == window_monitor: return
	var first_apply := _applied_fullscreen == null
	var monitor_changed := _applied_monitor != window_monitor
	_applied_fullscreen = fullscreen
	_applied_resolution = window_resolution
	_applied_aspect = lock_aspect_16_9
	var window := get_tree().root
	window.content_scale_mode = Window.CONTENT_SCALE_MODE_CANVAS_ITEMS
	window.content_scale_aspect = Window.CONTENT_SCALE_ASPECT_KEEP if lock_aspect_16_9 else Window.CONTENT_SCALE_ASPECT_EXPAND
	window.content_scale_size = Vector2i(1440, 810) if lock_aspect_16_9 else Vector2i(1440, 900)
	window.min_size = Vector2i(960, 540)
	if (first_apply or monitor_changed) and DisplayServer.get_name() != "headless":
		var positions: Array = []
		for index in DisplayServer.get_screen_count(): positions.append(DisplayServer.screen_get_position(index))
		window.current_screen = resolve_monitor(window_monitor, window_monitor_position, positions, window.current_screen)
	window.mode = Window.MODE_FULLSCREEN if fullscreen else Window.MODE_WINDOWED
	if not fullscreen:
		var requested := window_resolution
		if DisplayServer.get_name() != "headless":
			var area := DisplayServer.screen_get_usable_rect(window.current_screen)
			requested = fit_window(requested, area.size - Vector2i(32, 64))
			if lock_aspect_16_9:
				var unit := maxi(1, mini(requested.x / 16, requested.y / 9))
				requested = Vector2i(unit * 16, unit * 9)
			window.size = requested
			window.position = area.position + (area.size - requested) / 2
		else:
			window.size = requested
	_capture_monitor()
	_applied_monitor = window_monitor

static func resolve_monitor(saved_index: int, saved_position, positions: Array, fallback: int) -> int:
	if positions.is_empty(): return 0
	if saved_position is Vector2i:
		var matched := positions.find(saved_position)
		if matched >= 0: return matched
	if saved_index >= 0 and saved_index < positions.size(): return saved_index
	return fallback if fallback >= 0 and fallback < positions.size() else 0

func _capture_monitor() -> bool:
	if DisplayServer.get_name() == "headless" or not is_inside_tree(): return false
	var current := get_tree().root.current_screen
	if current < 0 or current >= DisplayServer.get_screen_count(): return false
	var origin := DisplayServer.screen_get_position(current)
	if window_monitor == current and window_monitor_position == origin: return false
	window_monitor = current
	window_monitor_position = origin
	return true

func _remember_monitor() -> void:
	# Moving between monitors persists without reapplying video or rebuilding UI.
	if _capture_monitor():
		_applied_monitor = window_monitor
		save(false)

static func valid_resolution(value: Vector2i) -> bool:
	return value.x >= 960 and value.y >= 540 and value.x <= 7680 and value.y <= 4320

static func is_16_9(value: Vector2i) -> bool:
	return value.x * 9 == value.y * 16

static func fit_window(requested: Vector2i, available: Vector2i) -> Vector2i:
	var ratio := minf(1.0, minf(float(available.x) / requested.x, float(available.y) / requested.y))
	return Vector2i(Vector2(requested) * maxf(ratio, .1))

func resolution_options() -> Array:
	var options := RESOLUTIONS.duplicate()
	var native := DisplayServer.screen_get_size() if DisplayServer.get_name() != "headless" else Vector2i.ZERO
	for value in [native, window_resolution]:
		if valid_resolution(value) and value not in options: options.append(value)
	options.sort_custom(func(a, b): return a.x < b.x or (a.x == b.x and a.y < b.y))
	if lock_aspect_16_9: options = options.filter(func(value): return is_16_9(value))
	return options

func set_resolution(value: Vector2i, persist := true) -> bool:
	if not valid_resolution(value) or (lock_aspect_16_9 and not is_16_9(value)): return false
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
