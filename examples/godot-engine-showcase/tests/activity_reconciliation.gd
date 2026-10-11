extends SceneTree
var failures := 0
class FakeSession extends RefCounted:
	var busy := false
	var calls: Array = []
	func submit_activity(choice: Dictionary) -> bool:
		calls.append(choice.duplicate(true))
		return false
func _init() -> void: call_deferred("_run")
func check(ok: bool, label: String) -> void:
	print("[PASS] " if ok else "[FAIL] ", label)
	if not ok: failures += 1
func settle() -> void:
	for _i in 8: await process_frame
func _run() -> void:
	var session = root.get_node("GameSession")
	session._run = {"runId": "activity", "sequence": 1, "lifecycle": "Active", "currentNodeId": "a", "map": {"nodes": [
		{"nodeId": "a", "activity": {"type": "Encounter"}, "nextNodeIds": ["b"]},
		{"nodeId": "b", "activity": {"type": "Preparation"}, "nextNodeIds": []}]}, "resources": {"gold": {"current": 40}}}
	session._available_commands = [{"type": "START_ENCOUNTER", "validPayload": {}}]
	var router = load("res://main.tscn").instantiate()
	root.add_child(router)
	router.show_activity()
	await settle()
	var screen = router.host.get_child(0)
	var fake := FakeSession.new()
	screen.controller.dispose()
	screen.controller = load("res://scripts/ui/controllers/activity_controller.gd").new(fake)
	screen.controller.bind(session.run, session.activity_choices())
	var route = screen.find_child("RouteView", true, false)
	if route == null: route = screen.find_children("*", "Control", true, false).filter(func(node): return node.get_script() == load("res://scripts/ui/route_view.gd"))[0]
	var route_id: int = route.get_instance_id()
	var first_id: int = screen.choice_buttons[0].get_instance_id()
	session._run.resources.gold.current = 25
	session._run.sequence = 2
	session._available_commands.append({"type": "RESOLVE_NODE", "validPayload": {}})
	screen.refresh_state(router.presentation)
	await settle()
	check(is_instance_valid(route) and route.get_instance_id() == route_id, "resource update retains route")
	check(screen.choice_buttons.any(func(button): return button.get_instance_id() == first_id), "existing option retains identity")
	var button = screen.choice_buttons.filter(func(option): return option.text.contains("COMPLETE"))[0]
	button.pressed.emit()
	await settle()
	check(fake.calls.size() == 1 and fake.calls[0].type == "RESOLVE_NODE", "new reconciled option delegates to live controller, not staging view")
	var before: int = load("res://scripts/ui/view_reconciler.gd").replaced
	screen.refresh_state(router.presentation)
	await settle()
	check(load("res://scripts/ui/view_reconciler.gd").replaced == before, "unchanged activity does not build staging view")
	check(route.nodes[0].current, "retained route projection is current")
	router.queue_free()
	await settle()
	root.get_node("GameAudio").shutdown()
	quit(0 if failures == 0 else 1)
