extends Node

signal availability_changed(available: bool)

var available := false
var last_error := ""
var timings: Array[Dictionary] = []
var default_timeout_seconds := 30.0
var _endpoint_epoch := 0
var _pending_reads: Array = []
var max_workers := 4
var _workers: Array[HTTPRequest] = []
var _idle: Array[HTTPRequest] = []
var _queue: Array = []
var _closing := false
var _serial := 0
var _get_flights: Dictionary = {}
class GetFlight extends RefCounted:
	signal completed
	var done := false
	var consumers := 0
	var response: Dictionary = {}
	var cancellation = preload("res://scripts/engine/read_ticket.gd").new()

func _acquire(method: HTTPClient.Method, path: String, epoch: int, ticket):
	var priority := 0 if method != HTTPClient.METHOD_GET else (2 if "/timeline" in path or "/commits/" in path else 1)
	_serial += 1
	var job := {"priority": priority, "serial": _serial}
	_queue.append(job)
	while not _closing and is_inside_tree() and epoch == _endpoint_epoch and not (ticket != null and ticket.is_cancelled):
		var first: Dictionary = _queue[0]
		for candidate in _queue:
			if candidate.priority < first.priority: first = candidate
		if is_same(first, job):
			if not _idle.is_empty():
				_queue.erase(job)
				return _idle.pop_front()
			if _workers.size() < maxi(1, max_workers):
				var worker := HTTPRequest.new()
				worker.use_threads = true
				add_child(worker); _workers.append(worker); _queue.erase(job)
				return worker
		await get_tree().process_frame
	_queue.erase(job)
	return null

func _release(worker: HTTPRequest, completion) -> void:
	if worker.request_completed.is_connected(completion.finish): worker.request_completed.disconnect(completion.finish)
	if not _closing and worker not in _idle: _idle.append(worker)
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
	_closing = true
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
	if method != HTTPClient.METHOD_GET or body != null:
		return await _perform_request(method, path, body, timeout_seconds, read_ticket)
	if read_ticket != null and read_ticket.is_cancelled: return read_ticket.response.duplicate(true)
	var key := JSON.stringify([endpoint_context(), path, timeout_seconds])
	var flight = _get_flights.get(key)
	if flight == null:
		flight = GetFlight.new(); _get_flights[key] = flight
		_launch_get.call_deferred(key, flight, path, timeout_seconds)
	flight.consumers += 1
	var waiter = preload("res://scripts/engine/read_ticket.gd").new()
	var deliver := func(): waiter.finish(flight.response)
	flight.completed.connect(deliver, CONNECT_ONE_SHOT)
	if read_ticket != null: read_ticket.cancelled.connect(waiter.cancel, CONNECT_ONE_SHOT)
	if flight.done: deliver.call()
	var result: Dictionary = await waiter.result()
	if flight.completed.is_connected(deliver): flight.completed.disconnect(deliver)
	if read_ticket != null and read_ticket.cancelled.is_connected(waiter.cancel): read_ticket.cancelled.disconnect(waiter.cancel)
	flight.consumers -= 1
	if flight.consumers == 0 and not flight.done:
		if _get_flights.get(key) == flight: _get_flights.erase(key)
		# Do not cancel/join a native I/O worker reentrantly inside a consumer's
		# cancellation signal. Settle it after the current signal stack unwinds.
		flight.cancellation.cancel.call_deferred()
	return result

func _launch_get(key: String, flight, path: String, timeout_seconds: float) -> void:
	flight.response = await _perform_request(HTTPClient.METHOD_GET, path, null, timeout_seconds, flight.cancellation)
	flight.done = true
	if _get_flights.get(key) == flight: _get_flights.erase(key)
	flight.completed.emit()

func _perform_request(method: HTTPClient.Method, path: String, body = null, timeout_seconds := -1.0, read_ticket = null) -> Dictionary:
	var started := Time.get_ticks_msec()
	var endpoint_ticket := _endpoint_epoch
	var node: HTTPRequest = await _acquire(method, path, endpoint_ticket, read_ticket)
	var queue_ms := Time.get_ticks_msec() - started
	if node == null:
		if endpoint_ticket != _endpoint_epoch:
			return {"ok": false, "status": 0, "error": "The engine connection changed during the request.", "errorKey": "The engine connection changed during the request.", "errorArgs": [], "data": null}
		return {"ok": false, "cancelled": true, "status": 0, "data": null}
	var resolved_timeout := default_timeout_seconds if timeout_seconds <= 0.0 else timeout_seconds
	node.timeout = maxf(resolved_timeout, 1.0)
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
		_release(node, completion)
		return _failure("Could not start the request (%s).", 0, null, [error])
	if not completion.done: await completion.completed
	var completed: Array = completion.value
	if read_ticket != null and read_ticket.cancelled.is_connected(cancel_request): read_ticket.cancelled.disconnect(cancel_request)
	if read_ticket != null and read_ticket.is_cancelled:
		_release(node, completion)
		return {"ok": false, "cancelled": true, "status": 0, "data": null}
	if endpoint_ticket != _endpoint_epoch:
		_release(node, completion)
		# Late responses from the previous host cannot change the new host's status.
		return {"ok": false, "status": 0, "error": "The engine connection changed during the request.",
			"errorKey": "The engine connection changed during the request.", "errorArgs": [], "data": null}
	var operation := str(body.get("type", "")) if body is Dictionary else ""
	var timing := {
		"queue_ms": queue_ms,
		"method": method,
		"path": path,
		"operation": operation,
		"ms": Time.get_ticks_msec() - started
	}
	_release(node, completion)
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
