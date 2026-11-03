extends SceneTree
var failures := 0
func _init() -> void: call_deferred("run")
func check(ok: bool, label: String) -> void:
	print("[PASS] " if ok else "[FAIL] ", label)
	if not ok: failures += 1
func settle() -> void:
	for _i in 8: await process_frame
func run() -> void:
	root.get_node("I18n").set_locale("en")
	var session = load("res://tests/fixtures/support_loadout_session.gd").new()
	var screen = load("res://scenes/ui/screens/unified_loadout.tscn").instantiate()
	screen.session = session; screen.initial_tab = "companions"; root.add_child(screen); await settle()
	var view = screen.supports_view
	check(view.scene_file_path.ends_with("support_workbench.tscn") and view.get_node("Equipment").visible, "live companion tab is an editable scene with current owned equipment")
	check(session.commits.is_empty() and view.get_node("Equipment/Routine").selected == 0, "opening the tab shows confirmed routine without saving")
	check(view.get_node("Equipment/Cadence").text.contains("3"), "cadence uses published interval")
	view._routine_selected(1); await settle()
	check(session.commits == ["support"] and session.routine == "defense", "routine change validates and autosaves once")
	check(view.get_node("Equipment/RoutineDescription").text == "Gain Block.", "authoritative routine description refreshes after saving")
	session.reject_preview = true; view._routine_selected(0); await settle()
	check(session.commits.size() == 1 and session.routine == "defense", "rejected preview preserves equipped routine")
	session.reject_preview = false; session.hold_preview = true
	view._routine_selected(0); await process_frame
	check(screen.controller.busy and not screen.controller.can_leave(), "pending validation blocks leaving and competing build writes")
	screen.controller.edit_support(null, null)
	check(session.commits.size() == 1, "second intent cannot race current autosave")
	session.hold_preview = false; session.release.emit(); await settle()
	check(session.commits.size() == 2 and session.routine == "offense", "original pending intent commits once")
	session.uncertain_next = true; view._routine_selected(1); await settle()
	check(screen.controller.uncertain and not screen.controller.can_leave(), "uncertain receipt uses shared recovery without retrying equipment blindly")
	session.uncertain_next = false; screen.controller.recover(); await settle()
	check(not screen.controller.uncertain and session.commits.size() == 3 and session.routine == "defense", "recover reads authoritative state without duplicate commit")
	session.hold_preview = true; view._routine_selected(0); await process_frame
	session.run.playerEntityId = "foreign"; session.release.emit(); await settle()
	check(session.commits.size() == 3 and not screen.controller.current(), "late preview cannot equip another character")
	screen.queue_free(); await settle()
	root.get_node("GameAudio").shutdown(); quit(1 if failures else 0)
