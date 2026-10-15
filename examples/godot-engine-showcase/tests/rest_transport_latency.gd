extends SceneTree
## Synthetic, read-only loopback HTTP probe; no engine, rules, saves or external host.
var failures := 0
func _init() -> void: call_deferred("run_checks")
func check(ok: bool, label: String) -> void:
	print("[PASS] " if ok else "[FAIL] ", label)
	if not ok: failures += 1
func serve(server: TCPServer, response: PackedByteArray) -> void:
	var deadline := Time.get_ticks_msec() + 5000
	while not server.is_connection_available() and Time.get_ticks_msec() < deadline: await process_frame
	if not server.is_connection_available(): check(false, "HTTP fixture accepts request"); return
	var peer := server.take_connection()
	peer.poll()
	while peer.get_available_bytes() == 0 and Time.get_ticks_msec() < deadline:
		await process_frame
		peer.poll()
	if peer.get_available_bytes() > 0: peer.get_data(peer.get_available_bytes())
	peer.put_data(response)
	# Keep the socket alive until the threaded client has consumed the body.
	while peer.get_status() == StreamPeerTCP.STATUS_CONNECTED and Time.get_ticks_msec() < deadline:
		await process_frame
		peer.poll()
	peer.disconnect_from_host()
func run_checks() -> void:
	var server := TCPServer.new()
	var port := 59600
	while port < 59620 and server.listen(port, "127.0.0.1") != OK: port += 1
	if port == 59620: check(false, "loopback port available"); quit(1); return
	var transport = preload("res://scripts/engine/http_transport.gd").new()
	root.add_child(transport)
	transport.base_url = "http://127.0.0.1:%s" % port
	transport.available = true
	var payload := "x".repeat(512 * 1024)
	var body := JSON.stringify({"payload": payload}).to_utf8_buffer()
	var response := ("HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: %s\r\nConnection: close\r\n\r\n" % body.size()).to_utf8_buffer()
	response.append_array(body)
	for fps in [30, 60, 144]:
		Engine.max_fps = fps
		var samples: Array[float] = []
		var valid := true
		for sample in 100:
			var started := Time.get_ticks_usec()
			var read = transport.read("/large/%s/%s" % [fps, sample])
			serve(server, response)
			var result: Dictionary = await read.result()
			samples.append((Time.get_ticks_usec() - started) / 1000.0)
			valid = valid and result.ok and str(result.data.get("payload", "")).length() == payload.length()
		samples.sort()
		check(valid, "512 KiB response intact for 100 reads at %s FPS" % fps)
		print("REST_LOOPBACK ", JSON.stringify({"fps": fps, "samples": samples.size(), "bytes": body.size(), "p50_ms": samples[49], "p95_ms": samples[94], "max_ms": samples[99]}))
	server.stop()
	transport.queue_free()
	await process_frame
	root.get_node("GameAudio").shutdown()
	quit(0 if failures == 0 else 1)
