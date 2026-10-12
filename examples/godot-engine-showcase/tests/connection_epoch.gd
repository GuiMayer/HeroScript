extends SceneTree
## Delayed real HTTP replies from a loopback fixture; never contacts the user's engine.
var failures := 0
func _init() -> void: call_deferred("_run")
func check(ok: bool, label: String) -> void:
	print("[PASS] " if ok else "[FAIL] ", label)
	if not ok: failures += 1
func collect(transport, replies: Array) -> void:
	replies.append(await transport.request(HTTPClient.METHOD_GET, "/fixture", null, 3.))
func _run() -> void:
	var server := TCPServer.new()
	var port := 59400
	while port < 59420 and server.listen(port, "127.0.0.1") != OK: port += 1
	if port >= 59420:
		check(false, "loopback HTTP fixture can bind a test port"); quit(1); return
	var transport = load("res://scripts/engine/http_transport.gd").new(); root.add_child(transport)
	for status in [200, 503]:
		transport.base_url = "http://127.0.0.1:%s" % port
		transport._set_available(true)
		var replies: Array = []
		collect(transport, replies)
		var started := Time.get_ticks_msec()
		while not server.is_connection_available() and Time.get_ticks_msec() - started < 2500: await process_frame
		if not server.is_connection_available():
			check(false, "HTTP request reaches fixture"); break
		var peer := server.take_connection()
		peer.poll()
		while peer.get_available_bytes() == 0 and Time.get_ticks_msec() - started < 2500:
			await process_frame; peer.poll()
		if peer.get_available_bytes() > 0: peer.get_data(peer.get_available_bytes())
		transport.base_url = "http://127.0.0.1:59499"
		check(not transport.available and transport.last_error.is_empty(), "endpoint change resets transport status before old reply")
		transport.last_error = "new-server-status"
		peer.put_data(("HTTP/1.1 %s Fixture\r\nContent-Type: application/json\r\nContent-Length: 2\r\nConnection: close\r\n\r\n{}" % status).to_utf8_buffer())
		while replies.is_empty() and Time.get_ticks_msec() - started < 3500: await process_frame
		check(replies.size() == 1 and not replies[0].ok and replies[0].errorKey == "The engine connection changed during the request.", "old-server HTTP %s response is discarded" % status)
		check(not transport.available and transport.last_error == "new-server-status", "old-server HTTP %s cannot replace new-server availability/error" % status)
		peer.disconnect_from_host()
	server.stop(); transport.queue_free()
	for _frame in 8: await process_frame
	root.get_node("GameAudio").shutdown(); await create_timer(.08).timeout
	quit(0 if failures == 0 else 1)
