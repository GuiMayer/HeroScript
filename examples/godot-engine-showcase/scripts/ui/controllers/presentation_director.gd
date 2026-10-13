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
	if advanced:
		var handle = _playback.active_handle
		if handle != null:
			handle.completed.connect(func(_result): if _alive: changed.emit(), CONNECT_ONE_SHOT)
		changed.emit()
	return advanced
func _process(delta: float) -> void:
	if not _alive or _paused.call(): return
	var handle = _playback.active_handle
	if handle != null:
		handle.elapsed += delta
		# Passive readers have no blocking visual executor; no artificial delay.
		if not handle.claimed: handle.finish()
		elif handle.elapsed > watchdog_seconds:
			push_warning("Presentation watchdog settled a stalled visual handle")
			handle.finish("fallback")
		return
	if _playback.has_frames() and (_preferences.auto_animations or _playback.next_is_passive()): advance()
func skip() -> void:
	if not _alive or _paused.call(): return
	_playback.skip(); changed.emit()
func dispose() -> void:
	_alive = false; set_process(false)
	_playback.clear()
