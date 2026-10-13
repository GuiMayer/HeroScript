extends SceneTree
var failures := 0
func _init() -> void: call_deferred("_run")
func check(ok: bool, label: String) -> void:
	print("[PASS] " if ok else "[FAIL] ", label)
	if not ok: failures += 1
func _run() -> void:
	var transport = load("res://scripts/engine/http_transport.gd").new()
	root.add_child(transport); transport.available = true
	var handle = transport.read("/cancel-before-start")
	var completed := [0]
	handle.completed.connect(func(): completed[0] += 1)
	handle.cancel(); handle.cancel()
	var response: Dictionary = await handle.result()
	check(response.cancelled and completed[0] == 1, "cancellation completes exactly once")
	for i in 3: await process_frame
	check(transport.available and transport.get_child_count() == 0 and transport._pending_reads.is_empty(), "pre-start cancellation creates no HTTP nodes or offline status")
	var server := TCPServer.new()
	var port := 59183
	var error := server.listen(port, "127.0.0.1")
	check(error == OK, "local stalled read fixture listens")
	if error == OK:
		transport.base_url = "http://127.0.0.1:%s" % port; transport.available = true
		var pending = transport.read("/stall")
		await create_timer(.05).timeout
		pending.cancel(); response = await pending.result()
		for i in 4: await process_frame
		check(response.cancelled and transport.available and transport.get_child_count() == 0, "in-flight cancellation releases await and node without marking offline")
		var changed = transport.read("/endpoint")
		await create_timer(.05).timeout
		transport.base_url = "http://127.0.0.1:59184"
		response = await changed.result()
		check(response.cancelled and transport.endpoint_context()[1] == 2, "endpoint change cancels reads from old context")
		server.stop()
	transport.queue_free()
	for i in 4: await process_frame
	root.get_node("GameAudio").shutdown(); await create_timer(.08).timeout
	quit(0 if failures == 0 else 1)
