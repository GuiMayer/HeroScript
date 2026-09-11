extends Node

signal availability_changed(available: bool)

var available := false
var last_error := ""

func request(method: HTTPClient.Method, path: String, body = null) -> Dictionary:
	var node := HTTPRequest.new()
	node.timeout = 12.0
	add_child(node)
	var headers := PackedStringArray(["Accept: application/json"])
	var encoded := ""
	if body != null:
		headers.append("Content-Type: application/json")
		encoded = JSON.stringify(body)
	var error := node.request(Preferences.api_url.trim_suffix("/") + path, headers, method, encoded)
	if error != OK:
		node.queue_free()
		return _failure("Não foi possível iniciar a requisição (%s)." % error)
	var completed: Array = await node.request_completed
	node.queue_free()
	var result_code := int(completed[0])
	var status := int(completed[1])
	var raw := (completed[3] as PackedByteArray).get_string_from_utf8()
	if result_code != HTTPRequest.RESULT_SUCCESS:
		return _failure("A conexão com a engine falhou (%s)." % result_code)
	var parsed = JSON.parse_string(raw) if not raw.is_empty() else {}
	if status < 200 or status >= 300:
		var message := "HTTP %s" % status
		if parsed is Dictionary and parsed.has("error"):
			message = str(parsed.error)
		return _failure(message, status, parsed)
	_set_available(true)
	last_error = ""
	return {"ok": true, "status": status, "data": parsed}

func health() -> Dictionary:
	return await request(HTTPClient.METHOD_GET, "/api/v1/health/ready")

func _failure(message: String, status := 0, data = null) -> Dictionary:
	last_error = message
	if status == 0:
		_set_available(false)
	return {"ok": false, "status": status, "error": message, "data": data}

func _set_available(value: bool) -> void:
	if available == value:
		return
	available = value
	availability_changed.emit(value)
