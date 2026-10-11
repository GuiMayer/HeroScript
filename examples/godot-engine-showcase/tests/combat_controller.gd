extends SceneTree
var failures := 0
class FakeSession extends RefCounted:
	signal release
	var busy := false
	var synchronized := true
	var calls: Array = []
	var hold := false
	var accepted := true
	func submit_candidate(value: Dictionary) -> bool:
		calls.append(value)
		if hold: await release
		return accepted
class FakePlayback extends RefCounted:
	var pending := false
	func has_frames() -> bool: return pending
func _init() -> void: call_deferred("_run")
func check(ok: bool, label: String) -> void:
	print("[PASS] " if ok else "[FAIL] ", label)
	if not ok: failures += 1
func _run() -> void:
	var session := FakeSession.new()
	var playback := FakePlayback.new()
	var controller = load("res://scripts/ui/controllers/combat_controller.gd").new(session, playback, func(): return false)
	var a := {"command": {"cardInstanceId": "c", "targetIds": ["foe"], "costOptionId": "a"}}
	var b := a.duplicate(true); b.command.costOptionId = "b"
	var projection = load("res://scripts/presentation/combat_presenter.gd").new({}, {}, [a, b], root.get_node("I18n"))
	controller.bind(projection, "run/branch/combat/actor", ["c"])
	check(controller.select_card("c"), "explicit selection")
	check(controller.target_options("invalid").is_empty() and controller.state.selected_target.is_empty(), "invalid target does not alter selection")
	check(controller.target_options("foe").size() == 2 and session.calls.is_empty(), "alternative payments remain explicit")
	var fake := a.duplicate(true); fake.command.costOptionId = "invented"
	check(not await controller.submit(fake) and session.calls.is_empty(), "unpublished command rejected locally")
	session.accepted = false
	check(not await controller.submit(a) and controller.state.selected_card == "c", "rejection preserves recovery selection")
	session.accepted = true
	check(await controller.submit(a) and controller.state.selected_card.is_empty(), "acceptance clears selection")
	playback.pending = true
	check(not controller.select_card("c"), "animation blocks input")
	playback.pending = false
	session.synchronized = false
	check(not controller.select_card("c"), "uncertain command blocks input")
	session.synchronized = true
	controller.select_card("c")
	controller.bind(projection, "other-branch", ["c"])
	check(controller.state.selected_card.is_empty(), "context switch clears selection")
	session.hold = true
	controller.submit(a)
	check(controller.state.submitting and not await controller.submit(a), "double submission blocked while waiting")
	controller.dispose()
	session.release.emit()
	check(not controller.select_card("c"), "disposed controller rejects input and late completion")
	root.get_node("GameAudio").shutdown()
	quit(0 if failures == 0 else 1)
