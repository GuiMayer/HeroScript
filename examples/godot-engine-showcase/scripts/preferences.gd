extends Node

signal changed

const SAVE_PATH := "user://heroscript_showcase.cfg"
const ACTIONS := ["pause_game", "end_turn", "open_timeline", "confirm_action"]
const DEFAULT_KEYS := {
	"pause_game": KEY_ESCAPE,
	"end_turn": KEY_E,
	"open_timeline": KEY_T,
	"confirm_action": KEY_SPACE
}

var master_volume := 0.80
var music_volume := 0.42
var sfx_volume := 0.75
var animation_speed := 1.0
var locale := "pt_BR"
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
		_set_action_key(action, int(config.get_value("input", action, DEFAULT_KEYS[action])))

func save() -> void:
	var config := ConfigFile.new()
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

func remap(action: String, keycode: Key) -> void:
	_set_action_key(action, keycode)
	save()

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

func _set_action_key(action: String, keycode: int) -> void:
	if not InputMap.has_action(action):
		InputMap.add_action(action)
	InputMap.action_erase_events(action)
	var event := InputEventKey.new()
	event.physical_keycode = keycode
	InputMap.action_add_event(action, event)
