extends SceneTree
var failures := 0
class Gateway extends RefCounted:
	var items: Array = []
	func connect_engine() -> Dictionary:
		return {"ok": true, "data": {"items": items.duplicate(true)}}
func _init() -> void: run.call_deferred()
func check(ok: bool, message: String) -> void:
	print("[PASS] " if ok else "[FAIL] ", message)
	if not ok: failures += 1
func run() -> void:
	var prefs = root.get_node("Preferences")
	var session = root.get_node("GameSession")
	var gateway := Gateway.new()
	session.configure(gateway)
	var volatile := {"settingId": "volatile-core", "currentRevision": "v", "displayName": "Volatile Crucible"}
	var mvp := {"settingId": "prototype-test", "currentRevision": "p", "displayName": "MVP"}
	gateway.items = [volatile, mvp]
	prefs.selected_setting_id = "prototype-test"
	prefs.save_session()
	prefs.selected_setting_id = "default"
	prefs.load_settings()
	check(prefs.selected_setting_id == "prototype-test", "MVP setting survives preferences reload")
	var router = load("res://main.tscn").instantiate()
	root.add_child(router)
	await process_frame
	await router._probe_engine()
	check(session.selected_setting_id == "prototype-test", "startup restores saved MVP instead of first server entry")
	gateway.items = [volatile]
	await router._probe_engine()
	check(session.selected_setting_id == "volatile-core" and prefs.selected_setting_id == "prototype-test", "missing MVP does not overwrite saved preference")
	prefs.selected_setting_id = "default"
	prefs.load_settings()
	check(prefs.selected_setting_id == "prototype-test", "saved MVP survives temporary server omission")
	gateway.items = [volatile, mvp]
	await router._probe_engine()
	check(session.selected_setting_id == "prototype-test", "MVP returns when server publishes it again")
	router.queue_free()
	await process_frame
	root.get_node("GameAudio").shutdown()
	quit(1 if failures else 0)
