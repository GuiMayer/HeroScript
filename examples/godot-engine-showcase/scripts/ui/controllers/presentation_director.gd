extends Node
## One coordinator, one cursor. Handles, not a frame countdown, gate the next beat.
signal changed
var _playback
var _preferences
var _paused: Callable
var _alive := true
var _pumping := false
var pump_budget_usec := 4000
var dispatches := 0
var instant_dispatches := 0
var budget_yields := 0
var longest_pump_usec := 0
var watchdog_seconds := 12.0
func _init(playback, preferences, paused: Callable) -> void:
	_playback = playback; _preferences = preferences; _paused = paused
func advance() -> bool:
	return _pump(true)
func _pump(manual: bool = false) -> bool:
	if not _alive or _pumping or _paused.call() or _playback.active_handle != null: return false
	_pumping = true
	var ticket: int = _playback.generation
	var start := Time.get_ticks_usec()
	var advanced := false
	while _alive and ticket == _playback.generation and not _paused.call() and _playback.active_handle == null and _playback.has_frames():
		var passive: bool = _playback.next_is_passive()
		if not _preferences.auto_animations and not passive and not manual: break
		if not _playback.advance(): break
		dispatches += 1
		advanced = true
		if not passive: manual = false
		var handle = _playback.active_handle
		if handle == null: instant_dispatches += 1
		else:
			handle.completed.connect(func(_result):
				if _alive and not _pumping and ticket == _playback.generation:
					_pump(); changed.emit(), CONNECT_ONE_SHOT)
		if Time.get_ticks_usec() - start >= pump_budget_usec:
			if handle == null and _playback.has_frames(): budget_yields += 1
			break
	longest_pump_usec = maxi(longest_pump_usec, Time.get_ticks_usec() - start)
	_pumping = false
	if advanced and _alive: changed.emit()
	return advanced
func metrics() -> Dictionary:
	return {"dispatches": dispatches, "instant": instant_dispatches, "cpu_yields": budget_yields, "longest_pump_us": longest_pump_usec}
func reset_metrics() -> void:
	dispatches = 0; instant_dispatches = 0; budget_yields = 0; longest_pump_usec = 0
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
		if _playback.active_handle != null: return
		changed.emit()
	_pump()
func skip() -> void:
	if not _alive or _paused.call(): return
	_playback.skip(); changed.emit()
func dispose() -> void:
	_alive = false; set_process(false)
	_playback.clear()
