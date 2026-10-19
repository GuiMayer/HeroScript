extends SceneTree
var failures := 0
class Owner extends RefCounted:
	var current_screen := "map"
	var dialogue_return_barrier := false
class Session extends RefCounted:
	var busy := false
func _init() -> void: call_deferred("_run")
func check(ok: bool, label: String) -> void:
	print("[PASS] " if ok else "[FAIL] ", label)
	if not ok: failures += 1
func _run() -> void:
	var session = root.get_node("GameSession")
	session._run = {"runId":"qa","currentNodeId":"reward"}; session.synchronized = true
	var script := GDScript.new()
	script.source_code = """extends "res://scripts/ui/map_screen.gd"
var captured: Array = []
func _intent(action: String, arguments: Array) -> void: captured.append([action,arguments])
"""
	check(script.reload() == OK, "probe uses production map workflow without a real transport")
	var screen = script.new(); var owner := Owner.new()
	screen.router = owner
	screen.controller = load("res://scripts/ui/controllers/activity_controller.gd").new(Session.new())
	screen._workflow_key = ["qa",7]; root.add_child(screen)
	var resolve := {"type":"RESOLVE_NODE","payload":{"currentNodeId":"reward"}}
	screen.controller.bind(session.run,[resolve])
	screen._auto_advance(["qa",7])
	check(screen.captured == [["command",[screen.controller.option_reference(resolve)]]], "map entry submits sole published completion through canonical command intent")
	for other in ["PICK_CARD_REWARD","BUY_SHOP_ITEM","CHOOSE_DIALOGUE_OPTION"]:
		screen.captured.clear(); screen.controller.bind(session.run,[resolve,{"type":other}]); screen._auto_advance(["qa",7])
		check(screen.captured.is_empty(), "map entry cannot skip a pending choice: " + other)
	screen.controller.bind(session.run,[resolve]); screen.captured.clear()
	screen._auto_advance(["qa",6])
	check(screen.captured.is_empty(), "obsolete workflow cannot complete a node")
	owner.dialogue_return_barrier = true; screen._auto_advance(["qa",7])
	check(screen.captured.is_empty(), "dialogue return barrier is preserved")
	owner.dialogue_return_barrier = false; session.synchronized = false; screen._auto_advance(["qa",7])
	check(screen.captured.is_empty(), "unsynchronized map never starts completion")
	session.synchronized = true
	var other_node := resolve.duplicate(true); other_node.payload.currentNodeId = "another"
	screen.controller.bind(session.run,[other_node]); screen._auto_advance(["qa",7])
	check(screen.captured.is_empty(), "completion cannot target an inspected or unrelated node")
	screen.queue_free(); await process_frame
	root.get_node("GameAudio").shutdown(); await create_timer(.08).timeout
	quit(0 if failures == 0 else 1)
