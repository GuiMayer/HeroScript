extends RefCounted
## One completion for every read, including cancellation before HTTP starts.
signal completed
signal cancelled
var done := false
var is_cancelled := false
var response: Dictionary = {}
func finish(value: Dictionary) -> void:
	if done: return
	done = true; response = value
	completed.emit()
func cancel() -> void:
	if done: return
	is_cancelled = true
	cancelled.emit()
	finish({"ok": false, "cancelled": true, "status": 0, "error": "Request cancelled.", "errorKey": "Request cancelled.", "data": null})
func result() -> Dictionary:
	if not done: await completed
	return response.duplicate(true)
