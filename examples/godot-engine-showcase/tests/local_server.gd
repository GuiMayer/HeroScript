extends SceneTree
var failures := 0
const Server = preload("res://scripts/engine/local_server.gd")
func _init() -> void: call_deferred("_run")
func check(ok: bool, label: String) -> void:
	print("[PASS] " if ok else "[FAIL] ", label)
	if not ok: failures += 1
func _run() -> void:
	var demo := "C:/Project with spaces/showcase"
	for url in ["http://127.0.0.1:5271", "http://localhost:5272/"]:
		var plan: Dictionary = Server.launch_plan(url, [], demo, true, true)
		check(not plan.is_empty() and plan.script == demo.path_join("Start-Demo.ps1"), "local editor URL produces the shared launcher plan: " + url)
	for url in ["https://127.0.0.1:5271", "http://192.168.1.2:5271", "http://example.com:5271", "http://localhost:0", "http://localhost:99999", "http://127.0.0.1:5271/other", "http://127.0.0.1:5271?x=1"]:
		check(Server.launch_plan(url, [], demo, true, true).is_empty(), "never launches a server for an unsupported endpoint: " + url)
	check(Server.launch_plan("http://localhost:5271", ["--layout-smoke"], demo, true, true).is_empty(), "offline tests never spawn servers")
	check(Server.launch_plan("http://localhost:5271", ["--no-auto-server"], demo, true, true).is_empty(), "explicit opt-out is respected")
	check(Server.launch_plan("http://localhost:5271", [], demo, false, true).is_empty(), "export without source/host is not advertised as auto-launchable")
	check(Server.launch_plan("http://localhost:5271", [], demo, true, false).is_empty(), "unsupported OS retains normal remote connection behavior")
	check(Server.startup_error_key("port_in_use").contains("port"), "occupied port has a specific startup error")
	check(Server.startup_error_key("build_failed").contains("built"), "build failure has a specific startup error")
	check(Server.startup_error_key("unknown").contains("launcher"), "unknown startup failure falls back to the generic error")
	var server := Server.new()
	root.add_child(server)
	check(server.startup_timeout_seconds >= 300.0, "cold builds can exceed two minutes without abandoning startup")
	check((await server.ensure_started("http://remote.example:5271")).ok and server.launcher_pid == -1, "remote configuration delegates to ordinary connection without creating a process")
	server.queue_free()
	root.get_node("GameAudio").shutdown()
	await create_timer(.12).timeout
	quit(0 if failures == 0 else 1)
