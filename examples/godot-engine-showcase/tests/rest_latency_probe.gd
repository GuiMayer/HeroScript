extends SceneTree
## Read-only probe. No command, save, content or profile mutations.
func _init() -> void: call_deferred("probe")
func probe() -> void:
	var transport = preload("res://scripts/engine/http_transport.gd").new()
	var run_id := ""
	Engine.max_fps = 60
	for argument in OS.get_cmdline_user_args():
		if argument.begins_with("--api-url="): transport.base_url = argument.trim_prefix("--api-url=")
		if argument.begins_with("--probe-run="): run_id = argument.trim_prefix("--probe-run=")
		if argument.begins_with("--probe-fps="): Engine.max_fps = int(argument.trim_prefix("--probe-fps="))
	root.add_child(transport)
	var paths := ["/api/v1/health/live"]
	if not run_id.is_empty(): paths.append("/api/v1/runs/%s" % run_id.uri_encode())
	var failed := false
	for path in paths:
		for sample in 5:
			var result: Dictionary = await transport.request(HTTPClient.METHOD_GET, path)
			failed = failed or not result.ok
			print("REST_METRIC ", JSON.stringify({"fps": Engine.max_fps, "sample": sample, "ok": result.ok, "timing": transport.timings[-1] if not transport.timings.is_empty() else {}}))
	transport.queue_free(); root.get_node("GameAudio").shutdown(); quit(1 if failed else 0)
