extends Node

signal availability_changed(available: bool)

var available := false
var last_error := ""
var timings: Array[Dictionary] = []
var default_timeout_seconds := 30.0
var _endpoint_epoch := 0
var _pending_reads: Array = []
var base_url := "http://127.0.0.1:5271":
	set(value):
		if base_url == value: return
		base_url = value
		_endpoint_epoch += 1
		for ticket in _pending_reads.duplicate(): ticket.cancel()
		_set_available(false)
		last_error = ""

class RequestBatch extends RefCounted:
	signal completed
	var remaining := 0
	var results: Dictionary = {}

class HttpCompletion extends RefCounted:
	signal completed
	var done := false
	var value: Array = []
	func finish(result_code: int, status: int, headers: PackedStringArray, body: PackedByteArray) -> void:
		if done: return
		done = true; value = [result_code, status, headers, body]; completed.emit()

func endpoint_context() -> Array: return [base_url, _endpoint_epoch]
func read(path: String):
	var ticket = preload("res://scripts/engine/read_ticket.gd").new()
	_pending_reads.append(ticket)
	ticket.completed.connect(func(): _pending_reads.erase(ticket), CONNECT_ONE_SHOT)
	_run_read.call_deferred(path, ticket)
	return ticket
func _run_read(path: String, ticket) -> void:
	if ticket.is_cancelled: return
	ticket.finish(await request(HTTPClient.METHOD_GET, path, null, -1., ticket))
func _exit_tree() -> void:
	for ticket in _pending_reads.duplicate(): ticket.cancel()

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

func request(method: HTTPClient.Method, path: String, body = null, timeout_seconds := -1.0, read_ticket = null) -> Dictionary:
	var started := Time.get_ticks_msec()
	var endpoint_ticket := _endpoint_epoch
	var node := HTTPRequest.new()
	var resolved_timeout := default_timeout_seconds if timeout_seconds <= 0.0 else timeout_seconds
	node.timeout = maxf(resolved_timeout, 1.0)
	add_child(node)
	var completion := HttpCompletion.new()
	node.request_completed.connect(completion.finish)
	var cancel_request := func():
		if is_instance_valid(node): node.cancel_request()
		completion.finish(HTTPRequest.RESULT_REQUEST_FAILED, 0, PackedStringArray(), PackedByteArray())
	if read_ticket != null: read_ticket.cancelled.connect(cancel_request, CONNECT_ONE_SHOT)
	var headers := PackedStringArray(["Accept: application/json"])
	var encoded := ""
	if body != null:
		headers.append("Content-Type: application/json")
		encoded = JSON.stringify(body)
	var error := node.request(base_url.trim_suffix("/") + path, headers, method, encoded)
	if error != OK:
		if read_ticket != null and read_ticket.cancelled.is_connected(cancel_request): read_ticket.cancelled.disconnect(cancel_request)
		node.queue_free()
		return _failure("Could not start the request (%s).", 0, null, [error])
	if not completion.done: await completion.completed
	var completed: Array = completion.value
	if read_ticket != null and read_ticket.cancelled.is_connected(cancel_request): read_ticket.cancelled.disconnect(cancel_request)
	if read_ticket != null and read_ticket.is_cancelled:
		node.queue_free()
		return {"ok": false, "cancelled": true, "status": 0, "data": null}
	if endpoint_ticket != _endpoint_epoch:
		node.queue_free()
		# Late responses from the previous host cannot change the new host's status.
		return {"ok": false, "status": 0, "error": "The engine connection changed during the request.",
			"errorKey": "The engine connection changed during the request.", "errorArgs": [], "data": null}
	var operation := str(body.get("type", "")) if body is Dictionary else ""
	var timing := {
		"method": method,
		"path": path,
		"operation": operation,
		"ms": Time.get_ticks_msec() - started
	}
	node.queue_free()
	var result_code := int(completed[0])
	var status := int(completed[1])
	var raw := (completed[3] as PackedByteArray).get_string_from_utf8()
	if result_code != HTTPRequest.RESULT_SUCCESS:
		return _failure("Connection to the engine failed (%s).", 0, null, [result_code])
	var parsed = JSON.parse_string(raw) if not raw.is_empty() else {}
	timing["parse_ms"] = Time.get_ticks_msec() - started - int(timing.ms)
	timing["bytes"] = (completed[3] as PackedByteArray).size()
	timing["transfer_ms"] = timing.ms
	timing["ms"] = Time.get_ticks_msec() - started
	timings.append(timing)
	if timings.size() > 200: timings.pop_front()
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
