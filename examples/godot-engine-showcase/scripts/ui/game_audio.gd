extends Node
## Cosmetic audio only: no engine RNG, commands or gameplay authority.
const MANIFEST := "res://data/audio_manifest.json"
const VOICE_COUNT := 8
var _music: AudioStreamPlayer
var _voices: Array[AudioStreamPlayer] = []
var _events: Dictionary = {}
var _streams: Dictionary = {}
var _last_played: Dictionary = {}
var _variant: Dictionary = {}
var resource_gain_cues: Dictionary = {}
var _stopped := false

func _ready() -> void:
	process_mode = Node.PROCESS_MODE_ALWAYS
	_ensure_bus("Music")
	_ensure_bus("SFX")
	_music = AudioStreamPlayer.new()
	_music.bus = "Music"
	add_child(_music)
	for i in VOICE_COUNT:
		var voice := AudioStreamPlayer.new()
		voice.bus = "SFX"
		add_child(voice)
		_voices.append(voice)
	var data = JSON.parse_string(FileAccess.get_file_as_string(MANIFEST))
	if data is Dictionary:
		_events = data.get("events", {}).duplicate(true)
		resource_gain_cues = data.get("resource_gain_cues", {}).duplicate(true)
		for event in _events.values():
			for path in event.get("clips", []):
				if not _streams.has(path) and ResourceLoader.exists(path): _streams[path] = load(path)
		var music_path := str(data.get("music", ""))
		if not music_path.is_empty() and ResourceLoader.exists(music_path):
			_music.stream = load(music_path)
			_music.play()
	Preferences.changed.connect(_apply)
	_apply()

func _ensure_bus(bus_name: String) -> void:
	if AudioServer.get_bus_index(bus_name) >= 0: return
	AudioServer.add_bus()
	var index := AudioServer.bus_count - 1
	AudioServer.set_bus_name(index, bus_name)
	AudioServer.set_bus_send(index, "Master")
	if bus_name == "SFX": AudioServer.add_bus_effect(index, AudioEffectLimiter.new())

func ui() -> void: play_event("ui")
func card() -> void: play_event("card_play")
func hit() -> void: play_event("impact")
func reward() -> void: play_event("reward")
func select_card() -> void: play_event("card_select")
func cancel() -> void: play_event("cancel")
func turn() -> void: play_event("turn")
func error() -> void: play_event("error")

func frame(frame_data: Dictionary) -> void:
	for event in preload("res://scripts/presentation/combat_audio_presenter.gd").cues(frame_data, resource_gain_cues):
		play_event(event)

func play_event(event_id: String) -> bool:
	if _stopped or not _events.has(event_id): return false
	var bus := AudioServer.get_bus_index("SFX")
	if AudioServer.is_bus_mute(bus) or AudioServer.is_bus_mute(0): return false
	var event: Dictionary = _events[event_id]
	var now := Time.get_ticks_msec()
	if now - int(_last_played.get(event_id, -100000)) < int(event.get("cooldown_ms", 60)): return false
	var clips: Array = event.get("clips", [])
	if clips.is_empty(): return false
	var variant := int(_variant.get(event_id, 0)) % clips.size()
	var stream = _streams.get(clips[variant])
	if not stream is AudioStream: return false
	var active := 0
	for voice in _voices:
		if voice.playing and voice.get_meta("event_id", "") == event_id: active += 1
	if active >= int(event.get("max_instances", 2)): return false
	var chosen := -1
	for i in _voices.size():
		if not _voices[i].playing:
			chosen = i
			break
	# Never interrupt an impact just because a menu click arrived.
	if chosen < 0: return false
	var player := _voices[chosen]
	player.volume_db = clampf(float(event.get("gain_db", -16)), -40, -10)
	player.pitch_scale = 1.0
	player.stream = stream
	player.set_meta("event_id", event_id)
	player.play()
	_variant[event_id] = variant + 1
	_last_played[event_id] = now
	return true

func _apply() -> void:
	for spec in [["Music", Preferences.music_volume], ["SFX", Preferences.sfx_volume]]:
		var bus := AudioServer.get_bus_index(spec[0])
		AudioServer.set_bus_volume_db(bus, linear_to_db(float(spec[1])))
		AudioServer.set_bus_mute(bus, float(spec[1]) <= 0.0)

func shutdown() -> void:
	_stopped = true
	if Preferences.changed.is_connected(_apply): Preferences.changed.disconnect(_apply)
	if is_instance_valid(_music):
		_music.stop()
		_music.stream = null
	for voice in _voices:
		voice.stop()
		voice.stream = null
	_streams.clear()
	_last_played.clear()

func _exit_tree() -> void: shutdown()
