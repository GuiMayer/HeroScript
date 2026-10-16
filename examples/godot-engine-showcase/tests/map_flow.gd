extends SceneTree
var failures := 0
var results: Array = []
class Session extends RefCounted:
	signal completed
	var busy := false
	var calls: Array = []
	var delayed := false
	var accepted := true
	func submit_activity(choice: Dictionary) -> bool:
		calls.append(choice.duplicate(true))
		if delayed: await completed
		return accepted
func _init() -> void: call_deferred("_run")
func check(ok: bool, label: String) -> void:
	print("[PASS] " if ok else "[FAIL] ", label)
	if not ok: failures += 1
func execute(controller, choice: Dictionary) -> void:
	results.append(await controller.execute(choice))
func _run() -> void:
	var session := Session.new()
	var controller = load("res://scripts/ui/controllers/activity_controller.gd").new(session)
	var choice := {"type":"ADVANCE_NODE", "subjectId":"b", "optionId":"opaque-v7", "expectedVersion":7, "payload":{"targetNodeId":"b"}}
	var run := {"runId":"r", "branchId":"main", "sequence":7}
	var before := JSON.stringify(choice)
	controller.bind(run,[choice])
	var reference: String = controller.option_reference(choice)
	check(await controller.execute(controller.choice_for(reference)) and JSON.stringify(session.calls[0]) == before, "map uses the original published payload and version byte-for-byte")
	controller.bind(run,[])
	check(controller.choice_for(reference).is_empty() and not await controller.execute(choice), "removed published options cannot travel")
	controller.bind(run,[choice]); session.busy = true
	check(not await controller.execute(choice) and session.calls.size() == 1, "busy state blocks duplicate travel")
	session.busy = false; session.accepted = false
	check(not await controller.execute(choice), "failed or uncertain submission does not report successful travel")
	session.accepted = true; session.delayed = true
	execute(controller,choice); await process_frame
	controller.bind({"runId":"r", "branchId":"other"},[choice]); session.completed.emit(); await process_frame
	check(results == [false], "late response from another branch cannot navigate the current screen")
	execute(controller,choice); await process_frame
	controller.dispose(); session.completed.emit(); await process_frame
	check(results == [false,false], "disposed map ignores a late completion")
	check(JSON.stringify(choice) == before, "coordination leaves caller-owned command immutable")
	root.get_node("GameAudio").shutdown(); await create_timer(.08).timeout
	quit(0 if failures == 0 else 1)
