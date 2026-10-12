extends SceneTree
var failures := 0
class Session extends RefCounted:
	var busy := false
	var synchronized := true
	var calls: Array = []
	func submit_activity(choice: Dictionary) -> bool: calls.append(choice.duplicate(true)); return true
func _init() -> void: call_deferred("_run")
func check(ok: bool, label: String) -> void:
	print("[PASS] " if ok else "[FAIL] ", label)
	if not ok: failures += 1
func _run() -> void:
	var session := Session.new()
	var controller = load("res://scripts/ui/controllers/dialogue_controller.gd").new(session)
	var choice := {"type": "CHOOSE_DIALOGUE_OPTION", "payload": {"dialogueInstanceId": "d", "nodeId": "n", "choiceId": "accept"}}
	var run := {"runId": "r", "currentNodeId": "stop"}
	var model := {"dialogueInstanceId": "d", "nodeId": "n", "options": [{"available": true, "reference": "current", "command": choice}]}
	controller.bind(run, [choice], model)
	check(not await controller.submit("old") and session.calls.is_empty(), "stale dialogue reference cannot submit")
	check(await controller.submit("current") and session.calls == [choice], "controller submits exactly the published option without interpreting accept")
	session.busy = true
	check(not await controller.submit("current") and session.calls.size() == 1, "busy blocks repeated submissions")
	session.busy = false; controller.dispose()
	check(not await controller.submit("current"), "disposed dialogue cannot execute callbacks")
	var source := FileAccess.get_file_as_string("res://scripts/ui/components/activity_options_view.gd")
	check(not source.contains("dialogue_panel.gd"), "activity options have no embedded dialogue path")
	source = FileAccess.get_file_as_string("res://scripts/ui/dialogue_view.gd")
	check(not ["GameSession", "Preferences.", "I18n.", "AppTheme."].any(func(token): return source.contains(token)), "dedicated dialogue view is independent of globals/gameplay services")
	root.get_node("GameAudio").shutdown(); await create_timer(.08).timeout
	quit(0 if failures == 0 else 1)
