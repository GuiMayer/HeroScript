extends SceneTree

func _init() -> void:
	call_deferred("_run")

func _run() -> void:
	var demo := OS.get_environment("HEROSCRIPT_AUTOSTART_DEMO_ROOT")
	var player := OS.get_environment("HEROSCRIPT_AUTOSTART_PLAYER_ROOT")
	if demo.is_empty() or player.is_empty() or not player.contains(".runtime"):
		push_error("Startup timing requires explicit isolated roots.")
		quit(1)
		return
	var started := Time.get_ticks_msec()
	var server = load("res://scripts/engine/local_server.gd").new(demo, player, [])
	root.add_child(server)
	var phases: Array[String] = []
	server.progress_changed.connect(func(phase): phases.append(phase))
	var result: Dictionary = await server.ensure_started("http://127.0.0.1:61989")
	if not result.ok:
		push_error("The isolated API failed to start.")
		quit(1)
		return
	var ready_ms := Time.get_ticks_msec() - started
	var transport = load("res://scripts/engine/http_transport.gd").new()
	root.add_child(transport)
	transport.base_url = "http://127.0.0.1:61989"
	var session = root.get_node("GameSession")
	session.configure(load("res://scripts/engine/engine_gateway.gd").new(transport))
	var connected: bool = await session.connect_engine("prototype-test")
	var total_ms := Time.get_ticks_msec() - started
	print("STARTUP_GODOT ready_ms=%s connected_ms=%s phases=%s" % [ready_ms, total_ms, phases])
	root.get_node("GameAudio").shutdown()
	quit(0 if connected else 1)
