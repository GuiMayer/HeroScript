extends SceneTree
var failures := 0
func _init() -> void: call_deferred("_run")
func check(ok: bool, label: String) -> void:
	print("[PASS] " if ok else "[FAIL] ", label)
	if not ok: failures += 1
func _run() -> void:
	var demo := OS.get_environment("HEROSCRIPT_AUTOSTART_DEMO_ROOT")
	var player := OS.get_environment("HEROSCRIPT_AUTOSTART_PLAYER_ROOT")
	if demo.is_empty() or player.is_empty() or not player.contains(".runtime"):
		push_error("Online autostart test requires explicit isolated roots.")
		quit(1)
		return
	var server = load("res://scripts/engine/local_server.gd").new(demo, player, [])
	root.add_child(server)
	var phases: Array = []
	server.progress_changed.connect(func(phase): phases.append(phase))
	var result: Dictionary = await server.ensure_started("http://127.0.0.1:61986")
	check(result.ok and server.launcher_pid > 0, "Godot creates the hidden launcher and awaits the real local API")
	check("loading_settings" in phases, "real API publishes loading progress before readiness")
	var reuse = load("res://scripts/engine/local_server.gd").new(demo, player, [])
	root.add_child(reuse)
	check((await reuse.ensure_started("http://127.0.0.1:61986")).ok and reuse.launcher_pid == -1, "Godot reuses a ready API without spawning a second launcher")
	check(FileAccess.file_exists(player.path_join("qa/61986/engine/migration-v1.json")), "Godot-started API uses the explicitly isolated store")
	server.queue_free()
	reuse.queue_free()
	root.get_node("GameAudio").shutdown()
	await create_timer(.12).timeout
	quit(0 if failures == 0 else 1)
