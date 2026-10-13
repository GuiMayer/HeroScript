extends Node
## One coordinator, one cursor. Handles, not a frame countdown, gate the next beat.
signal changed
var _playback
var _preferences
var _paused: Callable
var _alive := true
var watchdog_seconds := 12.0
func _init(playback, preferences, paused: Callable) -> void:
	_playback = playback; _preferences = preferences; _paused = paused
func advance() -> bool:
	if not _alive or _paused.call() or _playback.active_handle != null: return false
	var advanced: bool = _playback.advance()
	if advanced: changed.emit()
	return advanced
func _process(delta: float) -> void:
	if not _alive or _paused.call(): return
	var handle = _playback.active_handle
	if handle != null:
		handle.elapsed += delta
		# Passive readers have no executor; live combat claims every handle.
		if not handle.claimed and handle.elapsed >= .32 / maxf(_preferences.animation_speed, .25): handle.finish()
		elif handle.elapsed > watchdog_seconds:
			push_warning("Presentation watchdog settled a stalled visual handle")
			handle.finish("fallback")
		changed.emit()
		return
	if _preferences.auto_animations and _playback.has_frames(): advance()
func skip() -> void:
	if not _alive or _paused.call(): return
	_playback.skip(); changed.emit()
func dispose() -> void:
	_alive = false; set_process(false)
	_playback.clear()
