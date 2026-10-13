extends RefCounted
## Exactly-once completion for blocking motion; decorative tails use separate owners.
signal started
signal impact
signal completed(result: String)
var done := false
var result := ""
var claimed := false
var elapsed := 0.0
var _started := false
var _hit := false
var _tweens: Array = []
var _cleanup: Array[Callable] = []
func begin() -> void:
	if done or _started: return
	_started = true; started.emit()
func hit() -> void:
	if done or _hit: return
	_hit = true; impact.emit()
func own(tween: Tween) -> void:
	claimed = true
	_tweens.append(tween)
func cleanup(action: Callable) -> void: _cleanup.append(action)
func finish(outcome := "completed") -> void:
	if done: return
	done = true; result = outcome
	for tween in _tweens:
		if tween != null and tween.is_valid(): tween.kill()
	_tweens.clear()
	for action in _cleanup:
		if action.is_valid(): action.call()
	_cleanup.clear()
	completed.emit(outcome)
