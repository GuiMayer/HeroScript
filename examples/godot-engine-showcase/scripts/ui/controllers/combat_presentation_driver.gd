extends Node
## Timing only. The existing Playback remains the sole presentation cursor.
signal changed
var _playback
var _preferences
var _paused: Callable
var _generation := -1
var _delay := 0.0
var _alive := true

func _init(playback, preferences, paused: Callable) -> void:
	_playback = playback
	_preferences = preferences
	_paused = paused

func advance() -> bool:
	if not _alive or _paused.call(): return false
	_generation = _playback.generation
	var advanced: bool = _playback.advance()
	if advanced:
		_delay = .32 / maxf(_preferences.animation_speed, .25)
		changed.emit()
	return advanced

func _process(delta: float) -> void:
	if not _alive or _paused.call(): return
	if _generation != _playback.generation:
		_generation = _playback.generation
		_delay = 0.0
	_delay -= delta
	if _preferences.auto_animations and _delay <= 0.0 and _playback.has_frames(): advance()

func dispose() -> void:
	_alive = false
	set_process(false)
