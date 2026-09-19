extends Node

signal availability_changed(available: bool)

var base_url := "http://127.0.0.1:5271"
var available := false
var last_error := ""
var timings: Array[Dictionary] = []
var default_timeout_seconds := 30.0

class RequestBatch extends RefCounted:
	signal completed
	var remaining := 0
	var results: Dictionary = {}

func request_many(paths: Dictionary) -> Dictionary:
	var batch := RequestBatch.new()
	batch.remaining = paths.size()
	for key in paths:
		_request_part(batch, key, paths[key])
	if batch.remaining > 0:
		await batch.completed
	return batch.results

func _request_part(batch: RequestBatch, key: String, path: String) -> void:
	batch.results[key] = await request(HTTPClient.METHOD_GET, path)
	batch.remaining -= 1
	if batch.remaining == 0:
		batch.completed.emit()

func request(method: HTTPClient.Method, path: String, body = null, timeout_seconds := -1.0) -> Dictionary:
	var started := Time.get_ticks_msec()
	var node := HTTPRequest.new()
	var resolved_timeout := default_timeout_seconds if timeout_seconds <= 0.0 else timeout_seconds
	node.timeout = maxf(resolved_timeout, 1.0)
	add_child(node)
	var headers := PackedStringArray(["Accept: application/json"])
	var encoded := ""
	if body != null:
		headers.append("Content-Type: application/json")
		encoded = JSON.stringify(body)
	var error := node.request(base_url.trim_suffix("/") + path, headers, method, encoded)
	if error != OK:
		node.queue_free()
		return _failure("Could not start the request (%s).", 0, null, [error])
	var completed: Array = await node.request_completed
	var operation := str(body.get("type", "")) if body is Dictionary else ""
	timings.append({
		"method": method,
		"path": path,
		"operation": operation,
		"ms": Time.get_ticks_msec() - started
	})
	if timings.size() > 200:
		timings.pop_front()
	node.queue_free()
	var result_code := int(completed[0])
	var status := int(completed[1])
	var raw := (completed[3] as PackedByteArray).get_string_from_utf8()
	if result_code != HTTPRequest.RESULT_SUCCESS:
		return _failure("Connection to the engine failed (%s).", 0, null, [result_code])
	var parsed = JSON.parse_string(raw) if not raw.is_empty() else {}
	if status >= 200 and status < 300 and not (parsed is Dictionary or parsed is Array):
		return _failure("The engine returned an invalid JSON response.")
	if status < 200 or status >= 300:
		var message := "HTTP %s" % status
		if parsed is Dictionary and parsed.has("error"):
			message = str(parsed.error)
		elif parsed is Dictionary:
			message = str(parsed.get("detail", parsed.get("title", message)))
		return _failure(message, status, parsed)
	_set_available(true)
	last_error = ""
	return {"ok": true, "status": status, "data": parsed}

func _failure(message: String, status := 0, data = null, arguments: Array = []) -> Dictionary:
	last_error = message % arguments if not arguments.is_empty() else message
	if status == 0:
		_set_available(false)
	return {"ok": false, "status": status, "error": last_error, "errorKey": message if status == 0 else "", "errorArgs": arguments, "data": data}

func _set_available(value: bool) -> void:
	if available == value:
		return
	available = value
	availability_changed.emit(value)
