extends SceneTree
var failures := 0
func _init() -> void: call_deferred("run_checks")
func check(ok: bool, label: String) -> void:
	print("[PASS] " if ok else "[FAIL] ", label)
	if not ok: failures += 1
func reply(server) -> void:
	var deadline := Time.get_ticks_msec() + 3000
	while not server.is_connection_available() and Time.get_ticks_msec() < deadline: await process_frame
	if not server.is_connection_available(): check(false, "fixture receives request"); return
	var peer = server.take_connection(); peer.poll()
	while peer.get_available_bytes() == 0 and Time.get_ticks_msec() < deadline: await process_frame; peer.poll()
	if peer.get_available_bytes() > 0: peer.get_data(peer.get_available_bytes())
	peer.put_data("HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: 11\r\nConnection: close\r\n\r\n{\"value\":1}".to_utf8_buffer())
	await create_timer(.04).timeout; peer.disconnect_from_host()
func run_checks() -> void:
	var server := TCPServer.new(); var port := 59500
	while port < 59520 and server.listen(port, "127.0.0.1") != OK: port += 1
	if port == 59520: check(false, "fixture binds"); quit(1); return
	var transport = preload("res://scripts/engine/http_transport.gd").new(); root.add_child(transport)
	transport.max_workers = 1; transport.base_url = "http://127.0.0.1:%s" % port; transport.available = true
	var a = transport.read("/shared"); var b = transport.read("/shared"); var c = transport.read("/shared")
	for _i in 4: await process_frame
	a.cancel(); var cancelled: Dictionary = await a.result()
	check(cancelled.cancelled and not b.done, "one consumer cancels without cancelling shared GET")
	await reply(server)
	var first: Dictionary = await b.result(); var second: Dictionary = await c.result()
	check(first.ok and second.ok and transport.timings.size() == 1, "identical GET consumers use one actual request")
	first.data.value = 99
	check(second.data.value == 1, "coalesced responses are private defensive copies")
	check(transport._workers.size() == 1 and transport._workers[0].use_threads and transport._idle.size() == 1, "bounded threaded worker returns to pool")
	var next = transport.read("/next"); await reply(server); var result: Dictionary = await next.result()
	check(result.ok and transport._workers.size() == 1 and transport._queue.is_empty(), "subsequent request reuses worker and drains queue")
	server.stop(); transport.queue_free(); await process_frame; root.get_node("GameAudio").shutdown(); quit(0 if failures == 0 else 1)
